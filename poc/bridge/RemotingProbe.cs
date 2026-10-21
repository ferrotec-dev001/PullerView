// PullerView PoC: VB_FERROTEC.exe との 1 対 1 接続版（ホスト VB_FERROTEC_SERVER なし）
//
// 【役割】 VB_FERROTEC.exe は起動すると「ホスト」へ (1) TCP SocketPort に接続して SessionID を受け取り、
//         (2) Remoting RemotePort の "{SessionID}ePcObj" (clsPcObj) に毎秒データを書き込む。
//         本ツールはそのホスト側（待機側）の最小部分を代行し、書き込まれたデータを読み取って表示する。
//         ※ VB_FERROTEC_SERVER が同じ PC / ポートで動いている場合は競合するため、どちらか一方だけを起動すること。
// 【読取り専用】 PLC・VB_FERROTEC.exe への書込み・指示は一切行わない（SessionID の返信のみ）。
// 【設定】 VB_FERROTEC.exe と同じフォルダの Application.ini [Server] (HostIP / SocketPort / RemotePort) と
//         Ment.ini [Soket Package Element] (STX/ETX/FROM_ID/TO_ID/WORKSECTION_ID) を使う。DLL は同じフォルダの VB_FERROTEC_Common.dll。
// 使い方: RemotingProbe.exe               （待機→VB_FERROTEC.exe を（再）起動すると接続される。Ctrl+C で終了）
//         RemotingProbe.exe --seconds 60 --indices 65,92,115,116,117,118,119
//         RemotingProbe.exe --connect --host IP --port N --ip MyIP   （従来方式: 既存ホストへ読取りクライアントとして接続）
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading;

static class RemotingProbe
{
    static Assembly asm;
    static volatile object latestPc;
    static string sessionInfo = "";
    static int sessionSeq = 0;
    static char STX = (char)2, ETX = (char)3;
    static string FROM_ID = "02", TO_ID = "01", WS_ID = "01";

