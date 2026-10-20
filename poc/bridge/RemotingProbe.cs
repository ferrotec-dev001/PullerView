// PullerView PoC-B: 読取り専用 Remoting プローブ（Python 不要・単体 exe）
// VB_FERROTEC_Common.dll へはリフレクションで接続するため、装置PCの DLL をそのまま同じフォルダに置けば動作する（ビルド時の DLL 版数に依存しない）。
// 使い方: RemotingProbe.exe            （同フォルダの Application.ini [Server] から HostIP/RemotePort を読み、自PCのIPは自動検出）
//         RemotingProbe.exe --host 172.16.87.27 --port 10030 --ip 172.16.87.50 --seconds 60 --indices 65,92,115,116,117,118,119
// 出力  : 画面と poc_result.csv（1秒ごとに1行）。Getter のみ使用（書込み・権限取得は一切しない）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading;

static class RemotingProbe
{
    static Assembly asm;
    static object P(object o, string name)
    {
        // 透過プロキシでは GetType() が実型を返さない実装があるため、既知の Remoting 型からもプロパティを探す
        Type[] ts = new Type[] { o.GetType(), asm.GetType("VB_FERROTEC_Common.clsPcObj"), asm.GetType("VB_FERROTEC_Common.clsServerObj") };
        foreach (Type t in ts)
        {
            if (t == null) continue;
            PropertyInfo pi = t.GetProperty(name);
            if (pi != null && (pi.DeclaringType.IsInstanceOfType(o) || t == o.GetType())) return pi.GetValue(o, null);
        }
        throw new MissingMemberException(name);
    }
    static object F(object o, string name) { return o.GetType().GetField(name).GetValue(o); }
    static string S(object o) { return o == null ? "" : Convert.ToString(o); }
    static int CalcRaw(int lower, int upper) { return (int)(((uint)(upper & 0xFFFF) << 16) | (uint)(lower & 0xFFFF)); }

