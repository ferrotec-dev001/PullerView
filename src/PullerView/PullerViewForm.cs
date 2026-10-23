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

namespace PullerView
{

public class Surface
{
    public PullerViewForm Owner;
    public int Width { get { return CurW; } }
    public const int W = 390, IconW = 44, TitleH = 30, PanelH = 154, CmtH = 20, Gap = 2, Margin = 5;
    public static int CurW { get { return M.IconMode ? IconW : W; } }   // アイコン表示時は幅 44   // 式样 r3 図: 幅 ≒1280×0.306, 4グラフ時高さ ≒1024×0.66
    public static readonly Color WinBg = Color.FromArgb(0x4D, 0x4D, 0x4D), PlotBg = Color.FromArgb(0x3C, 0x3C, 0x3C);
    static readonly Color ColUsl = Color.FromArgb(0xFF, 0x30, 0x30), ColUcl = Color.FromArgb(0x00, 0xB0, 0xF0), ColPv = Color.FromArgb(0x30, 0xFF, 0x30), ColSv = Color.White;
    public Rectangle GearRect, MinMaxRect, ProcModeRect; float mx; public event Action ProcModeClicked; ToolTip tip = new ToolTip(); string lastTip = "";
    public bool Dragging; Point dragStart, formStart; public event Action Moved; public event Action GearClicked;

