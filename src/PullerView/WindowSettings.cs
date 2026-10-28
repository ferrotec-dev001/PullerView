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
        void PickFont(Action<string> set, string cur)
        {
            using (Form f = new Form())
            {
                f.Text = Lx.T("フォントを選ぶ", "Pick font", "选择字体"); f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false; f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(520, 480); f.BackColor = BG; f.ForeColor = WH; f.Font = new Font(UiFont, 11f);
                ListView lv = new ListView(); lv.View = View.Details; lv.FullRowSelect = true; lv.HideSelection = false; lv.BackColor = FIELD; lv.ForeColor = WH; lv.Font = new Font(UiFont, 11f); lv.SetBounds(12, 12, 496, 360); lv.MultiSelect = false;
                lv.Columns.Add(Lx.T("フォント", "Font", "字体"), 300); lv.Columns.Add("", 170);
                ListViewItem it0 = new ListViewItem(Lx.T("自動（言語ごとに選択）", "Auto (by language)", "自动（按语言）")); it0.SubItems.Add(M.FontName()); it0.Tag = ""; lv.Items.Add(it0); if (cur == "") it0.Selected = true;
                string[] rec = { "Meiryo UI", "Yu Gothic UI", "MS UI Gothic", "Microsoft YaHei UI", "Microsoft YaHei", "Microsoft JhengHei UI", "Segoe UI", "Arial", "Tahoma", "Verdana" };
                List<string> added = new List<string>();
                foreach (string n in rec) { try { using (Font tf = new Font(n, 9f)) if (tf.Name != n) continue; } catch (Exception) { continue; } ListViewItem it = new ListViewItem(n); it.SubItems.Add(Lx.T("推奨", "Recommended", "推荐")); it.Tag = n; lv.Items.Add(it); added.Add(n); if (string.Compare(n, cur, true) == 0) it.Selected = true; }
                try { using (System.Drawing.Text.InstalledFontCollection ic = new System.Drawing.Text.InstalledFontCollection()) foreach (FontFamily ff in ic.Families) { if (added.Contains(ff.Name) || !ff.IsStyleAvailable(FontStyle.Regular)) continue; ListViewItem it = new ListViewItem(ff.Name); it.SubItems.Add(""); it.Tag = ff.Name; lv.Items.Add(it); if (string.Compare(ff.Name, cur, true) == 0) it.Selected = true; } } catch (Exception) { }
                Label pv = new Label(); pv.SetBounds(12, 380, 496, 40); pv.BackColor = FIELD; pv.ForeColor = WH; pv.TextAlign = ContentAlignment.MiddleLeft; pv.Text = "  " + Lx.T("プレビュー: ヒーター温度 1455.9 deg  USL 1500", "Preview: Heater temp 1455.9 deg  USL 1500", "预览: 加热器温度 1455.9 deg  USL 1500"); f.Controls.Add(pv);
                lv.SelectedIndexChanged += delegate { if (lv.SelectedItems.Count > 0) { string n = (string)lv.SelectedItems[0].Tag; if (n == "") n = M.FontName(); try { pv.Font = new Font(n, 9f); } catch (Exception) { } } };
                f.Controls.Add(lv);
                Button ok = new Button(); ok.Text = Lx.T("選択", "Select", "选择"); ok.SetBounds(300, 432, 100, 38); ok.DialogResult = DialogResult.OK; ok.FlatStyle = FlatStyle.Flat; ok.BackColor = CY; ok.ForeColor = DK; f.Controls.Add(ok);
                Button ca = new Button(); ca.Text = Lx.T("キャンセル", "Cancel", "取消"); ca.SetBounds(408, 432, 100, 38); ca.DialogResult = DialogResult.Cancel; ca.FlatStyle = FlatStyle.Flat; ca.BackColor = BTN; ca.ForeColor = WH; f.Controls.Add(ca);
                lv.DoubleClick += delegate { if (lv.SelectedItems.Count > 0) f.DialogResult = DialogResult.OK; };
                f.AcceptButton = ok; f.CancelButton = ca;
                if (f.ShowDialog(this) == DialogResult.OK && lv.SelectedItems.Count > 0) set((string)lv.SelectedItems[0].Tag);
            }
        }

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
            // ---- フォント ----
            Label(g, Lx.T("フォント", "Font", "字体"), x, y + 254);
            Rectangle fb = new Rectangle(x, y + 270, 150, 24); Box(g, fb, Hover(fb) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, LINE, 6, 1);
            TxtC(g, (s.FontName == "" ? Lx.T("自動", "Auto", "自动") + " (" + M.FontName() + ")" : s.FontName) + " ▾", 7.5f, WH, fb, false);
            Add(fb, delegate { PickFont(delegate (string n) { Set(delegate { s.FontName = n; }); }, s.FontName); }, null);
            Label(g, Lx.T("文字の描画", "Text rendering", "文字渲染"), x + 162, y + 254);
            Segment(g, x + 162, y + 270, 24, new[] { Lx.T("なめらか", "Smooth", "平滑"), Lx.T("くっきり", "Sharp", "清晰"), Lx.T("非AA", "No AA", "无抗锯齿") }, s.FontRender, delegate (int i) { Set(delegate { s.FontRender = i; }); }, 7.5f, 8);
            // ---- コメント領域 ----
            Rectangle c2 = new Rectangle(12, 320, colW, Height - 12 - 320);   // 下端をテンキーカード（Height-12）に揃える Card(g, c2, Lx.T("コメント領域", "Comment area", "注释区"));
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
            Label(g, Lx.T("縦軸", "Y axis", "纵轴"), x, y + 118);
            Segment(g, x, y + 136, 32, new[] { Lx.T("固定（lsl〜usl ±20%）", "Fixed (lsl–usl ±20%)", "固定（lsl～usl ±20%）"), Lx.T("自動スケール", "Auto scale", "自动缩放") }, s.AutoY ? 1 : 0, delegate (int i) { Set(delegate { s.AutoY = i == 1; }); }, 9f);
            ToggleRow(g, x, y + 178, midW - 32, Lx.T("単体起動（VB_FERROTEC.exe 無しでも表示）", "Standalone (no VB_FERROTEC.exe)", "独立启动（无 VB_FERROTEC.exe 也显示）"), s.Standalone, delegate (bool b) { Set(delegate { s.Standalone = b; }); });
            Label(g, Lx.T("データリスト", "Data list", "数据列表"), x, y + 214);
            Segment(g, x + 110, y + 208, 30, new[] { Lx.T("McMeta.ini に従う", "Use McMeta.ini", "以 McMeta.ini 为准"), Lx.T("受信で上書き", "Overwrite", "接收覆盖") }, s.McMetaUpdate ? 1 : 0, delegate (int i) { Set(delegate { s.McMetaUpdate = i == 1; }); }, 8.5f);
            Label(g, Lx.T("工程追従", "Process tracking", "工序跟踪"), x, y + 254);
            Segment(g, x + 110, y + 248, 30, new[] { Lx.T("自動（Remoting）", "Auto (Remoting)", "自动（Remoting）"), Lx.T("手動", "Manual", "手动") }, s.ProcAuto ? 0 : 1, delegate (int i) { Set(delegate { s.ProcAuto = i == 0; }); }, 8.5f);
            // ---- グラフ X 軸 ----
            Rectangle c4 = new Rectangle(midX, 320, midW, Height - 12 - 320); Card(g, c4, Lx.T("グラフ X 軸", "Graph X axis", "图表 X 轴"));
            y = c4.Y;
            // --- 結晶長さ軸（NECK〜TAIL）---  v41: 共通／工程別／自動拡張
            Txt(g, Lx.T("結晶長さ軸（NECK〜TAIL）", "Crystal length axis (NECK–TAIL)", "晶体长度轴（NECK～TAIL）"), 8.5f, CY, x, y + 34, true);
            Label(g, Lx.T("最大値", "Maximum", "最大值"), x, y + 54);
            Segment(g, x + 68, y + 48, 24, new[] { Lx.T("共通", "Common", "共用"), Lx.T("工程別", "Per process", "按工序"), Lx.T("自動拡張", "Auto extend", "自动扩展") }, s.XMode, delegate (int i) { Set(delegate { s.XMode = i; }); active = null; buf = ""; }, 8f, 12);
            int qx = x + 190;
            if (s.XMode == 0)
            {
                NumField fx = Field("xmax", Lx.T("X 軸最大値 [mm]", "X axis maximum [mm]", "X 轴最大值 [mm]"), delegate { return s.XMax; }, delegate (double v) { s.XMax = v; }, true, new Rectangle(x, y + 80, 96, 32), 10, 10000, 100);
                NumBox(g, fx.R, fx, "mm"); StepBtns(g, x + 102, y + 80, fx);
                double[] quick = { 2000, 2500, 3000, 3500 };
                for (int i = 0; i < quick.Length; i++) { Rectangle r = new Rectangle(qx + (i % 2) * 56, y + 79 + (i / 2) * 18, 52, 15); double q = quick[i]; bool on = s.XMax == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString("0"), 7.5f, on ? CY : WH, r, false); Add(r, delegate { Set(delegate { s.XMax = q; }); }, null); }
                Txt(g, Lx.T("全工程で同じ最大値（超過時は目盛単位で伸長）", "Same maximum for all processes (extends by tick when exceeded)", "所有工序使用同一最大值（超过时按刻度延伸）"), 7.5f, GR, x, y + 116, false);
            }
            else if (s.XMode == 1)
            {
                List<string> lp = new List<string>(); foreach (string pn in M.Procs) if (s.IsLengthProc(pn)) lp.Add(pn);
                int n = Math.Max(1, lp.Count), fw = Math.Min(96, (midW - 32 + 6) / n - 6), fx0 = x;
                for (int i = 0; i < lp.Count; i++)
                {
                    string pn = lp[i]; int rx = fx0 + i * (fw + 6);
                    Txt(g, pn, 7.5f, GR, rx + 2, y + 76, false);
                    NumField f = Field("xmax_" + pn, pn + " " + Lx.T("X 軸最大値 [mm]", "X axis maximum [mm]", "X 轴最大值 [mm]"), delegate { return s.XMaxFor(pn); }, delegate (double v) { s.XMaxProc[pn] = v; }, true, new Rectangle(rx, y + 90, fw, 30), 10, 10000, 100);
                    NumBox(g, f.R, f, "");
                }
                Txt(g, Lx.T("工程ごとに最大値を設定（mm。超過時は目盛単位で伸長）", "Maximum per process (mm; extends by tick when exceeded)", "按工序设置最大值（mm，超过时按刻度延伸）"), 7.5f, GR, x, y + 124, false);
            }
            else
            {
                Label(g, Lx.T("拡張単位", "Step", "扩展单位"), x, y + 76);
                NumField fst = Field("xstep", Lx.T("自動拡張の単位 [mm]", "Auto-extend step [mm]", "自动扩展单位 [mm]"), delegate { return s.XStep; }, delegate (double v) { s.XStep = v; }, true, new Rectangle(x, y + 90, 96, 30), 10, 1000, 10);
                NumBox(g, fst.R, fst, "mm"); StepBtns(g, x + 102, y + 90, fst);
                double[] sq = { 50, 100, 200, 500 };
                for (int i = 0; i < sq.Length; i++) { Rectangle r = new Rectangle(qx + (i % 2) * 56, y + 89 + (i / 2) * 18, 52, 15); double q = sq[i]; bool on = s.XStep == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString("0"), 7.5f, on ? CY : WH, r, false); Add(r, delegate { Set(delegate { s.XStep = q; }); }, null); }
                Txt(g, Lx.T("0〜単位 から始め、結晶長さが上端に達するたびに単位ぶん伸長", "Starts at 0–step and extends by one step whenever the length reaches the top", "从 0～单位开始，晶体长度到达上端时按单位延伸"), 7.5f, GR, x, y + 124, false);
            }
            Label(g, Lx.T("目盛", "Tick", "刻度"), x, y + 138);
            {   // v42: 1〜25mm の細かい目盛を追加（X 軸最大 100mm 以下でも目盛が出るように）。2 段のセグメント
                string[] r1 = { Lx.T("自動", "Auto", "自动"), "1", "2", "5", "10", "20", "25" }; double[] r1v = { 0, 1, 2, 5, 10, 20, 25 };
                string[] r2 = { "50", "100", "200", "250", "500", "1000" }; double[] r2v = { 50, 100, 200, 250, 500, 1000 };
                int s1 = -1, s2 = -1; for (int i = 0; i < r1v.Length; i++) if (s.XSpan == r1v[i]) s1 = i; for (int i = 0; i < r2v.Length; i++) if (s.XSpan == r2v[i]) s2 = i; if (s1 < 0 && s2 < 0) s1 = 0;
                Segment(g, x + 40, y + 134, 22, r1, s1, delegate (int i) { Set(delegate { s.XSpan = r1v[i]; }); }, 7.5f, 9);
                Segment(g, x + 40, y + 158, 22, r2, s2, delegate (int i) { Set(delegate { s.XSpan = r2v[i]; }); }, 7.5f, 9);
                Txt(g, "mm", 7.5f, GR, x + 40 + 2, y + 182, false);
            }
            using (Pen p = new Pen(LINE)) g.DrawLine(p, x, y + 194, c4.Right - 16, y + 194);
            y += 54;   // 以降（時間軸）を下げる
            // --- 時間軸（結晶長さの無い工程）---
            Txt(g, Lx.T("時間軸（結晶長さの無い工程: 工程開始からの経過）", "Time axis (processes without length: since start)", "时间轴（无晶体长度的工序: 自工序开始）"), 8.5f, CY, x, y + 142, true);
            Label(g, Lx.T("最大値", "Maximum", "最大值"), x, y + 158);
            NumField ft = Field("tmax", Lx.T("時間軸最大値 [分]", "Time axis maximum [min]", "时间轴最大值 [分]"), delegate { return s.TMax; }, delegate (double v) { s.TMax = v; }, true, new Rectangle(x, y + 172, 96, 30), 5, 1440, 10);
            NumBox(g, ft.R, ft, Lx.T("分", "min", "分")); StepBtns(g, x + 102, y + 172, ft);
            double[] tq = { 60, 120, 240, 480 };
            for (int i = 0; i < tq.Length; i++) { Rectangle r = new Rectangle(qx + (i % 2) * 56, y + 171 + (i / 2) * 18, 52, 15); double q = tq[i]; bool on = s.TMax == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString("0"), 7.5f, on ? CY : GR, r, false); Add(r, delegate { Set(delegate { s.TMax = q; }); }, null); }
            Label(g, Lx.T("目盛", "Tick", "刻度"), x, y + 210);
            string[] tsp = { Lx.T("自動", "Auto", "自动"), "5", "10", "15", "30", "60" }; double[] tspv = { 0, 5, 10, 15, 30, 60 };
            int tsel = 0; for (int i = 1; i < tspv.Length; i++) if (s.TSpan == tspv[i]) tsel = i;
            Segment(g, x + 40, y + 206, 24, tsp, tsel, delegate (int i) { Set(delegate { s.TSpan = tspv[i]; }); }, 8f, 14);
            Txt(g, Lx.T("※ 最大値を超えたら最新が右端になるようスクロール", "※ Scrolls so the latest point stays at the right edge once exceeded", "※ 超过最大值后滚动，使最新点保持在右端"), 7.5f, GR, x, y + 234, false);

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
