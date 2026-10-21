// PullerView メインウィンドウ（モック）— 式样 r2/r3「最大化／最小化」デザイン準拠
//   タイトル行: "PullerView" + 現在工程(白箱) + 歯車(設定) + ○矢印(最小化/最大化)
//   グラフ枠  : 管理項目名 [単位] / 暗灰プロット / 緑点線 PV / USL,LSL=赤 UCL,LCL=青 SV=白 / 右側に値ラベル / X軸=結晶長さ(縦書き)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;

public class Surface
{
    public PullerViewForm Owner;
    public int Width { get { return W; } }
    public const int W = 390, TitleH = 30, PanelH = 154, CmtH = 20, Gap = 2, Margin = 5;   // 式样 r3 図: 幅 ≒1280×0.306, 4グラフ時高さ ≒1024×0.66
    public static readonly Color WinBg = Color.FromArgb(0x4D, 0x4D, 0x4D), PlotBg = Color.FromArgb(0x3C, 0x3C, 0x3C);
    static readonly Color ColUsl = Color.FromArgb(0xFF, 0x30, 0x30), ColUcl = Color.FromArgb(0x00, 0xB0, 0xF0), ColPv = Color.FromArgb(0x30, 0xFF, 0x30), ColSv = Color.White;
    static Image gear, circle;
    public Rectangle GearRect, MinMaxRect; float mx; ToolTip tip = new ToolTip(); string lastTip = "";
    public bool Dragging; Point dragStart, formStart; public event Action Moved; public event Action GearClicked;

    static Surface()
    {
        Assembly a = Assembly.GetExecutingAssembly();
        try { gear = Image.FromStream(a.GetManifestResourceStream("icon_gear.png")); circle = Image.FromStream(a.GetManifestResourceStream("icon_circle.png")); } catch (Exception) { }
    }
    public Surface(PullerViewForm owner)
    {
        Owner = owner;
        Timer t = new Timer(); t.Interval = 40; t.Tick += delegate { if (M.CommentItems().Count > 0 || !M.DataOk) { mx += 0.6f * M.S.CmtSpeed; Owner.Redraw(); } else if (mx != 0) { mx = 0; Owner.Redraw(); } }; t.Start();
    }
    Point PointToClient(Point p) { return Owner.PointToClient(p); }
    Form FindForm() { return Owner; }
    void Invalidate(Rectangle r) { Owner.Redraw(); }
    Cursor Cursor { set { Owner.Cursor = value; } }
    // 透過: 背景のみ透過モードでは背景色に alpha を付け、線・文字は不透明のまま
    static int BgAlpha { get { return M.S.BgOnly ? (int)Math.Round(255 * M.S.OpacityPct / 100.0) : 255; } }
    static Color A(Color c) { return Color.FromArgb(BgAlpha, c); }
    public static bool CommentShown { get { return true; } }   // コメント行は常時表示（2026-10-20 中村氏決定）
    public static int TotalHeight()
    {
        int h = TitleH;
        if (CommentShown) h += CmtH;
        if (M.Maximized) { int n = M.Active().Count; if (n > 0) h += n * (PanelH + Gap) + Margin; else h += 24; }
        else h += Margin;   // 最小化時: コメント枠の下にも左右と同じクリアランス（Margin）を確保し、外枠線に隠れないようにする
        return h;
    }
    static Font F(float pt, FontStyle st) { return new Font("Yu Gothic UI", pt, st, GraphicsUnit.Point); }

