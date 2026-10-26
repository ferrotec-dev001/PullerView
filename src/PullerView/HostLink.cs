// HostLink.cs — VB_FERROTEC.exe との 1 対 1 接続（PullerView が待受側）
//   (1) SocketPort で TCP 待受。VB_FERROTEC.exe が TextID=00（PC名）を送ってきたら SessionID を返信し、
//       Remoting の "{SessionID}ePcObj" に clsPcObj を公開する（VB_FERROTEC_SERVER と同じ手順）。
//   (2) VB_FERROTEC.exe は同一接続上で ReConnect_Time 秒ごとに TextID=00 を再送する → 接続ごとに最初の 00 だけ処理。
//   (3) 読取りスレッドが毎秒 clsPcObj の stcRealData.intIOBit / stcPcInfo / stcPCINF_DATA.stcMCValue を読む。
//   (4) 鮮度判定: VB_FERROTEC は書込みのたびに新しい TP_RealData を送ってくるので、intIOBit の参照が前回と同じなら「更新なし」。
//       stale_sec 秒以上更新が無ければ通信断扱い（古い値を生きた値として表示・記録しない）。
//   (5) 複数接続: 最後にハンドシェイクした接続を採用し、それが切れたら残っている接続へ自動で戻る。
//   (6) 半開ソケット対策: TextID=00 再送もデータ更新も heartbeat_timeout_sec 秒無ければソケットを閉じてセッションを破棄（VB_FERROTEC 側の再接続を促す）。
//   (7) 電文: 最後の STX から ETX までを 1 フレームとして再同期、BCC 不一致は破棄、ETX の来ない受信は 64KB で捨てる。
//   PullerView は読取り専用（Remoting / PLC へ書き込まない）。要件 D-02 決定 2026-10-21。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Text;
using System.Threading;

namespace PullerView
{
    public class HostSample
    {
        public DateTime Time; public string Session = ""; public string Process = ""; public bool PlcOk; public double IngotLen; public int[] IoBit; public bool IoFresh;
        /// <summary>stcMCValue index n のライブ値（intIOBit の 2 ワードを結合し除数で割る）。除数不明なら null。</summary>
        public double? Value(int idx)
        {
            // VB_FERROTEC frmMain: lngVal = fnc_CalcDoubleVal(iPCINF(200 + iCnt*2), iPCINF(200 + iCnt*2 + 1))  … 先頭 200 ワードはアラーム／状態ビット領域
            //                      一部項目は 2 項目の合計（17+101, 22+105 …）。PoC RemotingProbe と同じ復号。
            if (IoBit == null) return null; long div = McMeta.DivOf(idx); if (div <= 0) return null;
            long? raw = HostLink.RawOf(IoBit, idx); if (raw == null) return null;
            int pair = HostLink.SumPairOf(idx); if (pair >= 0) { long? r2 = HostLink.RawOf(IoBit, pair); if (r2 != null) raw += r2; }
            return (double)raw.Value / div;
        }
    }

    public class HostLink
    {
        public int RemotePort = 10030, SocketPort = 10031; public string BindIp = "";
        public char STX = (char)2, ETX = (char)3; public string FromId = "02", ToId = "01", WsId = "01";
        public string DllPath = "VB_FERROTEC_Common.dll";

