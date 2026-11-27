// RoiForm.cs — 探索枠の設定画面（V2-v11）
// 最後に受け取った画像の全体を表示し、リング（メニスカス）を探す範囲を、画像上のドラッグまたは数値（画像に対する %）で工程ごとに指定する。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PullerView
{
    public class RoiPanel : DarkPanel
    {
        readonly int pi; readonly VisionProcCfg pc; readonly bool oRoi, oAuto; readonly double ox, oy, ow, oh;
        Bitmap img; Size full; Rectangle box, win; int imgProc = -1; long seen = -1; Rectangle ir; bool drag; PointF d0; readonly Timer tm = new Timer();
        public event Action<bool> Done;
        public RoiPanel(int proc)
        {
            pi = proc; pc = VisionCfg.P[proc]; oRoi = pc.Roi; oAuto = pc.RoiAuto; ox = pc.Rx; oy = pc.Ry; ow = pc.Rw; oh = pc.Rh;
            Fetch(); tm.Interval = 500; tm.Tick += delegate { if (VV.Frames != seen) { Fetch(); Invalidate(); } }; tm.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing) { tm.Stop(); tm.Dispose(); if (img != null) img.Dispose(); } base.Dispose(disposing); }
        protected override void OnTouched() { M.Fire(); }
        void Set(Action a) { a(); Touch(); }
        public void Cancel() { pc.Roi = oRoi; pc.RoiAuto = oAuto; pc.Rx = ox; pc.Ry = oy; pc.Rw = ow; pc.Rh = oh; }
        void Fetch()
        {
            lock (VV.Lk)
            {
                seen = VV.Frames; if (VV.Full == null) return;
                try { Bitmap b = (Bitmap)VV.Full.Clone(); if (img != null) img.Dispose(); img = b; full = VV.FullSize; box = VV.FullBox; win = VV.FullWin; imgProc = VV.FullProc; } catch (Exception) { }
            }
        }
        void Clamp() { pc.Rw = Math.Max(1, Math.Min(100, pc.Rw)); pc.Rh = Math.Max(1, Math.Min(100, pc.Rh)); pc.Rx = Math.Max(0, Math.Min(100 - pc.Rw, pc.Rx)); pc.Ry = Math.Max(0, Math.Min(100 - pc.Rh, pc.Ry)); }
        PointF Pct(Point p) { return new PointF(Math.Max(0, Math.Min(100, (p.X - ir.X) * 100f / ir.Width)), Math.Max(0, Math.Min(100, (p.Y - ir.Y) * 100f / ir.Height))); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !ir.IsEmpty && ir.Contains(e.Location)) { CommitIfValid(); active = null; drag = true; d0 = Pct(e.Location); Capture = true; return; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag)
            {
                PointF p = Pct(e.Location); double x = Math.Min(d0.X, p.X), y = Math.Min(d0.Y, p.Y), w = Math.Abs(p.X - d0.X), h = Math.Abs(p.Y - d0.Y);
                if (w >= 1 && h >= 1) { pc.Rx = Math.Round(x, 1); pc.Ry = Math.Round(y, 1); pc.Rw = Math.Round(w, 1); pc.Rh = Math.Round(h, 1); pc.Roi = true; Clamp(); Touch(); }
                return;
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { if (drag) { drag = false; Capture = false; Invalidate(); return; } base.OnMouseUp(e); }
        RectangleF Scr(Rectangle r) { return new RectangleF(ir.X + r.X * (float)ir.Width / full.Width, ir.Y + r.Y * (float)ir.Height / full.Height, r.Width * (float)ir.Width / full.Width, r.Height * (float)ir.Height / full.Height); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; BeginPaint(g); Clamp();
            Rectangle area = new Rectangle(12, 12, 688, 470); Box(g, area, CARD, null, 10, 1); ir = Rectangle.Empty;
            if (img == null) TxtC(g, Lx.T("画像待ち（画像を 1 枚受け取ると表示します）", "Waiting for an image", "等待图像（收到 1 张图像后显示）"), 10f, GR, area, false);
            else
            {
                int ih = area.Height - 16, iw = (int)Math.Round(ih * (double)img.Width / img.Height); if (iw > area.Width - 16) { iw = area.Width - 16; ih = (int)Math.Round(iw * (double)img.Height / img.Width); }
                ir = new Rectangle(area.X + (area.Width - iw) / 2, area.Y + (area.Height - ih) / 2, iw, ih);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(img, ir); GraphicsState gs = g.Save(); g.SetClip(ir);
                if (!win.IsEmpty && imgProc == pi) using (Pen p = new Pen(Color.FromArgb(0xFF, 0xE6, 0x00), 1.2f)) { p.DashPattern = new float[] { 4, 3 }; RectangleF r = Scr(win); g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
                if (!box.IsEmpty && imgProc == pi) using (Pen p = new Pen(Color.FromArgb(0, 255, 0), 1.5f)) { RectangleF r = Scr(box); g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
                RectangleF f = new RectangleF(ir.X + (float)(pc.Rx / 100 * ir.Width), ir.Y + (float)(pc.Ry / 100 * ir.Height), (float)(pc.Rw / 100 * ir.Width), (float)(pc.Rh / 100 * ir.Height));
                using (Pen p = new Pen(pc.Roi ? CY : GR, 2f)) g.DrawRectangle(p, f.X, f.Y, f.Width, f.Height);
                g.Restore(gs);
            }
            int y = 490;
            ToggleRow(g, 24, y, 310, Lx.T("探索枠を使う（" + pc.Name + "）", "Use the search frame (" + pc.Name + ")", "使用搜索框（" + pc.Name + "）"), pc.Roi, delegate (bool v) { Set(delegate { pc.Roi = v; }); });
            ToggleRow(g, 366, y, 322, Lx.T("枠の中でメニスカスを自動検出", "Auto-detect the meniscus in the frame", "在框内自动检出光圈"), pc.RoiAuto, delegate (bool v) { Set(delegate { pc.RoiAuto = v; }); });
            string[] lb = { Lx.T("左 [%]", "Left [%]", "左 [%]"), Lx.T("上 [%]", "Top [%]", "上 [%]"), Lx.T("幅 [%]", "Width [%]", "宽 [%]"), Lx.T("高さ [%]", "Height [%]", "高 [%]") };
            Func<double>[] gt = { delegate { return pc.Rx; }, delegate { return pc.Ry; }, delegate { return pc.Rw; }, delegate { return pc.Rh; } };
            Action<double>[] st = { delegate (double v) { pc.Rx = v; }, delegate (double v) { pc.Ry = v; }, delegate (double v) { pc.Rw = v; }, delegate (double v) { pc.Rh = v; } };
            for (int i = 0; i < 4; i++)
            {
                int x = 24 + i * 170; Txt(g, lb[i], 8.5f, GR, x, y + 38, false);
                NumField nf = Field("roi" + i, lb[i], gt[i], st[i], false, new Rectangle(x, y + 56, 110, 32), i < 2 ? 0 : 1, 100, 0.5); NumBox(g, nf.R, nf, "%");
            }
            string note = img == null ? "" : Lx.T("水色＝探索枠（画像上をドラッグして指定）　緑＝見つけたメニスカス　黄の破線＝解析する範囲", "Cyan = search frame (drag on the image)   Green = meniscus found   Yellow dashed = analysed area", "浅蓝＝搜索框（在图像上拖动指定）　绿＝找到的光圈　黄色虚线＝解析范围");
            Txt(g, note, 7.5f, GR, 24, y + 96, false);
            if (img != null && full.Width > 0) Txt(g, Lx.T("画像 ", "Image ", "图像 ") + full.Width + "×" + full.Height + (imgProc >= 0 && imgProc != pi ? Lx.T("　※ 表示中の画像は別の工程（" + VisionCfg.P[imgProc].Name + "）のものです", "   * The image shown belongs to another process (" + VisionCfg.P[imgProc].Name + ")", "　※ 显示的图像属于其他工程（" + VisionCfg.P[imgProc].Name + "）") : ""), 7.5f, imgProc >= 0 && imgProc != pi ? AMB : GR, 24, y + 112, false);
            string[] bt = { Lx.T("キャンセル", "Cancel", "取消"), "OK" };
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = new Rectangle(700 - 12 - (2 - i) * 118 + 6, Height - 12 - 38, 112, 38); bool ok = i == 1; Box(g, r, ok ? (Hover(r) ? ControlPaint.Light(CY, 0.3f) : CY) : Hover(r) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 8, 1); TxtC(g, bt[i], 10f, ok ? DK : WH, r, ok);
                Add(r, delegate { if (ok && !CommitIfValid()) return; if (!ok) Cancel(); else Clamp(); if (Done != null) Done(ok); }, null);
            }
            DrawNumPad(g, 712, Lx.T("・枠は画像上のドラッグでも指定可", "- The frame can also be drawn by dragging", "・也可在图像上拖动指定搜索框"));
        }
    }
    public class RoiForm : Form
    {
        readonly RoiPanel p;
        public RoiForm(int proc)
        {
            Text = Lx.T("探索枠の設定 — ", "Search frame — ", "搜索框设置 — ") + VisionCfg.P[proc].Name; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; TopMost = true;
            ClientSize = new Size(1000, 672); BackColor = DarkPanel.BG; p = new RoiPanel(proc); p.Bounds = new Rectangle(0, 0, 1000, 672); Controls.Add(p);
            bool closed = false; p.Done += delegate (bool ok) { closed = true; DialogResult = ok ? DialogResult.OK : DialogResult.Cancel; Close(); };
            FormClosing += delegate { if (!closed) p.Cancel(); };   // × で閉じたときはキャンセル扱い
        }
    }
}
