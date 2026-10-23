// Program.cs — PullerView エントリポイント
//   PullerView.exe            : 通常起動。VB_FERROTEC.exe からの接続を待ち、実データを表示（設定 standalone=1 なら未接続でも表示）
//   PullerView.exe --sim      : 模擬データ（操作パネル＋frmAutoStatus ダミー）。VB_FERROTEC 無しで画面確認
//   PullerView.exe --sim-standalone : 模擬データで PullerView のみ表示
//   PullerView.exe --render out.png [opacity] [logo] : 描画検証用 PNG 出力
//   --no-layered : 透過方式を使わない（描画トラブル切り分け用）
// 設定ファイル（すべて EXE と同じフォルダ）: DataSetting.ini / PullerView.ini / McMeta.ini、ログ: PullerView.log / PullerView_crash.log
using System;
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
            try { MainBody(args); } catch (Exception ex) { Crash(ex); }
            Log.Write("==== PullerView 終了");
        }

        static void MainBody(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string mode = args.Length >= 1 ? args[0] : "";
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
            link.StateChanged += delegate { try { pv.BeginInvoke(new Action(delegate { M.HostInfo = link.Connected ? "接続: " + link.SessionInfo : link.Listening ? "待受中 (port " + link.SocketPort + "/" + link.RemotePort + ")" : link.LastError; M.Fire(); })); } catch (Exception) { } };

            if (!link.Start())
            {
                M.HostInfo = link.LastError;
                MessageBox.Show("VB_FERROTEC.exe からの接続を待ち受けできません。\n\n" + link.LastError + "\n\nPullerView.ini の [Host] remote_port / socket_port を確認してください。", "PullerView", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else M.HostInfo = "待受中 (port " + link.SocketPort + "/" + link.RemotePort + ")";

            Timer tm = new Timer(); tm.Interval = 1000;
            tm.Tick += delegate
            {
                bool ch = watch.Poll();
                M.Running = watch.ProcessRunning; M.AutoStatusShown = watch.AutoStatusVisible; M.TrackHost();
                if (watch.AutoStatusVisible) { pv.AnchorRect = watch.AutoStatusRect; pv.FollowAnchor(); }
                M.Watch(link.Connected);
                if (ch) M.Fire();
            };
            tm.Start();
            pv.Show(); pv.ApplyState();
            Log.Write("表示開始 layered=" + pv.IsLayered + " standalone=" + M.S.Standalone + " pos=(" + pv.OffX + "," + pv.OffY + ")");
            Application.Run(pv);
            link.Stop();
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