        public static int IoOffsetWords = 200;          // intIOBit 内の stcMCValue 値の開始ワード（VB_FERROTEC: ZR110000 からの iPCINF(200 + n*2)）
        static Dictionary<int, int> sumPairs = DefaultSumPairs();
        static Dictionary<int, int> DefaultSumPairs() { Dictionary<int, int> d = new Dictionary<int, int>(); int[,] p = { { 17, 101 }, { 22, 105 }, { 23, 107 }, { 27, 111 }, { 29, 113 }, { 19, 103 }, { 35, 123 }, { 39, 125 }, { 42, 130 }, { 46, 132 }, { 71, 109 } }; for (int i = 0; i < p.GetLength(0); i++) d[p[i, 0]] = p[i, 1]; return d; }
        public static long? RawOf(int[] io, int idx) { int off = IoOffsetWords + idx * 2; if (idx < 0 || io.Length < off + 2) return null; return CalcRaw(io[off], io[off + 1]); }
        public static int SumPairOf(int idx) { int p; return sumPairs.TryGetValue(idx, out p) ? p : -1; }
        public int StaleSec = 5;                 // データ更新がこの秒数無ければ「更新なし」（通信断扱い）
        public int HeartbeatTimeoutSec = 60;     // TextID=00 再送もデータ更新も無い状態がこの秒数続いたらソケットを閉じる（0=無効）
        public bool Listening { get; private set; }
        /// <summary>採用中セッションの clsPcObj（検証ツール用。PullerView 本体は読取りにしか使わない）</summary>
        public object CurrentPcObject { get { return latestPc; } }
        public bool Connected { get { return latestPc != null; } }
        /// <summary>直近 StaleSec 秒以内に新しいデータを受け取っているか。</summary>
        public bool DataFresh { get { return latestPc != null && (DateTime.Now - LastFreshAt).TotalSeconds <= StaleSec; } }
        public DateTime LastFreshAt = DateTime.MinValue; public int Stale;   // Stale = 更新なしで読み飛ばした回数
        public int BadFrames;
        public string SessionInfo = ""; public string LastError = "";
        public HostSample Last; public int Reads, Fails;
        public event Action<HostSample> Sample;        // 読取りスレッドから呼ばれる（UI スレッドではない）
        public event Action StateChanged;

        Assembly asm; object latestPc; int sessionSeq; TcpListener lis; Thread reader; volatile bool stop;
        int[] lastIo; object lastIoRef; string lastInfoKey = ""; DateTime lastIoAt; bool staleLogged;
        class Session { public object Pc; public string Info; public TcpClient Tcp; public DateTime LastHb = DateTime.Now; }
        readonly List<Session> sessions = new List<Session>(); readonly object slk = new object();

        public static int CalcRaw(int lower, int upper) { return (int)(((uint)(upper & 0xFFFF) << 16) | (uint)(lower & 0xFFFF)); }

        /// <summary>PullerView.ini [Host] / [Ment] を読む。application_ini が指定されていればそこからポートを取る。</summary>
        public void Configure(Ini ini)
        {
            RemotePort = ini.GetInt("Host", "remote_port", RemotePort); SocketPort = ini.GetInt("Host", "socket_port", SocketPort);
            BindIp = ini.Get("Host", "bind_ip", ""); DllPath = ini.Get("Host", "common_dll", DllPath);
            string app = ini.Get("Host", "application_ini", "");
            if (app != "")
            {
                string p = Path.IsPathRooted(app) ? app : Ini.InExeDir(app);
                if (File.Exists(p))
                {
                    Ini a = Ini.Load(p);
                    RemotePort = a.GetInt("Server", "RemotePort", RemotePort); SocketPort = a.GetInt("Server", "SocketPort", SocketPort);
                    Log.Write("Application.ini から RemotePort=" + RemotePort + " SocketPort=" + SocketPort + " を取得（" + p + "）");
                }
                else Log.Write("警告: application_ini が見つかりません: " + p + "（PullerView.ini の値を使用）");
            }
            StaleSec = Math.Max(2, ini.GetInt("Host", "stale_sec", StaleSec));
            IoOffsetWords = ini.GetInt("Host", "io_offset_words", IoOffsetWords);
            string sp = ini.Get("Host", "sum_items", ""); if (sp.Trim() != "") { sumPairs = new Dictionary<int, int>(); foreach (string e in sp.Split(',')) { string[] ab = e.Split('+'); int a, b; if (ab.Length == 2 && int.TryParse(ab[0].Trim(), out a) && int.TryParse(ab[1].Trim(), out b)) sumPairs[a] = b; } }
            Log.Write("復号設定: io_offset_words=" + IoOffsetWords + " sum_items=" + sumPairs.Count + " 組"); HeartbeatTimeoutSec = ini.GetInt("Host", "heartbeat_timeout_sec", HeartbeatTimeoutSec);
            STX = (char)ini.GetInt("Ment", "stx", 2); ETX = (char)ini.GetInt("Ment", "etx", 3);
            FromId = ini.Get("Ment", "from_id", "02"); ToId = ini.Get("Ment", "to_id", "01"); WsId = ini.Get("Ment", "worksection_id", "01");
        }

