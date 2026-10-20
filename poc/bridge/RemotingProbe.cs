// PullerView PoC-B: C# ブリッジ（pythonnet で問題が出た場合の代替・切り分け用）
// ビルド: build.bat（Windows 10 同梱の .NET Framework 4.8 csc.exe を使用。Visual Studio 不要）
// 実行  : RemotingProbe.exe <HostIP> <RemotePort> <自PC IP> <秒数> [index,...]
// 出力  : 1秒ごとに 1 行の JSON（標準出力）。Getter のみ使用。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading;
using VB_FERROTEC_Common;

static class RemotingProbe
{
    static int CalcRaw(int lower, int upper) { return (int)(((uint)(upper & 0xFFFF) << 16) | (uint)(lower & 0xFFFF)); }

    static int Main(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: RemotingProbe <HostIP> <RemotePort> <MyIP> <Seconds> [idx,...]"); return 2; }
        string host = args[0]; int port = int.Parse(args[1]); string myIp = args[2]; int secs = int.Parse(args[3]);
        int[] idx = (args.Length > 4 ? args[4] : "65,92,115,116,117,118,119").Split(',').Select(int.Parse).ToArray();

        var props = new Hashtable { { "port", 0 }, { "name", "PullerViewProbe" } };
        var srv = new BinaryServerFormatterSinkProvider { TypeFilterLevel = TypeFilterLevel.Full };
        ChannelServices.RegisterChannel(new TcpChannel(props, new BinaryClientFormatterSinkProvider(), srv), false);

        string url = "tcp://" + host + ":" + port + "/" + Enum_FormID.eServer;
        var server = (clsServerObj)Activator.GetObject(typeof(clsServerObj), url);
        Console.Error.WriteLine("Server=" + server.ServerName + " ConnectPc=" + server.ConnectPc.Count);
        clsPcObj pc = server.GetPcObject(myIp);
        if (pc == null) foreach (clsPcObj p in server.ConnectPc) if (Convert.ToString(p.IPAddress).Contains(myIp)) { pc = p; break; }
        if (pc == null) { Console.Error.WriteLine("NG: clsPcObj for " + myIp + " not found"); return 3; }

        TP_MCINFDAT[] mc = pc.stcPCINF_DATA.stcMCValue;
        foreach (int n in idx) Console.Error.WriteLine("[" + n + "] " + mc[n].sCaption_j + " [" + mc[n].sUnit + "] div=" + mc[n].lDivVal + " fmt=" + mc[n].sFormat);

        for (int i = 0; i < secs; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                TP_RealData rd = pc.stcRealData; TP_PcInfo info = pc.stcPcInfo;
                int[] io = rd.intIOBit; int n_io = io == null ? 0 : io.Length;
                var sb = new StringBuilder();
                sb.Append("{\"t\":\"").Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("\",\"io\":").Append(n_io)
                  .Append(",\"proc\":\"").Append(info.strPROCESS).Append("\",\"plc\":").Append(info.bStatePlc ? "true" : "false")
                  .Append(",\"ingot\":\"").Append(info.strINGOT_LENGTH).Append("\"");
                foreach (int n in idx)
                {
                    int off = 200 + n * 2; string v = "null";
                    if (io != null && io.Length >= off + 2 && mc[n].lDivVal > 0)
                        v = ((double)CalcRaw(io[off], io[off + 1]) / mc[n].lDivVal).ToString("0.####");
                    sb.Append(",\"i").Append(n).Append("\":").Append(v);
                }
                sb.Append(",\"ms\":").Append(sw.ElapsedMilliseconds).Append("}");
                Console.WriteLine(sb.ToString());
            }
            catch (Exception ex) { Console.WriteLine("{\"error\":\"" + ex.GetType().Name + ": " + ex.Message.Replace("\"", "'") + "\"}"); }
            int rest = 1000 - (int)sw.ElapsedMilliseconds; if (rest > 0) Thread.Sleep(rest);
        }
        return 0;
    }
}
