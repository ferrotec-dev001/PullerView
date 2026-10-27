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
        public int AltWidth = 976; public string[] Markers = new string[] { "AUTO STATUS", "自動状態", "自动状态" }; public string[] ChildSizes = new string[0]; /* 既定は無効（実機の frmAutoStatus は直下が 976x670 のパネル 1 枚で Designer の寸法では同定できない） */ public bool ProcessRunning, AutoStatusVisible; int missCount; DateTime lastMissLog = DateTime.MinValue; string lastActiveInfo; string coveredInfo; string matchedBy = "", lastMarker = ""; DateTime lastNoMdiLog = DateTime.MinValue; public Rectangle AutoStatusRect; public IntPtr AutoStatusHwnd;
        DateTime lastProcCheck = DateTime.MinValue; int[] pids = new int[0];

        public void Configure(Ini ini)
        {
            ProcessName = ini.Get("Host", "process_name", ProcessName); Title = ini.Get("Host", "autostatus_title", Title);
            string[] sz = ini.Get("Host", "autostatus_size", "1126,670").Split(','); int w, h;
            if (sz.Length == 2 && int.TryParse(sz[0].Trim(), out w) && int.TryParse(sz[1].Trim(), out h)) WantSize = new Size(w, h);
            AltWidth = ini.GetInt("Host", "autostatus_alt_width", AltWidth);
            string cs = ini.Get("Host", "autostatus_children", ""); List<string> cl = new List<string>(); foreach (string s in cs.Split('|')) if (s.Trim() != "") cl.Add(s.Trim().ToLower()); ChildSizes = cl.ToArray();
            string mk = ini.Get("Host", "autostatus_marker", "AUTO STATUS|自動状態|自动状态"); List<string> ml = new List<string>(); foreach (string s in mk.Split('|')) if (s.Trim() != "") ml.Add(s.Trim()); Markers = ml.ToArray();
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
                    IntPtr found = IntPtr.Zero, bySize = IntPtr.Zero; int cand = 0; List<string> seen = new List<string>(); matchedBy = "";
                    EnumWindowsProc check = delegate(IntPtr h, IntPtr l)
                    {
                        if (found != IntPtr.Zero || !IsWindowVisible(h)) return true;
                        if (Title != "" && WinText(h) == Title) { found = h; matchedBy = "タイトル"; return false; }
                        RECT r; if (!GetClientRect(h, out r)) return true;
                        int cw = r.Right - r.Left, chh = r.Bottom - r.Top;
                        if (chh != WantSize.Height || (cw != WantSize.Width && cw != AltWidth)) return true;
                        cand++; if (bySize == IntPtr.Zero) bySize = h;
                        if (HasMarker(h, seen)) { found = h; matchedBy = "目印(" + lastMarker + ")"; return false; }
                        if (HasChildSizes(h)) { found = h; matchedBy = "直下寸法"; return false; }
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
                            IntPtr act, res0; if (SendMessageTimeoutP(mdi, 0x0229 /*WM_MDIGETACTIVE*/, IntPtr.Zero, IntPtr.Zero, 0x0002, 200, out res0) == IntPtr.Zero) res0 = IntPtr.Zero; act = res0;
                            if (act != IntPtr.Zero && IsWindowVisible(act))
                            {
                                string tx = WinText(act); RECT cr; GetClientRect(act, out cr); List<string> texts = new List<string>();
                                bool mk = HasMarker(act, texts) || HasChildSizes(act);   // 文字列が読めない場合は直下コントロールの寸法（pnlSeaquence 102x628 等）で同定
                                activeInfo = "title='" + tx + "' client=" + (cr.Right - cr.Left) + "x" + (cr.Bottom - cr.Top) + " 直下寸法[" + ChildSizeList(act) + "] 文字列[" + Join(texts, 16) + "]";
                                if (Title != "" && tx == Title) { found = act; matchedBy = "タイトル"; return false; }
                                if (mk) { found = act; matchedBy = "目印/寸法"; return false; }
                            }
                            else activeInfo = "(MDI 子なし)";
                        }
                        check(h, l);                                    // トップレベル自身（単独表示の場合）
                        if (found == IntPtr.Zero && mdi == IntPtr.Zero) EnumChildWindows(h, check, IntPtr.Zero);   // MDI でない場合の従来探索
                        return true;
                    }, IntPtr.Zero);
                    if (!mdiSeen && found == IntPtr.Zero && (DateTime.Now - lastNoMdiLog).TotalSeconds > 30)
                    {
                        lastNoMdiLog = DateTime.Now; List<string> tops = new List<string>();
                        EnumWindows(delegate(IntPtr h, IntPtr l) { uint pid; GetWindowThreadProcessId(h, out pid); if (Array.IndexOf(pids, (int)pid) < 0) return true; RECT wr; GetWindowRect(h, out wr); StringBuilder cn = new StringBuilder(64); GetClassName(h, cn, cn.Capacity); tops.Add((IsWindowVisible(h) ? "" : "(非表示)") + "'" + WinText(h) + "' " + cn + " " + (wr.Right - wr.Left) + "x" + (wr.Bottom - wr.Top)); return tops.Count < 8; }, IntPtr.Zero);
                        Log.Write("MDIClient が見つかりません。" + ProcessName + " のトップレベル窓: " + string.Join(" / ", tops.ToArray()));
                    }
                    if (activeInfo != lastActiveInfo) { lastActiveInfo = activeInfo; if (activeInfo != null) Log.Write("MDI アクティブ子フォーム: " + activeInfo + (found != IntPtr.Zero ? " → frmAutoStatus" : " → frmAutoStatus ではない")); }
                    if (found == IntPtr.Zero && bySize != IntPtr.Zero && Markers.Length == 0) found = bySize;   // 目印未設定なら従来どおりサイズ一致
                    if (found != IntPtr.Zero)
                    {
                        RECT wr; if (GetWindowRect(found, out wr))
                        {
                            Rectangle ar = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
                            string cov = CoveredBy(found, ar);   // frmAutoStatus は存在するが、VB_FERROTEC の別画面（兄弟子窓／TopMost ダイアログ）に覆われていれば「非表示」扱い
                            if (cov == null) { AutoStatusHwnd = found; AutoStatusVisible = true; AutoStatusRect = ar; coveredInfo = null; }
                            else if (cov != coveredInfo) { coveredInfo = cov; Log.Write("frmAutoStatus は存在するが他画面に覆われている: " + cov); }
                        }
                    }
                    else if (!mdiSeen && cand > 0 && (DateTime.Now - lastMissLog).TotalSeconds > 30)
                    {   // サイズは合うが目印の無い画面 → 別画面と判断。何が見えたかを 30 秒に 1 回だけ記録（目印の調整用）
                        lastMissLog = DateTime.Now; Log.Write("frmAutoStatus 目印 [" + string.Join("|", Markers) + "] 未検出。同サイズ画面 " + cand + " 個の文字列: " + Join(seen, 12));
                    }
                }
            }
            catch (Exception ex) { Log.Write("HostWatch 例外: " + ex.Message); }
            bool changed = pr != ProcessRunning || vis != AutoStatusVisible || rc != AutoStatusRect;
            if (pr != ProcessRunning) Log.Write(ProcessName + ".exe " + (ProcessRunning ? "検出" : "未検出"));
            if (vis != AutoStatusVisible) Log.Write("frmAutoStatus " + (AutoStatusVisible ? "表示 " + AutoStatusRect + " 同定=" + matchedBy + " 直下寸法[" + ChildSizeList(AutoStatusHwnd) + "]" : "非表示"));
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
                string sn = Norm(s);   // 言語切替（Indication.csv）で "Auto Status" / "自動状態" / "自动状态" に入れ替わり、"状 态" のように文字間に空白が入ることがある → 空白除去・大文字小文字無視で比較
                foreach (string m in Markers) if (sn.IndexOf(Norm(m), StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; lastMarker = s.Trim(); return false; }
                return true;
            }, IntPtr.Zero);
            return hit;
        }
        /// <summary>トップレベル窓の直下〜子孫からクラス名 "MDIClient" の窓を探す（WinForms の MdiClient は MDIClient クラス）。</summary>
        static IntPtr FindMdiClient(IntPtr top)
        {
            IntPtr r = IntPtr.Zero;
            EnumChildWindows(top, delegate(IntPtr c, IntPtr l) { StringBuilder sb = new StringBuilder(64); GetClassName(c, sb, sb.Capacity); if (string.Compare(sb.ToString(), "MDICLIENT", StringComparison.OrdinalIgnoreCase) == 0) { r = c; return false; } return true; }, IntPtr.Zero);
            return r;
        }
        /// <summary>直下の子ウィンドウの寸法が autostatus_children（既定: pnlSeaquence 102x628 / pnlStatus 1030x622 / lblTitle 1126x42 / btnDisPlay 109x42）のうち 2 つ以上一致するか。</summary>
        bool HasChildSizes(IntPtr form)
        {
            if (ChildSizes.Length == 0) return false; int hit = 0; List<string> matched = new List<string>();
            for (IntPtr c = GetWindow(form, 5 /*GW_CHILD*/); c != IntPtr.Zero; c = GetWindow(c, 2 /*GW_HWNDNEXT*/))
            {
                RECT r; if (!GetWindowRect(c, out r)) continue; string sz = (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
                foreach (string want in ChildSizes) if (sz == want && !matched.Contains(sz)) { matched.Add(sz); hit++; }
            }
            return hit >= 2;
        }
        /// <summary>frmAutoStatus を覆っている VB_FERROTEC の窓を探す。(1) 同じ親の中で Z 順が上の可視兄弟窓 (2) 親の兄弟 (3) VB_FERROTEC の可視トップレベル窓（ダイアログ等）
        /// のうち、frmAutoStatus の矩形と 30% 以上重なるもの。見つかれば説明文字列、無ければ null。</summary>
        string CoveredBy(IntPtr auto, Rectangle ar)
        {
            double need = ar.Width * (double)ar.Height * 0.3; if (need <= 0) return null;
            IntPtr root = GetAncestor(auto, 2 /*GA_ROOT*/);
            // (1)(2) 兄弟窓（自分の階層と親の階層）
            IntPtr lvl = auto;
            for (int depth = 0; depth < 2 && lvl != IntPtr.Zero && lvl != root; depth++)
            {
                for (IntPtr h = GetWindow(lvl, 3 /*GW_HWNDPREV*/); h != IntPtr.Zero; h = GetWindow(h, 3))
                {
                    if (!IsWindowVisible(h)) continue; RECT r; if (!GetWindowRect(h, out r)) continue;
                    Rectangle rr = Rectangle.Intersect(ar, new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                    if (rr.Width * (double)rr.Height >= need) return "子窓 '" + WinText(h) + "' " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
                }
                lvl = GetParent(lvl);
            }
            // (3) VB_FERROTEC のトップレベル窓で root より Z 順が上のもの
            for (IntPtr h = GetWindow(root, 3); h != IntPtr.Zero; h = GetWindow(h, 3))
            {
                if (!IsWindowVisible(h)) continue; uint pid; GetWindowThreadProcessId(h, out pid); if (Array.IndexOf(pids, (int)pid) < 0) continue;
                RECT r; if (!GetWindowRect(h, out r)) continue;
                Rectangle rr = Rectangle.Intersect(ar, new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                if (rr.Width * (double)rr.Height >= need) return "トップレベル窓 '" + WinText(h) + "' " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
            }
            return null;
        }
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
        static string ChildSizeList(IntPtr form)
        {
            List<string> l = new List<string>();
            for (IntPtr c = GetWindow(form, 5); c != IntPtr.Zero && l.Count < 12; c = GetWindow(c, 2)) { RECT r; if (GetWindowRect(c, out r)) l.Add((r.Right - r.Left) + "x" + (r.Bottom - r.Top)); }
            return string.Join(",", l.ToArray());
        }
        static string Norm(string s) { StringBuilder b = new StringBuilder(s.Length); foreach (char ch in s) if (!char.IsWhiteSpace(ch) && ch != '\u3000') b.Append(ch); return b.ToString(); }
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
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] static extern IntPtr SendMessageTimeoutP(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    }
}
