// HostWatch.cs — VB_FERROTEC.exe の実行状態と frmAutoStatus ウィンドウの検出（Win32）
//   ・frmAutoStatus は frmMain の MDI 子フォーム（ClientSize 1126×670。MANUAL TUNE GRAPH / FINE TUNE GRAPH では Width=976）。
//     MDI 子はトップレベルではないので EnumWindows では見えない → VB_FERROTEC のトップレベル窓の子孫を EnumChildWindows で探す。
//     タイトル（Form.Text）一致 → 見つからなければクライアント高さ 670 かつ幅 1126/976 の可視子窓で検出する。
//   ・検出条件は PullerView.ini [Host] process_name / autostatus_title / autostatus_size / autostatus_alt_width / autostatus_marker で変更できる。
//   ・PullerView は frmAutoStatus 表示中のみ表示し、その左上を基準に追従する（単体起動設定 ON のときは未検出でも表示）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace PullerView
{
    public class HostWatch
    {
        public string ProcessName = "VB_FERROTEC", Title = "frmAutoStatus"; public Size WantSize = new Size(1126, 670);
        public int AltWidth = 976; public string[] Markers = new string[] { "AUTO STATUS" }; public bool ProcessRunning, AutoStatusVisible; int missCount; DateTime lastMissLog = DateTime.MinValue; string lastActiveInfo; public Rectangle AutoStatusRect; public IntPtr AutoStatusHwnd;
        DateTime lastProcCheck = DateTime.MinValue; int[] pids = new int[0];

        public void Configure(Ini ini)
        {
            ProcessName = ini.Get("Host", "process_name", ProcessName); Title = ini.Get("Host", "autostatus_title", Title);
            string[] sz = ini.Get("Host", "autostatus_size", "1126,670").Split(','); int w, h;
            if (sz.Length == 2 && int.TryParse(sz[0].Trim(), out w) && int.TryParse(sz[1].Trim(), out h)) WantSize = new Size(w, h);
            AltWidth = ini.GetInt("Host", "autostatus_alt_width", AltWidth);
            string mk = ini.Get("Host", "autostatus_marker", "AUTO STATUS"); List<string> ml = new List<string>(); foreach (string s in mk.Split('|')) if (s.Trim() != "") ml.Add(s.Trim()); Markers = ml.ToArray();
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
                if (pids.Length > 0) { ProcessRunning = true; missCount = 0; } else if (++missCount >= 3) ProcessRunning = false;   // 列挙が一時的に空になることがあるため 3 回連続（約 6 秒）で未検出とする
                AutoStatusHwnd = IntPtr.Zero; AutoStatusVisible = false;
                if (ProcessRunning && Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    // frmAutoStatus の同定: タイトル（Form.Text）は空なので使えず、他の画面（frmParameter/frmProfile …）も同じ 1126×670 の MDI 子フォーム。
                    // → 子孫ウィンドウ（Label/Button）の文字列に目印（既定 "AUTO STATUS" = lblTitle.Text）があるかで判定する。
                    IntPtr found = IntPtr.Zero, bySize = IntPtr.Zero; int cand = 0; List<string> seen = new List<string>();
                    EnumWindowsProc check = delegate(IntPtr h, IntPtr l)
                    {
                        if (found != IntPtr.Zero || !IsWindowVisible(h)) return true;
                        if (Title != "" && WinText(h) == Title) { found = h; return false; }
                        RECT r; if (!GetClientRect(h, out r)) return true;
                        int cw = r.Right - r.Left, chh = r.Bottom - r.Top;
                        if (chh != WantSize.Height || (cw != WantSize.Width && cw != AltWidth)) return true;
                        cand++; if (bySize == IntPtr.Zero) bySize = h;
                        if (HasMarker(h, seen)) { found = h; return false; }
                        return true;
                    };
                    // 方式1（優先）: VB_FERROTEC frmMain の MDIClient に WM_MDIGETACTIVE を送り「今アクティブな MDI 子フォーム」を得る
                    //   → frmMain.vb の  With frmAutoStatus : .MdiParent = Me : .Show()  と同じ状態を外部プロセスから判定する。
                    //   アクティブ子のタイトルが autostatus_title（既定 frmAutoStatus。VB 側で Me.Text = "frmAutoStatus" を設定）か、
                    //   子孫コントロールに目印（AUTO STATUS）があれば frmAutoStatus。それ以外の子（frmParameter 等）がアクティブなら非表示扱い。
                    string activeInfo = null; bool mdiSeen = false;
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        if (found != IntPtr.Zero || !IsWindowVisible(h)) return true;
                        uint pid; GetWindowThreadProcessId(h, out pid); if (Array.IndexOf(pids, (int)pid) < 0) return true;
                        IntPtr mdi = FindMdiClient(h);
                        if (mdi != IntPtr.Zero)
                        {
                            mdiSeen = true;
                            IntPtr act = SendMessage(mdi, 0x0229 /*WM_MDIGETACTIVE*/, IntPtr.Zero, IntPtr.Zero);
                            if (act != IntPtr.Zero && IsWindowVisible(act))
                            {
                                string tx = WinText(act); RECT cr; GetClientRect(act, out cr);
                                activeInfo = "title='" + tx + "' client=" + (cr.Right - cr.Left) + "x" + (cr.Bottom - cr.Top);
                                if ((Title != "" && tx == Title) || HasMarker(act, seen)) { found = act; return false; }
                            }
                            else activeInfo = "(MDI 子なし)";
                        }
                        check(h, l);                                    // トップレベル自身（単独表示の場合）
                        if (found == IntPtr.Zero && mdi == IntPtr.Zero) EnumChildWindows(h, check, IntPtr.Zero);   // MDI でない場合の従来探索
                        return true;
                    }, IntPtr.Zero);
                    if (activeInfo != lastActiveInfo) { lastActiveInfo = activeInfo; if (activeInfo != null) Log.Write("MDI アクティブ子フォーム: " + activeInfo + (found != IntPtr.Zero ? " → frmAutoStatus" : " → frmAutoStatus ではない")); }
                    if (found == IntPtr.Zero && bySize != IntPtr.Zero && Markers.Length == 0) found = bySize;   // 目印未設定なら従来どおりサイズ一致
                    if (found != IntPtr.Zero)
                    {
                        RECT wr; if (GetWindowRect(found, out wr)) { AutoStatusHwnd = found; AutoStatusVisible = true; AutoStatusRect = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top); }
                    }
                    else if (vis && cand > 0 && (DateTime.Now - lastMissLog).TotalSeconds > 30)
                    {   // サイズは合うが目印の無い画面 → 別画面と判断。何が見えたかを 30 秒に 1 回だけ記録（目印の調整用）
                        lastMissLog = DateTime.Now; Log.Write("frmAutoStatus 目印 [" + string.Join("|", Markers) + "] 未検出。同サイズ画面 " + cand + " 個の文字列: " + Join(seen, 12));
                    }
                }
            }
            catch (Exception ex) { Log.Write("HostWatch 例外: " + ex.Message); }
            bool changed = pr != ProcessRunning || vis != AutoStatusVisible || rc != AutoStatusRect;
            if (pr != ProcessRunning) Log.Write(ProcessName + ".exe " + (ProcessRunning ? "検出" : "未検出"));
            if (vis != AutoStatusVisible) Log.Write("frmAutoStatus " + (AutoStatusVisible ? "表示 " + AutoStatusRect : "非表示"));
            return changed;
        }

        /// <summary>子孫ウィンドウの文字列（WM_GETTEXT。他プロセスのコントロールでも取得できる）に目印が含まれるか。</summary>
        bool HasMarker(IntPtr form, List<string> seen)
        {
            bool hit = false; int n = 0;
            EnumChildWindows(form, delegate(IntPtr c, IntPtr l)
            {
                if (++n > 400) return false;
                string s = WinText(c); if (s == "") return true;
                if (seen.Count < 40) seen.Add(s);
                foreach (string m in Markers) if (string.Compare(s.Trim(), m, StringComparison.OrdinalIgnoreCase) == 0 || s.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; return false; }
                return true;
            }, IntPtr.Zero);
            return hit;
        }
        /// <summary>トップレベル窓の直下〜子孫からクラス名 "MDIClient" の窓を探す（WinForms の MdiClient は MDIClient クラス）。</summary>
        static IntPtr FindMdiClient(IntPtr top)
        {
            IntPtr r = IntPtr.Zero;
            EnumChildWindows(top, delegate(IntPtr c, IntPtr l) { StringBuilder sb = new StringBuilder(64); GetClassName(c, sb, sb.Capacity); if (sb.ToString() == "MDIClient") { r = c; return false; } return true; }, IntPtr.Zero);
            return r;
        }
        static string WinText(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256); IntPtr res;
            if (SendMessageTimeout(h, 0x000D /*WM_GETTEXT*/, (IntPtr)sb.Capacity, sb, 0x0002 /*SMTO_ABORTIFHUNG*/, 100, out res) == IntPtr.Zero) { sb.Length = 0; GetWindowText(h, sb, sb.Capacity); }
            return sb.ToString();
        }
        static string Join(List<string> l, int max) { StringBuilder sb = new StringBuilder(); for (int i = 0; i < l.Count && i < max; i++) { if (i > 0) sb.Append(" / "); sb.Append(l[i].Length > 20 ? l[i].Substring(0, 20) : l[i]); } return sb.ToString(); }
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    }
}
