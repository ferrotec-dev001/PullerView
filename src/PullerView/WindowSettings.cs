using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PullerView
{
    /// <summary>「ウィンドウ／表示」タブ。グラフ設定タブと同じ DarkPanel 部品（カード・セグメント・トグル・スライダー・テンキー）で構成。
    /// 変更は即時に M.S（PullerView.ini の内容）へ反映し M.Fire() で PullerView を再描画する（保存はフッターの「保存」）。</summary>
    public class WindowSettingsPanel : DarkPanel
    {
        public WindowSettingsPanel() { }
        protected override void OnTouched() { M.Fire(); }
        void Set(Action a) { a(); Touch(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; BeginPaint(g);
            Settings s = M.S;
            int colW = 340, midX = 364, midW = 336;
            // ===================== 左列 =====================
            // ---- ウィンドウ ----
            Rectangle c1 = new Rectangle(12, 12, colW, 296); Card(g, c1, Lx.T("ウィンドウ", "Window", "窗口"));
            int x = c1.X + 16, y = c1.Y;
            Label(g, Lx.T("言語", "Language", "语言"), x, y + 36);
            Segment(g, x, y + 54, 34, new[] { "日本語", "English", "中文" }, s.Lang, delegate (int i) { Set(delegate { s.Lang = i; }); }, 9.5f);
            Label(g, Lx.T("背景色", "Background color", "背景色"), x, y + 100);
            ColorBtn(g, new Rectangle(x, y + 118, 110, 34), s.BgColor, delegate (Color c) { Set(delegate { s.BgColor = c; }); });
            Label(g, Lx.T("透過方式", "Transparency mode", "透明方式"), x + 130, y + 100);
            Segment(g, x + 130, y + 119, 32, new[] { Lx.T("背景のみ", "Background", "仅背景"), Lx.T("全体", "Whole", "整体") }, s.BgOnly ? 0 : 1, delegate (int i) { Set(delegate { s.BgOnly = i == 0; }); }, 9f);
            Label(g, Lx.T("背景の不透明度", "Background opacity", "背景不透明度"), x, y + 164);
            NumField fo = Field("opa", Lx.T("背景の不透明度 [%]", "Background opacity [%]", "背景不透明度 [%]"), delegate { return s.OpacityPct; }, delegate (double v) { s.OpacityPct = (int)v; }, true, new Rectangle(x + 232, y + 182, 76, 34), 0, 100, 5);
            Slider(g, new Rectangle(x - 4, y + 182, 228, 34), s.OpacityPct, 0, 100, delegate (double v) { s.OpacityPct = (int)Math.Round(v); Touch(); }, CY);
            NumBox(g, fo.R, fo, "%");
            Txt(g, Lx.T("※ 背景のみ = 線・文字・枠は不透明のまま背景だけ透ける", "※ Background = lines/text stay opaque, only backdrop is see-through", "※ 仅背景 = 线条/文字不透明，仅背景透明"), 7.5f, GR, x, y + 226, false);
            Txt(g, Lx.T("    全体 = ウィンドウ全体が半透明になる", "    Whole = the entire window becomes translucent", "    整体 = 整个窗口半透明"), 7.5f, GR, x, y + 241, false);
            Txt(g, Lx.T("※ ロゴは案1（インゴット＋トレンド）固定", "※ Logo is fixed to Plan 1 (ingot + trend)", "※ 标志固定为方案1（晶锭＋趋势）"), 7.5f, GR, x, y + 248, false);
            // ---- コメント領域 ----
            Rectangle c2 = new Rectangle(12, 320, colW, 272); Card(g, c2, Lx.T("コメント領域", "Comment area", "注释区"));
            y = c2.Y;
            Label(g, Lx.T("文字色", "Text color", "文字颜色"), x, y + 36);
            ColorBtn(g, new Rectangle(x, y + 54, 110, 34), s.CmtColor, delegate (Color c) { Set(delegate { s.CmtColor = c; }); });
            Label(g, Lx.T("文字の大きさ", "Font size", "文字大小"), x + 150, y + 36);
            NumField fs = Field("cmtsize", Lx.T("文字の大きさ [pt]", "Font size [pt]", "文字大小 [pt]"), delegate { return s.CmtSize; }, delegate (double v) { s.CmtSize = (int)v; }, true, new Rectangle(x + 150, y + 54, 76, 34), 8, 24, 1);
            NumBox(g, fs.R, fs, "pt"); StepBtns(g, x + 232, y + 54, fs);
            Label(g, Lx.T("スクロール向き", "Scroll direction", "滚动方向"), x, y + 98);
            Segment(g, x, y + 116, 32, new[] { Lx.T("右 → 左", "Right → Left", "右 → 左"), Lx.T("左 → 右", "Left → Right", "左 → 右") }, s.CmtLeft ? 0 : 1, delegate (int i) { Set(delegate { s.CmtLeft = i == 0; }); }, 9f);
            Label(g, Lx.T("速さ", "Speed", "速度"), x, y + 158);
            NumField fv = Field("cmtspd", Lx.T("スクロール速さ (1〜10)", "Scroll speed (1–10)", "滚动速度 (1～10)"), delegate { return s.CmtSpeed; }, delegate (double v) { s.CmtSpeed = (int)v; }, true, new Rectangle(x + 232, y + 176, 76, 34), 1, 10, 1);
            Slider(g, new Rectangle(x - 4, y + 176, 228, 34), s.CmtSpeed, 1, 10, delegate (double v) { s.CmtSpeed = (int)Math.Round(v); Touch(); }, CY);
            NumBox(g, fv.R, fv, null);
            // プレビュー
            Rectangle pv = new Rectangle(x, y + 216, colW - 32, 26); Box(g, pv, Color.FromArgb(0x3C, 0x3C, 0x3C), null, 6, 1);
            using (Font f = new Font(UiFont, s.CmtSize, GraphicsUnit.Point)) using (SolidBrush b = new SolidBrush(s.CmtColor)) { StringFormat sf = new StringFormat(); sf.LineAlignment = StringAlignment.Center; sf.FormatFlags = StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter; g.DrawString((s.CmtLeft ? "◀ " : "▶ ") + Lx.T("コメント表示例 Sample 123", "Comment sample 123", "注释示例 Sample 123"), f, b, pv, sf); }

            // ===================== 中列 =====================
            // ---- 判定・表示 ----
            Rectangle c3 = new Rectangle(midX, 12, midW, 296); Card(g, c3, Lx.T("判定・表示", "Judgement / Display", "判定・显示"));
            x = c3.X + 16; y = c3.Y;
            Label(g, Lx.T("異常確定までの連続秒数", "Seconds to confirm abnormal", "异常确定的连续秒数"), x, y + 36);
            NumField fj = Field("judge", Lx.T("異常確定までの連続秒数 [s]", "Seconds to confirm abnormal [s]", "异常确定的连续秒数 [s]"), delegate { return s.JudgeSec; }, delegate (double v) { s.JudgeSec = (int)v; }, true, new Rectangle(x, y + 54, 76, 34), 1, 10, 1);
            NumBox(g, fj.R, fj, "s"); StepBtns(g, x + 82, y + 54, fj);
            Txt(g, Lx.T("※ 管理外／規格外が連続してこの秒数続いたら確定（背景色を変える）", "※ Abnormal is confirmed after this many consecutive seconds", "※ 连续超限达到此秒数后确定异常（改变背景色）"), 7.5f, GR, x, y + 94, false);
            Label(g, Lx.T("縦軸", "Y axis", "纵轴"), x, y + 120);
            Segment(g, x, y + 138, 32, new[] { Lx.T("固定（lsl〜usl ±20%）", "Fixed (lsl–usl ±20%)", "固定（lsl～usl ±20%）"), Lx.T("自動スケール", "Auto scale", "自动缩放") }, s.AutoY ? 1 : 0, delegate (int i) { Set(delegate { s.AutoY = i == 1; }); }, 9f);
            ToggleRow(g, x, y + 186, midW - 32, Lx.T("単体起動（VB_FERROTEC.exe 無しでも表示）", "Standalone (no VB_FERROTEC.exe)", "独立启动（无 VB_FERROTEC.exe 也显示）"), s.Standalone, delegate (bool b) { Set(delegate { s.Standalone = b; }); });
            Label(g, Lx.T("工程追従", "Process tracking", "工序跟踪"), x, y + 238);
            Segment(g, x + 110, y + 232, 30, new[] { Lx.T("自動（Remoting）", "Auto (Remoting)", "自动（Remoting）"), Lx.T("手動", "Manual", "手动") }, s.ProcAuto ? 0 : 1, delegate (int i) { Set(delegate { s.ProcAuto = i == 0; }); }, 8.5f);
            // ---- グラフ X 軸 ----
            Rectangle c4 = new Rectangle(midX, 320, midW, 272); Card(g, c4, Lx.T("グラフ X 軸（結晶長さ）", "Graph X axis (crystal length)", "图表 X 轴（晶体长度）"));
            y = c4.Y;
            Label(g, Lx.T("最大値", "Maximum", "最大值"), x, y + 36);
            NumField fx = Field("xmax", Lx.T("X 軸最大値 [mm]", "X axis maximum [mm]", "X 轴最大值 [mm]"), delegate { return s.XMax; }, delegate (double v) { s.XMax = v; }, true, new Rectangle(x, y + 54, 96, 34), 500, 10000, 100);
            NumBox(g, fx.R, fx, "mm"); StepBtns(g, x + 102, y + 54, fx);
            double[] quick = { 2000, 2500, 3000, 3500 }; int qx = x + 190;
            for (int i = 0; i < quick.Length; i++) { Rectangle r = new Rectangle(qx + (i % 2) * 56, y + 54 + (i / 2) * 20 - (i / 2 == 0 ? 2 : 0), 52, 16); double q = quick[i]; bool on = s.XMax == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString("0"), 7.5f, on ? CY : GR, r, false); Add(r, delegate { Set(delegate { s.XMax = q; }); }, null); }
            Label(g, Lx.T("目盛間隔", "Tick spacing", "刻度间隔"), x, y + 98);
            string[] sp = { Lx.T("自動", "Auto", "自动"), "100", "200", "250", "500", "1000" }; double[] spv = { 0, 100, 200, 250, 500, 1000 };
            int sel = 0; for (int i = 1; i < spv.Length; i++) if (s.XSpan == spv[i]) sel = i;
            Segment(g, x, y + 116, 30, sp, sel, delegate (int i) { Set(delegate { s.XSpan = spv[i]; }); }, 8.5f, 16);
            Txt(g, Lx.T("※ 自動 = 最大値から 5〜8 本になるよう選択", "※ Auto = 5–8 ticks chosen from the maximum", "※ 自动 = 按最大值选择 5～8 条刻度"), 7.5f, GR, x, y + 152, false);
            // 軸プレビュー
            Rectangle ax = new Rectangle(x, y + 176, midW - 32, 60); Box(g, ax, Color.FromArgb(0x3C, 0x3C, 0x3C), null, 6, 1);
            double span = s.SpanFor(s.XMax); int ax0 = ax.X + 14, ax1 = ax.Right - 14, ay = ax.Y + 30;
            using (Pen p = new Pen(GR, 1f)) { g.DrawLine(p, ax0, ay, ax1, ay); for (double v = 0; v <= s.XMax + 0.001; v += span) { int tx = ax0 + (int)((ax1 - ax0) * v / s.XMax); g.DrawLine(p, tx, ay - 4, tx, ay + 4); TxtC(g, v.ToString("0"), 7f, GR, new RectangleF(tx - 24, ay + 6, 48, 14), false); } }
            Txt(g, Lx.T("0 〜 ", "0 – ", "0 ～ ") + s.XMax.ToString("0") + " mm  /  " + Lx.T("目盛 ", "tick ", "刻度 ") + span.ToString("0") + " mm", 7.5f, CY, ax.X + 10, ax.Y + 6, false);

            if (err != "" && (DateTime.Now - errAt).TotalSeconds < 4) Txt(g, err, 8.5f, RED, 16, 596, true);
            else Txt(g, Lx.T("※ 変更は直ちに PullerView へ反映されます。保存するには下の「保存」を押してください（PullerView.ini）", "※ Changes apply to PullerView immediately. Press Save below to write PullerView.ini", "※ 更改会立即反映到 PullerView。按下方“保存”写入 PullerView.ini"), 7.5f, GR, 16, 597, false);

            // ===================== 右: テンキー =====================
            DrawNumPad(g, 712, Lx.T("・不透明度・速さはスライダーでも変更可", "- Opacity / speed can also be set with the sliders", "・不透明度、速度也可用滑块调整"));
        }
        void Card(Graphics g, Rectangle r, string title) { Box(g, r, CARD, null, 10, 1); Txt(g, title, 10f, WH, r.X + 16, r.Y + 10, true); using (Pen p = new Pen(LINE)) g.DrawLine(p, r.X + 16, r.Y + 30, r.Right - 16, r.Y + 30); }
        void Label(Graphics g, string s, int x, int y) { Txt(g, s, 8.5f, GR, x, y, false); }
        /// <summary>数値欄の横の −／＋ ボタン（Step 刻み）。</summary>
        void StepBtns(Graphics g, int x, int y, NumField f)
        {
            string[] t = { "−", "＋" }; double[] d = { -1, 1 };
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = new Rectangle(x + i * 40, y, 36, 34); Box(g, r, Hover(r) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 8, 1); TxtC(g, t[i], 12f, WH, r, false);
                double dd = d[i] * Math.Max(1, f.Step);
                Add(r, delegate { double v = f.Get() + dd; if (v < f.Min || v > f.Max) return; if (active == f) buf = ""; f.Set(v); Touch(); }, null);
            }
        }
    }
}
