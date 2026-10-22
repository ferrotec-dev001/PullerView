// HostLink.cs — VB_FERROTEC.exe との 1 対 1 接続（PullerView が待受側）
//   (1) SocketPort で TCP 待受。VB_FERROTEC.exe が TextID=00（PC名）を送ってきたら SessionID を返信し、
//       Remoting の "{SessionID}ePcObj" に clsPcObj を公開する（VB_FERROTEC_SERVER と同じ手順）。
//   (2) VB_FERROTEC.exe は同一接続上で ReConnect_Time 秒ごとに TextID=00 を再送する → 接続ごとに最初の 00 だけ処理。
//   (3) 読取りスレッドが毎秒 clsPcObj の stcRealData.intIOBit / stcPcInfo / stcPCINF_DATA.stcMCValue を読む。
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
        public DateTime Time; public string Process = ""; public bool PlcOk; public double IngotLen; public int[] IoBit; public bool IoFresh;
        /// <summary>stcMCValue index n のライブ値（intIOBit の 2 ワードを結合し除数で割る）。除数不明なら null。</summary>
        public double? Value(int idx)
        {
            if (IoBit == null) return null; int off = idx * 2; if (IoBit.Length < off + 2) return null;
            long div = McMeta.DivOf(idx); if (div <= 0) return null;
            return (double)HostLink.CalcRaw(IoBit[off], IoBit[off + 1]) / div;
        }
    }

    public class HostLink
    {
        public int RemotePort = 10030, SocketPort = 10031; public string BindIp = "";
        public char STX = (char)2, ETX = (char)3; public string FromId = "02", ToId = "01", WsId = "01";
        public string DllPath = "VB_FERROTEC_Common.dll";

        public bool Listening { get; private set; }
        public bool Connected { get { return latestPc != null; } }
        public string SessionInfo = ""; public string LastError = "";
        public HostSample Last; public int Reads, Fails;
        public event Action<HostSample> Sample;        // 読取りスレッドから呼ばれる（UI スレッドではない）
        public event Action StateChanged;

        Assembly asm; object latestPc; int sessionSeq; TcpListener lis; Thread reader; volatile bool stop;
        int[] lastIo; DateTime lastIoAt;

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
            try { ClientBody((TcpClient)state); } catch (Exception ex) { Log.Write("[SOCKET] クライアント処理で例外: " + ex); }
        }
        void ClientBody(TcpClient c)
        {
            string ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
            Log.Write("[SOCKET] 接続: " + c.Client.RemoteEndPoint);
            StringBuilder buf = new StringBuilder(); byte[] rb = new byte[2048]; object connPc = null; int dup00 = 0;
            try
            {
                NetworkStream ns = c.GetStream();
                while (!stop)
                {
                    int n = ns.Read(rb, 0, rb.Length); if (n <= 0) break;
                    buf.Append(Encoding.ASCII.GetString(rb, 0, n));
                    while (true)
                    {
                        string all = buf.ToString(); int e = all.IndexOf(ETX); if (e < 0) break;
                        string frame = all.Substring(0, e + 1); buf.Remove(0, e + 1);
                        int s = frame.IndexOf(STX); if (s > 0) frame = frame.Substring(s);
                        if (frame.Length < 24) continue;
                        string textId = frame.Substring(1, 2);
                        if (textId != "00") { Log.Write("[SOCKET] 受信 TextID=" + textId + "（無視）"); continue; }
                        if (connPc != null) { dup00++; if (dup00 <= 2 || dup00 % 100 == 0) Log.Write("[SOCKET] TextID=00 再送 " + dup00 + " 回目（無視: 同じ SessionID を維持）"); continue; }
                        string data = frame.Substring(21, frame.Length - 21 - 3); string pcName = data.Split(new char[] { ',' })[0].Trim();
                        string sid = "PC_ID" + (++sessionSeq) + ":" + ip;
                        object pc = Activator.CreateInstance(asm.GetType("VB_FERROTEC_Common.clsPcObj", true));
                        Reflect.Set(pc, "IPAddress", ip); Reflect.Set(pc, "SessionID", sid); Reflect.Set(pc, "PcName", pcName);
                        RemotingServices.Marshal((MarshalByRefObject)pc, sid + "ePcObj");
                        latestPc = pc; connPc = pc; SessionInfo = sid + " (" + pcName + ")"; lastIo = null;
                        byte[] reply = BuildFrame("00", sid); ns.Write(reply, 0, reply.Length);
                        Log.Write("[SOCKET] TextID=00 PC名=" + pcName + " → SessionID=" + sid + " 返信。Remoting 公開 " + sid + "ePcObj");
                        Fire();
                    }
                }
            }
            catch (Exception ex) { if (!stop) Log.Write("[SOCKET] 切断/例外: " + ex.GetType().Name + " " + ex.Message); }
            finally
            {
                c.Close(); Log.Write("[SOCKET] 切断: " + ip);
                if (connPc != null) { try { RemotingServices.Disconnect((MarshalByRefObject)connPc); } catch (Exception) { } if (latestPc == connPc) { latestPc = null; SessionInfo = ""; Fire(); } }
            }
        }

        void ReadLoop()
        {
            while (!stop)
            {
                try { ReadOnce(); } catch (Exception ex) { Fails++; if (Fails <= 3 || Fails % 60 == 0) Log.Write("読取りループ例外(" + Fails + "): " + ex.GetType().Name + " " + ex.Message); Thread.Sleep(1000); }
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
                        HostSample sm = new HostSample(); sm.Time = DateTime.Now;
                        sm.IoFresh = io != null && io.Length > 0;
                        if (sm.IoFresh) { lastIo = io; lastIoAt = sm.Time; }
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
