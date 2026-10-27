// Program.cs — PullerView エントリポイント
//   PullerView.exe            : 通常起動。VB_FERROTEC.exe からの接続を待ち、実データを表示（設定 standalone=1 なら未接続でも表示）
//   PullerView.exe --sim      : 模擬データ（操作パネル＋frmAutoStatus ダミー）。VB_FERROTEC 無しで画面確認
//   PullerView.exe --sim-standalone : 模擬データで PullerView のみ表示
//   PullerView.exe --render out.png [opacity] [logo] : 描画検証用 PNG 出力
//   PullerView.exe --headless [秒] : 画面なしで待受・受信のみ（接続テスト／現地診断用。標準出力に状態を 1 行/秒）
//   --no-layered : 透過方式を使わない（描画トラブル切り分け用）
// 設定ファイル（すべて EXE と同じフォルダ）: DataSetting.ini / PullerView.ini / McMeta.ini、ログ: PullerView.log / PullerView_crash.log
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PullerView
{
    static class Program
    {
        static string crashLog = Ini.InExeDir("PullerView_crash.log");
        static void Crash(object ex)
        {
            string msg = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " OS=" + Environment.OSVersion + " 64bit=" + (IntPtr.Size == 8) + " CLR=" + Environment.Version + "\r\n" + ex + "\r\n\r\n";
            try { File.AppendAllText(crashLog, msg); } catch (Exception) { }
            Log.Write("致命的エラー: " + ex);
            try { MessageBox.Show("PullerView でエラーが発生しました。\n\n" + ex + "\n\n記録先: " + crashLog, "PullerView エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch (Exception) { }
        }

        [STAThread] static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Crash(e.ExceptionObject); };
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { Crash(e.Exception); };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Log.File = Ini.InExeDir("PullerView.log");
            try { FileInfo fi = new FileInfo(Log.File); if (fi.Exists && fi.Length > 5 * 1024 * 1024) { File.Copy(Log.File, Log.File + ".1", true); File.Delete(Log.File); } } catch (Exception) { }
            Log.Write("==== PullerView 起動 args=[" + string.Join(" ", args) + "] OS=" + Environment.OSVersion + " CLR=" + Environment.Version + " 64bit=" + (IntPtr.Size == 8) + " dir=" + Ini.ExeDir);
            // 二重起動防止（1 プロセスのみ）: 既に起動していれば既存の PullerView を前面にして終了
            bool first; mutex = new System.Threading.Mutex(true, "Local\\PullerView_SingleInstance_7F3A1C2E", out first);
            if (!first)
            {
                Log.Write("二重起動のため終了（既存の PullerView を前面へ）");
                try { foreach (Process p in Process.GetProcessesByName("PullerView")) { if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(p.MainWindowHandle); p.Dispose(); } } catch (Exception) { }
                int lang = 0; try { lang = Ini.Load(Settings.IniFile).GetInt("Window", "lang", 0); } catch (Exception) { }
                MessageBox.Show(lang == 1 ? "PullerView is already running." : lang == 2 ? "PullerView 已在运行。" : "PullerView は既に起動しています。", "PullerView", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { MainBody(args); } catch (Exception ex) { Crash(ex); }
            Log.Write("==== PullerView 終了");
            try { mutex.ReleaseMutex(); mutex.Close(); } catch (Exception) { }
            // .NET Framework 4.0 RTM（clr.dll 4.0.30319.1）では、ここから先の CLR シャットダウン中に clr.dll で APPCRASH(c0000005) が起きる
            // （実機で再現。ログは「終了完了」まで出ている）。ソケット・ファイルは全て閉じ済みなので、CLR のシャットダウン処理を通さず即時終了する。
            try { TerminateProcess(GetCurrentProcess(), 0); } catch (Exception) { }
        }
        static System.Threading.Mutex mutex;
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        static void MainBody(string[] args)
        {
            string mode = args.Length >= 1 ? args[0] : "";
            if (mode == "--headless") { RunHeadless(args.Length >= 2 ? int.Parse(args[1]) : 0); return; }   // 画面を使わないので WinForms 初期化前に分岐
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (mode == "--logos" && args.Length >= 2) { using (Bitmap b = Logo.Sheet()) b.Save(args[1]); return; }
            if (mode == "--render" && args.Length >= 2) { Render(args); return; }
            if (mode == "--render-settings" && args.Length >= 2) { RenderSettings(args); return; }
            if (mode == "--sim") { M.Sim = true; M.LoadIni(Ini.InExeDir("DataSetting.ini")); Application.Run(new ControlPanel()); return; }
            if (mode == "--sim-standalone" || mode == "--standalone" || mode == "-s") { M.Sim = true; RunSimStandalone(); return; }
            RunLive();
        }

        // ===== 通常起動: VB_FERROTEC.exe からの実データ =====
        static void RunLive()
        {
            M.LoadIni(Ini.InExeDir("DataSetting.ini"));
            McMeta.Load();
            Ini ini = Ini.Load(Settings.IniFile);
            HostLink link = new HostLink(); link.Configure(ini);
            HostWatch watch = new HostWatch(); watch.Configure(ini);
            M.Running = false; M.AutoStatusShown = false; M.Comm = false;

            PullerViewForm pv = new PullerViewForm(); pv.OffX = M.S.PosX; pv.OffY = M.S.PosY;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea; pv.Location = new Point(wa.Left + pv.OffX, wa.Top + pv.OffY);
            Action openSettings = delegate { using (SettingsForm f = new SettingsForm()) { f.Location = SettingsForm.PlaceNear(pv); f.ShowDialog(pv); } M.Fire(); };
            pv.Surf.GearClicked += openSettings; pv.SettingsRequested += openSettings;
            M.Changed += delegate { pv.ApplyState(); };

            // 読取りスレッド → UI スレッドへ
            link.Sample += delegate(HostSample sm) { try { pv.BeginInvoke(new Action<HostSample>(M.Feed), sm); } catch (Exception) { } };
            link.StateChanged += delegate { try { pv.BeginInvoke(new Action(delegate { M.HostInfo = link.Connected ? (link.DataFresh ? Lx.T("接続: ", "Connected: ", "已连接: ") : Lx.T("接続（データ更新なし）: ", "Connected (no data update): ", "已连接（无数据更新）: ")) + link.SessionInfo : link.Listening ? Lx.T("待受中", "Listening", "等待连接") + " (port " + link.SocketPort + "/" + link.RemotePort + ")" : link.LastError; M.Fire(); })); } catch (Exception) { } };

            if (!link.Start())
            {
                M.HostInfo = link.LastError;
                MessageBox.Show("VB_FERROTEC.exe からの接続を待ち受けできません。\n\n" + link.LastError + "\n\nPullerView.ini の [Host] remote_port / socket_port を確認してください。", "PullerView", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else M.HostInfo = Lx.T("待受中", "Listening", "等待连接") + " (port " + link.SocketPort + "/" + link.RemotePort + ")";

            Timer tm = new Timer(); tm.Interval = 1000; DateTime lastAbove = DateTime.MinValue; string lastAboveInfo = null;
            WinEventTrigger evt = null; bool inTick = false;
            // 監視処理（1 秒周期とイベント即時判定の両方から呼ばれる。再入防止）
            Action tick = delegate
            {
                if (inTick) return; inTick = true;
                try
                {
                    bool ch = watch.Poll();
                    M.Running = watch.ProcessRunning; M.AutoStatusShown = watch.AutoStatusVisible; M.TrackHost();
                    if (watch.AutoStatusVisible) { pv.AnchorRect = watch.AutoStatusRect; pv.FollowAnchor(); }
                    M.Watch(link.Connected && link.DataFresh);
                    if (ch) M.Fire();
                    if (M.Running) pv.EnsureTopMost();   // VB_FERROTEC の画面切替で TopMost 窓の下に隠れたら最前面へ戻す
                    // 診断: frmAutoStatus 表示中に、その上に可視窓があれば 10 秒ごと（内容が変わった時のみ）に記録 → 「連動しない」時に何が覆っているかを特定するため
                    if (watch.AutoStatusVisible && (DateTime.Now - lastAbove).TotalSeconds >= 10)
                    {
                        lastAbove = DateTime.Now; string a = watch.AboveList();
                        if (a != lastAboveInfo) { lastAboveInfo = a; Log.Write("frmAutoStatus の上にある可視窓: " + a); }
                    }
                }
                finally { inTick = false; }
            };
            tm.Tick += delegate { tick(); };
            tm.Start();
            // イベント駆動の即時判定: VB_FERROTEC の窓の SHOW/HIDE/CREATE/DESTROY/REORDER/LOCATIONCHANGE/FOREGROUND を受けたら event_delay_ms 後に 1 回判定
            if (watch.EventDelayMs > 0)
            {
                WinEventTrigger[] box = new WinEventTrigger[1];
                box[0] = new WinEventTrigger(delegate { return watch.Pids; }, delegate
                {
                    bool before = watch.AutoStatusVisible; tick();
                    if (watch.AutoStatusVisible != before) Log.Write("即時判定（イベント " + box[0].LastEventName + "）で状態変化を検出");
                }, watch.EventDelayMs);
                evt = box[0];
            }
            // 終了順序: 監視タイマー停止 → 受信イベント解除 → 待受／Remoting 停止（スレッド終了待ち）→ CSV・ログを閉じる → フォーム破棄
            // （.NET 4.0 CLR はバックグラウンドのソケット／Remoting スレッドが動いたまま終了すると clr.dll で APPCRASH することがある）
            pv.FormClosing += delegate(object sndr, FormClosingEventArgs e)
            {
                if (e.Cancel) return;
                try { tm.Stop(); tm.Dispose(); } catch (Exception) { }
                try { if (evt != null) evt.Dispose(); } catch (Exception) { }
                try { link.Stop(); } catch (Exception ex) { Log.Write("終了処理(link.Stop)で例外: " + ex.Message); }
                try { DataLog.Close(); } catch (Exception) { }
                Log.Write("終了: ウィンドウを閉じます");
            };
            pv.Show(); pv.ApplyState();
            Log.Write("表示開始 layered=" + pv.IsLayered + " standalone=" + M.S.Standalone + " pos=(" + pv.OffX + "," + pv.OffY + ")");
            Application.Run(pv);
            try { link.Stop(); DataLog.Close(); } catch (Exception) { }
            try { pv.Dispose(); } catch (Exception) { }
            Log.Write("終了完了"); Log.Close();
            GC.Collect(); GC.WaitForPendingFinalizers();   // ファイナライザ（GDI/ソケット）をここで片付けてから CLR のシャットダウンへ
        }

        // ===== 画面なし: 待受＋受信のみ（接続テスト／現地診断）=====
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AllocConsole();
        static void RunHeadless(int seconds)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) { try { AllocConsole(); } catch (Exception) { } }   // WinExe なのでコンソールを明示的に開く
            M.LoadIni(Ini.InExeDir("DataSetting.ini")); McMeta.Load();
            Ini ini = Ini.Load(Settings.IniFile);
            HostLink link = new HostLink(); link.Configure(ini);
            Log.Line += delegate(string l) { Console.WriteLine(l); };
            M.Running = true; M.AutoStatusShown = true; M.Comm = false;
            object lk = new object();
            link.Sample += delegate(HostSample sm) { lock (lk) { M.Feed(sm); } };
            if (!link.Start()) { Console.WriteLine("NG " + link.LastError); return; }
            DateTime end = seconds > 0 ? DateTime.Now.AddSeconds(seconds) : DateTime.MaxValue; int lastReads = 0;
            while (DateTime.Now < end)
            {
                System.Threading.Thread.Sleep(1000);
                lock (lk) { M.Watch(link.Connected && link.DataFresh); }
                HostSample sm = link.Last;
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [STAT] conn=" + (link.Connected ? 1 : 0) + " comm=" + (M.Comm ? 1 : 0) + " reads=" + link.Reads + " (+" + (link.Reads - lastReads) + ") fails=" + link.Fails
                    + " fresh=" + (link.DataFresh ? 1 : 0) + " stale=" + link.Stale + " bad=" + link.BadFrames + " proc=" + (sm == null ? "-" : sm.Process) + " len=" + (sm == null ? "-" : sm.IngotLen.ToString("0.0")) + " sess=" + link.SessionInfo
                    + " mem=" + (GC.GetTotalMemory(false) / 1048576) + "MB thr=" + System.Diagnostics.Process.GetCurrentProcess().Threads.Count);
                lastReads = link.Reads;
            }
            link.Stop(); DataLog.Close();
        }

        // ===== 模擬データで PullerView のみ表示 =====
        static void RunSimStandalone()
        {
            M.LoadIni(Ini.InExeDir("DataSetting.ini")); M.AutoAdvance = true; M.ProcSec = 60;
            PullerViewForm pv = new PullerViewForm();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea; pv.Location = new Point(wa.Left + M.S.PosX, wa.Top + M.S.PosY);
            Action openSettings = delegate { using (SettingsForm f = new SettingsForm()) { f.Location = SettingsForm.PlaceNear(pv); f.ShowDialog(pv); } M.Fire(); };
            pv.Surf.GearClicked += openSettings; pv.SettingsRequested += openSettings;
            M.Changed += delegate { pv.ApplyState(); };
            Timer tm = new Timer(); tm.Interval = 1000; tm.Tick += delegate { M.Tick(); }; tm.Start();
            pv.Show(); pv.ApplyState();
            Application.Run(pv);
        }

        // ===== 描画検証: 設定ウィンドウを PNG 出力（PVMOCK_LANG=0/1/2, PVMOCK_TAB=0/1）=====
        static void RenderSettings(string[] args)
        {
            M.Sim = true; M.LoadIni(Ini.InExeDir("DataSetting.ini")); McMeta.Load();
            string lg = Environment.GetEnvironmentVariable("PVMOCK_LANG"); if (lg != null) M.S.Lang = int.Parse(lg);
            M.SetProc(8, "test"); for (int i = 0; i < 40; i++) M.Tick();
            using (SettingsForm f = new SettingsForm())
            {
                string tb = Environment.GetEnvironmentVariable("PVMOCK_TAB"); if (tb == "0") f.GetType().GetMethod("SetTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, new object[] { 0 });
                f.Show(); Application.DoEvents();
                string hold = Environment.GetEnvironmentVariable("PVMOCK_HOLD"); if (hold != null) { DateTime end = DateTime.Now.AddSeconds(int.Parse(hold)); while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(50); } }
                using (Bitmap b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(args[1]); }
                f.Close();
            }
        }
        // ===== 描画検証: 透過ビットマップを frmAutoStatus 画像に合成して PNG 出力 =====
        static void Render(string[] args)
        {
            M.Sim = true; M.LoadIni(Ini.InExeDir("DataSetting.ini"));
            string tst = Environment.GetEnvironmentVariable("PVMOCK_TEST"); if (tst != null) { string[] a = tst.Split(','); M.SetProc(int.Parse(a[0]), "test"); for (int i = 1; i < a.Length && i <= 4; i++) M.Inject[i - 1] = int.Parse(a[i]); }
            if (args.Length >= 3) M.S.OpacityPct = int.Parse(args[2]); if (args.Length >= 4) M.S.Logo = int.Parse(args[3]);
            for (int i = 0; i < 40; i++) M.Tick();
            if (Environment.GetEnvironmentVariable("PVMOCK_MIN") == "1") M.Maximized = false;
            if (Environment.GetEnvironmentVariable("PVMOCK_ICON") == "1") M.IconMode = true;
            if (Environment.GetEnvironmentVariable("PVMOCK_MANUAL") == "1") M.S.ProcAuto = false;
            PullerViewForm pv = new PullerViewForm();
            using (Bitmap bg = new Bitmap(Image.FromStream(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PullerView.Resources.frmAutoStatus.png")), 1280, 1024))
            using (Bitmap ov = pv.RenderBitmap()) using (Graphics g = Graphics.FromImage(bg)) { g.DrawImage(ov, pv.OffX, pv.OffY); bg.Save(args[1]); }
        }
    }
}