    static int Main(string[] args)
    {
        string host = null; int port = 10030; string myIp = null; int secs = 60; string idxs = "65,92,115,116,117,118,119";
        string ini = "Application.ini", csv = "poc_result.csv", dll = "VB_FERROTEC_Common.dll";
        for (int a = 0; a < args.Length; a++)
        {
            string k = args[a];
            if (k == "--host") host = args[++a]; else if (k == "--port") port = int.Parse(args[++a]);
            else if (k == "--ip") myIp = args[++a]; else if (k == "--seconds") secs = int.Parse(args[++a]);
            else if (k == "--indices") idxs = args[++a]; else if (k == "--ini") ini = args[++a];
            else if (k == "--csv") csv = args[++a]; else if (k == "--dll") dll = args[++a];
            else { Console.WriteLine("RemotingProbe [--host IP] [--port N] [--ip MyIP] [--seconds N] [--indices 65,92,..] [--ini path] [--csv path] [--dll path]"); return 0; }
        }
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!Path.IsPathRooted(dll)) dll = Path.Combine(baseDir, dll);
        if (!Path.IsPathRooted(ini) && !File.Exists(ini)) ini = Path.Combine(baseDir, ini);
        if (!File.Exists(dll)) { Console.Error.WriteLine("NG: DLL が見つかりません: " + dll + "（VB_FERROTEC.exe と同じフォルダの VB_FERROTEC_Common.dll をコピーしてください）"); return 2; }
        asm = Assembly.LoadFrom(dll);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e) { return e.Name.StartsWith("VB_FERROTEC_Common") ? asm : null; };

        if (host == null && File.Exists(ini))
        {
            bool inSrv = false;
            foreach (string raw in File.ReadAllLines(ini, Encoding.Default))
            {
                string l = raw.Trim();
                if (l.StartsWith("[")) { inSrv = l.Equals("[Server]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inSrv) continue;
                int e = l.IndexOf('='); if (e < 0) continue;
                string key = l.Substring(0, e).Trim(), val = l.Substring(e + 1).Trim();
                if (key.Equals("HostIP", StringComparison.OrdinalIgnoreCase)) host = val;
                else if (key.Equals("RemotePort", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out port);
            }
        }
        if (host == null) { Console.Error.WriteLine("NG: --host を指定するか、Application.ini を同じフォルダに置いてください"); return 2; }
        List<string> myIps = new List<string>();
        if (myIp != null) myIps.Add(myIp);
        else foreach (System.Net.IPAddress ia in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
            if (ia.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) myIps.Add(ia.ToString());
        int[] idx = idxs.Split(',').Select(int.Parse).ToArray();
        Console.WriteLine("[1] Host=" + host + ":" + port + " MyIP=" + string.Join(",", myIps.ToArray()) + " DLL=" + asm.GetName().Version);

        // VB クライアント(mdlLocalService.sub_Start_Service)と同じ構成
        Hashtable props = new Hashtable(); props["port"] = 0; props["name"] = "PullerViewProbe";
        BinaryServerFormatterSinkProvider srv = new BinaryServerFormatterSinkProvider(); srv.TypeFilterLevel = TypeFilterLevel.Full;
        ChannelServices.RegisterChannel(new TcpChannel(props, new BinaryClientFormatterSinkProvider(), srv), false);

        Type tServer = asm.GetType("VB_FERROTEC_Common.clsServerObj", true);
        string url = "tcp://" + host + ":" + port + "/eServer";
        object server = Activator.GetObject(tServer, url);
        Stopwatch sw0 = Stopwatch.StartNew();
        Console.WriteLine("[2] eServer: Name=" + S(P(server, "ServerName")) + " IP=" + S(P(server, "ServerIP")) + " RemotePort=" + S(P(server, "RemotePort")) + " SocketPort=" + S(P(server, "SocketPort")) + " (" + sw0.ElapsedMilliseconds + "ms)");
        ArrayList pcs = (ArrayList)P(server, "ConnectPc");
        Console.WriteLine("    ConnectPc=" + pcs.Count);
        foreach (object p in pcs) Console.WriteLine("      - IP=" + S(P(p, "IPAddress")) + " Name=" + S(P(p, "PcName")));

        object pc = null;
        MethodInfo getPc = tServer.GetMethod("GetPcObject");
        foreach (string ip in myIps) { object c = getPc.Invoke(server, new object[] { ip }); if (c != null && S(P(c, "IPAddress")) == ip) { pc = c; break; } }
        if (pc == null) foreach (object p in pcs) { foreach (string ip in myIps) if (S(P(p, "IPAddress")).Contains(ip)) pc = p; if (pc != null) break; }
        if (pc == null) { Console.Error.WriteLine("NG: 自PCの clsPcObj が見つかりません（--ip で装置PCのIPを指定してください）"); return 3; }
        Console.WriteLine("[3] OK: IP=" + S(P(pc, "IPAddress")) + " Name=" + S(P(pc, "PcName")));

        object pcinf = P(pc, "stcPCINF_DATA");
        Array mc = (Array)F(pcinf, "stcMCValue");
        Console.WriteLine("[4] stcMCValue len=" + mc.Length);
        long[] div = new long[idx.Length];
        for (int k = 0; k < idx.Length; k++)
        {
            object m = mc.GetValue(idx[k]); div[k] = Convert.ToInt64(F(m, "lDivVal"));
            Console.WriteLine("    [" + idx[k] + "] " + S(F(m, "sCaption_j")) + "/" + S(F(m, "sCaption")) + " [" + S(F(m, "sUnit")) + "] div=" + div[k] + " fmt=" + S(F(m, "sFormat")) + " iDispNo=" + S(F(m, "iDispNo")));
        }

        Console.WriteLine("[5] 毎秒読取り " + secs + "秒 → " + csv);
        StreamWriter log = new StreamWriter(csv, false, new UTF8Encoding(true));
        log.WriteLine("time,ms,iobit_len,process,plc,ingot_len,proc_len," + string.Join(",", idx.Select(n => "idx" + n).ToArray()));
        List<long> lat = new List<long>(); int fails = 0;
        Process me = Process.GetCurrentProcess();
        for (int i = 0; i < secs; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                object rd = P(pc, "stcRealData"); object info = P(pc, "stcPcInfo");
                int[] io = (int[])F(rd, "intIOBit"); int nio = io == null ? 0 : io.Length;
                string proc = S(F(info, "strPROCESS")).ToUpper();
                string plen = "";
                if (proc == "NECK") plen = S(F(info, "strNECK_LENGTH")); else if (proc == "CROWN") plen = S(F(info, "strCROWN_LENGTH"));
                else if (proc == "SHOULDER") plen = S(F(info, "strSHOULDER_LENGTH")); else if (proc == "BODY") plen = S(F(info, "strBODY_LENGTH"));
                else if (proc == "TAIL") plen = S(F(info, "strTAIL_LENGTH"));
                List<string> vals = new List<string>();
                for (int k = 0; k < idx.Length; k++)
                {
                    int off = 200 + idx[k] * 2;
                    vals.Add(io != null && io.Length >= off + 2 && div[k] > 0 ? ((double)CalcRaw(io[off], io[off + 1]) / div[k]).ToString("0.####") : "");
                }
                long ms = sw.ElapsedMilliseconds; lat.Add(ms);
                string plc = S(F(info, "bStatePlc"));
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + "," + ms + "," + nio + "," + proc + "," + plc + "," + S(F(info, "strINGOT_LENGTH")).Trim() + "," + plen.Trim() + "," + string.Join(",", vals.ToArray());
                log.WriteLine(line); log.Flush();
                me.Refresh();
                Console.WriteLine("  " + line.Replace(",", "  ") + "  mem=" + (me.WorkingSet64 / 1000000) + "MB");
                if (nio != 2100) Console.WriteLine("    WARN intIOBit len=" + nio + " (期待 2100)");
            }
            catch (Exception ex) { fails++; Console.WriteLine("  ERR " + ex.GetType().Name + ": " + ex.Message); log.WriteLine("ERR," + ex.GetType().Name); }
            int rest = 1000 - (int)sw.ElapsedMilliseconds; if (rest > 0) Thread.Sleep(rest);
        }
        log.Close();
        if (lat.Count > 0) { lat.Sort(); Console.WriteLine("[6] reads=" + lat.Count + " errors=" + fails + " latency avg=" + (long)lat.Average() + "ms max=" + lat.Max() + "ms"); }
        else Console.WriteLine("[6] reads=0 errors=" + fails);
        return fails == 0 && lat.Count > 0 ? 0 : 1;
    }
}