    static Surface()
    {
        Assembly a = Assembly.GetExecutingAssembly();
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
        if (M.IconMode) return TitleH;
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
        if (M.IconMode) { if (e.Button == MouseButtons.Left) { M.RestoreFromIcon(); return; } }
        if (GearRect.Contains(e.Location)) { if (GearClicked != null) GearClicked(); return; }
        if (ProcModeRect.Contains(e.Location)) { if (ProcModeClicked != null) ProcModeClicked(); return; }
        if (MinMaxRect.Contains(e.Location)) { M.Maximized = !M.Maximized; M.Log("PullerView: " + (M.Maximized ? "最大化" : "最小化")); M.Fire(); return; }
        Dragging = true; dragStart = Control.MousePosition; formStart = FindForm().Location;
    }
    public void OnMouseMove(MouseEventArgs e)
    {
        if (Dragging) { Point p = Control.MousePosition; FindForm().Location = new Point(formStart.X + p.X - dragStart.X, formStart.Y + p.Y - dragStart.Y); return; }
        string t = M.IconMode ? "PullerView（クリックでウィンドウ表示）" : GearRect.Contains(e.Location) ? "設定メニュー" : MinMaxRect.Contains(e.Location) ? (M.Maximized ? "最小化" : "最大化") : ProcModeRect.Contains(e.Location) ? (M.ProcAuto ? "工程追従: 自動（クリックで切替）" : "工程追従: 手動 " + M.Procs[M.S.ManualProc] + "（クリックで切替）") : "";
        Cursor = t != "" ? Cursors.Hand : Cursors.SizeAll;
        if (t != lastTip) { lastTip = t; tip.SetToolTip(Owner, t); Invalidate(new Rectangle(0, 0, Width, TitleH)); }
    }
    public void OnMouseUp(MouseEventArgs e) { if (Dragging) { Dragging = false; if (Moved != null) Moved(); } }
    /// <summary>タイトル行（ロゴ・工程名・アイコン類のある行）のダブルクリックでアイコン表示へ（2026-10-23 中村氏要望）。各アイコン上は除く。</summary>
    public void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || M.IconMode) return;
        if (e.Y >= TitleH || GearRect.Contains(e.Location) || MinMaxRect.Contains(e.Location) || ProcModeRect.Contains(e.Location)) return;
        Dragging = false; M.EnterIcon("タイトル行ダブルクリック");
    }
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
        if (M.IconMode)
        {   // ---- アイコン表示: ロゴマークのみ（frmAutoStatus 未検出時など）。クリックで最小化ウィンドウへ復帰 ----
            Point mp0 = PointToClient(Control.MousePosition);
            if (new Rectangle(0, 0, Width, Height).Contains(mp0)) using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) using (GraphicsPath gp = Rounded(new RectangleF(0, 0, Width, Height), 8)) g.FillPath(b, gp);
            Logo.DrawMark(g, new RectangleF(11, 4, 22, 22));
            using (Pen pn = new Pen(Color.FromArgb(0xA0, 0xA0, 0xA0), 1f)) using (GraphicsPath gp = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8)) g.DrawPath(pn, gp);
            GearRect = MinMaxRect = ProcModeRect = Rectangle.Empty;
            return;
        }
        // ---- タイトル行 ----
        Logo.Draw(g, M.S.Logo, new RectangleF(6, 4, 80, 22));   // ロゴ（設定で 5 案を切替。0=テキスト）
        RectangleF box = new RectangleF(M.S.Logo > 0 ? 100 : 84, 6, 66, 18);   // 工程名の白箱: ロゴ（案1）に被らないよう x=100（式样図 84 から +16）
        using (GraphicsPath gp = Rounded(box, 2)) { g.FillPath(Brushes.White, gp); }
        using (Font f = F(9f, FontStyle.Regular)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString(M.Abbr, f, Brushes.Black, box, sf); }
        GearRect = new Rectangle(Width - 60, 4, 22, 22); MinMaxRect = new Rectangle(Width - 32, 4, 22, 22); ProcModeRect = new Rectangle(Width - 88, 4, 22, 22);   // 工程追従アイコンは歯車の左隣
        Point mp = PointToClient(Control.MousePosition);
        if (GearRect.Contains(mp)) using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, GearRect);
        if (MinMaxRect.Contains(mp)) using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, MinMaxRect);
        if (ProcModeRect.Contains(mp)) using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, ProcModeRect);
        DrawProcMode(g, ProcModeRect, M.ProcAuto);
        if (!M.ProcAuto)   // 手動モード: 工程名の白箱を琥珀色にして「手動固定」を明示
        {
            using (GraphicsPath gp = Rounded(box, 2)) using (SolidBrush b = new SolidBrush(Color.FromArgb(0xFF, 0xC1, 0x07))) g.FillPath(b, gp);
            using (Font f = F(9f, FontStyle.Bold)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString(M.Abbr, f, Brushes.Black, box, sf); }
            using (Font f = F(6.5f, FontStyle.Regular)) g.DrawString("手動", f, new SolidBrush(Color.FromArgb(0xFF, 0xC1, 0x07)), box.Right + 3, 9);
        }
        DrawGearSolid(g, GearRect);   // 歯車は白ベタ塗りのベクタ描画（透過を強めてもかすれない）
        DrawCircleArrow(g, MinMaxRect, M.Maximized);
        int y = TitleH;
        // ---- コメント行（常時表示。異常名称をスクロール、正常時は「異常なし」）----
        {
            Rectangle cr = new Rectangle(Margin, y + 1, Width - Margin * 2, CmtH - 2);
            using (GraphicsPath gp = Rounded(new RectangleF(cr.X, cr.Y, cr.Width, cr.Height), 3)) { using (SolidBrush b = new SolidBrush(A(Color.FromArgb(0x33, 0x33, 0x33)))) g.FillPath(b, gp); using (Pen p = new Pen(Color.FromArgb(0xA0, 0xA0, 0xA0), 1f)) g.DrawPath(p, gp); }
            List<string> items = M.CommentItems(); bool scroll = items.Count > 0 || !M.DataOk;
            string txt = !M.DataOk ? (!M.HostAlive ? (M.Sim ? "単体起動中 — VB_FERROTEC.exe（frmAutoStatus）未検出。接続を待っています" : "VB_FERROTEC.exe（frmAutoStatus）未検出 — " + M.HostInfo) : "通信断（VB_FERROTEC.exe からのデータ待ち） — " + M.HostInfo) : items.Count > 0 ? string.Join("　　", items.ToArray()) : (M.IsTarget(M.Proc) ? "異常なし" : "工程 " + M.Procs[M.Proc] + "：監視対象外");
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
    /// <summary>工程追従モードのアイコン。自動: 2 本の循環矢印（Remoting 同期）＋「A」。手動: 人差し指の手＋「M」。</summary>
    static void DrawProcMode(Graphics g, Rectangle r, bool auto)
    {
        GraphicsState st = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        if (auto)
        {   // 循環矢印（同期・追従）: 2 本の円弧＋矢尻、中央に A
            Color c = Color.FromArgb(0x4F, 0xC3, 0xF7);
            using (Pen p = new Pen(c, 2f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; RectangleF a = new RectangleF(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6); g.DrawArc(p, a, 200, 140); g.DrawArc(p, a, 20, 140); }
            using (SolidBrush b = new SolidBrush(c))
            {
                float rr = (r.Width - 6) / 2f; PointF e1 = new PointF(cx + rr * (float)Math.Cos(-20 * Math.PI / 180), cy + rr * (float)Math.Sin(-20 * Math.PI / 180));
                g.FillPolygon(b, new PointF[] { new PointF(e1.X + 1.5f, e1.Y - 3.5f), new PointF(e1.X + 1.5f, e1.Y + 2.5f), new PointF(e1.X - 3.5f, e1.Y + 0.5f) });
                PointF e2 = new PointF(cx + rr * (float)Math.Cos(160 * Math.PI / 180), cy + rr * (float)Math.Sin(160 * Math.PI / 180));
                g.FillPolygon(b, new PointF[] { new PointF(e2.X - 1.5f, e2.Y + 3.5f), new PointF(e2.X - 1.5f, e2.Y - 2.5f), new PointF(e2.X + 3.5f, e2.Y - 0.5f) });
            }
            using (Font f = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString("A", f, Brushes.White, new RectangleF(r.X, r.Y + 0.5f, r.Width, r.Height), sf); }
        }
        else
        {   // 手（人差し指で選ぶ）: 琥珀色。掌＋指 4 本＋親指、右下に M
            Color c = Color.FromArgb(0xFF, 0xC1, 0x07);
            using (SolidBrush b = new SolidBrush(c)) using (Pen p = new Pen(c, 1.2f))
            {
                RectangleF palm = new RectangleF(r.X + 6, r.Y + 10, 10, 8); g.FillEllipse(b, palm);
                g.FillRectangle(b, r.X + 9f, r.Y + 2f, 3f, 11f);      // 人差し指（立てる）
                g.FillRectangle(b, r.X + 12.5f, r.Y + 7f, 2.6f, 6f);  // 中指
                g.FillRectangle(b, r.X + 15.2f, r.Y + 8.5f, 2.4f, 5f);// 薬指
                g.FillRectangle(b, r.X + 5.2f, r.Y + 9.5f, 3.6f, 3f); // 親指
            }
            using (Font f = new Font("Segoe UI", 7f, FontStyle.Bold, GraphicsUnit.Point)) using (SolidBrush b = new SolidBrush(Color.FromArgb(0x33, 0x33, 0x33))) g.DrawString("M", f, Brushes.White, r.Right - 10, r.Bottom - 11);
        }
        g.Restore(st);
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
            List<PointF> sv = new List<PointF>(s.X.Count), pv = new List<PointF>(s.X.Count);
            for (int i = 0; i < s.X.Count; i++) { if (!double.IsNaN(s.Sv[i])) sv.Add(new PointF(X(s.X[i]), Y(s.Sv[i]))); if (!double.IsNaN(s.Pv[i])) pv.Add(new PointF(X(s.X[i]), Y(s.Pv[i]))); }
            if (sv.Count >= 2) using (Pen p = new Pen(ColSv, 1.2f)) g.DrawLines(p, sv.ToArray());
            if (pv.Count >= 2) using (Pen p = new Pen(ColPv, 2f)) { p.DashStyle = DashStyle.Dot; g.DrawLines(p, pv.ToArray()); }
        }
        else if (valid) using (Pen p = new Pen(ColSv, 1.2f)) g.DrawLine(p, pr.X, Y(lastSv), pr.Right, Y(lastSv));
        g.ResetClip();
        if (!valid) using (Font f = F(16, FontStyle.Bold)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString("---", f, new SolidBrush(Color.FromArgb(220, 255, 255, 255)), pr, sf); }
        // 右側ラベル: 式样図と同じ固定位置（上から USL / UCL / PV / SV / LCL / LSL を等間隔。値で位置は動かない）
        string fmt = span < 2 ? "0.00" : span < 20 ? "0.0" : "0";
        string[] ltxt = { "USL " + c.Usl.ToString(fmt), "UCL " + c.Ucl.ToString(fmt), "PV " + (valid ? lastPv.ToString(fmt) : "---"), "SV " + (valid && !double.IsNaN(lastSv) ? lastSv.ToString(fmt) : "---"), "LCL " + c.Lcl.ToString(fmt), "LSL " + c.Lsl.ToString(fmt) };
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
        Text = "PullerView"; ClientSize = new Size(Surface.CurW, Surface.TotalHeight());
        Surf.ProcModeClicked += ShowProcModeMenu;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Surf.Moved += delegate
        {
            if (Anchor0 != null) { OffX = Left - Anchor0.Left; OffY = Top - Anchor0.Top; }
            else if (AnchorRect.Width > 0) { OffX = Left - AnchorRect.Left; OffY = Top - AnchorRect.Top; }
            else return;
            M.Log("PullerView 位置: frmAutoStatus 左上から (" + OffX + ", " + OffY + ")"); M.S.PosX = OffX; M.S.PosY = OffY; M.S.Save();   // 位置は PullerView.ini に保存
        };
    }
    protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; if (layered) cp.ExStyle |= 0x80000; return cp; } }   // WS_EX_LAYERED
    protected override void OnMouseDown(MouseEventArgs e) { Surf.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { Surf.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Surf.OnMouseUp(e); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { Surf.OnMouseDoubleClick(e); }
    protected override void OnMouseLeave(EventArgs e) { Surf.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) { if (!layered) Surf.Render(e.Graphics, ClientSize.Height, false); }

    public Bitmap RenderBitmap()
    {
        int h = Surface.TotalHeight();
        Bitmap b = new Bitmap(Surface.CurW, h, PixelFormat.Format32bppArgb);
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
    public Rectangle AnchorRect;   // 実運用: 外部ウィンドウ（VB_FERROTEC.exe の frmAutoStatus）の画面座標
    public void FollowAnchor()
    {
        if (Surf.Dragging) return;
        if (Anchor0 != null) { if (Anchor0.Visible) Location = new Point(Anchor0.Left + OffX, Anchor0.Top + OffY); }
        else if (AnchorRect.Width > 0) { Point p = new Point(AnchorRect.Left + OffX, AnchorRect.Top + OffY); if (Location != p) Location = p; }
    }
    public event Action SettingsRequested;
    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        ContextMenuStrip m = new ContextMenuStrip();
        m.Items.Add("設定...", null, delegate { if (SettingsRequested != null) SettingsRequested(); });
        if (M.IconMode) m.Items.Add("ウィンドウ表示", null, delegate { M.RestoreFromIcon(); });
        else { m.Items.Add(M.Maximized ? "最小化" : "最大化", null, delegate { M.Maximized = !M.Maximized; M.Fire(); }); m.Items.Add("アイコン表示", null, delegate { M.EnterIcon("メニュー"); }); }
        m.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem pm = new ToolStripMenuItem("工程追従"); BuildProcModeItems(pm.DropDownItems); m.Items.Add(pm);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("PullerView を終了", null, delegate { if (MessageBox.Show("PullerView を終了しますか？", "PullerView", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Application.Exit(); });
        m.Show(this, e.Location);
    }
    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Redraw(); }
    /// <summary>工程追従アイコンのクリック: 自動／手動（13 工程から選択）のメニューを出す。</summary>
    void ShowProcModeMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip(); BuildProcModeItems(m.Items);
        m.Show(this, new Point(Surf.ProcModeRect.X, Surf.ProcModeRect.Bottom + 2));
    }
    void BuildProcModeItems(ToolStripItemCollection items)
    {
        ToolStripMenuItem a = new ToolStripMenuItem("自動（Remoting の工程に追従）"); a.Checked = M.ProcAuto; a.Click += delegate { M.SetProcMode(true, -1); }; items.Add(a);
        ToolStripMenuItem mm = new ToolStripMenuItem("手動（工程を固定）"); mm.Checked = !M.ProcAuto;
        for (int i = 0; i < M.Procs.Length; i++)
        {
            int ix = i; ToolStripMenuItem it = new ToolStripMenuItem(M.Procs[i] + (M.Abbrs[i] != M.Procs[i] ? "  (" + M.Abbrs[i] + ")" : "")); it.Checked = !M.ProcAuto && M.S.ManualProc == i;
            it.Click += delegate { M.SetProcMode(false, ix); }; mm.DropDownItems.Add(it);
        }
        items.Add(mm);
    }
    public void ApplyState()
    {
        int h = Surface.TotalHeight();
        int w = Surface.CurW;
        if (ClientSize.Height != h || ClientSize.Width != w) { ClientSize = new Size(w, h); if (!layered) using (GraphicsPath gp = Surface.Rounded(new RectangleF(0, 0, w, h), 8)) Region = new Region(gp); }
        if (!layered) Opacity = M.S.OpacityPct / 100.0;
        bool show = M.OverlayShown;
        if (show && !Visible) { Show(); FollowAnchor(); } else if (!show && Visible) Hide();
        Redraw();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
}
}