    public void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (GearRect.Contains(e.Location)) { if (GearClicked != null) GearClicked(); return; }
        if (MinMaxRect.Contains(e.Location)) { M.Maximized = !M.Maximized; M.Log("PullerView: " + (M.Maximized ? "最大化" : "最小化")); M.Fire(); return; }
        Dragging = true; dragStart = Control.MousePosition; formStart = FindForm().Location;
    }
    public void OnMouseMove(MouseEventArgs e)
    {
        if (Dragging) { Point p = Control.MousePosition; FindForm().Location = new Point(formStart.X + p.X - dragStart.X, formStart.Y + p.Y - dragStart.Y); return; }
        string t = GearRect.Contains(e.Location) ? "設定メニュー" : MinMaxRect.Contains(e.Location) ? (M.Maximized ? "最小化" : "最大化") : "";
        Cursor = t != "" ? Cursors.Hand : Cursors.SizeAll;
        if (t != lastTip) { lastTip = t; tip.SetToolTip(Owner, t); Invalidate(new Rectangle(0, 0, Width, TitleH)); }
    }
    public void OnMouseUp(MouseEventArgs e) { if (Dragging) { Dragging = false; if (Moved != null) Moved(); } }
    public void OnMouseLeave(EventArgs e) { lastTip = ""; Invalidate(new Rectangle(0, 0, Width, TitleH)); }

    public static GraphicsPath Rounded(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }

    public void Render(Graphics g, int Height, bool layered)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = layered ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);
        using (GraphicsPath gp = Rounded(new RectangleF(0, 0, Width, Height), 8)) using (SolidBrush b = new SolidBrush(layered ? A(M.S.BgColor) : M.S.BgColor)) g.FillPath(b, gp);
        // ---- タイトル行 ----
        Logo.Draw(g, M.S.Logo, new RectangleF(6, 4, 80, 22));   // ロゴ（設定で 5 案を切替。0=テキスト）
        RectangleF box = new RectangleF(M.S.Logo > 0 ? 100 : 84, 6, 66, 18);   // 工程名の白箱: ロゴ（案1）に被らないよう x=100（式样図 84 から +16）
        using (GraphicsPath gp = Rounded(box, 2)) { g.FillPath(Brushes.White, gp); }
        using (Font f = F(9f, FontStyle.Regular)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString(M.Abbr, f, Brushes.Black, box, sf); }
        GearRect = new Rectangle(Width - 60, 4, 22, 22); MinMaxRect = new Rectangle(Width - 32, 4, 22, 22);
        Point mp = PointToClient(Control.MousePosition);
        if (GearRect.Contains(mp)) using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, GearRect);
        if (MinMaxRect.Contains(mp)) using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, MinMaxRect);
        DrawGearSolid(g, GearRect);   // 歯車は白ベタ塗りのベクタ描画（透過を強めてもかすれない）
        DrawCircleArrow(g, MinMaxRect, M.Maximized);
        int y = TitleH;
        // ---- コメント行（常時表示。異常名称をスクロール、正常時は「異常なし」）----
        {
            Rectangle cr = new Rectangle(Margin, y + 1, Width - Margin * 2, CmtH - 2);
            using (GraphicsPath gp = Rounded(new RectangleF(cr.X, cr.Y, cr.Width, cr.Height), 3)) { using (SolidBrush b = new SolidBrush(A(Color.FromArgb(0x33, 0x33, 0x33)))) g.FillPath(b, gp); using (Pen p = new Pen(Color.FromArgb(0xA0, 0xA0, 0xA0), 1f)) g.DrawPath(p, gp); }
            List<string> items = M.CommentItems(); bool scroll = items.Count > 0 || !M.DataOk;
            string txt = !M.HostAlive ? "単体起動中 — VB_FERROTEC.exe（frmAutoStatus）未検出。接続を待っています" : !M.Comm ? "通信断（VB_FERROTEC.exe からのデータ待ち）" : items.Count > 0 ? string.Join("　　", items.ToArray()) : (M.IsTarget(M.Proc) ? "異常なし" : "工程 " + M.Procs[M.Proc] + "：監視対象外");
            Color tc = !M.DataOk ? Color.FromArgb(0xFF, 0x90, 0x60) : items.Count > 0 ? M.S.CmtColor : Color.FromArgb(0xC0, 0xC0, 0xC0);
            g.SetClip(cr);
            using (Font f = F(M.S.CmtSize, scroll ? FontStyle.Bold : FontStyle.Regular)) using (SolidBrush b = new SolidBrush(tc))
            {
                SizeF sz = g.MeasureString(txt, f); float yy = cr.Y + (cr.Height - sz.Height) / 2;
                if (scroll) { float period = sz.Width + 60; float off = ((mx % period) + period) % period; for (float x = (M.S.CmtLeft ? Width - off : off - period); x < Width + period; x += period) g.DrawString(txt, f, b, x, yy); }
                else g.DrawString(txt, f, b, cr.X + 6, yy);
            }
            g.ResetClip(); y += CmtH;
        }
        // ---- グラフ ----
        if (M.Maximized)
        {
            List<GraphCfg> act = M.Active();
            if (act.Count == 0)
            {
                using (Font f = F(8.5f, FontStyle.Regular)) { string m = M.IsTarget(M.Proc) ? "有効なグラフ設定なし（DataSetting.ini）" : "工程 " + M.Procs[M.Proc] + " はグラフ対象外"; StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString(m, f, new SolidBrush(Color.FromArgb(0xC8, 0xC8, 0xC8)), new Rectangle(0, y, Width, 24), sf); }
            }
            for (int i = 0; i < act.Count; i++) DrawGraph(g, new Rectangle(Margin, y + i * (PanelH + Gap), Width - Margin * 2, PanelH), act[i], M.Slots[act[i].N - 1]);
        }
        // ---- 外枠（白・角丸）----
        using (Pen p = new Pen(Color.White, 2f)) using (GraphicsPath gp = Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 7)) g.DrawPath(p, gp);
    }
    static void DrawGearSolid(Graphics g, Rectangle r)
    {
        // 8枚歯の歯車を白で塗りつぶす（内部も白）。外径 r.Width、歯の高さ 15%
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, ro = r.Width / 2f - 1f, ri = ro * 0.72f;
        const int teeth = 8; PointF[] pts = new PointF[teeth * 4];
        for (int i = 0; i < teeth; i++)
        {
            double a0 = i * 2 * Math.PI / teeth, half = Math.PI / teeth, tw = half * 0.5;   // 歯の幅 = ピッチの 50%
            pts[i * 4 + 0] = new PointF(cx + (float)(ri * Math.Cos(a0 - half)), cy + (float)(ri * Math.Sin(a0 - half)));
            pts[i * 4 + 1] = new PointF(cx + (float)(ro * Math.Cos(a0 - tw)), cy + (float)(ro * Math.Sin(a0 - tw)));
            pts[i * 4 + 2] = new PointF(cx + (float)(ro * Math.Cos(a0 + tw)), cy + (float)(ro * Math.Sin(a0 + tw)));
            pts[i * 4 + 3] = new PointF(cx + (float)(ri * Math.Cos(a0 + half)), cy + (float)(ri * Math.Sin(a0 + half)));
        }
        using (GraphicsPath gp = new GraphicsPath()) { gp.AddPolygon(pts); g.FillPath(Brushes.White, gp); using (Pen p = new Pen(Color.White, 1f)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, gp); } }
    }
    static void DrawGearFallback(Graphics g, Rectangle r) { using (Pen p = new Pen(Color.White, 2f)) { g.DrawEllipse(p, r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8); g.DrawEllipse(p, r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16); } }
    static void DrawCircleArrow(Graphics g, Rectangle r, bool up)
    {
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(0x4D, 0x4D, 0x4D)))
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            PointF[] tri = up ? new PointF[] { new PointF(cx, cy - 5), new PointF(cx - 6, cy + 4), new PointF(cx + 6, cy + 4) } : new PointF[] { new PointF(cx, cy + 5), new PointF(cx - 6, cy - 4), new PointF(cx + 6, cy - 4) };
            g.FillPolygon(b, tri);
        }
    }

    void DrawGraph(Graphics g, Rectangle r, GraphCfg c, GraphState s)
    {
        Color panelBg = M.S.BgColor, plotBg = PlotBg;
        if (s.Confirmed == St.SpecHi || s.Confirmed == St.SpecLo) { panelBg = c.Alarm; plotBg = Mix(c.Alarm, Color.Black, 0.25); }
        else if (s.Confirmed == St.CtrlHi || s.Confirmed == St.CtrlLo) { panelBg = c.Warn; plotBg = Mix(c.Warn, Color.Black, 0.25); }
        using (GraphicsPath gp = Rounded(new RectangleF(r.X, r.Y, r.Width, r.Height), 5))
        {
            using (SolidBrush b = new SolidBrush(A(panelBg))) g.FillPath(b, gp);
            using (Pen p = new Pen(Color.White, 1.5f)) g.DrawPath(p, gp);
        }
        string cap = c.Caption(M.S.Lang), unit = c.Unit();
        using (Font f = F(9f, FontStyle.Regular)) g.DrawString(cap + (unit != "" ? " [" + unit + "]" : ""), f, Brushes.White, r.X + 5, r.Y + 3);
        // プロット領域（右に値ラベル列 62px、下に X 軸ラベル 32px）
        Rectangle pr = new Rectangle(r.X + 6, r.Y + 21, r.Width - 6 - 64, r.Height - 21 - 33);
        using (SolidBrush b = new SolidBrush(A(plotBg))) g.FillRectangle(b, pr);
        double span = c.Usl - c.Lsl, y0 = c.Lsl - 0.3 * span, y1 = c.Usl + 0.3 * span;
        if (M.S.AutoY && s.Pv.Count > 0) { double mn = double.MaxValue, mxv = double.MinValue; for (int i = 0; i < s.Pv.Count; i++) { mn = Math.Min(mn, Math.Min(s.Pv[i], s.Sv[i])); mxv = Math.Max(mxv, Math.Max(s.Pv[i], s.Sv[i])); } mn = Math.Min(mn, c.Lsl); mxv = Math.Max(mxv, c.Usl); double pad = (mxv - mn) * 0.15; y0 = mn - pad; y1 = mxv + pad; }
        double curL = s.X.Count > 0 ? s.X[s.X.Count - 1] : 0;
        double xmax = M.S.XMax; double xs = M.S.SpanFor(xmax); if (curL > xmax) { xmax = Math.Ceiling(curL / xs) * xs; xs = M.S.SpanFor(xmax); }   // X 軸 = 結晶長さ。最大値は設定（PullerView.ini）
        Func<double, float> X = delegate(double v) { return (float)(pr.X + (v / xmax) * pr.Width); };
        Func<double, float> Y = delegate(double v) { return (float)(pr.Bottom - (v - y0) / (y1 - y0) * pr.Height); };
        // 横グリッド（白・細）
        using (Pen gp = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0), 1f)) for (int i = 1; i < 4; i++) { float yy = pr.Y + pr.Height * i / 4f; g.DrawLine(gp, pr.X, yy, pr.Right, yy); }
        // X 軸ラベル（結晶長さ・縦書き）
        using (Font f = F(6f, FontStyle.Regular)) using (SolidBrush tb = new SolidBrush(Color.White))
        {
            int ticks = (int)Math.Round(xmax / xs); int step = Math.Max(1, (int)Math.Ceiling(ticks / Math.Max(1.0, pr.Width / 11.0)));
            for (int i = 0; i <= ticks; i += step)
            {
                float x = X(i * xs); string t = (i * xs).ToString("0"); SizeF sz = g.MeasureString(t, f);
                GraphicsState st = g.Save(); g.TranslateTransform(x + sz.Height / 2, pr.Bottom + 2); g.RotateTransform(90); g.DrawString(t, f, tb, 0, 0); g.Restore(st);
            }
        }
        // 基準線 + PV/SV
        g.SetClip(pr);
        using (Pen p = new Pen(ColUsl, 2f)) { g.DrawLine(p, pr.X, Y(c.Usl), pr.Right, Y(c.Usl)); g.DrawLine(p, pr.X, Y(c.Lsl), pr.Right, Y(c.Lsl)); }
        using (Pen p = new Pen(ColUcl, 2f)) { g.DrawLine(p, pr.X, Y(c.Ucl), pr.Right, Y(c.Ucl)); g.DrawLine(p, pr.X, Y(c.Lcl), pr.Right, Y(c.Lcl)); }
        bool valid = M.DataOk && s.Valid && s.Pv.Count > 0;
        double lastPv = s.Pv.Count > 0 ? s.Pv[s.Pv.Count - 1] : double.NaN, lastSv = s.Sv.Count > 0 ? s.Sv[s.Sv.Count - 1] : double.NaN;
        if (s.Pv.Count > 1)
        {
            PointF[] sv = new PointF[s.X.Count], pv = new PointF[s.X.Count];
            for (int i = 0; i < s.X.Count; i++) { sv[i] = new PointF(X(s.X[i]), Y(s.Sv[i])); pv[i] = new PointF(X(s.X[i]), Y(s.Pv[i])); }
            using (Pen p = new Pen(ColSv, 1.2f)) g.DrawLines(p, sv);
            using (Pen p = new Pen(ColPv, 2f)) { p.DashStyle = DashStyle.Dot; g.DrawLines(p, pv); }
        }
        else if (valid) using (Pen p = new Pen(ColSv, 1.2f)) g.DrawLine(p, pr.X, Y(lastSv), pr.Right, Y(lastSv));
        g.ResetClip();
        if (!valid) using (Font f = F(16, FontStyle.Bold)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString("---", f, new SolidBrush(Color.FromArgb(220, 255, 255, 255)), pr, sf); }
        // 右側ラベル: 式样図と同じ固定位置（上から USL / UCL / PV / SV / LCL / LSL を等間隔。値で位置は動かない）
        string fmt = span < 2 ? "0.00" : span < 20 ? "0.0" : "0";
        string[] ltxt = { "USL " + c.Usl.ToString(fmt), "UCL " + c.Ucl.ToString(fmt), "PV " + (valid ? lastPv.ToString(fmt) : "---"), "SV " + (valid ? lastSv.ToString(fmt) : "---"), "LCL " + c.Lcl.ToString(fmt), "LSL " + c.Lsl.ToString(fmt) };
        Color[] lcol = { ColUsl, ColUcl, ColPv, ColSv, ColUcl, ColUsl };
        float lstep = (pr.Height + 10) / 6f;
        using (Font f = F(7f, FontStyle.Bold))
            for (int i = 0; i < 6; i++) using (SolidBrush b = new SolidBrush(lcol[i])) g.DrawString(ltxt[i], f, b, pr.Right + 3, pr.Y - 5 + lstep * i + (lstep - 11) / 2);
    }
    static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
}

