// FakeFerrotec.cs — VB_FERROTEC.exe の代わりに PullerView.exe へ接続してテストデータを書き込むツール（装置なしで結合テスト・接続安定性試験）
//   手順: (1) PullerView.exe を起動（待受）→ (2) FakeFerrotec.exe を起動（先に起動しても --retry（既定）で接続できるまで再試行）
//   動作: SocketPort へ TextID=00 を送って SessionID を受け取り、Remoting "{SessionID}ePcObj" の clsPcObj に
//         stcPCINF_DATA.stcMCValue（除数・項目名）を 1 回、stcRealData.intIOBit / stcPcInfo を毎秒書き込む。
//         工程は NECK→CROWN→SHOULDER→BODY→TAIL→AUTO を --proc-sec 秒ごとに進める。
//   使い方: FakeFerrotec.exe [--host 127.0.0.1] [--socket-port 10031] [--remote-port 10030] [--proc-sec 60] [--dll VB_FERROTEC_Common.dll] [--meta McMeta.ini]
//   試験用オプション（接続安定性シナリオ）:
//     --duration S          S 秒後に正常終了（統計を出力）
//     --connect-delay S     起動後 S 秒待ってから接続
//     --no-retry            接続失敗で即終了（既定は 2 秒ごとに再試行）
//     --disconnect-at S     接続後 S 秒でソケットを正常クローズ → --reconnect-after R 秒後に再接続（既定 5）
//     --kill-at S           接続後 S 秒でプロセスを即死（Environment.FailFast。FIN は OS が送る）
//     --silent-at S --silent-for D   S 秒から D 秒間、書込みを止める（ソケットと TextID=00 再送は維持 = VB_FERROTEC がデータ更新を止めた状態）
//     --freeze-at S         S 秒以降、書込みも TextID=00 再送も止めるがソケットは開いたまま（半開状態の模擬）
//     --flap N              接続→3 秒書込→切断 を N 回繰り返す
//     --no-meta             stcMCValue を送らない
//     --garbage             接続直後にゴミ（STX なし／BCC 不正／ETX の無い長文）を送ってから正しい TextID=00 を 1 バイトずつ送る
//     --interval MS         書込み間隔 [ms]（既定 1000。100 で高負荷）
//     --heartbeat S         TextID=00 再送間隔 [s]（既定 14 = ReConnect_Time 相当。0 で再送なし）
//     --event-proxy         VB_FERROTEC.exe と同じく clsProxyEventObj を作り、サーバー側 clsPcObj.WriteMsgEvent に AddHandler する。
//                           サーバーが WriteMessage(eBTN_Execute, ePLC) を呼ぶと stcSendPLC を読み出して「PLC へ書くはずの内容」を表示する（実 PLC には書かない）
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Text;
using System.Threading;

class FakeFerrotec
{
    static char STX = (char)2, ETX = (char)3; static string FROM = "02", TO = "01", WS = "01";
    static string host = "127.0.0.1", dll = "VB_FERROTEC_Common.dll", meta = "McMeta.ini"; static int sport = 10031, rport = 10030, procSec = 60;
    static int duration = 0, connectDelay = 0, disconnectAt = 0, reconnectAfter = 5, killAt = 0, silentAt = 0, silentFor = 0, freezeAt = 0, flap = 0, interval = 1000, heartbeat = 14;
    static bool retry = true, noMeta = false, garbage = false, eventProxy = false; static object proxyObj; static int eventsReceived;
    static Assembly asm; static Type tPc, tReal, tInfo, tPcinf, tMc;
    static Dictionary<int, long> div = new Dictionary<int, long>(); static Dictionary<int, string[]> cap = new Dictionary<int, string[]>();
    static List<double> lat = new List<double>(); static int writes, writeErr, sessions; static Stopwatch sw = Stopwatch.StartNew();
    static string[] procs = { "NECK", "CROWN", "SHOULDER", "BODY", "TAIL", "AUTO" }; static int pi = 0; static double len = 0; static int t = 0; static Random rnd = new Random(1);

