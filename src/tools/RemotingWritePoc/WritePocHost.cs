// WritePocHost.cs — 「RemotingService 経由で VB_FERROTEC.exe に PLC 書込みをさせられるか」の検証用ホスト（PullerView 本体には組み込まない）
//   PullerView.exe の HostLink（待受・SessionID 返信・clsPcObj 公開）をそのまま使い、クライアント接続から N 秒後に
//   VB_FERROTEC_SERVER／リモート UI と同じ手順で  clsPcObj.stcSendPLC = {device, address, size, data}  → clsPcObj.WriteMessage(eBTN_Execute, ePLC)  を実行する。
//   VB_FERROTEC.exe（または FakeFerrotec.exe --event-proxy）は WriteMsgEvent を受けて stcSendPLC を読み、sub_Plc_Set で PLC に書く。
//   使い方: WritePocHost.exe [秒] [device] [address] [data,…]   例: WritePocHost.exe 6 R 33600 1
//   ※ 実機の VB_FERROTEC.exe に対して実行すると本当に PLC へ書き込まれる。アドレスは必ず安全なものを指定すること。
using System;
using System.Reflection;
using System.Threading;
using PullerView;

class WritePocHost
{
    static void Main(string[] args)
    {
        int after = args.Length > 0 ? int.Parse(args[0]) : 6; string dev = args.Length > 1 ? args[1] : "R", addr = args.Length > 2 ? args[2] : "33600";
        int[] data = args.Length > 3 ? Array.ConvertAll(args[3].Split(','), int.Parse) : new int[] { 1 };
        Log.Line += delegate(string l) { Console.WriteLine(l); };
        HostLink link = new HostLink(); link.Configure(Ini.Load(Ini.InExeDir("PullerView.ini")));
        if (!link.Start()) { Console.WriteLine("NG " + link.LastError); return; }
        Assembly asm = Assembly.LoadFrom(Ini.InExeDir(link.DllPath));
        Type tPc = asm.GetType("VB_FERROTEC_Common.clsPcObj", true), tSend = asm.GetType("VB_FERROTEC_Common.TP_SendPLC", true), tItem = asm.GetType("VB_FERROTEC_Common.TP_SendPLCData", true);
        Type tBtn = asm.GetType("VB_FERROTEC_Common.Enum_ButtonEvent", true), tForm = asm.GetType("VB_FERROTEC_Common.Enum_FormID", true);
        object pc = null; DateTime connectedAt = DateTime.MinValue; bool done = false;
        while (!done)
        {
            Thread.Sleep(500);
            object cur = link.CurrentPcObject;
            if (cur != null && pc == null) { pc = cur; connectedAt = DateTime.Now; Console.WriteLine("接続検出 → " + after + " 秒後に書込み要求を送る"); }
            if (pc != null && (DateTime.Now - connectedAt).TotalSeconds >= after)
            {
                int handlers = 0; try { FieldInfo ev = tPc.BaseType.GetField("WriteMsgEventEvent", BindingFlags.NonPublic | BindingFlags.Instance); Delegate d = ev == null ? null : ev.GetValue(pc) as Delegate; handlers = d == null ? 0 : d.GetInvocationList().Length; } catch (Exception) { }
                Console.WriteLine("WriteMsgEvent のハンドラ数（クライアントが AddHandler 済みか）= " + handlers);
                object item = Activator.CreateInstance(tItem);
                tItem.GetField("strDevice").SetValue(item, dev); tItem.GetField("strAddress").SetValue(item, addr); tItem.GetField("intSize").SetValue(item, data.Length); tItem.GetField("intDatas").SetValue(item, data);
                Array items = Array.CreateInstance(tItem, 1); items.SetValue(item, 0);
                object send = Activator.CreateInstance(tSend); tSend.GetField("stcPLCData").SetValue(send, items); tSend.GetField("strReadAddr").SetValue(send, new string[] { addr });
                tPc.GetProperty("stcSendPLC").SetValue(pc, send, null);
                { object rb = tPc.GetProperty("stcSendPLC").GetValue(pc, null); Console.WriteLine("ローカル読み返し: stcPLCData=" + (tSend.GetField("stcPLCData").GetValue(rb) == null ? "null" : "OK") + " pcType=" + pc.GetType().FullName); }
                Console.WriteLine("stcSendPLC = {" + dev + " " + addr + " size=" + data.Length + " data=[" + string.Join(",", Array.ConvertAll(data, delegate(int x) { return x.ToString(); })) + "]} を設定 → WriteMessage(eBTN_Execute, ePLC) 呼出し");
                DateTime t0 = DateTime.Now;
                try { tPc.GetMethod("WriteMessage").Invoke(pc, new object[] { Enum.ToObject(tBtn, 0), Enum.ToObject(tForm, 3) }); Console.WriteLine("WriteMessage 完了 " + (DateTime.Now - t0).TotalMilliseconds.ToString("0") + "ms（例外なし。クライアント側の受信ログを確認）"); }
                catch (Exception ex) { Console.WriteLine("WriteMessage 例外: " + ex); }
                done = true;
            }
        }
        Thread.Sleep(3000); link.Stop();
    }
}
