// FakeFerrotec.cs — VB_FERROTEC.exe の代わりに PullerView.exe へ接続してテストデータを書き込むツール（装置なしで結合テストするため）
//   手順: (1) PullerView.exe を起動（待受）→ (2) FakeFerrotec.exe を起動
//   動作: SocketPort へ TextID=00 を送って SessionID を受け取り、Remoting "{SessionID}ePcObj" の clsPcObj に
//         stcPCINF_DATA.stcMCValue（除数・項目名）を 1 回、stcRealData.intIOBit / stcPcInfo を毎秒書き込む。
//         工程は NECK→CROWN→SHOULDER→BODY→TAIL→OTHER(AUTO) を --proc-sec 秒ごとに進める。
//   使い方: FakeFerrotec.exe [--host 127.0.0.1] [--socket-port 10031] [--remote-port 10030] [--proc-sec 60] [--dll VB_FERROTEC_Common.dll]
//           [--meta McMeta.ini]  … 実機の McMeta.ini（PullerView が保存したもの）があれば除数・項目名をそこから使う
using System;
using System.Collections;
using System.Collections.Generic;
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
    static string Bcc(string body) { byte[] b = Encoding.ASCII.GetBytes(body); byte x = b[0]; for (int i = 1; i < b.Length; i++) x ^= b[i]; string h = "00" + x.ToString("X"); return h.Substring(h.Length - 2); }
    static byte[] Frame(string id, string data) { string body = id + FROM + TO + "000000" + DateTime.Now.ToString("HHmmss") + WS + data; return Encoding.ASCII.GetBytes(STX + body + Bcc(body) + ETX); }
    static void SetF(object boxed, string name, object v) { boxed.GetType().GetField(name).SetValue(boxed, v); }

    static int Main(string[] args)
    {
        string host = "127.0.0.1", dll = "VB_FERROTEC_Common.dll", meta = "McMeta.ini"; int sport = 10031, rport = 10030, procSec = 60;
        for (int a = 0; a < args.Length; a++)
        {
            string k = args[a];
            if (k == "--host") host = args[++a]; else if (k == "--socket-port") sport = int.Parse(args[++a]); else if (k == "--remote-port") rport = int.Parse(args[++a]);
            else if (k == "--proc-sec") procSec = int.Parse(args[++a]); else if (k == "--dll") dll = args[++a]; else if (k == "--meta") meta = args[++a];
        }
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!File.Exists(dll)) dll = Path.Combine(baseDir, dll);
        if (!File.Exists(meta)) meta = Path.Combine(baseDir, meta);
        Assembly asm = Assembly.LoadFrom(dll);
        Type tPc = asm.GetType("VB_FERROTEC_Common.clsPcObj", true), tReal = asm.GetType("VB_FERROTEC_Common.TP_RealData", true);
        Type tInfo = asm.GetType("VB_FERROTEC_Common.TP_PcInfo", true), tPcinf = asm.GetType("VB_FERROTEC_Common.TP_PCINF_DATA", true), tMc = asm.GetType("VB_FERROTEC_Common.TP_MCINFDAT", true);

        // ---- (1) Socket: TextID=00 → SessionID ----
        TcpClient c = new TcpClient(host, sport); NetworkStream ns = c.GetStream();
        byte[] f = Frame("00", Environment.MachineName); ns.Write(f, 0, f.Length);
        byte[] rb = new byte[1024]; int n = ns.Read(rb, 0, rb.Length); string rep = Encoding.ASCII.GetString(rb, 0, n);
        int s0 = rep.IndexOf(STX); string sid = rep.Substring(s0 + 21, rep.Length - (s0 + 21) - 3);
        Console.WriteLine("SessionID=" + sid);

        // ---- (2) Remoting: clsPcObj プロキシ取得 ----
        BinaryServerFormatterSinkProvider srv = new BinaryServerFormatterSinkProvider(); srv.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;
        Hashtable cp = new Hashtable(); cp["port"] = 0; cp["name"] = "FakeFerrotec";
        ChannelServices.RegisterChannel(new TcpChannel(cp, new BinaryClientFormatterSinkProvider(), srv), false);
        object pc = Activator.GetObject(tPc, "tcp://" + host + ":" + rport + "/" + sid + "ePcObj");

        // ---- (3) stcMCValue（除数・項目名）を 1 回送る（実機では VB_FERROTEC 起動後の初回接続時のみ）----
        Dictionary<int, long> div = new Dictionary<int, long>(); Dictionary<int, string[]> cap = new Dictionary<int, string[]>();
        if (File.Exists(meta))
        {
            string sec = ""; foreach (string raw in File.ReadAllLines(meta, Encoding.UTF8)) { string l = raw.Trim(); if (l.StartsWith("[")) { sec = l.Trim('[', ']'); continue; } int e = l.IndexOf('='); if (e < 0 || !sec.StartsWith("Item")) continue; int ix = int.Parse(sec.Substring(4)); string key = l.Substring(0, e), val = l.Substring(e + 1); if (!cap.ContainsKey(ix)) cap[ix] = new string[] { "", "", "" }; if (key == "div") div[ix] = long.Parse(val); else if (key == "caption") cap[ix][0] = val; else if (key == "caption_j") cap[ix][1] = val; else if (key == "unit") cap[ix][2] = val; }
            Console.WriteLine("McMeta.ini から " + div.Count + " 項目を使用");
        }
        int N = 150; Array mc = Array.CreateInstance(tMc, N);
        for (int i = 0; i < N; i++)
        {
            object m = Activator.CreateInstance(tMc); long d; string[] cc;
            SetF(m, "lDivVal", div.TryGetValue(i, out d) ? d : 10L);
            SetF(m, "sCaption", cap.TryGetValue(i, out cc) ? cc[0] : "ITEM" + i); SetF(m, "sCaption_j", cap.TryGetValue(i, out cc) ? cc[1] : ""); SetF(m, "sUnit", cap.TryGetValue(i, out cc) ? cc[2] : "-");
            SetF(m, "sFormat", "0.0"); SetF(m, "iDispNo", i); mc.SetValue(m, i);
        }
        object pcinf = Activator.CreateInstance(tPcinf); SetF(pcinf, "stcMCValue", mc);
        tPc.GetProperty("stcPCINF_DATA").SetValue(pc, pcinf, null);
        Console.WriteLine("stcMCValue 送信 (" + N + " 項目)");

        // ---- (4) 毎秒 intIOBit / stcPcInfo を書く ----
        string[] procs = { "NECK", "CROWN", "SHOULDER", "BODY", "TAIL", "AUTO" }; int pi = 0; double len = 0; int t = 0; Random rnd = new Random(1);
        Thread hb = new Thread(delegate() { while (true) { Thread.Sleep(14000); try { byte[] h = Frame("00", Environment.MachineName); ns.Write(h, 0, h.Length); } catch (Exception) { return; } } }); hb.IsBackground = true; hb.Start();   // 実機同様 ReConnect_Time ごとに 00 を再送
        while (true)
        {
            t++; if (t % procSec == 0) { pi = (pi + 1) % procs.Length; if (pi == 0) len = 0; }
            if (pi < 5) len += 0.5;
            int[] io = new int[2300];
            Action<int, double> put = delegate(int idx, double v) { long d; long dv = div.TryGetValue(idx, out d) ? d : 10L; int raw = (int)Math.Round(v * dv); io[idx * 2] = raw & 0xFFFF; io[idx * 2 + 1] = (raw >> 16) & 0xFFFF; };
            double dia = 215 + 8 * Math.Sin(t * 0.05) + 2 * (rnd.NextDouble() - 0.5);
            put(65, dia); put(92, 215); put(66, len); put(67, len * 0.2);
            put(21, 1500 + 20 * Math.Sin(t * 0.02)); put(22, 1500); put(23, 100); put(24, 100 + 5 * Math.Sin(t * 0.1)); put(17, 2000); put(18, 2000 + 30 * Math.Sin(t * 0.3)); put(19, 100); put(20, 100 + 3 * Math.Sin(t * 0.2)); put(27, 1400); put(28, 1400 + 10 * Math.Sin(t * 0.07)); put(31, 5);
            put(115, Math.Min(len, 300)); put(116, Math.Max(0, Math.Min(len - 300, 100))); put(117, Math.Max(0, Math.Min(len - 400, 50))); put(118, Math.Max(0, Math.Min(len - 450, 1500))); put(119, Math.Max(0, len - 1950));
            object real = Activator.CreateInstance(tReal); SetF(real, "intIOBit", io); tPc.GetProperty("stcRealData").SetValue(pc, real, null);
            object info = Activator.CreateInstance(tInfo); SetF(info, "strPROCESS", procs[pi]); SetF(info, "bStatePlc", true); SetF(info, "strINGOT_LENGTH", len.ToString("0.0")); SetF(info, "strINGOT_WIDTH", dia.ToString("0.0"));
            tPc.GetProperty("stcPcInfo").SetValue(pc, info, null);
            if (t % 10 == 1) Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " proc=" + procs[pi] + " len=" + len.ToString("0.0") + " dia=" + dia.ToString("0.0"));
            Thread.Sleep(1000);
        }
    }
}