    static string Bcc(string body) { byte[] b = Encoding.ASCII.GetBytes(body); byte x = b[0]; for (int i = 1; i < b.Length; i++) x ^= b[i]; string h = "00" + x.ToString("X"); return h.Substring(h.Length - 2); }
    static byte[] Frame(string id, string data) { string body = id + FROM + TO + "000000" + DateTime.Now.ToString("HHmmss") + WS + data; return Encoding.ASCII.GetBytes(STX + body + Bcc(body) + ETX); }
    static void SetF(object boxed, string name, object v) { boxed.GetType().GetField(name).SetValue(boxed, v); }
    static void P(string s) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [FAKE] " + s); Console.Out.Flush(); }

    static int Main(string[] args)
    {
        for (int a = 0; a < args.Length; a++)
        {
            string k = args[a];
            if (k == "--host") host = args[++a]; else if (k == "--socket-port") sport = int.Parse(args[++a]); else if (k == "--remote-port") rport = int.Parse(args[++a]);
            else if (k == "--proc-sec") procSec = int.Parse(args[++a]); else if (k == "--dll") dll = args[++a]; else if (k == "--meta") meta = args[++a];
            else if (k == "--duration") duration = int.Parse(args[++a]); else if (k == "--connect-delay") connectDelay = int.Parse(args[++a]); else if (k == "--no-retry") retry = false;
            else if (k == "--disconnect-at") disconnectAt = int.Parse(args[++a]); else if (k == "--reconnect-after") reconnectAfter = int.Parse(args[++a]); else if (k == "--kill-at") killAt = int.Parse(args[++a]);
            else if (k == "--silent-at") silentAt = int.Parse(args[++a]); else if (k == "--silent-for") silentFor = int.Parse(args[++a]); else if (k == "--freeze-at") freezeAt = int.Parse(args[++a]);
            else if (k == "--flap") flap = int.Parse(args[++a]); else if (k == "--no-meta") noMeta = true; else if (k == "--garbage") garbage = true; else if (k == "--event-proxy") eventProxy = true;
            else if (k == "--interval") interval = int.Parse(args[++a]); else if (k == "--heartbeat") heartbeat = int.Parse(args[++a]);
            else { Console.WriteLine("unknown option " + k); return 2; }
        }
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!File.Exists(dll)) dll = Path.Combine(baseDir, dll);
        if (!File.Exists(meta)) meta = Path.Combine(baseDir, meta);
        asm = Assembly.LoadFrom(dll);
        tPc = asm.GetType("VB_FERROTEC_Common.clsPcObj", true); tReal = asm.GetType("VB_FERROTEC_Common.TP_RealData", true);
        tInfo = asm.GetType("VB_FERROTEC_Common.TP_PcInfo", true); tPcinf = asm.GetType("VB_FERROTEC_Common.TP_PCINF_DATA", true); tMc = asm.GetType("VB_FERROTEC_Common.TP_MCINFDAT", true);
        LoadMeta();
        BinaryServerFormatterSinkProvider srv = new BinaryServerFormatterSinkProvider(); srv.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;
        Hashtable cp = new Hashtable(); cp["port"] = 0; cp["name"] = "FakeFerrotec";
        ChannelServices.RegisterChannel(new TcpChannel(cp, new BinaryClientFormatterSinkProvider(), srv), false);
        P("start host=" + host + " socket=" + sport + " remoting=" + rport + " interval=" + interval + "ms duration=" + duration + "s");
        if (connectDelay > 0) { P("connect-delay " + connectDelay + "s"); Thread.Sleep(connectDelay * 1000); }

