// HostWatch.cs — VB_FERROTEC.exe の実行状態と frmAutoStatus ウィンドウの検出（Win32）
//   ・frmAutoStatus は frmMain の MDI 子フォーム（ClientSize 1126×670。MANUAL TUNE GRAPH / FINE TUNE GRAPH では Width=976）。
//     MDI 子はトップレベルではないので EnumWindows では見えない → VB_FERROTEC のトップレベル窓の子孫を EnumChildWindows で探す。
//     タイトル（Form.Text）一致 → 見つからなければクライアント高さ 670 かつ幅 1126/976 の可視子窓で検出する。
//   ・検出条件は PullerView.ini [Host] process_name / autostatus_title / autostatus_size で変更できる。
//   ・PullerView は frmAutoStatus 表示中のみ表示し、その左上を基準に追従する（単体起動設定 ON のときは未検出でも表示）。
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace PullerView
{
    public class HostWatch
    {
        public string ProcessName = "VB_FERROTEC", Title = "frmAutoStatus"; public Size WantSize = new Size(1126, 670);
        public int AltWidth = 976; public bool ProcessRunning, AutoStatusVisible; public Rectangle AutoStatusRect; public IntPtr AutoStatusHwnd;
        DateTime lastProcCheck = DateTime.MinValue; int[] pids = new int[0];

        public void Configure(Ini ini)
        {
            ProcessName = ini.Get("Host", "process_name", ProcessName); Title = ini.Get("Host", "autostatus_title", Title);
            string[] sz = ini.Get("Host", "autostatus_size", "1126,670").Split(','); int w, h;
            if (sz.Length == 2 && int.TryParse(sz[0].Trim(), out w) && int.TryParse(sz[1].Trim(), out h)) WantSize = new Size(w, h);
            AltWidth = ini.GetInt("Host", "autostatus_alt_width", AltWidth);
        }

        /// <summary>毎秒呼ぶ。状態が変わったら true。</summary>
        public bool Poll()
        {
            bool pr = ProcessRunning, vis = AutoStatusVisible; Rectangle rc = AutoStatusRect;
            try
            {
                if ((DateTime.Now - lastProcCheck).TotalSeconds >= 2)
                {   // プロセス列挙は重いので 2 秒ごと
                    Process[] ps = Process.GetProcessesByName(ProcessName); pids = new int[ps.Length];
                    for (int i = 0; i < ps.Length; i++) { pids[i] = ps[i].Id; ps[i].Dispose(); }
                    lastProcCheck = DateTime.Now;
                }
                ProcessRunning = pids.Length > 0;
                AutoStatusHwnd = IntPtr.Zero; AutoStatusVisible = false;
                if (ProcessRunning && Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    IntPtr bySize = IntPtr.Zero, byTitle = IntPtr.Zero;
                    EnumWindowsProc check = delegate(IntPtr h, IntPtr l)
                    {
                        if (!IsWindowVisible(h)) return true;
                        StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity);
                        if (byTitle == IntPtr.Zero && Title != "" && sb.ToString() == Title) byTitle = h;
                        RECT r; if (bySize == IntPtr.Zero && GetClientRect(h, out r) && r.Bottom - r.Top == WantSize.Height && (r.Right - r.Left == WantSize.Width || r.Right - r.Left == AltWidth)) bySize = h;
                        return true;
                    };
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        if (!IsWindowVisible(h)) return true;
                        uint pid; GetWindowThreadProcessId(h, out pid); if (Array.IndexOf(pids, (int)pid) < 0) return true;
                        check(h, l);                                    // トップレベル自身（単独表示の場合）
                        EnumChildWindows(h, check, IntPtr.Zero);        // MDI 子フォーム（MdiClient の子）
                        return true;
                    }, IntPtr.Zero);
                    IntPtr hw = byTitle != IntPtr.Zero ? byTitle : bySize;
                    if (hw != IntPtr.Zero)
                    {
                        RECT wr; if (GetWindowRect(hw, out wr)) { AutoStatusHwnd = hw; AutoStatusVisible = true; AutoStatusRect = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top); }
                    }
                }
            }
            catch (Exception ex) { Log.Write("HostWatch 例外: " + ex.Message); }
            bool changed = pr != ProcessRunning || vis != AutoStatusVisible || rc != AutoStatusRect;
            if (pr != ProcessRunning) Log.Write(ProcessName + ".exe " + (ProcessRunning ? "検出" : "未検出"));
            if (vis != AutoStatusVisible) Log.Write("frmAutoStatus " + (AutoStatusVisible ? "表示 " + AutoStatusRect : "非表示"));
            return changed;
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    }
}
