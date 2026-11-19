// CvRecv40.exe: CvImageSaver の受信・保存部（.NET Framework 4.0 移植版）の動作確認用コンソール。
using System;
using System.IO;
using System.Threading;
using Ferrotec.CvImageSaver40;

static class Program
{
    static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory, ini = Path.Combine(dir, "CvRecv40.ini"); int seconds = 0;
        for (int i = 0; i < args.Length; i++) { if (args[i] == "--ini" && i + 1 < args.Length) ini = args[++i]; else if (args[i] == "--seconds" && i + 1 < args.Length) seconds = int.Parse(args[++i]); }
        CvSettings s = CvSettings.Load(ini);
        FileLog log = new FileLog(Path.IsPathRooted(s.LogFolder) ? s.LogFolder : Path.Combine(dir, s.LogFolder)); log.Echo = true;
        log.Info("CvRecv40 起動 CLR=" + Environment.Version + " 64bit=" + (IntPtr.Size == 8) + " OS=" + Environment.OSVersion + " ini=" + ini);
        foreach (string e in s.Validate()) { log.Error("設定エラー: " + e); return 2; }
        log.Info("接続先 " + s.Ip + ":" + s.Port + " camera_no=" + (s.CameraNo == 0 ? "全カメラ" : s.CameraNo.ToString()) + " 保存先=" + s.Folder + " max_files=" + s.MaxFiles + " keep_days=" + s.KeepDays);
        ImageStore store = new ImageStore(delegate { return s; }, log, dir); ImageReceiver rx = new ImageReceiver(delegate { return s; }, log, dir);
        rx.FrameReceived += delegate (ImageFrame f) { store.Save(f); };
        bool quit = false; Console.CancelKeyPress += delegate (object o, ConsoleCancelEventArgs e) { e.Cancel = true; quit = true; };
        rx.Start(); DateTime end = seconds > 0 ? DateTime.Now.AddSeconds(seconds) : DateTime.MaxValue;
        while (!quit && DateTime.Now < end) Thread.Sleep(200);
        rx.Stop();
        log.Info("終了 接続回数=" + rx.Connects + " 受信バイト=" + rx.BytesReceived + " 受信画像=" + rx.FramesReceived + " 保存=" + store.Saved + " 保存しない=" + store.Skipped + " 削除=" + store.Deleted);
        return 0;
    }
}