    static object P(object o, string name)
    {
        Type[] ts = new Type[] { o.GetType(), asm.GetType("VB_FERROTEC_Common.clsPcObj"), asm.GetType("VB_FERROTEC_Common.clsServerObj") };
        foreach (Type t in ts)
        {
            if (t == null) continue;
            PropertyInfo pi = t.GetProperty(name);
            if (pi != null && (pi.DeclaringType.IsInstanceOfType(o) || t == o.GetType())) return pi.GetValue(o, null);
        }
        throw new MissingMemberException(name);
    }
    static void SetP(object o, string name, object v) { o.GetType().GetProperty(name).SetValue(o, v, null); }
    static object F(object o, string name) { return o.GetType().GetField(name).GetValue(o); }
    static object FI(object o, string name) { return o == null ? null : F(o, name); }
    static string S(object o) { return o == null ? "" : Convert.ToString(o); }
    static int CalcRaw(int lower, int upper) { return (int)(((uint)(upper & 0xFFFF) << 16) | (uint)(lower & 0xFFFF)); }
    static void Log(string s) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); }

    static string IniGet(string path, string section, string key, string def)
    {
        if (!File.Exists(path)) return def;
        bool inSec = false;
        foreach (string raw in File.ReadAllLines(path, Encoding.Default))
        {
            string l = raw.Trim();
            if (l.StartsWith("[")) { inSec = l.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSec || l.StartsWith(";")) continue;
            int e = l.IndexOf('='); if (e < 0) continue;
            if (l.Substring(0, e).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return l.Substring(e + 1).Trim();
        }
        return def;
    }

    // ---- ソケット電文（VB_FERROTEC_Common の TP_SOCKETINFO と同一書式） ----
    static string Bcc(string body)
    {
        byte[] b = Encoding.ASCII.GetBytes(body); byte x = b[0];
        for (int i = 1; i < b.Length; i++) x ^= b[i];
        return ("00" + x.ToString("X")).Substring(("00" + x.ToString("X")).Length - 2);
    }
    static byte[] BuildFrame(string textId, string data)
    {
        string body = textId + FROM_ID + TO_ID + "000000" + DateTime.Now.ToString("HHmmss") + WS_ID + data;
        return Encoding.ASCII.GetBytes(STX + body + Bcc(body) + ETX);
    }

    static void ClientThread(object state)
    {
        TcpClient c = (TcpClient)state;
        string ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
        Log("[SOCKET] 接続: " + c.Client.RemoteEndPoint);
        StringBuilder buf = new StringBuilder();
        byte[] rb = new byte[2048];
        try
        {
            NetworkStream ns = c.GetStream();
            while (true)
            {
                int n = ns.Read(rb, 0, rb.Length);
                if (n <= 0) break;
                buf.Append(Encoding.ASCII.GetString(rb, 0, n));
                while (true)
                {
                    string all = buf.ToString(); int e = all.IndexOf(ETX);
                    if (e < 0) break;
                    string frame = all.Substring(0, e + 1); buf.Remove(0, e + 1);
                    int s = frame.IndexOf(STX); if (s > 0) frame = frame.Substring(s);
                    if (frame.Length < 24) continue;
                    string textId = frame.Substring(1, 2);
                    if (textId != "00") { Log("[SOCKET] 受信 TextID=" + textId + "（無視）"); continue; }
                    string data = frame.Substring(21, frame.Length - 21 - 3);
                    string pcName = data.Split(new char[] { ',' })[0].Trim();
                    string sid = "PC_ID" + (++sessionSeq) + ":" + ip;
                    object pc = Activator.CreateInstance(asm.GetType("VB_FERROTEC_Common.clsPcObj", true));
                    SetP(pc, "IPAddress", ip); SetP(pc, "SessionID", sid); SetP(pc, "PcName", pcName);
                    RemotingServices.Marshal((MarshalByRefObject)pc, sid + "ePcObj");
                    latestPc = pc; sessionInfo = sid + " (" + pcName + ")";
                    byte[] reply = BuildFrame("00", sid);
                    ns.Write(reply, 0, reply.Length);
                    Log("[SOCKET] TextID=00 受信 PC名=" + pcName + " → SessionID=" + sid + " を返信。Remoting 公開: " + sid + "ePcObj");
                }
            }
        }
        catch (Exception ex) { Log("[SOCKET] 切断/例外: " + ex.GetType().Name + " " + ex.Message); }
        finally { c.Close(); Log("[SOCKET] 切断: " + ip); }
    }

    static int Main(string[] args)
    {
        string host = null; int port = 0, sport = 0; string myIp = null; int secs = 0; string idxs = "65,92,115,116,117,118,119";
        string ini = "Application.ini", ment = "Ment.ini", csv = "poc_result.csv", dll = "VB_FERROTEC_Common.dll"; bool connect = false;
        for (int a = 0; a < args.Length; a++)
        {
            string k = args[a];
            if (k == "--connect") connect = true;
            else if (k == "--host") host = args[++a]; else if (k == "--port") port = int.Parse(args[++a]);
            else if (k == "--socket-port") sport = int.Parse(args[++a]);
            else if (k == "--ip") myIp = args[++a]; else if (k == "--seconds") secs = int.Parse(args[++a]);
            else if (k == "--indices") idxs = args[++a]; else if (k == "--ini") ini = args[++a]; else if (k == "--ment") ment = args[++a];
            else if (k == "--csv") csv = args[++a]; else if (k == "--dll") dll = args[++a];
            else { Console.WriteLine("RemotingProbe [--seconds N] [--indices 65,92,..] [--ini path] [--ment path] [--csv path] [--dll path] [--host IP --port N --socket-port N] [--connect --ip MyIP]"); return 0; }
        }
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!Path.IsPathRooted(dll)) dll = Path.Combine(baseDir, dll);
        if (!File.Exists(ini)) ini = Path.Combine(baseDir, ini);
        if (!File.Exists(ment)) ment = Path.Combine(baseDir, ment);
        if (!File.Exists(dll)) { Console.Error.WriteLine("NG: DLL が見つかりません: " + dll + "（VB_FERROTEC.exe と同じフォルダの VB_FERROTEC_Common.dll をコピーしてください）"); return 2; }
        asm = Assembly.LoadFrom(dll);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e) { return e.Name.StartsWith("VB_FERROTEC_Common") ? asm : null; };

        if (host == null) host = IniGet(ini, "Server", "HostIP", null);
        if (port == 0) int.TryParse(IniGet(ini, "Server", "RemotePort", "10030"), out port);
        if (sport == 0) int.TryParse(IniGet(ini, "Server", "SocketPort", "10031"), out sport);
        try
        {
            STX = (char)int.Parse(IniGet(ment, "Soket Package Element", "STX", "2")); ETX = (char)int.Parse(IniGet(ment, "Soket Package Element", "ETX", "3"));
            FROM_ID = IniGet(ment, "Soket Package Element", "FROM_ID", "02"); TO_ID = IniGet(ment, "Soket Package Element", "TO_ID", "01");
            WS_ID = IniGet(ment, "Soket Package Element", "WORKSECTION_ID", "01");
        }
        catch (Exception) { Console.Error.WriteLine("WARN: Ment.ini の読取りに失敗。既定値(STX=2,ETX=3,FROM=02,TO=01,WS=01)を使用"); }
        int[] idx = idxs.Split(new char[] { ',' }).Select(int.Parse).ToArray();

        BinaryServerFormatterSinkProvider srv = new BinaryServerFormatterSinkProvider(); srv.TypeFilterLevel = TypeFilterLevel.Full;
        object pc = null;
        if (connect)
        {
            if (host == null) { Console.Error.WriteLine("NG: --host を指定するか Application.ini を置いてください"); return 2; }
            List<string> myIps = new List<string>();
            if (myIp != null) myIps.Add(myIp);
            else foreach (IPAddress ia in Dns.GetHostAddresses(Dns.GetHostName())) if (ia.AddressFamily == AddressFamily.InterNetwork) myIps.Add(ia.ToString());
            Hashtable cp = new Hashtable(); cp["port"] = 0; cp["name"] = "PullerViewProbe";
            ChannelServices.RegisterChannel(new TcpChannel(cp, new BinaryClientFormatterSinkProvider(), srv), false);
            Type tServer = asm.GetType("VB_FERROTEC_Common.clsServerObj", true);
            object server = Activator.GetObject(tServer, "tcp://" + host + ":" + port + "/eServer");
            ArrayList pcs = (ArrayList)P(server, "ConnectPc");
            MethodInfo getPc = tServer.GetMethod("GetPcObject");
            foreach (string ip in myIps) { object c = getPc.Invoke(server, new object[] { ip }); if (c != null && S(P(c, "IPAddress")) == ip) { pc = c; break; } }
            if (pc == null) foreach (object p in pcs) { foreach (string ip in myIps) if (S(P(p, "IPAddress")).Contains(ip)) pc = p; if (pc != null) break; }
            if (pc == null) { Console.Error.WriteLine("NG: 自PCの clsPcObj が見つかりません"); return 3; }
            latestPc = pc; Log("[connect] OK: IP=" + S(P(pc, "IPAddress")));
        }
        else
        {
            IPAddress bind = IPAddress.Any;
            IPAddress hip;
            if (host != null && IPAddress.TryParse(host, out hip))
            {
                bool local = IPAddress.IsLoopback(hip);
                foreach (IPAddress ia in Dns.GetHostAddresses(Dns.GetHostName())) if (ia.Equals(hip)) local = true;
                if (local) bind = hip;
            }
            Log("[待機] DLL=" + asm.GetName().Version + " bind=" + bind + " RemotePort=" + port + " SocketPort=" + sport + "（Application.ini HostIP=" + host + "）");
            try
            {
                Hashtable sp = new Hashtable(); sp["port"] = port; sp["name"] = "PullerViewProbe";
                if (!bind.Equals(IPAddress.Any)) sp["bindTo"] = bind.ToString();
                ChannelServices.RegisterChannel(new TcpServerChannel(sp, srv), false);
                RemotingConfiguration.CustomErrorsEnabled(false);
                TcpListener lis = new TcpListener(bind, sport); lis.Start();
                Thread th = new Thread(delegate()
                {
                    while (true) { TcpClient c = lis.AcceptTcpClient(); Thread t = new Thread(ClientThread); t.IsBackground = true; t.Start(c); }
                });
                th.IsBackground = true; th.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("NG: ポートを待ち受けできません（" + ex.GetType().Name + ": " + ex.Message + "）");
                Console.Error.WriteLine("    VB_FERROTEC_SERVER など別プログラムが " + port + " / " + sport + " を使用中でないか確認してください。");
                return 4;
            }
            Log("VB_FERROTEC.exe を（再）起動してください。接続を待っています… (Ctrl+C で終了)");
        }

        if (!Path.IsPathRooted(csv)) csv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, csv);
        StreamWriter logf = new StreamWriter(new FileStream(csv, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true));
        Console.WriteLine("CSV出力先: " + csv);
        logf.WriteLine("time,ms,iobit_len,process,plc,ingot_len,proc_len," + string.Join(",", idx.Select(n => "idx" + n).ToArray())); logf.Flush();
        Dictionary<int, long> div = new Dictionary<int, long>(); bool metaShown = false;
        List<long> lat = new List<long>(); int fails = 0, reads = 0; string lastSession = ""; int[] lastIo = null; Array lastMc = null; string lastProc = ""; DateTime lastIoAt = DateTime.MinValue;
        Process me = Process.GetCurrentProcess();
        DateTime t0 = DateTime.Now;
        while (secs == 0 || (DateTime.Now - t0).TotalSeconds < secs + (connect ? 0 : 0))
        {
            Stopwatch sw = Stopwatch.StartNew();
            object cur = latestPc;
            try
            {
                if (cur == null) { Thread.Sleep(500); continue; }
                if (sessionInfo != lastSession) { lastSession = sessionInfo; metaShown = false; div.Clear(); }
                object rd = null, info = null, pcinf = null;
                try { rd = P(cur, "stcRealData"); } catch (Exception) { }
                try { info = P(cur, "stcPcInfo"); } catch (Exception) { }
                try { pcinf = P(cur, "stcPCINF_DATA"); } catch (Exception) { }
                int[] io0 = rd == null ? null : (int[])F(rd, "intIOBit");
                Array mc0 = pcinf == null ? null : (Array)F(pcinf, "stcMCValue");
                string proc0 = info == null ? "" : S(FI(info, "strPROCESS"));
                bool fresh = io0 != null && io0.Length > 0;
                // VB_FERROTEC は項目を断続的にしか書込まないため、最後に取得できた値を保持して使う
                if (fresh) { lastIo = io0; lastIoAt = DateTime.Now; }
                if (mc0 != null && mc0.Length > 0) { if (lastMc == null) metaShown = false; lastMc = mc0; }
                if (proc0 != "") lastProc = proc0;
                string plc0 = info == null ? "" : S(F(info, "bStatePlc"));
                int[] io = lastIo; Array mc = lastMc;
                if (io == null || mc == null)
                {
                    logf.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + ",WAIT,io=" + (io0 == null ? "null" : io0.Length.ToString()) + ",mc=" + (mc0 == null ? "null" : mc0.Length.ToString()) + ",plc=" + plc0 + ",process=[" + proc0 + "],cacheIo=" + (lastIo != null) + ",cacheMc=" + (lastMc != null)); logf.Flush();
                    if (reads == 0 && (int)(DateTime.Now - t0).TotalSeconds % 5 == 0) Log("[待機] 値の一部が未取得 io=" + (io0 == null ? "null" : io0.Length.ToString()) + " mc=" + (mc0 == null ? "null" : mc0.Length.ToString()) + " plc=" + plc0 + " process=[" + proc0 + "]");
                    Thread.Sleep(1000); continue;
                }
                if (!metaShown)
                {
                    Log("stcMCValue len=" + mc.Length);
                    foreach (int n in idx)
                    {
                        object m = mc.GetValue(n); div[n] = Convert.ToInt64(F(m, "lDivVal"));
                        Log("  [" + n + "] " + S(F(m, "sCaption_j")) + "/" + S(F(m, "sCaption")) + " [" + S(F(m, "sUnit")) + "] div=" + div[n] + " fmt=" + S(F(m, "sFormat")) + " iDispNo=" + S(F(m, "iDispNo")) + " lValue(init)=" + S(F(m, "lValue")));
                    }
                    metaShown = true;
                }
                string proc = lastProc.ToUpper();
                string plen = "";
                if (proc == "NECK") plen = S(FI(info, "strNECK_LENGTH")); else if (proc == "CROWN") plen = S(FI(info, "strCROWN_LENGTH"));
                else if (proc == "SHOULDER") plen = S(FI(info, "strSHOULDER_LENGTH")); else if (proc == "BODY") plen = S(FI(info, "strBODY_LENGTH"));
                else if (proc == "TAIL") plen = S(FI(info, "strTAIL_LENGTH"));
                List<string> vals = new List<string>();
                foreach (int n in idx)
                {
                    int off = 200 + n * 2;
                    vals.Add(io.Length >= off + 2 && div[n] > 0 ? ((double)CalcRaw(io[off], io[off + 1]) / div[n]).ToString("0.####") : "");
                }
                long ms = sw.ElapsedMilliseconds; lat.Add(ms); reads++;
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + "," + ms + "," + io.Length + "," + proc + "," + plc0 + "," + S(FI(info, "strINGOT_LENGTH")).Trim() + "," + plen.Trim() + "," + string.Join(",", vals.ToArray());
                logf.WriteLine(line); logf.Flush(); me.Refresh();
                Console.WriteLine("  " + line.Replace(",", "  ") + "  mem=" + (me.WorkingSet64 / 1000000) + "MB");
                if (io.Length < 2100) Console.WriteLine("    WARN intIOBit len=" + io.Length + " (期待 2100以上)"); if (!fresh) Console.WriteLine("    (io は保持値: " + (int)(DateTime.Now - lastIoAt).TotalSeconds + "秒前)");
            }
            catch (Exception ex) { fails++; Log("ERR " + ex.GetType().Name + ": " + ex.Message); logf.WriteLine("ERR," + ex.GetType().Name); logf.Flush(); }
            int rest = 1000 - (int)sw.ElapsedMilliseconds; if (rest > 0) Thread.Sleep(rest);
        }
        logf.Close();
        Console.WriteLine("[結果] reads=" + reads + " errors=" + fails + (lat.Count > 0 ? " latency avg=" + (long)lat.Average() + "ms max=" + lat.Max() + "ms" : ""));
        return fails == 0 && reads > 0 ? 0 : 1;
    }
}