public class PullerViewForm : Form
{
    public Surface Surf;
    public int OffX = 300, OffY = 67;   // 既定位置: 式样 r3「ウィンドウデフォルト位置」の図から算出（frmAutoStatus 左上基準、1280×1024）
    public Form Anchor0;
    bool layered; Bitmap bmp;
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; public POINT(int a, int b) { x = a; y = b; } }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int a, int b) { cx = a; cy = b; } }
    [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    public bool IsLayered { get { return layered; } }
    public PullerViewForm()
    {
        Surf = new Surface(this);
        layered = Environment.OSVersion.Platform == PlatformID.Win32NT && Array.IndexOf(Environment.GetCommandLineArgs(), "--no-layered") < 0;   // Windows: 背景のみ透過（UpdateLayeredWindow）。mono/Linux: 通常描画
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
        Text = "PullerView"; ClientSize = new Size(Surface.W, Surface.TotalHeight());
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Surf.Moved += delegate { if (Anchor0 != null) { OffX = Left - Anchor0.Left; OffY = Top - Anchor0.Top; M.Log("PullerView 位置: frmAutoStatus 左上から (" + OffX + ", " + OffY + ")"); } };
    }
    protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; if (layered) cp.ExStyle |= 0x80000; return cp; } }   // WS_EX_LAYERED
    protected override void OnMouseDown(MouseEventArgs e) { Surf.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { Surf.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Surf.OnMouseUp(e); }
    protected override void OnMouseLeave(EventArgs e) { Surf.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) { if (!layered) Surf.Render(e.Graphics, ClientSize.Height, false); }

    public Bitmap RenderBitmap()
    {
        int h = Surface.TotalHeight();
        Bitmap b = new Bitmap(Surface.W, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b)) Surf.Render(g, h, true);
        return b;
    }
    public void Redraw()
    {
        if (!Visible || !IsHandleCreated) return;
        if (!layered) { Invalidate(); return; }
        try
        {
            if (bmp != null) bmp.Dispose(); bmp = RenderBitmap();
            IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0)); old = SelectObject(memDc, hBmp);
                SIZE size = new SIZE(bmp.Width, bmp.Height); POINT src = new POINT(0, 0), dst = new POINT(Left, Top);
                BLENDFUNCTION bf = new BLENDFUNCTION(); bf.BlendOp = 0; bf.BlendFlags = 0; bf.AlphaFormat = 1; bf.SourceConstantAlpha = (byte)(M.S.BgOnly ? 255 : (int)Math.Round(255 * M.S.OpacityPct / 100.0));
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, 2);   // ULW_ALPHA
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); if (hBmp != IntPtr.Zero) { SelectObject(memDc, old); DeleteObject(hBmp); } DeleteDC(memDc); }
        }
        catch (Exception ex) { layered = false; M.Log("透過描画に失敗したため通常描画へ切替: " + ex.Message); RecreateHandle(); Invalidate(); }
    }
    public void FollowAnchor() { if (Anchor0 != null && Anchor0.Visible && !Surf.Dragging) Location = new Point(Anchor0.Left + OffX, Anchor0.Top + OffY); }
    public event Action SettingsRequested;
    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("設定...", null, delegate { if (SettingsRequested != null) SettingsRequested(); });
        m.Items.Add(M.Maximized ? "最小化" : "最大化", null, delegate { M.Maximized = !M.Maximized; M.Fire(); });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("PullerView を終了", null, delegate { if (MessageBox.Show("PullerView を終了しますか？", "PullerView", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Application.Exit(); });
        m.Show(this, e.Location);
    }
    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Redraw(); }
    public void ApplyState()
    {
        int h = Surface.TotalHeight();
        if (ClientSize.Height != h) { ClientSize = new Size(Surface.W, h); if (!layered) using (GraphicsPath gp = Surface.Rounded(new RectangleF(0, 0, Surface.W, h), 8)) Region = new Region(gp); }
        if (!layered) Opacity = M.S.OpacityPct / 100.0;
        bool show = M.OverlayShown;
        if (show && !Visible) { Show(); FollowAnchor(); } else if (!show && Visible) Hide();
        Redraw();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
}
