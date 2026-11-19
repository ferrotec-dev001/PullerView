// VisionView の画面（統合画面 案 B-2）: タイトル行のカメラのアイコンと判定バッジ、右欄（判定・真偽度／解析画像／座標展開グラフ・真偽度の推移・判定履歴）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PullerView
{
public partial class Surface
{
    public const int W2 = 775, PaneH = TitleH + CmtH + 4 * (PanelH + Gap) + Margin;   // 右欄を開いたときの幅と高さ（4 グラフ時と同じ高さ）
    public Rectangle VisionRect;
    /// <summary>右欄（VisionView）を表示しているか。晶線解析が有効・右欄を開く設定・最大化・アイコン化していない、のすべてを満たすとき。</summary>
    public static bool PaneOpen { get { return VisionCfg.Enable && VisionIni.Pane && M.Maximized && !M.IconMode; } }
    static readonly Color OkG = Color.FromArgb(0x33, 0xCC, 0x33), NgR = Color.FromArgb(0xE5, 0x39, 0x35), Gry = Color.FromArgb(0x80, 0x80, 0x80), Cy = Color.FromArgb(0x4F, 0xC3, 0xF7), LineY = Color.FromArgb(0xFF, 0xE6, 0x00), MarkG = Color.FromArgb(0, 255, 0), Lg = Color.FromArgb(0xD0, 0xD0, 0xD0);
    static Color JudgeColor(string j) { return j == "OK" ? OkG : j == "NG" ? NgR : Gry; }

    /// <summary>文字を描く。align: 0=左 1=中央 2=右（x が基準）。y は文字の上下中央。</summary>
    static float Tx(Graphics g, string s, float pt, Color c, float x, float y, bool bold, int align)
    {
        using (Font f = F(pt, bold ? FontStyle.Bold : FontStyle.Regular)) using (SolidBrush b = new SolidBrush(c))
        {
            SizeF z = g.MeasureString(s, f, 4000, StringFormat.GenericTypographic); float w = z.Width, h = f.GetHeight(g);
            g.DrawString(s, f, b, align == 0 ? x : align == 1 ? x - w / 2 : x - w, y - h / 2, StringFormat.GenericTypographic); return w;
        }
    }
    static void BoxLine(Graphics g, RectangleF r) { using (Pen p = new Pen(Color.White, 1.5f)) using (GraphicsPath gp = Rounded(r, 5)) g.DrawPath(p, gp); }
    static void Dash(Graphics g, Color c, float w, float x0, float y0, float x1, float y1, float on, float off) { using (Pen p = new Pen(c, w)) { p.DashPattern = new float[] { Math.Max(0.1f, on / w), Math.Max(0.1f, off / w) }; g.DrawLine(p, x0, y0, x1, y1); } }
    static void Cross(Graphics g, float x, float y, float d, float w) { using (Pen p = new Pen(MarkG, w)) { g.DrawLine(p, x - d, y - d, x + d, y + d); g.DrawLine(p, x - d, y + d, x + d, y - d); } }

    /// <summary>右欄の表示切替アイコン（カメラ）。右欄を開いているときは水色＋下線。</summary>
    static void DrawCamIcon(Graphics g, Rectangle r, bool on)
    {
        Color c = on ? Cy : Color.White; float x = r.X, y = r.Y;
        using (Pen p = new Pen(c, 1.6f)) using (SolidBrush b = new SolidBrush(c))
        {
            using (GraphicsPath gp = Rounded(new RectangleF(x + 2.5f, y + 6, 17, 12), 2.5f)) g.DrawPath(p, gp);
            g.FillRectangle(b, x + 7.5f, y + 3.2f, 7, 3.2f); g.DrawEllipse(p, x + 7.2f, y + 8.2f, 7.6f, 7.6f); g.FillEllipse(b, x + 9.9f, y + 10.9f, 2.2f, 2.2f);
            if (on) g.FillRectangle(b, x + 3, y + 23, 16, 2);
        }
    }
    /// <summary>タイトル行の判定バッジ（右欄を畳んでいるとき・最小化中）。xMax までに収まらないときは描かない。</summary>
    static void DrawJudgeBadge(Graphics g, float x, float xMax)
    {
        string j = VV.Judge, t = j == "OFF" ? "－" : VV.JudgeText(j);
        using (Font f = F(8f, FontStyle.Bold))
        {
            float w = g.MeasureString(t, f, 400, StringFormat.GenericTypographic).Width + 10; if (x + w > xMax) return;
            RectangleF r = new RectangleF(x, 6, w, 18); using (GraphicsPath gp = Rounded(r, 3)) using (SolidBrush b = new SolidBrush(JudgeColor(j))) g.FillPath(b, gp);
            Tx(g, t, 8f, Color.White, x + w / 2, 15, true, 1);
        }
    }

    /// <summary>右欄。x・y は左上、H はウィンドウの高さ。</summary>
    void DrawVisionPane(Graphics g, int x, int y, int H)
    {
        const int PW = 380;
        lock (VV.Lk)
        {
            VFrame f = VV.Cur; string judge = VV.Judge; double conf = VV.Conf; bool hasConf = !double.IsNaN(conf);
            // ---- 判定・真偽度
            BoxLine(g, new RectangleF(x, y, PW, 70)); float tw = (PW - 10 - 6) / 2f;
            RectangleF t1 = new RectangleF(x + 5, y + 5, tw, 60), t2 = new RectangleF(x + 5 + tw + 6, y + 5, tw, 60);
            using (GraphicsPath gp = Rounded(t1, 4)) using (SolidBrush b = new SolidBrush(JudgeColor(judge))) g.FillPath(b, gp);
            using (Pen p = new Pen(Color.White, 1.2f)) { using (GraphicsPath gp = Rounded(t1, 4)) g.DrawPath(p, gp); }
            Tx(g, VV.JudgeText(judge), judge == "OK" || judge == "NG" ? 22f : 16f, Color.White, t1.X + t1.Width / 2, t1.Y + 30, true, 1);
            Color c2 = !hasConf || judge == "OFF" ? Gry : conf >= VisionCfg.NgPct ? OkG : NgR;
            using (GraphicsPath gp = Rounded(t2, 4)) using (SolidBrush b = new SolidBrush(c2)) g.FillPath(b, gp);
            using (Pen p = new Pen(Color.White, 1.2f)) { using (GraphicsPath gp = Rounded(t2, 4)) g.DrawPath(p, gp); }
            Tx(g, Lx.T("真偽度", "Confidence", "真伪度"), 8.5f, Color.White, t2.X + 8, t2.Y + 12, false, 0); Tx(g, VV.Hit + "/" + VV.Cnt, 8.5f, Color.White, t2.Right - 8, t2.Y + 12, false, 2);
            Tx(g, hasConf && judge != "OFF" ? conf.ToString("0") + "%" : "---", 19f, Color.White, t2.X + t2.Width / 2, t2.Y + 39, true, 1);

            // ---- 解析画像（識別線＝黄、凹凸マーク ×＝緑の細線、シールドの陰の晶線＝黄の破線）
            RectangleF ib = new RectangleF(x, y + 72, PW, 322); BoxLine(g, ib); float ix = x + 5, iy = y + 77, iw = PW - 10, ih = 312;
            using (SolidBrush b = new SolidBrush(Color.Black)) g.FillRectangle(b, ix, iy, iw, ih);
            if (f == null || f.Disp == null)
            {
                string s = VV.Status(); Tx(g, Lx.T("画像待ち", "Waiting for image", "等待图像"), 10f, Lg, ix + iw / 2, iy + ih / 2 - 10, false, 1); if (s != "") Tx(g, s, 8f, Lg, ix + iw / 2, iy + ih / 2 + 10, false, 1);
            }
            else
            {
                ih = Math.Min(312, f.Disp.Height); g.DrawImage(f.Disp, ix, iy, f.Disp.Width, f.Disp.Height); GraphicsState gs = g.Save(); g.SetClip(new RectangleF(ix, iy, iw, ih));
                VResult R = f.R; float sc = (float)f.DispScale;
                if (R != null && R.Valid)
                {
                    foreach (VSeam s in R.Shown())
                    {
                        float mx = ix + (float)s.X * sc, my = iy + (float)s.Y * sc; PointF e; if (SeamLineEnd(f.Proc, R, s.X, s.Y, out e)) using (Pen p = new Pen(LineY, 1.6f)) g.DrawLine(p, mx, my, ix + e.X * sc, iy + e.Y * sc);
                        Cross(g, mx, my, 4.2f, 1f); double qd = s.Mark * Math.PI / 180; float ls = f.Proc == 0 ? -1.3f : 1f;   // NECK はリングが小さいので、角度の文字をリングの外側に出す
                        Tx(g, Math.Round(s.Mark).ToString("0") + "°", 8f, Color.White, mx - (float)Math.Cos(qd) * 17 * ls, my - (float)Math.Sin(qd) * 15 * ls, true, 1);
                    }
                    if (!double.IsNaN(f.HiddenDeg))
                    {   // シールドの陰の晶線（回転からの推算位置。× は付けない）
                        double d = f.HiddenDeg * Math.PI / 180, hx = R.Cx + Math.Cos(d) * R.A, hy = R.Cy + Math.Sin(d) * R.B; PointF e;
                        if (SeamLineEnd(f.Proc, R, hx, hy, out e)) Dash(g, LineY, 1.6f, ix + (float)hx * sc, iy + (float)hy * sc, ix + e.X * sc, iy + e.Y * sc, 5, 4);
                    }
                }
                g.Restore(gs);
                using (Font fn = F(7f, FontStyle.Regular))
                {
                    float nw = g.MeasureString(f.Name, fn, 1000, StringFormat.GenericTypographic).Width + 8; using (SolidBrush b = new SolidBrush(Color.Black)) g.FillRectangle(b, ix, iy + ih - 15, nw, 15); Tx(g, f.Name, 7f, Color.FromArgb(0xE0, 0xE0, 0xE0), ix + 4, iy + ih - 7.5f, false, 0);
                    if (f.Comment != "") { GraphicsState g2 = g.Save(); g.SetClip(new RectangleF(ix, iy, iw, 15)); using (SolidBrush b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(b, ix, iy, iw, 15); Tx(g, f.Comment, 7.5f, f.St == 'N' ? Color.FromArgb(0xFF, 0x90, 0x80) : Color.White, ix + 4, iy + 7.5f, false, 0); g.Restore(g2); }
                }
            }

            // ---- 座標展開グラフ ＋ 真偽度の推移 ＋ 判定履歴
            float uy = y + 396, uh = H - Margin - uy; BoxLine(g, new RectangleF(x, uy, PW, uh));
            VisionProcCfg pc = f != null && f.Proc >= 0 ? VisionCfg.P[f.Proc] : null; bool bright = pc != null && pc.Contour == 0;
            Tx(g, Lx.T("座標展開グラフ　", "Unfolded graph  ", "坐标展开图　") + (bright ? Lx.T("輝度特徴量", "brightness feature", "亮度特征量") : Lx.T("段差特徴量 D [%]", "step feature D [%]", "台阶特征量 D [%]")), 9f, Color.White, x + 5, uy + 11.5f, false, 0);
            float px = x + 6, py = uy + 21, pw = PW - 6 - 64, ph = uh - 21 - 20 - 96; using (SolidBrush b = new SolidBrush(A(PlotBg))) g.FillRectangle(b, px, py, pw, ph);
            if (pc != null)
            {
                double[][] rg = VV.Ranges(pc); double a0 = Math.Floor((rg[0][0] - 8) / 10) * 10, a1 = Math.Ceiling((rg[rg.Length - 1][1] + 8) / 10) * 10; VResult R = f.R; List<VSeam> shown = R != null && R.Valid ? R.Shown() : new List<VSeam>();
                double dmaxv = 0; if (R != null && R.D != null) foreach (double v in R.D) if (!double.IsNaN(v) && v > dmaxv) dmaxv = v;
                double thrTop = pc.Judge == 1 ? Math.Max(pc.Thr, pc.ThrStrong) : pc.Thr, dmax = Math.Max(Math.Max(0.5, dmaxv * 1.35), thrTop * 1.6);
                Func<double, float> X = delegate (double a) { return (float)(px + pw * (a1 - a) / (a1 - a0)); }; Func<double, float> Y = delegate (double v) { return (float)(py + ph * (1 - Math.Min(Math.Max(v, 0), dmax) / dmax)); };
                if (rg.Length >= 2)
                {   // シールド遮蔽範囲（検出範囲の間）
                    using (SolidBrush b = new SolidBrush(A(Color.FromArgb(0x55, 0x55, 0x55)))) g.FillRectangle(b, X(rg[1][0]), py, X(rg[0][1]) - X(rg[1][0]), ph);
                    Tx(g, Lx.T("シールド遮蔽範囲", "Shield-hidden range", "热屏遮挡范围"), 7f, Color.FromArgb(0xB8, 0xB8, 0xB8), (X(rg[0][1]) + X(rg[1][0])) / 2, py + ph * 0.5f, false, 1);
                }
                for (int i = 0; i < rg.Length; i++) for (int k = 0; k < 2; k++) { bool outer = (i == 0 && k == 0) || (i == rg.Length - 1 && k == 1); float vx = X(rg[i][k]); Dash(g, outer ? Color.FromArgb(0x6E, 0xC6, 0xFF) : Color.FromArgb(0xFF, 0x80, 0x80), 0.8f, vx, py, vx, py + ph, 2, 2); }
                Dash(g, Color.White, 1f, px, Y(pc.Thr), px + pw, Y(pc.Thr), 4, 3); if (pc.Judge == 1 && pc.ThrStrong > pc.Thr) Dash(g, Color.FromArgb(0xB0, 0xB0, 0xB0), 1f, px, Y(pc.ThrStrong), px + pw, Y(pc.ThrStrong), 2, 3);
                if (R != null && R.Ang != null)
                {
                    GraphicsState gs = g.Save(); g.SetClip(new RectangleF(px, py - 12, pw, ph + 12)); List<PointF> seg = new List<PointF>();
                    using (Pen p = new Pen(ColPv, 1.3f))
                    {
                        for (int i = 0; i <= R.Ang.Length; i++)
                        {
                            bool ok = i < R.Ang.Length && !double.IsNaN(R.D[i]) && R.Ang[i] >= a0 && R.Ang[i] <= a1;
                            if (ok) seg.Add(new PointF(X(R.Ang[i]), Y(R.D[i]))); else { if (seg.Count > 1) g.DrawLines(p, seg.ToArray()); seg.Clear(); }
                        }
                    }
                    foreach (VSeam s in shown) { float mx = X(s.Deg), my = Y(s.D); Cross(g, mx, my, 3.6f, 1.2f); Tx(g, Math.Round(s.Mark).ToString("0") + "°", 7f, Color.White, mx, my - 9, true, 1); }
                    g.Restore(gs);
                }
                float lx = px + pw + 5; double dm = 0; foreach (VSeam s in shown) if (s.D > dm) dm = s.D;
                Tx(g, "D " + dm.ToString("0.00"), 7.5f, ColPv, lx, py + ph * 0.08f, true, 0);
                Tx(g, Lx.T("閾値 ", "Thr ", "阈值 ") + (pc.Judge == 1 && pc.ThrStrong > pc.Thr ? pc.ThrStrong.ToString("0.0#") + "/" + pc.Thr.ToString("0.0#") : pc.Thr.ToString("0.00")), 7.5f, Color.White, lx, py + ph * 0.30f, true, 0);
                Tx(g, Lx.T("範囲", "Range", "范围"), 7f, Color.FromArgb(0x6E, 0xC6, 0xFF), lx, py + ph * 0.52f, true, 0);
                for (int i = 0; i < rg.Length; i++) Tx(g, rg[i][0].ToString("0.#") + "–" + rg[i][1].ToString("0.#"), 7f, Color.FromArgb(0x6E, 0xC6, 0xFF), lx, py + ph * (0.52f + 0.19f * (i + 1)), true, 0);
                using (Pen p = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0), 0.8f)) for (double a = Math.Ceiling(a0 / 20) * 20; a <= a1; a += 20) { g.DrawLine(p, X(a), py + ph, X(a), py + ph + 2); Tx(g, a.ToString("0"), 6.5f, Lg, X(a), py + ph + 9, false, 1); }
                Tx(g, "°", 6.5f, Lg, px + pw + 4, py + ph + 9, false, 0);
            }
            else Tx(g, VisionCfg.Enable ? VV.JudgeText("OFF") : "", 9f, Lg, px + pw / 2, py + ph / 2, false, 1);
            // 真偽度の推移（直近 15 枚。右端が最新）
            int n = VV.HistN; float cx0 = x + 62, cw = (PW - 62 - 70) / (float)n, gw = cw * n, gy = py + ph + 22, gh = 40;
            using (SolidBrush b = new SolidBrush(A(PlotBg))) g.FillRectangle(b, cx0, gy, gw, gh);
            Func<double, float> YC = delegate (double v) { return (float)(gy + gh * (1 - v / 100.0) * 0.86 + gh * 0.07); };
            Tx(g, Lx.T("真偽度", "Conf.", "真伪度"), 8f, Color.White, x + 6, gy + gh / 2 - 7, false, 0); Tx(g, "[%]", 7f, Lg, x + 6, gy + gh / 2 + 7, false, 0);
            foreach (int v in new int[] { 0, 50, 100 }) Tx(g, v.ToString(), 6f, Lg, cx0 - 3, YC(v), false, 2);
            using (Pen p = new Pen(Color.FromArgb(0x62, 0x62, 0x62), 0.6f)) { g.DrawLine(p, cx0, YC(100), cx0 + gw, YC(100)); g.DrawLine(p, cx0, YC(0), cx0 + gw, YC(0)); }
            Dash(g, ColUsl, 1.2f, cx0, YC(VisionCfg.NgPct), cx0 + gw, YC(VisionCfg.NgPct), 4, 3);
            int hc = VV.Hist.Count, o0 = n - hc; List<PointF> pts = new List<PointF>(); for (int i = 0; i < hc; i++) if (!double.IsNaN(VV.ConfHist[i])) pts.Add(new PointF(cx0 + cw * (o0 + i + 0.5f), YC(VV.ConfHist[i])));
            if (pts.Count > 1) using (Pen p = new Pen(ColPv, 1.4f)) g.DrawLines(p, pts.ToArray());
            for (int i = 0; i < hc; i++) if (VV.Hist[i] != '-' && !double.IsNaN(VV.ConfHist[i])) using (SolidBrush b = new SolidBrush(VV.Hist[i] == 'o' ? ColPv : NgR)) g.FillEllipse(b, cx0 + cw * (o0 + i + 0.5f) - 2, YC(VV.ConfHist[i]) - 2, 4, 4);
            float lx2 = cx0 + gw + 5; Tx(g, Lx.T("真偽度 ", "Conf. ", "真伪度 ") + (hasConf ? conf.ToString("0") + "%" : "---"), 7.5f, ColPv, lx2, gy + 8, true, 0); Tx(g, VV.Hit + "/" + VV.Cnt, 7f, Color.White, lx2, gy + 21, false, 0); Tx(g, Lx.T("NG閾値 ", "NG thr ", "NG阈值 ") + VisionCfg.NgPct, 7f, ColUsl, lx2, gy + 34, true, 0);
            // 判定履歴
            float ry = gy + gh + 5, ch = 17; Tx(g, Lx.T("判定履歴", "History", "判定历史"), 8f, Color.White, x + 6, ry + ch / 2, false, 0);
            for (int i = 0; i < n; i++)
            {
                RectangleF r = new RectangleF(cx0 + cw * i + 1.5f, ry, cw - 3, ch); int k = i - o0; char hv = k >= 0 ? VV.Hist[k] : ' ';
                using (GraphicsPath gp = Rounded(r, 3))
                {
                    if (hv == 'o' || hv == 'n') { using (SolidBrush b = new SolidBrush(hv == 'o' ? OkG : NgR)) g.FillPath(b, gp); if (i == n - 1) using (Pen p = new Pen(Color.White, 1.6f)) g.DrawPath(p, gp); }
                    else using (Pen p = new Pen(Color.FromArgb(hv == '-' ? 0xA0 : 0x60, hv == '-' ? 0xA0 : 0x60, hv == '-' ? 0xA0 : 0x60), 1f)) g.DrawPath(p, gp);
                }
            }
            float ly = ry + ch + 10, lgx = cx0; string[] lab = { Lx.T("検出", "Detected", "检出"), Lx.T("未検出（NG）", "Not detected (NG)", "未检出（NG）"), Lx.T("判定対象外", "Not judged", "不判定") };
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = new RectangleF(lgx, ly - 4, 8, 8); using (GraphicsPath gp = Rounded(r, 1.5f)) { if (i < 2) using (SolidBrush b = new SolidBrush(i == 0 ? OkG : NgR)) g.FillPath(b, gp); else using (Pen p = new Pen(Color.FromArgb(0xA0, 0xA0, 0xA0), 1f)) g.DrawPath(p, gp); }
                lgx += 11 + Tx(g, lab[i], 6.5f, Lg, lgx + 11, ly, false, 0) + 8;
            }
            Tx(g, Lx.T("右端が最新", "newest at right", "右端为最新"), 6.5f, Lg, x + PW - 6, ly, false, 2);
        }
    }
    /// <summary>識別線の終点（元画像の座標）。BODY は消失点から離れる向きにリング長半径×0.55、CROWN は頂点まで。それ以外の工程は線を引かない（× のみ）。
    /// 基準点は 2026-11-19 の赤線 16 本から求めた仮の値（Python 参照実装 seam_overlay.py と同じ）。</summary>
    static bool SeamLineEnd(int proc, VResult R, double x, double y, out PointF e)
    {
        e = PointF.Empty;
        if (proc == 3) { double px = R.Cx - 0.072 * R.A, py = R.Cy + 3.138 * R.B, ux = x - px, uy = y - py, n = Math.Sqrt(ux * ux + uy * uy); if (n < 1e-6) return false; double t = R.A * 0.55; e = new PointF((float)(x + ux / n * t), (float)(y + uy / n * t)); return true; }
        if (proc == 1) { e = new PointF((float)(R.Cx + 0.015 * R.A), (float)(R.Cy - 0.777 * R.B)); return true; }
        return false;
    }
}
}