        public bool Start()
        {
            try
            {
                string dll = Path.IsPathRooted(DllPath) ? DllPath : Ini.InExeDir(DllPath);
                if (!File.Exists(dll)) { LastError = "VB_FERROTEC_Common.dll が見つかりません: " + dll; Log.Write("NG: " + LastError); return false; }
                asm = Assembly.LoadFrom(dll);
                IPAddress bind = IPAddress.Any, hip;
                if (BindIp != "" && IPAddress.TryParse(BindIp, out hip)) bind = hip;
                BinaryServerFormatterSinkProvider srv = new BinaryServerFormatterSinkProvider(); srv.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;
                Hashtable sp = new Hashtable(); sp["port"] = RemotePort; sp["name"] = "PullerViewHost";
                if (!bind.Equals(IPAddress.Any)) sp["bindTo"] = bind.ToString();
                ChannelServices.RegisterChannel(new TcpServerChannel(sp, srv), false);
                RemotingConfiguration.CustomErrorsEnabled(false);
                lis = new TcpListener(bind, SocketPort); lis.Start();
                Thread acc = new Thread(AcceptLoop); acc.IsBackground = true; acc.Name = "HostLink.Accept"; acc.Start();
                reader = new Thread(ReadLoop); reader.IsBackground = true; reader.Name = "HostLink.Read"; reader.Start();
                Listening = true;
                Log.Write("待受開始: RemotePort=" + RemotePort + " SocketPort=" + SocketPort + " bind=" + bind + " DLL v" + asm.GetName().Version);
                Fire(); return true;
            }
            catch (Exception ex)
            {
                LastError = "ポートを待ち受けできません（" + ex.GetType().Name + ": " + ex.Message + "）。VB_FERROTEC_SERVER 等が " + RemotePort + "/" + SocketPort + " を使用中でないか確認してください。";
                Log.Write("NG: " + LastError); return false;
            }
        }

        public void Stop() { stop = true; try { if (lis != null) lis.Stop(); } catch (Exception) { } }
        void Fire() { if (StateChanged != null) StateChanged(); }

        void AcceptLoop()
        {
            while (!stop)
            {
                try { TcpClient c = lis.AcceptTcpClient(); Thread t = new Thread(ClientThread); t.IsBackground = true; t.Start(c); }
                catch (Exception) { if (stop) return; Thread.Sleep(500); }
            }
        }

        // ---- ソケット電文（VB_FERROTEC_Common の TP_SOCKETINFO と同一書式） ----
        string Bcc(string body) { byte[] b = Encoding.ASCII.GetBytes(body); byte x = b[0]; for (int i = 1; i < b.Length; i++) x ^= b[i]; string h = "00" + x.ToString("X"); return h.Substring(h.Length - 2); }
        byte[] BuildFrame(string textId, string data) { string body = textId + FromId + ToId + "000000" + DateTime.Now.ToString("HHmmss") + WsId + data; return Encoding.ASCII.GetBytes(STX + body + Bcc(body) + ETX); }