        int rc = 0;
        if (flap > 0) { for (int i = 0; i < flap && !TimeUp(); i++) { P("flap " + (i + 1) + "/" + flap); Session(3, false); Thread.Sleep(300); } }
        else
        {
            while (!TimeUp())
            {
                int r = Session(disconnectAt, true);
                if (r == 1) { P("disconnect-at reached → close socket, reconnect after " + reconnectAfter + "s"); disconnectAt = 0; Thread.Sleep(reconnectAfter * 1000); continue; }
                if (r == 2) break;   // duration
                if (r < 0 && !retry) { rc = 1; break; }
                Thread.Sleep(2000);
            }
        }
        Stats(); return rc;
    }
    static bool TimeUp() { return duration > 0 && sw.Elapsed.TotalSeconds >= duration; }

    /// <summary>1 セッション: 接続→SessionID→Remoting 書込みループ。戻り値 0=正常終了(切断検知) 1=disconnect-at 2=duration -1=接続失敗</summary>
    static int Session(int closeAfter, bool full)
    {
        TcpClient c = null; NetworkStream ns = null; Thread hb = null; bool hbStop = false;
        try
        {
            try { c = new TcpClient(); c.Connect(host, sport); } catch (Exception ex) { P("connect failed: " + ex.Message); return -1; }
            ns = c.GetStream(); c.NoDelay = true;
            if (garbage)
            {
                P("garbage: junk / bad BCC / no-ETX / byte-by-byte frame");
                byte[] junk = Encoding.ASCII.GetBytes("HELLO WORLD\r\n"); ns.Write(junk, 0, junk.Length);
                byte[] bad = Frame("00", "BADBCC"); bad[bad.Length - 2] ^= 0x7F; ns.Write(bad, 0, bad.Length);
                byte[] noEtx = Encoding.ASCII.GetBytes(STX + new string('X', 5000)); ns.Write(noEtx, 0, noEtx.Length);   // ETX の無い長文（バッファ肥大の確認）
                Thread.Sleep(500);
                byte[] ok = Frame("00", Environment.MachineName); for (int i = 0; i < ok.Length; i++) { ns.Write(ok, i, 1); Thread.Sleep(20); }
            }
            else { byte[] f = Frame("00", Environment.MachineName); ns.Write(f, 0, f.Length); }
            c.ReceiveTimeout = 5000;
            byte[] rb = new byte[1024]; int n = ns.Read(rb, 0, rb.Length); if (n <= 0) { P("no reply"); return -1; }
            string rep = Encoding.ASCII.GetString(rb, 0, n); int s0 = rep.IndexOf(STX); int e0 = rep.IndexOf(ETX);
            if (s0 < 0 || e0 < 0) { P("bad reply: " + rep); return -1; }
            string sid = rep.Substring(s0 + 21, e0 - (s0 + 21) - 2); sessions++;
            P("SessionID=" + sid + " (session #" + sessions + ")");
            c.ReceiveTimeout = 0;
            object pc = Activator.GetObject(tPc, "tcp://" + host + ":" + rport + "/" + sid + "ePcObj"); currentPc = pc;
            if (!noMeta) { SendMeta(pc); }
            if (eventProxy) RegisterEventProxy(pc);
            if (heartbeat > 0)
            {
                hb = new Thread(delegate() { while (!hbStop) { for (int i = 0; i < heartbeat * 10 && !hbStop; i++) Thread.Sleep(100); if (hbStop || frozen) return; try { byte[] h = Frame("00", Environment.MachineName); ns.Write(h, 0, h.Length); } catch (Exception) { return; } } });
                hb.IsBackground = true; hb.Start();
            }
            Stopwatch st = Stopwatch.StartNew(); int localWrites = 0;
            while (true)
            {
                if (TimeUp()) return 2;
                double el = st.Elapsed.TotalSeconds;
                if (closeAfter > 0 && el >= closeAfter) { if (full) return 1; return 0; }
                if (killAt > 0 && el >= killAt) { P("kill-at reached → FailFast"); Console.Out.Flush(); Environment.FailFast("test kill"); }
                if (freezeAt > 0 && el >= freezeAt) { if (!frozen) { P("freeze: stop writing and heartbeat, keep socket open"); frozen = true; } Thread.Sleep(1000); if (c.Client.Poll(0, SelectMode.SelectRead) && c.Client.Available == 0) { P("socket closed by host while frozen"); return 0; } continue; }
                bool silent = silentAt > 0 && el >= silentAt && el < silentAt + silentFor;
                if (silent) { if (!wasSilent) P("silent: stop writing for " + silentFor + "s (socket + heartbeat kept)"); wasSilent = true; }
                else
                {
                    if (wasSilent) { P("silent end: resume writing"); wasSilent = false; }
                    try { WriteOnce(pc); localWrites++; if (eventProxy) HandlePendingEvent(pc); }
                    catch (Exception ex) { writeErr++; P("remoting write failed: " + ex.GetType().Name + " " + ex.Message); return 0; }
                }
                // ソケットが相手側から閉じられたか
                if (c.Client.Poll(0, SelectMode.SelectRead)) { byte[] tmp = new byte[256]; int rn = 0; try { rn = c.Client.Receive(tmp); } catch (Exception) { } if (rn == 0) { P("socket closed by host (after " + localWrites + " writes)"); return 0; } }
                Thread.Sleep(interval);
            }
        }
        finally
        {
            hbStop = true; frozen = false;
            try { if (ns != null) ns.Close(); if (c != null) c.Close(); } catch (Exception) { }
            P("session closed");
        }
    }
    static bool frozen, wasSilent;

    // ---- VB_FERROTEC.exe の mdlPcObjRWPLC.ThreadStart と同じ手順: proxy = New clsProxyEventObj / AddHandler g_CurrentPcObj.WriteMsgEvent, AddressOf proxy.WriteMessage ----
    static void RegisterEventProxy(object pc)
    {
        try
        {
            Type tProxy = asm.GetType("VB_FERROTEC_Common.clsProxyEventObj", true), tDlg = asm.GetType("VB_FERROTEC_Common.DlgWriteMessage", true);
            proxyObj = Activator.CreateInstance(tProxy);
            // proxy.WriteMessageEvent += OnWriteMessage（VB_FERROTEC では sub_Button_Envent）
            Type[] pt = Array.ConvertAll(tDlg.GetMethod("Invoke").GetParameters(), delegate(ParameterInfo pi) { return pi.ParameterType; });
            Delegate local = Delegate.CreateDelegate(tDlg, typeof(FakeFerrotec).GetMethod("OnWriteMessageT", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(pt));
            tProxy.GetEvent("WriteMessageEvent").AddEventHandler(proxyObj, local);
            // g_CurrentPcObj.WriteMsgEvent += proxy.WriteMessage（サーバー側へデリゲートを渡す = サーバーからのコールバック経路）
            Delegate remote = Delegate.CreateDelegate(tDlg, proxyObj, tProxy.GetMethod("WriteMessage"));
            tPc.GetMethod("add_WriteMsgEvent").Invoke(pc, new object[] { remote });   // EventInfo.AddEventHandler は透過プロキシで失敗することがあるので add_ メソッドを直接呼ぶ
            P("event-proxy: AddHandler WriteMsgEvent OK（サーバーからの WriteMessage を待機）");
        }
        catch (Exception ex) { P("event-proxy: AddHandler failed: " + ex.GetType().Name + " " + ex.Message); }
    }
    static object currentPc; static volatile int pendingEvent = -1;
    /// <summary>コールバック内ではなく書込みループ側で stcSendPLC を読む（Mono ではコールバック内の入れ子 Remoting 呼出しが空を返すため。.NET の VB_FERROTEC はコールバック内で直接読む）</summary>
    static void HandlePendingEvent(object pc)
    {
        int ev = pendingEvent; if (ev < 0) return; pendingEvent = -1;
        if (ev != 3) return;   // Enum_FormID.ePLC
        try
        {
            // Mono では透過プロキシに対する PropertyInfo.GetValue が空を返すため get_ メソッドを直接呼ぶ（.NET ではどちらでも可）
            object sp = tPc.GetMethod("get_stcSendPLC").Invoke(pc, null);
            Array items = sp.GetType().GetField("stcPLCData").GetValue(sp) as Array; string[] ra = sp.GetType().GetField("strReadAddr").GetValue(sp) as string[];
            if (items == null) { P("  stcSendPLC.stcPLCData = null"); return; }
            foreach (object it in items)
            {
                Type ti = it.GetType(); int[] d = ti.GetField("intDatas").GetValue(it) as int[];
                P("  → sub_Plc_Set(device=" + ti.GetField("strDevice").GetValue(it) + ", address=" + ti.GetField("strAddress").GetValue(it) + ", size=" + ti.GetField("intSize").GetValue(it) + ", data=[" + (d == null ? "" : string.Join(",", Array.ConvertAll(d, delegate(int x) { return x.ToString(); }))) + "])  ※模擬: 実 PLC へは書かない");
            }
            P("  → 再読込アドレス: " + (ra == null ? "(なし)" : string.Join(",", ra)));
        }
        catch (Exception ex) { P("  stcSendPLC 読出し失敗: " + ex.GetType().Name + " " + ex.Message); }
    }
    static void OnWriteMessageT<T1, T2>(T1 a, T2 b) { OnWriteMessage(a, b); }
    static void OnWriteMessage(object eEventType, object eFormID)
    {
        eventsReceived++; pendingEvent = Convert.ToInt32(eFormID);
        P("WriteMsgEvent 受信 #" + eventsReceived + " eEventType=" + eEventType + " eFormID=" + eFormID + " (thread " + Thread.CurrentThread.ManagedThreadId + ")");
        try
        {
            if (false)
            {   // VB_FERROTEC: stcPLC = g_CurrentPcObj.stcSendPLC → sub_Plc_Set(.strDevice, .strAddress, .intSize, .intDatas)
                try { object inf = tPc.GetProperty("stcPcInfo").GetValue(currentPc, null); P("  診断: SessionID=" + tPc.GetProperty("SessionID").GetValue(currentPc, null) + " stcPcInfo.strPROCESS=" + inf.GetType().GetField("strPROCESS").GetValue(inf) + " isProxy=" + System.Runtime.Remoting.RemotingServices.IsTransparentProxy(currentPc)); } catch (Exception ex) { P("  診断失敗: " + ex.Message); }
                object sp = tPc.GetProperty("stcSendPLC").GetValue(currentPc, null);
                Array items = sp.GetType().GetField("stcPLCData").GetValue(sp) as Array; string[] ra = sp.GetType().GetField("strReadAddr").GetValue(sp) as string[];
                if (items == null) { P("  stcSendPLC.stcPLCData = null (strReadAddr=" + (ra == null ? "null" : string.Join(",", ra)) + ", type=" + sp.GetType().FullName + ")"); return; }
                foreach (object it in items)
                {
                    Type ti = it.GetType(); int[] d = ti.GetField("intDatas").GetValue(it) as int[];
                    P("  → sub_Plc_Set(device=" + ti.GetField("strDevice").GetValue(it) + ", address=" + ti.GetField("strAddress").GetValue(it) + ", size=" + ti.GetField("intSize").GetValue(it) + ", data=[" + (d == null ? "" : string.Join(",", Array.ConvertAll(d, delegate(int x) { return x.ToString(); }))) + "])  ※模擬: 実 PLC へは書かない");
                }
                P("  → 再読込アドレス: " + (ra == null ? "(なし)" : string.Join(",", ra)));
            }
        }
        catch (Exception ex) { P("  stcSendPLC 読出し失敗: " + ex.GetType().Name + " " + ex.Message); }
    }

    static void LoadMeta()
    {
        if (!File.Exists(meta)) return;
        string sec = "";
        foreach (string raw in File.ReadAllLines(meta, Encoding.UTF8))
        {
            string l = raw.Trim(); if (l.StartsWith("[")) { sec = l.Trim('[', ']'); continue; }
            int e = l.IndexOf('='); if (e < 0 || !sec.StartsWith("Item")) continue;
            int idx; if (!int.TryParse(sec.Substring(4), out idx)) continue;
            string key = l.Substring(0, e).Trim().ToLower(), val = l.Substring(e + 1).Trim();
            if (key == "div") { long d; if (long.TryParse(val, out d)) div[idx] = d; }
            else { string[] cc; if (!cap.TryGetValue(idx, out cc)) { cc = new string[] { "", "", "" }; cap[idx] = cc; } if (key == "caption") cc[0] = val; else if (key == "caption_j") cc[1] = val; else if (key == "unit") cc[2] = val; }
        }
        P("McMeta.ini: " + div.Count + " items");
    }
    static void SendMeta(object pc)
    {
        int N = 150; Array mc = Array.CreateInstance(tMc, N);
        for (int i = 0; i < N; i++)
        {
            object m = Activator.CreateInstance(tMc); long d; string[] cc;
            SetF(m, "lDivVal", div.TryGetValue(i, out d) ? d : 10L);
            SetF(m, "sCaption", cap.TryGetValue(i, out cc) ? cc[0] : "ITEM" + i); SetF(m, "sCaption_j", cap.TryGetValue(i, out cc) ? cc[1] : ""); SetF(m, "sUnit", cap.TryGetValue(i, out cc) ? cc[2] : "-");
            SetF(m, "sFormat", "0.0"); SetF(m, "iDispNo", i); mc.SetValue(m, i);
        }
        object pcinf = Activator.CreateInstance(tPcinf); SetF(pcinf, "stcMCValue", mc);
        Stopwatch s = Stopwatch.StartNew(); tPc.GetProperty("stcPCINF_DATA").SetValue(pc, pcinf, null);
        P("stcMCValue sent (" + N + " items, " + s.ElapsedMilliseconds + "ms)");
    }
    static void WriteOnce(object pc)
    {
        t++; if (t % procSec == 0) { pi = (pi + 1) % procs.Length; if (pi == 0) len = 0; }
        if (pi < 5) len += 0.5;
        int[] io = new int[2300];
        Action<int, double> put = delegate(int idx, double v) { long d; long dv = div.TryGetValue(idx, out d) ? d : 10L; int raw = (int)Math.Round(v * dv); io[200 + idx * 2] = raw & 0xFFFF; io[200 + idx * 2 + 1] = (raw >> 16) & 0xFFFF; };   // VB_FERROTEC と同じく先頭 200 ワードはビット領域
        double dia = 215 + 8 * Math.Sin(t * 0.05) + 2 * (rnd.NextDouble() - 0.5);
        put(65, dia); put(92, 215); put(66, len); put(67, len * 0.2);
        put(21, 1500 + 20 * Math.Sin(t * 0.02)); put(22, 1500); put(23, 100); put(24, 100 + 5 * Math.Sin(t * 0.1)); put(17, 2000); put(18, 2000 + 30 * Math.Sin(t * 0.3)); put(19, 100); put(20, 100 + 3 * Math.Sin(t * 0.2)); put(27, 1400); put(28, 1400 + 10 * Math.Sin(t * 0.15)); put(1, 1000 * (1 + 0.1 * Math.Sin(t * 0.01))); put(2, 1e-3); put(3, 2e-3);
        put(115, Math.Min(len, 300)); put(116, Math.Max(0, Math.Min(len - 300, 100))); put(117, Math.Max(0, Math.Min(len - 400, 50))); put(118, Math.Max(0, Math.Min(len - 450, 1500))); put(119, Math.Max(0, len - 1950));
        Stopwatch s = Stopwatch.StartNew();
        // 制御モード信号（テスト用に 30 秒ごとに切替）: R110140 BIT06=TEMP / BIT07=POWER、R104019 BIT00=APC PRESSURE PL / BIT01=APC ANGLE PL
        bool tempMode = (t / 30) % 2 == 0; io[140] = (tempMode ? 1 << 6 : 1 << 7) | 1 << 1 /*MANUAL*/; io[66] = tempMode ? 1 << 1 : 1 << 0;
        int[] man = new int[170]; man[19] = (t / 45) % 2 == 0 ? 1 : 2;
        object real = Activator.CreateInstance(tReal); SetF(real, "intIOBit", io); try { SetF(real, "intManual", man); } catch (Exception) { } tPc.GetProperty("stcRealData").SetValue(pc, real, null);
        object info = Activator.CreateInstance(tInfo); SetF(info, "strPROCESS", procs[pi]); SetF(info, "bStatePlc", true); SetF(info, "strINGOT_LENGTH", len.ToString("0.0")); SetF(info, "strINGOT_WIDTH", dia.ToString("0.0"));
        tPc.GetProperty("stcPcInfo").SetValue(pc, info, null);
        double ms = s.Elapsed.TotalMilliseconds; lat.Add(ms); writes++;
        if (ms > 200) P("slow write " + ms.ToString("0") + "ms");
        if (t % 10 == 1) P("proc=" + procs[pi] + " len=" + len.ToString("0.0") + " dia=" + dia.ToString("0.0") + " write=" + ms.ToString("0.0") + "ms");
    }
    static void Stats()
    {
        lat.Sort(); Func<double, double> pct = delegate(double q) { return lat.Count == 0 ? 0 : lat[Math.Min(lat.Count - 1, (int)(q * lat.Count))]; };
        P("STATS sessions=" + sessions + " events=" + eventsReceived + " writes=" + writes + " writeErr=" + writeErr + " lat_p50=" + pct(0.5).ToString("0.0") + "ms p99=" + pct(0.99).ToString("0.0") + "ms max=" + (lat.Count == 0 ? 0 : lat[lat.Count - 1]).ToString("0.0") + "ms elapsed=" + sw.Elapsed.TotalSeconds.ToString("0") + "s");
    }
}
