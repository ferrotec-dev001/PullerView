// PullerView ロゴ（5案）。GDI+ でベクタ描画するため、タイトル行の実寸（高さ 22px）でも拡大でも崩れない。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PullerView
{

public static class Logo
{
    public static readonly string[] Names = { "テキストのみ", "案1 インゴット＋トレンド（採用）", "案2 スコープ（監視レンズ）", "案3 上下限バー ワードマーク", "案4 引上げ矢印 PV", "案5 PV バッジ" };
    public static readonly string[] Concepts = {
        "",
        "単結晶インゴット（ネック〜ボディ〜テール）のシルエットを緑の点線トレンドが貫く。“引上げ炉の値を見守る” を直截に表現",
        "レンズ（View）の中に USL/LSL の赤線と緑の PV 点。“規格の中に収まっているかを覗く” 監視ツールらしさ",
        "文字そのものをグラフに見立て、上に赤 USL・下に赤 LSL、内側に青 UCL/LCL の細線。アイコンなしで最もコンパクト",
        "引上げ（Pull）を上向き矢印で、矢印の軸を結晶のシード軸に。“V” はトレンドの折れ線で View を表す",
        "角丸バッジに PV モノグラム。V を緑の折れ線に。工程名の白箱と並べたとき統一感が出る" };
    static readonly Color Cyan = Color.FromArgb(0x4F, 0xD2, 0xFF), Green = Color.FromArgb(0x30, 0xFF, 0x30), Red = Color.FromArgb(0xFF, 0x30, 0x30), Blue = Color.FromArgb(0x00, 0xB0, 0xF0);

    // r: ロゴ領域（高さ基準でスケール。基準高さ 22px）
    public static void Draw(Graphics g, int idx, RectangleF r)
    {
        if (idx <= 0) { using (Font f = new Font(M.FontName(), r.Height * 0.43f, FontStyle.Regular, GraphicsUnit.Pixel)) g.DrawString("PullerView", f, Brushes.White, r.X, r.Y + r.Height * 0.12f); return; }
        GraphicsState st = g.Save();
        float s = r.Height / 22f; g.TranslateTransform(r.X, r.Y); g.ScaleTransform(s, s);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        switch (idx) { case 1: Ingot(g); break; case 2: Scope(g); break; case 3: Bars(g); break; case 4: Arrow(g); break; case 5: Badge(g); break; }
        g.Restore(st);
    }
    /// <summary>アイコン表示用: 案1 のインゴット＋トレンドのマーク部分のみ（文字なし）を r に描く。</summary>
    public static void DrawMark(Graphics g, RectangleF r)
    {
        GraphicsState st = g.Save();
        float s = r.Height / 22f; g.TranslateTransform(r.X, r.Y); g.ScaleTransform(s, s);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Ingot(g, false);
        g.Restore(st);
    }
    static Font FB(float px) { return new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel); }
    static Font FR(float px) { return new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel); }
    static void Word(Graphics g, float x, float y, float px, Color c1, Color c2)
    {   // "Puller"(太) + "View"(細・色違い)
        using (Font b = FB(px)) using (Font n = FR(px)) using (SolidBrush s1 = new SolidBrush(c1)) using (SolidBrush s2 = new SolidBrush(c2))
        {
            StringFormat sf = StringFormat.GenericTypographic; sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            g.DrawString("Puller", b, s1, x, y, sf); float w = g.MeasureString("Puller", b, 500, sf).Width;
            g.DrawString("View", n, s2, x + w + px * 0.05f, y, sf);
        }
    }
    static void Ingot(Graphics g) { Ingot(g, true); }
    static void Ingot(Graphics g, bool word)
    {   // 20x22 のインゴット（ネック→クラウン→ボディ→テール）
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddLines(new PointF[] { new PointF(9.2f, 1), new PointF(10.8f, 1), new PointF(10.8f, 4), new PointF(15, 6.5f), new PointF(15, 16), new PointF(10, 20.5f), new PointF(5, 16), new PointF(5, 6.5f), new PointF(9.2f, 4) }); p.CloseFigure();
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(5, 0), new PointF(15, 0), Color.FromArgb(0x9A, 0xE6, 0xFF), Color.FromArgb(0x1F, 0x8F, 0xC8))) g.FillPath(b, p);
            using (Pen pen = new Pen(Color.White, 0.8f)) g.DrawPath(pen, p);
        }
        using (Pen pen = new Pen(Green, 1.6f)) { pen.DashStyle = DashStyle.Dot; g.DrawLines(pen, new PointF[] { new PointF(1, 13), new PointF(5, 11), new PointF(9, 13.5f), new PointF(13, 10), new PointF(19, 12) }); }
        if (word) Word(g, 22, 3.5f, 13, Color.White, Cyan);
    }
    static void Scope(Graphics g)
    {
        RectangleF c = new RectangleF(1.5f, 1.5f, 19, 19);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(0x2A, 0x2A, 0x2A))) g.FillEllipse(b, c);
        using (Pen p = new Pen(Color.White, 1.6f)) g.DrawEllipse(p, c);
        using (GraphicsPath ep = RoundEllipse(c)) g.SetClip(ep);
        using (Pen p = new Pen(Red, 1.4f)) { g.DrawLine(p, 0, 6.5f, 22, 6.5f); g.DrawLine(p, 0, 15.5f, 22, 15.5f); }
        using (Pen p = new Pen(Blue, 1f)) { g.DrawLine(p, 0, 9, 22, 9); g.DrawLine(p, 0, 13, 22, 13); }
        using (Pen p = new Pen(Green, 1.4f)) { p.DashStyle = DashStyle.Dot; g.DrawLines(p, new PointF[] { new PointF(3, 11.5f), new PointF(7, 10), new PointF(11, 12), new PointF(15, 10.5f), new PointF(19, 11.5f) }); }
        g.ResetClip();
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, 9.6f, 9.6f, 2.8f, 2.8f);
        Word(g, 24, 3.5f, 13, Color.White, Cyan);
    }
    static GraphicsPath RoundEllipse(RectangleF r) { GraphicsPath p = new GraphicsPath(); p.AddEllipse(r); return p; }
    static void Bars(Graphics g)
    {
        Word(g, 1, 3.5f, 13.5f, Color.White, Cyan);
        float w = 76;
        using (Pen p = new Pen(Red, 1.5f)) { g.DrawLine(p, 1, 1.5f, w, 1.5f); g.DrawLine(p, 1, 20.5f, w, 20.5f); }
        using (Pen p = new Pen(Blue, 0.8f)) { g.DrawLine(p, 1, 4, w, 4); g.DrawLine(p, 1, 18, w, 18); }
        using (SolidBrush b = new SolidBrush(Green)) { for (float x = 2; x < w; x += 4) g.FillRectangle(b, x, 10.5f, 1.4f, 1.2f); }
    }
    static void Arrow(Graphics g)
    {   // 上向き矢印（引上げ）＋ V（折れ線）
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddLines(new PointF[] { new PointF(7, 1), new PointF(13.5f, 8.5f), new PointF(9.2f, 8.5f), new PointF(9.2f, 21), new PointF(4.8f, 21), new PointF(4.8f, 8.5f), new PointF(0.5f, 8.5f) }); p.CloseFigure();
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, 1), new PointF(0, 21), Cyan, Color.FromArgb(0x1F, 0x8F, 0xC8))) g.FillPath(b, p);
        }
        using (Pen p = new Pen(Green, 2.2f)) { p.LineJoin = LineJoin.Round; g.DrawLines(p, new PointF[] { new PointF(12, 12), new PointF(16.5f, 20.5f), new PointF(21, 12) }); }
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, 15.3f, 19.2f, 2.4f, 2.4f);
        Word(g, 24, 3.5f, 13, Color.White, Cyan);
    }
    static void Badge(Graphics g)
    {
        using (GraphicsPath p = Surface.Rounded(new RectangleF(0.5f, 0.5f, 21, 21), 4))
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, 0), new PointF(21, 21), Color.FromArgb(0x1E, 0x7F, 0xB8), Color.FromArgb(0x0D, 0x3F, 0x66))) g.FillPath(b, p);
            using (Pen pen = new Pen(Color.White, 1f)) g.DrawPath(pen, p);
        }
        using (Font f = FB(14)) g.DrawString("P", f, Brushes.White, 1.5f, 1.5f, StringFormat.GenericTypographic);
        using (Pen p = new Pen(Green, 2.4f)) { p.LineJoin = LineJoin.Round; g.DrawLines(p, new PointF[] { new PointF(11, 8), new PointF(15, 17), new PointF(19.5f, 8) }); }
        Word(g, 25, 3.5f, 13, Color.White, Cyan);
    }

    // 提案シート（5案を拡大＋実寸で並べる）
    public static Bitmap Sheet()
    {
        int W = 1000, rowH = 150; Bitmap bmp = new Bitmap(W, 60 + rowH * 5);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(0x2B, 0x2B, 0x2B)); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Font f = new Font("Yu Gothic UI", 15, FontStyle.Bold)) g.DrawString("PullerView ロゴ提案（左: 4倍拡大 ／ 右: タイトル行の実寸・高さ 22px）", f, Brushes.White, 20, 16);
            for (int i = 1; i <= 5; i++)
            {
                int y = 60 + (i - 1) * rowH;
                using (Pen p = new Pen(Color.FromArgb(0x55, 0x55, 0x55))) g.DrawLine(p, 20, y, W - 20, y);
                using (Font f = new Font("Yu Gothic UI", 12, FontStyle.Bold)) g.DrawString(Names[i], f, Brushes.White, 20, y + 10);
                using (Font f = new Font("Yu Gothic UI", 9)) g.DrawString(Concepts[i], f, new SolidBrush(Color.FromArgb(0xC8, 0xC8, 0xC8)), new RectangleF(20, y + 32, 430, 60));
                // 4倍
                RectangleF big = new RectangleF(470, y + 20, 360, 88);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0x4D, 0x4D, 0x4D))) g.FillRectangle(b, 460, y + 12, 380, 104);
                Draw(g, i, big);
                // 実寸: タイトル行の再現
                RectangleF bar = new RectangleF(850, y + 40, 130, 30);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0x4D, 0x4D, 0x4D))) g.FillRectangle(b, bar);
                using (Pen p = new Pen(Color.White, 1.5f)) g.DrawRectangle(p, bar.X, bar.Y, bar.Width, bar.Height);
                Draw(g, i, new RectangleF(bar.X + 6, bar.Y + 4, 80, 22));
                RectangleF box = new RectangleF(bar.X + 100, bar.Y + 6, 34, 18); using (GraphicsPath gp = Surface.Rounded(box, 2)) g.FillPath(Brushes.White, gp);
                using (Font f = new Font("Yu Gothic UI", 7f)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString("BODY", f, Brushes.Black, box, sf); }
            }
        }
        return bmp;
    }
}
}