        void ClientThread(object state)
        {   // スレッド境界: ここで例外を漏らすとプロセスが落ちるため、必ず捕捉する
            try { ClientBody((TcpClient)state); } catch (ThreadAbortException) { } catch (Exception ex) { Log.Write("[SOCKET] クライアント処理で例外: " + ex); }
        }
        void ClientBody(TcpClient c)
        {
            string ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
            Log.Write("[SOCKET] 接続: " + c.Client.RemoteEndPoint);
            try { c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch (Exception) { }
            StringBuilder buf = new StringBuilder(); byte[] rb = new byte[2048]; Session ses = null; int dup00 = 0;
            try
            {
                NetworkStream ns = c.GetStream(); c.ReceiveTimeout = 5000;
                while (!stop)
                {
                    int n;
                    try { n = ns.Read(rb, 0, rb.Length); }
                    catch (IOException)
                    {   // 受信タイムアウト（5 s）: 半開ソケットの検査。TextID=00 再送もデータ更新も HeartbeatTimeoutSec 秒無ければ切る
                        if (!c.Connected) break;
                        if (HeartbeatTimeoutSec > 0 && ses != null)
                        {
                            double hb = (DateTime.Now - ses.LastHb).TotalSeconds, fr = (DateTime.Now - LastFreshAt).TotalSeconds;
                            if (hb > HeartbeatTimeoutSec && (latestPc != ses.Pc || fr > HeartbeatTimeoutSec)) { Log.Write("[SOCKET] " + HeartbeatTimeoutSec + " 秒間 TextID=00 もデータ更新も無し → 切断して再接続を待つ"); break; }
                        }
                        continue;
                    }
                    if (n <= 0) break;
                    buf.Append(Encoding.ASCII.GetString(rb, 0, n));
                    if (buf.Length > 65536) { Log.Write("[SOCKET] ETX の無い受信が 64KB を超過 → 破棄"); buf.Length = 0; BadFrames++; continue; }
                    while (true)
                    {
                        string all = buf.ToString(); int e = all.IndexOf(ETX); if (e < 0) break;
                        string frame = all.Substring(0, e + 1); buf.Remove(0, e + 1);
                        int s = frame.LastIndexOf(STX); if (s < 0) { BadFrames++; continue; } if (s > 0) frame = frame.Substring(s);   // 最後の STX から再同期
                        if (frame.Length < 24) { BadFrames++; continue; }
                        string body = frame.Substring(1, frame.Length - 4), bcc = frame.Substring(frame.Length - 3, 2);
                        if (!string.Equals(Bcc(body), bcc, StringComparison.OrdinalIgnoreCase)) { BadFrames++; Log.Write("[SOCKET] BCC 不一致（受信 " + bcc + " 計算 " + Bcc(body) + "）→ 破棄"); continue; }
                        string textId = frame.Substring(1, 2);
                        if (textId != "00") { Log.Write("[SOCKET] 受信 TextID=" + textId + "（無視）"); continue; }
                        if (ses != null) { ses.LastHb = DateTime.Now; dup00++; if (dup00 <= 2 || dup00 % 100 == 0) Log.Write("[SOCKET] TextID=00 再送 " + dup00 + " 回目（同じ SessionID を維持）"); continue; }
                        string data = frame.Substring(21, frame.Length - 21 - 3); string pcName = data.Split(new char[] { ',' })[0].Trim();
                        string sid = "PC_ID" + (++sessionSeq) + ":" + ip;
                        object pc = Activator.CreateInstance(asm.GetType("VB_FERROTEC_Common.clsPcObj", true));
                        Reflect.Set(pc, "IPAddress", ip); Reflect.Set(pc, "SessionID", sid); Reflect.Set(pc, "PcName", pcName);
                        RemotingServices.Marshal((MarshalByRefObject)pc, sid + "ePcObj");
                        ses = new Session(); ses.Pc = pc; ses.Info = sid + " (" + pcName + ")"; ses.Tcp = c;
                        lock (slk) { sessions.Add(ses); Adopt(ses, "新規接続"); }
                        byte[] reply = BuildFrame("00", sid); ns.Write(reply, 0, reply.Length);
                        Log.Write("[SOCKET] TextID=00 PC名=" + pcName + " → SessionID=" + sid + " 返信。Remoting 公開 " + sid + "ePcObj" + (sessions.Count > 1 ? "（接続数 " + sessions.Count + "）" : ""));
                        Fire();
                    }
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception ex) { if (!stop) Log.Write("[SOCKET] 切断/例外: " + ex.GetType().Name + " " + ex.Message); }
            finally
            {
                try { c.Close(); } catch (Exception) { }
                Log.Write("[SOCKET] 切断: " + ip + (ses != null ? " " + ses.Info : ""));
                if (ses != null)
                {
                    try { RemotingServices.Disconnect((MarshalByRefObject)ses.Pc); } catch (Exception) { }
                    lock (slk)
                    {
                        sessions.Remove(ses);
                        if (latestPc == ses.Pc)
                        {
                            if (sessions.Count > 0) Adopt(sessions[sessions.Count - 1], "採用中の接続が切れたため残りの接続へ戻す");
                            else { latestPc = null; SessionInfo = ""; Fire(); }
                        }
                    }
                }
            }
        }
        /// <summary>読取り対象セッションを切り替える（slk ロック内で呼ぶ）。</summary>
        void Adopt(Session s, string why)
        {
            latestPc = s.Pc; SessionInfo = s.Info; lastIo = null; lastIoRef = null; lastInfoKey = ""; LastFreshAt = DateTime.MinValue; staleLogged = false;
            Log.Write("[SESSION] 採用: " + s.Info + "（" + why + "）"); Fire();
        }

        void ReadLoop()
        {
            while (!stop)
            {
                try { ReadOnce(); } catch (ThreadAbortException) { return; } catch (Exception ex) { Fails++; if (Fails <= 3 || Fails % 60 == 0) Log.Write("読取りループ例外(" + Fails + "): " + ex.GetType().Name + " " + ex.Message); Thread.Sleep(1000); }
            }
        }
        void ReadOnce()
        {
            {
                DateTime t0 = DateTime.Now; object cur = latestPc;
                if (cur != null)
                {
                    try
                    {
                        object rd = null, info = null, pcinf = null;
                        try { rd = Reflect.P(cur, "stcRealData"); } catch (Exception) { }
                        try { info = Reflect.P(cur, "stcPcInfo"); } catch (Exception) { }
                        try { pcinf = Reflect.P(cur, "stcPCINF_DATA"); } catch (Exception) { }
                        int[] io = rd == null ? null : Reflect.P(rd, "intIOBit") as int[];
                        Array mc = pcinf == null ? null : Reflect.P(pcinf, "stcMCValue") as Array;
                        if (mc != null && mc.Length > 0 && (McMeta.Source != "remoting" || McMeta.Items.Count != mc.Length)) McMeta.Update(mc);
                        HostSample sm = new HostSample(); sm.Time = DateTime.Now; sm.Session = SessionInfo;
                        // 鮮度: VB_FERROTEC は毎回新しい TP_RealData / TP_PcInfo を書くので、参照が前回と同じ＝更新なし
                        // TP_PcInfo は構造体（読むたびに別のボックス）なので内容で比較。intIOBit は配列参照で比較
                        string infoKey = info == null ? "" : Reflect.S(info, "strPROCESS") + "|" + Reflect.S(info, "strINGOT_LENGTH") + "|" + Reflect.B(info, "bStatePlc");
                        bool ioNew = io != null && io.Length > 0 && !ReferenceEquals(io, lastIoRef), infoNew = infoKey != "" && infoKey != lastInfoKey && infoKey != "||False";
                        if (io == null && (info == null || infoKey == "||False")) { Reads++; goto Wait; }   // 接続直後でまだ何も書かれていない → 読み飛ばす（空の工程で誤判定しない）
                        if (!ioNew && !infoNew)
                        {
                            Stale++; double st = (DateTime.Now - LastFreshAt).TotalSeconds;
                            if (st > StaleSec && !staleLogged) { staleLogged = true; Log.Write("[鮮度] " + st.ToString("0") + " 秒間データ更新なし → 通信断扱い（接続は維持）"); Fire(); }
                            Reads++; goto Wait;                                            // 古い値は Sample として流さない（グラフ・CSV に重複を入れない）
                        }
                        if (staleLogged) { Log.Write("[鮮度] データ更新再開"); staleLogged = false; Fire(); }
                        LastFreshAt = sm.Time; lastIoRef = io; lastInfoKey = infoKey;
                        sm.IoFresh = ioNew;
                        if (ioNew) { lastIo = io; lastIoAt = sm.Time; }
                        sm.IoBit = lastIo;
                        if (info != null)
                        {
                            sm.Process = Reflect.S(info, "strPROCESS"); sm.PlcOk = Reflect.B(info, "bStatePlc");
                            double len; if (double.TryParse(Reflect.S(info, "strINGOT_LENGTH").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out len)) sm.IngotLen = len;
                        }
                        Last = sm; Reads++;
                        if (Sample != null) Sample(sm);
                        long ms = (long)(DateTime.Now - t0).TotalMilliseconds;
                        if (ms > 500) Log.Write("[遅延] 読取りに " + ms + "ms（GC gen2=" + GC.CollectionCount(2) + " mem=" + (GC.GetTotalMemory(false) / 1000000) + "MB）");
                    }
                    catch (Exception ex) { Fails++; if (Fails <= 3 || Fails % 60 == 0) Log.Write("読取り失敗(" + Fails + "): " + ex.GetType().Name + " " + ex.Message); }
                }
            Wait:
                int rest = 1000 - (int)(DateTime.Now - t0).TotalMilliseconds; if (rest > 0) Thread.Sleep(rest);
            }
        }
    }

    public static class Log
    {
        public static event Action<string> Line;
        static readonly object lk = new object(); public static string File = "";
        public static void Write(string s)
        {
            string l = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
            if (Line != null) Line(l);
            if (File != "") { lock (lk) { try { System.IO.File.AppendAllText(File, l + "\r\n", Encoding.UTF8); } catch (Exception) { } } }
        }
    }
}
