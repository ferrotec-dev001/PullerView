// GraphSettings.cs — DataSetting.ini をアプリ内で編集する設定画面（2026-10-23 中村氏要望）
//   ・工程（13）× グラフ（4）の全セクションを読み書きする DsDoc
//   ・ダークテーマの自作描画 UI（タッチ操作向けの大きなボタン、常時表示のテンキー、物理キーボード併用）
//   ・日本語／英語／中国語の切替（Lx）。ラベル幅は英語を基準に確保
//   ※ .NET Framework 4.0 / C# 5 互換で記述（string 補間・null 条件演算子などは使わない）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PullerView
{
    /// <summary>設定画面の多言語文字列。M.S.Lang（0=日 1=英 2=中）で切替。</summary>
    public static class Lx
    {
        public static string T(string ja, string en, string zh) { int l = M.S.Lang; return l == 1 ? en : l == 2 ? zh : ja; }
    }

    /// <summary>DataSetting.ini の編集用ドキュメント。全 13 工程 × 4 グラフを保持し、未定義セクションは既定値で補う。</summary>
    public class DsDoc
    {
        public string Path; public List<string> Header = new List<string>();
        public GraphCfg[,] C = new GraphCfg[13, 4];
        public Dictionary<string, Dictionary<string, string>> Extra = new Dictionary<string, Dictionary<string, string>>();   // 未知キーはそのまま書き戻す
        public bool Dirty; public DateTime Saved = DateTime.MinValue; public int DirtyCount;
        static readonly string[] Known = { "enable", "pv_pcinfodata_index", "caption", "caption_text", "unit", "unit_text", "sv_pcinfodata_index", "usl", "ucl", "lcl", "lsl", "warningcolor", "alarmcolor", "warningcolorrgb", "alarmcolorrgb" };

        public static DsDoc Load(string path)
        {
            DsDoc d = new DsDoc(); d.Path = path;
            for (int p = 0; p < 13; p++) for (int n = 0; n < 4; n++) { GraphCfg c = new GraphCfg(); c.Abbr = M.Abbrs[p]; c.N = n + 1; c.Section = c.Abbr + (n + 1); c.Enable = false; c.Pv = 65; c.Sv = 92; c.Usl = 230; c.Ucl = 225; c.Lcl = 205; c.Lsl = 200; d.C[p, n] = c; }
            string[] lines; try { lines = File.ReadAllLines(path, Encoding.UTF8); } catch (Exception) { lines = new string[0]; }
            try { if (File.Exists(path)) d.Saved = File.GetLastWriteTime(path); } catch (Exception) { }
            Dictionary<string, string> cur = null; string curName = null; bool inHeader = true;
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                if (l.StartsWith("[") && l.EndsWith("]"))
                {
                    inHeader = false; curName = l.Substring(1, l.Length - 2).Trim().ToUpper(); cur = new Dictionary<string, string>();
                    string ab = M.AbbrOf(curName); int i = curName.Length; while (i > 0 && char.IsDigit(curName[i - 1])) i--; int n;
                    if (i < curName.Length && int.TryParse(curName.Substring(i), out n) && n >= 1 && n <= 4 && Array.IndexOf(M.Abbrs, ab) >= 0) { d.Extra[ab + n] = cur; cur["__p"] = Array.IndexOf(M.Abbrs, ab).ToString(); cur["__n"] = (n - 1).ToString(); }
                    else cur = null;
                    continue;
                }
                if (inHeader) { if (l == "" || l.StartsWith(";") || l.StartsWith("#")) d.Header.Add(raw); continue; }
                if (l == "" || l.StartsWith(";") || l.StartsWith("#") || cur == null) continue;
                int e = l.IndexOf('='); if (e < 0) continue;
                cur[l.Substring(0, e).Trim().ToLower()] = l.Substring(e + 1).Trim();
            }
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in d.Extra)
            {
                Dictionary<string, string> s = kv.Value; int p = int.Parse(s["__p"]), n = int.Parse(s["__n"]); GraphCfg c = d.C[p, n];
                c.Enable = G(s, "enable", "0") == "1"; c.Pv = I(s, "pv_pcinfodata_index", c.Pv); c.Sv = I(s, "sv_pcinfodata_index", c.Sv);
                c.CapCustom = G(s, "caption", "0") == "1"; c.CapText = G(s, "caption_text", ""); c.UnitCustom = G(s, "unit", "0") == "1"; c.UnitText = G(s, "unit_text", "");
                c.Usl = D(s, "usl", c.Usl); c.Ucl = D(s, "ucl", c.Ucl); c.Lcl = D(s, "lcl", c.Lcl); c.Lsl = D(s, "lsl", c.Lsl);
                c.Warn = M.Hex(G(s, "warningcolorrgb", G(s, "warningcolor", "")), c.Warn); c.Alarm = M.Hex(G(s, "alarmcolorrgb", G(s, "alarmcolor", "")), c.Alarm);
            }
            if (d.Header.Count == 0) d.Header.AddRange(new string[] { "; PullerView DataSetting.ini — 工程ごとのグラフ設定（PullerView の設定画面から編集・保存される）", "; セクション: [<工程略称><n>] n=1..4   enable / pv_pcinfodata_index / sv_pcinfodata_index / caption / caption_text / unit / unit_text / usl / ucl / lcl / lsl / warningcolor / alarmcolor" });
            return d;
        }
        static string G(Dictionary<string, string> s, string k, string def) { string v; return s.TryGetValue(k, out v) ? v : def; }
        static int I(Dictionary<string, string> s, string k, int def) { int v; return int.TryParse(G(s, k, ""), out v) ? v : def; }
        static double D(Dictionary<string, string> s, string k, double def) { double v; return double.TryParse(G(s, k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        static string Hx(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        static string Num(double v) { return v.ToString("0.0##", CultureInfo.InvariantCulture); }

        /// <summary>検証。問題なければ null、あればメッセージ。</summary>
        public static string Validate(GraphCfg c)
        {
            if (c.Pv < 0 || c.Pv > 134 || c.Sv < 0 || c.Sv > 134) return Lx.T("index は 0〜134", "index must be 0–134", "index 须为 0～134");
            if (!(c.Usl >= c.Ucl)) return Lx.T("USL ≧ UCL が必要", "USL must be ≥ UCL", "需 USL ≥ UCL");
            if (!(c.Ucl > c.Lcl)) return Lx.T("UCL ＞ LCL が必要", "UCL must be > LCL", "需 UCL > LCL");
            if (!(c.Lcl >= c.Lsl)) return Lx.T("LCL ≧ LSL が必要", "LCL must be ≥ LSL", "需 LCL ≥ LSL");
            return null;
        }
        public int EnabledCount(int p) { int n = 0; for (int i = 0; i < 4; i++) if (C[p, i].Enable) n++; return n; }

        public void Save()
        {
            List<string> o = new List<string>(Header); o.Add("");
            for (int p = 0; p < 13; p++) for (int n = 0; n < 4; n++)
            {
                GraphCfg c = C[p, n]; string key = M.Abbrs[p] + (n + 1);
                o.Add("[" + key + "]");
                o.Add("enable=" + (c.Enable ? 1 : 0)); o.Add("pv_pcinfodata_index=" + c.Pv); o.Add("sv_pcinfodata_index=" + c.Sv);
                o.Add("caption=" + (c.CapCustom ? 1 : 0)); if (c.CapText != "") o.Add("caption_text=" + c.CapText);
                o.Add("unit=" + (c.UnitCustom ? 1 : 0)); if (c.UnitText != "") o.Add("unit_text=" + c.UnitText);
                o.Add("usl=" + Num(c.Usl)); o.Add("ucl=" + Num(c.Ucl)); o.Add("lcl=" + Num(c.Lcl)); o.Add("lsl=" + Num(c.Lsl));
                o.Add("warningcolor=" + Hx(c.Warn)); o.Add("alarmcolor=" + Hx(c.Alarm));
                Dictionary<string, string> ex; if (Extra.TryGetValue(key, out ex)) foreach (KeyValuePair<string, string> kv in ex) if (!kv.Key.StartsWith("__") && Array.IndexOf(Known, kv.Key) < 0) o.Add(kv.Key + "=" + kv.Value);
                o.Add("");
            }
            string tmp = Path + ".tmp"; File.WriteAllText(tmp, string.Join("\r\n", o.ToArray()), new UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(tmp, Path, Path + ".bak"); else File.Move(tmp, Path);
            Dirty = false; DirtyCount = 0; Saved = DateTime.Now;
            M.Log("DataSetting.ini 保存: " + Path);
        }
        /// <summary>保存せずに M へ反映（プレビュー）。</summary>
        public void ApplyToModel()
        {
            M.Cfg.Clear();
            for (int p = 0; p < 13; p++) for (int n = 0; n < 4; n++) { GraphCfg c = C[p, n]; GraphCfg k = Clone(c); if (Validate(k) != null) k.Enable = false; M.Cfg.Add(k); }
            foreach (GraphState s in M.Slots) s.Clear();
            M.Fire();
        }
        public static GraphCfg Clone(GraphCfg c) { return c.CloneCfg(); }
    }

    /// <summary>DataSetting.ini 編集パネル（自作描画）。</summary>
    public class GraphSettingsPanel : Control
    {
        public DsDoc Doc; int proc = 8, slot = 0;                   // 選択中の工程（既定 BODY）とグラフ枠
        // 色
        public static readonly Color BG = Color.FromArgb(0x2B, 0x2B, 0x30), CARD = Color.FromArgb(0x3A, 0x3A, 0x42), FIELD = Color.FromArgb(0x24, 0x24, 0x2A), CY = Color.FromArgb(0x4F, 0xC3, 0xF7),
            AMB = Color.FromArgb(0xFF, 0xC1, 0x07), GRN = Color.FromArgb(0x4C, 0xD9, 0x64), RED = Color.FromArgb(0xFF, 0x5A, 0x5A), BLU = Color.FromArgb(0x3F, 0xA9, 0xF5), GR = Color.FromArgb(0x9A, 0x9A, 0xA5),
            WH = Color.FromArgb(240, 240, 240), DK = Color.FromArgb(0x1E, 0x1E, 0x24), SEL = Color.FromArgb(0x1F, 0x4B, 0x5E), BTN = Color.FromArgb(0x4A, 0x4A, 0x55), BTNR = Color.FromArgb(0x5A, 0x3A, 0x3A), LINE = Color.FromArgb(0x50, 0x50, 0x58);
        // ヒット領域（描画のたびに再構築する簡易イミディエイトモード UI）
        class Hit { public Rectangle R; public Action A; public string Tip; }
        List<Hit> hits = new List<Hit>();
        // 数値フィールド
        public class NumField { public string Key; public Func<string> Label; public Func<double> Get; public Action<double> Set; public bool IsInt; public Rectangle R; public Func<string> Range; }
        List<NumField> fields = new List<NumField>(); NumField active; string buf = ""; string err = ""; DateTime errAt;
        TextBox capBox, unitBox;                                     // 任意入力（表示名・単位）は実 TextBox
        Point mouse = new Point(-1, -1);
        public event Action Changed;

        public GraphSettingsPanel(DsDoc doc)
        {
            Doc = doc; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = BG; TabStop = true;
            capBox = MakeBox(); unitBox = MakeBox();
            capBox.TextChanged += delegate { if (!loading) { Cur.CapText = capBox.Text; Touch(); } };
            unitBox.TextChanged += delegate { if (!loading) { Cur.UnitText = unitBox.Text; Touch(); } };
            proc = Math.Max(0, Math.Min(12, M.Proc));
        }
        bool loading;
        TextBox MakeBox() { TextBox b = new TextBox(); b.BorderStyle = BorderStyle.FixedSingle; b.BackColor = FIELD; b.ForeColor = WH; b.Font = new Font(UiFont, 11f); Controls.Add(b); return b; }
        public static string UiFont { get { return FontExists("Yu Gothic UI") ? "Yu Gothic UI" : FontExists("Meiryo UI") ? "Meiryo UI" : FontFamily.GenericSansSerif.Name; } }
        static bool FontExists(string n) { try { using (Font f = new Font(n, 9f)) return f.Name == n; } catch (Exception) { return false; } }
        GraphCfg Cur { get { return Doc.C[proc, slot]; } }
        void Touch() { Doc.Dirty = true; Doc.DirtyCount++; if (Changed != null) Changed(); Invalidate(); }
        public void Select(int p) { proc = p; slot = 0; active = null; buf = ""; Invalidate(); }

        // ---------- 描画 ----------
        Font F(float pt, bool bold) { return new Font(UiFont, pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
        static GraphicsPath RR(RectangleF r, float rad) { GraphicsPath p = new GraphicsPath(); float d = rad * 2; p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
        void Box(Graphics g, Rectangle r, Color fill, Color? outline, int rad, float w) { using (GraphicsPath p = RR(r, rad)) { using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p); if (outline != null) using (Pen pn = new Pen(outline.Value, w)) g.DrawPath(pn, p); } }
        void Txt(Graphics g, string s, float pt, Color c, float x, float y, bool bold) { using (Font f = F(pt, bold)) using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, x, y); }
        void TxtC(Graphics g, string s, float pt, Color c, RectangleF r, bool bold) { using (Font f = F(pt, bold)) using (SolidBrush b = new SolidBrush(c)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap; g.DrawString(s, f, b, r, sf); } }
        void TxtR(Graphics g, string s, float pt, Color c, float right, float y) { using (Font f = F(pt, false)) using (SolidBrush b = new SolidBrush(c)) { SizeF sz = g.MeasureString(s, f); g.DrawString(s, f, b, right - sz.Width, y); } }
        float TW(Graphics g, string s, float pt) { using (Font f = F(pt, false)) return g.MeasureString(s, f).Width; }
        void Toggle(Graphics g, Rectangle r, bool on) { Box(g, r, on ? GRN : Color.FromArgb(0x60, 0x60, 0x68), null, r.Height / 2, 1); int d = r.Height - 4; using (SolidBrush b = new SolidBrush(WH)) g.FillEllipse(b, on ? r.Right - d - 2 : r.X + 2, r.Y + 2, d, d); }
        void Add(Rectangle r, Action a, string tip) { Hit h = new Hit(); h.R = r; h.A = a; h.Tip = tip; hits.Add(h); }
        bool Hover(Rectangle r) { return r.Contains(mouse); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(BG); hits.Clear(); fields.Clear();
            GraphCfg c = Cur;
            // ===== 左: 工程リスト =====
            Rectangle L = new Rectangle(12, 12, 172, Height - 24); Box(g, L, CARD, null, 10, 1);
            Txt(g, Lx.T("工程", "Process", "工序"), 9f, GR, 24, 20, false);
            int rowH = (L.Height - 72) / 13;
            for (int i = 0; i < 13; i++)
            {
                Rectangle r = new Rectangle(20, 40 + i * rowH, 156, rowH - 4); bool sel = i == proc; int ii = i;
                Box(g, r, sel ? SEL : Hover(r) ? Color.FromArgb(0x30, 0x30, 0x38) : FIELD, sel ? CY : (Color?)null, 7, 2);
                if (sel) using (SolidBrush b = new SolidBrush(CY)) g.FillRectangle(b, r.X, r.Y + 7, 4, r.Height - 14);
                Txt(g, M.Procs[i], 9.5f, sel ? WH : Color.FromArgb(210, 210, 215), r.X + 12, r.Y + (r.Height - 17) / 2, sel);
                int en = Doc.EnabledCount(i); if (en > 0) { Rectangle bd = new Rectangle(r.Right - 30, r.Y + (r.Height - 20) / 2, 22, 20); Box(g, bd, sel ? CY : Color.FromArgb(0x55, 0x55, 0x60), null, 10, 1); TxtC(g, en.ToString(), 8f, sel ? DK : WH, bd, true); }
                Add(r, delegate { Select(ii); }, null);
            }
            Txt(g, Lx.T("● 数字 = 表示 ON のグラフ数", "● number = graphs ON", "● 数字 = 显示中的图表数"), 7.5f, GR, 24, L.Bottom - 22, false);

            // ===== 中央: グラフ 1〜4 セグメント + カード =====
            int cx = 196, cw = 500;
            Txt(g, Lx.T(M.Procs[proc] + " のグラフ（最大 4 本・上から順に表示）", "Graphs for " + M.Procs[proc] + " (max 4, top to bottom)", M.Procs[proc] + " 的图表（最多 4 个，自上而下）"), 9.5f, WH, cx, 16, false);
            for (int i = 0; i < 4; i++)
            {
                Rectangle r = new Rectangle(cx + i * 125, 38, 120, 40); bool act = i == slot; int ii = i; GraphCfg ci = Doc.C[proc, i];
                Box(g, r, act ? SEL : FIELD, act ? CY : LINE, 8, act ? 2 : 1);
                Txt(g, Lx.T("グラフ " + (i + 1), "Graph " + (i + 1), "图表 " + (i + 1)), 9f, act ? WH : Color.FromArgb(205, 205, 210), r.X + 10, r.Y + 11, false);
                Rectangle tg = new Rectangle(r.Right - 42, r.Y + 11, 34, 18); Toggle(g, tg, ci.Enable);
                Add(tg, delegate { ci.Enable = !ci.Enable; slot = ii; Touch(); }, Lx.T("表示 ON/OFF", "Show ON/OFF", "显示 开/关"));
                Add(r, delegate { slot = ii; active = null; buf = ""; Invalidate(); }, null);
            }
            Rectangle card = new Rectangle(cx, 86, cw, Height - 98); Box(g, card, CARD, null, 10, 1);
            int lx = cx + 16, lw = 250, y0 = card.Y;
            // 表示する
            Txt(g, Lx.T("このグラフを表示する", "Show this graph", "显示此图表"), 9.5f, WH, lx, y0 + 14, false);
            Rectangle en1 = new Rectangle(lx + lw - 56, y0 + 12, 56, 26); Toggle(g, en1, c.Enable); Add(en1, delegate { c.Enable = !c.Enable; Touch(); }, null);
            using (Pen pn = new Pen(LINE)) g.DrawLine(pn, lx, y0 + 48, lx + lw, y0 + 48);
            // PV / SV
            DrawIndexRow(g, lx, lw, y0 + 58, Lx.T("PV（実測値）  stcMCValue index", "PV (actual)  stcMCValue index", "PV（实测值）  stcMCValue index"), "pv", delegate { return c.Pv; }, delegate(double v) { c.Pv = (int)v; });
            DrawIndexRow(g, lx, lw, y0 + 120, Lx.T("SV（設定値）  stcMCValue index", "SV (setpoint)  stcMCValue index", "SV（设定值）  stcMCValue index"), "sv", delegate { return c.Sv; }, delegate(double v) { c.Sv = (int)v; });
            // 表示名 / 単位
            DrawPillRow(g, lx, lw, y0 + 182, Lx.T("表示名", "Caption", "显示名称"), c.CapCustom, delegate(bool v) { c.CapCustom = v; Touch(); }, capBox, c.CapText, c.CapCustom ? "" : M.Tag(c.Pv)[M.S.Lang]);
            DrawPillRow(g, lx, lw, y0 + 244, Lx.T("単位", "Unit", "单位"), c.UnitCustom, delegate(bool v) { c.UnitCustom = v; Touch(); }, unitBox, c.UnitText, c.UnitCustom ? "" : M.Tag(c.Pv)[3]);
            using (Pen pn = new Pen(LINE)) g.DrawLine(pn, lx, y0 + 310, lx + lw, y0 + 310);
            // 背景色
            Txt(g, Lx.T("グラフ背景色", "Graph background", "图表背景色"), 8.5f, GR, lx, y0 + 318, false);
            Rectangle sw1 = new Rectangle(lx, y0 + 340, 22, 22); Box(g, sw1, c.Warn, LINE, 4, 1); Txt(g, Lx.T("管理外（UCL/LCL 超過）", "Out of control (UCL/LCL)", "超出管理限（UCL/LCL）") + "  " + Hx6(c.Warn), 8.5f, WH, lx + 30, y0 + 343, false);
            Add(new Rectangle(lx, y0 + 338, lw, 26), delegate { PickColor(delegate(Color k) { c.Warn = k; Touch(); }, c.Warn); }, null);
            Rectangle sw2 = new Rectangle(lx, y0 + 372, 22, 22); Box(g, sw2, c.Alarm, LINE, 4, 1); Txt(g, Lx.T("規格外（USL/LSL 超過）", "Out of spec (USL/LSL)", "超出规格限（USL/LSL）") + "  " + Hx6(c.Alarm), 8.5f, WH, lx + 30, y0 + 375, false);
            Add(new Rectangle(lx, y0 + 370, lw, 26), delegate { PickColor(delegate(Color k) { c.Alarm = k; Touch(); }, c.Alarm); }, null);
            // ---- 右カラム: 上下限（縦配置） ----
            int rx0 = cx + 288, rw0 = cw - 288 - 16;
            Txt(g, Lx.T("上下限（PV の絶対値）", "Limits (absolute PV)", "上下限（PV 绝对值）"), 9f, WH, rx0 + 24, y0 + 12, false);
            int railTop = y0 + 36, railBot = y0 + 352;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0x55, 0x55, 0x60))) g.FillRectangle(b, rx0 + 6, railTop, 4, railBot - railTop);
            using (SolidBrush b = new SolidBrush(RED)) { g.FillPolygon(b, new Point[] { new Point(rx0 + 8, railTop - 4), new Point(rx0 + 2, railTop + 8), new Point(rx0 + 14, railTop + 8) }); g.FillPolygon(b, new Point[] { new Point(rx0 + 8, railBot + 4), new Point(rx0 + 2, railBot - 8), new Point(rx0 + 14, railBot - 8) }); }
            Txt(g, Lx.T("上限側", "upper", "上限侧"), 7f, GR, rx0 + 18, railTop - 2, false); Txt(g, Lx.T("下限側", "lower", "下限侧"), 7f, GR, rx0 + 18, railBot - 10, false);
            string[] lk = { "usl", "ucl", "lcl", "lsl" }; string[] ln = { Lx.T("USL 規格上限", "USL  Spec upper", "USL 规格上限"), Lx.T("UCL 管理上限", "UCL  Control upper", "UCL 管理上限"), Lx.T("LCL 管理下限", "LCL  Control lower", "LCL 管理下限"), Lx.T("LSL 規格下限", "LSL  Spec lower", "LSL 规格下限") };
            Color[] lc = { RED, BLU, BLU, RED }; int[] ly = { y0 + 50, y0 + 120, y0 + 218, y0 + 288 };
            Func<double>[] get = { delegate { return c.Usl; }, delegate { return c.Ucl; }, delegate { return c.Lcl; }, delegate { return c.Lsl; } };
            Action<double>[] set = { delegate(double v) { c.Usl = v; }, delegate(double v) { c.Ucl = v; }, delegate(double v) { c.Lcl = v; }, delegate(double v) { c.Lsl = v; } };
            string unit = c.Unit();
            for (int i = 0; i < 4; i++)
            {
                Rectangle r = new Rectangle(rx0 + 24, ly[i], rw0 - 24, 58); NumField f = Field(lk[i], ln[i], get[i], set[i], false, r, LimitRange(c, i));
                bool act = active == f;
                Box(g, r, FIELD, act ? CY : LINE, 8, act ? 2 : 1);
                using (SolidBrush b = new SolidBrush(lc[i])) { g.FillRectangle(b, r.X + 3, r.Y + 8, 4, r.Height - 16); g.FillEllipse(b, rx0 + 3, r.Y + 24, 10, 10); }
                Txt(g, ln[i], 8f, GR, r.X + 16, r.Y + 6, false); Txt(g, act ? buf + "_" : Fmt(get[i]()), 13f, act ? CY : WH, r.X + 14, r.Y + 24, true); TxtR(g, unit, 8f, GR, r.Right - 10, r.Y + 32);
            }
            Rectangle curR = new Rectangle(rx0 + 24, y0 + 186, rw0 - 24, 26); Box(g, curR, SEL, null, 13, 1);
            string curTxt = CurrentValueText(c); TxtC(g, curTxt, 8f, Color.FromArgb(190, 235, 255), curR, false); using (SolidBrush b = new SolidBrush(GRN)) g.FillEllipse(b, rx0 + 3, y0 + 194, 10, 10);
            string vmsg = DsDoc.Validate(c);
            Txt(g, vmsg == null ? "OK  USL ≥ UCL > LCL ≥ LSL" : "✖ " + vmsg, 8f, vmsg == null ? GRN : RED, rx0 + 24, y0 + 360, false);
            // プレビュー帯
            Rectangle pv = new Rectangle(lx, card.Bottom - 94, cw - 32, 34); Box(g, pv, Color.FromArgb(0x3C, 0x3C, 0x3C), null, 6, 1); DrawPreview(g, pv, c);
            Txt(g, Lx.T("※ 数値欄をタップするとテンキーで入力できます。キーボードからも入力可", "※ Tap a numeric field to edit with the keypad; keyboard also works", "※ 点击数值栏可用数字键盘输入，也可使用键盘"), 7.5f, GR, lx, card.Bottom - 50, false);
            if (err != "" && (DateTime.Now - errAt).TotalSeconds < 4) Txt(g, err, 8.5f, RED, lx, card.Bottom - 30, true);

            // ===== 右: テンキー =====
            int rx = 712, rw = 276; Rectangle K = new Rectangle(rx, 12, rw, Height - 24); Box(g, K, CARD, null, 10, 1);
            string head = Lx.T("テンキー  — 入力中: ", "Keypad  — Editing: ", "数字键盘  — 输入中: "); Txt(g, head, 8.5f, GR, rx + 16, 20, false);
            Txt(g, active == null ? Lx.T("（数値欄を選択）", "(select a field)", "（请选择数值栏）") : active.Label(), 8.5f, CY, rx + 16 + TW(g, head, 8.5f), 20, false);
            Rectangle disp = new Rectangle(rx + 16, 40, rw - 32, 52); Box(g, disp, Color.FromArgb(0x14, 0x14, 0x18), active != null ? CY : LINE, 8, 2);
            TxtR(g, active == null ? "" : (buf == "" ? Fmt(active.Get()) : buf), 18f, active == null ? GR : WH, disp.Right - 14, disp.Y + 6);
            if (active != null) Txt(g, active.Range(), 7.5f, GR, disp.X + 8, disp.Bottom - 16, false);
            string[][] keys = { new[] { "7", "8", "9" }, new[] { "4", "5", "6" }, new[] { "1", "2", "3" }, new[] { "±", "0", "." } };
            int kw = 72, kh = (Height - 24 - 92 - 66 - 70) / 5 - 8; if (kh > 62) kh = 62; if (kh < 40) kh = 40; int ky = 104;
            for (int r = 0; r < 4; r++) for (int col = 0; col < 3; col++)
            {
                string k = keys[r][col]; Rectangle kr = new Rectangle(rx + 16 + col * (kw + 8), ky + r * (kh + 8), kw, kh);
                Box(g, kr, Hover(kr) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 9, 1); TxtC(g, k, 15f, WH, kr, false); Add(kr, delegate { Key(k); }, null);
            }
            int yy = ky + 4 * (kh + 8);
            string[] fn = { "BS", "C", Lx.T("次へ", "Next", "下一项") }; Color[] fc = { BTNR, BTNR, BTN }; Action[] fa = { delegate { Key("BS"); }, delegate { Key("C"); }, delegate { Key("TAB"); } };
            for (int i = 0; i < 3; i++) { Rectangle kr = new Rectangle(rx + 16 + i * (kw + 8), yy, kw, kh); Box(g, kr, Hover(kr) ? Color.FromArgb(0x6A, 0x4A, 0x4A) : fc[i], null, 9, 1); TxtC(g, fn[i], 10f, WH, kr, false); Add(kr, fa[i], null); }
            yy += kh + 10; Rectangle okr = new Rectangle(rx + 16, yy, rw - 32, 56); Box(g, okr, Hover(okr) ? Color.FromArgb(0x7F, 0xD8, 0xFF) : CY, null, 9, 1); TxtC(g, Lx.T("確定 (Enter)", "Enter", "确定 (Enter)"), 12f, DK, okr, true); Add(okr, delegate { Key("ENTER"); }, null);
            yy += 70;
            Txt(g, Lx.T("・物理キーボードでも入力可（0-9 . - / Enter 確定 /", "- Physical keyboard also accepted (0-9 . - / Enter /", "・也可使用实体键盘（0-9 . - / Enter 确定 /"), 7.5f, GR, rx + 16, yy, false);
            Txt(g, Lx.T("  Esc 取消 / Tab 次の項目 / ↑↓ で 0.1 刻み増減）", "  Esc cancel / Tab next field / Up-Down ±0.1)", "  Esc 取消 / Tab 下一项 / 上下键 ±0.1）"), 7.5f, GR, rx + 16, yy + 15, false);
            Txt(g, Lx.T("・index 欄は整数のみ。範囲外は赤表示で確定不可", "- Index fields: integer only. Out of range = rejected", "・index 栏仅限整数。超范围不可确定"), 7.5f, GR, rx + 16, yy + 36, false);
        }
        static string Hx6(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        static string Fmt(double v) { return v.ToString("0.0##", CultureInfo.InvariantCulture); }
        string CurrentValueText(GraphCfg c)
        {
            string pv = "---", sv = "---";
            foreach (GraphCfg a in M.Active()) if (a.Abbr == c.Abbr && a.N == c.N) { GraphState s = M.Slots[a.N - 1]; if (s.Valid && s.Pv.Count > 0) { pv = Fmt(s.Pv[s.Pv.Count - 1]); double lv = s.Sv[s.Sv.Count - 1]; if (!double.IsNaN(lv)) sv = Fmt(lv); } }
            return Lx.T("現在値  PV " + pv + " / SV " + sv, "Current  PV " + pv + " / SV " + sv, "当前值  PV " + pv + " / SV " + sv);
        }
        Func<string> LimitRange(GraphCfg c, int i)
        {
            return delegate
            {
                switch (i)
                {
                    case 0: return Lx.T("範囲 UCL " + Fmt(c.Ucl) + " 以上", "Range: ≥ UCL " + Fmt(c.Ucl), "范围 ≥ UCL " + Fmt(c.Ucl));
                    case 1: return Lx.T("範囲 LCL " + Fmt(c.Lcl) + " ＜ … ≦ USL " + Fmt(c.Usl), "Range: LCL " + Fmt(c.Lcl) + " < … ≤ USL " + Fmt(c.Usl), "范围 LCL " + Fmt(c.Lcl) + " < … ≤ USL " + Fmt(c.Usl));
                    case 2: return Lx.T("範囲 LSL " + Fmt(c.Lsl) + " ≦ … ＜ UCL " + Fmt(c.Ucl), "Range: LSL " + Fmt(c.Lsl) + " ≤ … < UCL " + Fmt(c.Ucl), "范围 LSL " + Fmt(c.Lsl) + " ≤ … < UCL " + Fmt(c.Ucl));
                    default: return Lx.T("範囲 LCL " + Fmt(c.Lcl) + " 以下", "Range: ≤ LCL " + Fmt(c.Lcl), "范围 ≤ LCL " + Fmt(c.Lcl));
                }
            };
        }
        NumField Field(string key, string label, Func<double> get, Action<double> set, bool isInt, Rectangle r, Func<string> range)
        {
            NumField f = new NumField(); f.Key = key; f.Label = delegate { return label; }; f.Get = get; f.Set = set; f.IsInt = isInt; f.R = r; f.Range = range; fields.Add(f);
            if (active != null && active.Key == key) { active = f; }   // 再描画後も同じ論理フィールドを維持
            Add(r, delegate { Activate(f); }, null); return f;
        }
        void DrawIndexRow(Graphics g, int lx, int lw, int y, string label, string key, Func<double> get, Action<double> set)
        {
            Txt(g, label, 8.5f, GR, lx, y, false);
            Rectangle r = new Rectangle(lx, y + 16, 76, 36); NumField f = Field(key, label, get, set, true, r, delegate { return Lx.T("範囲 0〜134（整数）", "Range 0–134 (integer)", "范围 0～134（整数）"); });
            bool act = active == f; Box(g, r, FIELD, act ? CY : LINE, 7, act ? 2 : 1); Txt(g, act ? buf + "_" : ((int)get()).ToString(), 11f, act ? CY : WH, r.X + 10, r.Y + 8, false);
            int idx = (int)get(); string[] tag = M.Tag(idx);
            Rectangle pb = new Rectangle(lx + lw - 86, y + 16, 86, 36);
            using (Font tf = F(8.5f, false)) using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 235, 255))) { StringFormat sf = new StringFormat(); sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap; g.DrawString(tag[M.S.Lang] + (tag[3] != "" ? " [" + tag[3] + "]" : ""), tf, b, new RectangleF(lx + 82, y + 16, pb.X - lx - 86, 36), sf); }   // 長い項目名は … で省略（英語名対策） Box(g, pb, Hover(pb) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 7, 1); TxtC(g, Lx.T("一覧から選ぶ", "Pick from list", "从列表选择"), 8f, WH, pb, false);
            Add(pb, delegate { PickIndex(delegate(int v) { set(v); Touch(); }, idx); }, null);
        }
        void DrawPillRow(Graphics g, int lx, int lw, int y, string label, bool custom, Action<bool> setCustom, TextBox box, string text, string autoText)
        {
            Txt(g, label, 8.5f, GR, lx, y, false);
            string a = "McMeta", b = Lx.T("任意入力", "Custom", "自定义"); int wa = (int)TW(g, a, 8.5f) + 22, wb = (int)TW(g, b, 8.5f) + 22;
            Rectangle pill = new Rectangle(lx, y + 16, wa + wb + 4, 34); Box(g, pill, FIELD, null, 17, 1);
            Rectangle on = custom ? new Rectangle(lx + 2 + wa, y + 18, wb, 30) : new Rectangle(lx + 2, y + 18, wa, 30); Box(g, on, CY, null, 15, 1);
            TxtC(g, a, 8.5f, custom ? GR : DK, new Rectangle(lx + 2, y + 18, wa, 30), false); TxtC(g, b, 8.5f, custom ? DK : GR, new Rectangle(lx + 2 + wa, y + 18, wb, 30), false);
            Add(new Rectangle(lx + 2, y + 18, wa, 30), delegate { setCustom(false); }, null); Add(new Rectangle(lx + 2 + wa, y + 18, wb, 30), delegate { setCustom(true); }, null);
            Rectangle tb = new Rectangle(pill.Right + 8, y + 16, lx + lw - pill.Right - 8, 34);
            loading = true; Rectangle nb = new Rectangle(tb.X + 1, tb.Y + 6, tb.Width - 2, 24); if (box.Bounds != nb) box.Bounds = nb; box.Enabled = custom; string nt = custom ? text : autoText; if (box.Text != nt) box.Text = nt; box.BackColor = custom ? FIELD : Color.FromArgb(0x2E, 0x2E, 0x35); box.ForeColor = custom ? WH : Color.FromArgb(140, 140, 150); loading = false;
            Box(g, tb, custom ? FIELD : Color.FromArgb(0x2E, 0x2E, 0x35), LINE, 7, 1);
        }
        void DrawPreview(Graphics g, Rectangle r, GraphCfg c)
        {
            double hi = c.Usl, lo = c.Lsl, span = hi - lo; if (span <= 0) span = 1; hi += span * 0.15; lo -= span * 0.15; span = hi - lo;
            Func<double, float> Y = delegate(double v) { return (float)(r.Bottom - 3 - (v - lo) / span * (r.Height - 6)); };
            Color[] cs = { RED, BLU, BLU, RED }; double[] vs = { c.Usl, c.Ucl, c.Lcl, c.Lsl };
            for (int i = 0; i < 4; i++) using (Pen p = new Pen(cs[i], 1f)) g.DrawLine(p, r.X + 4, Y(vs[i]), r.Right - 70, Y(vs[i]));
            double mid = (c.Ucl + c.Lcl) / 2; using (Pen p = new Pen(GRN, 2f)) g.DrawLine(p, r.X + 14, Y(mid - span * 0.03), r.Right - 80, Y(mid + span * 0.02));
            TxtR(g, Lx.T("プレビュー", "Preview", "预览"), 7.5f, GR, r.Right - 8, r.Y + 9);
        }

        // ---------- 入力 ----------
        void Activate(NumField f) { if (active == f) return; CommitIfValid(); active = f; buf = ""; Focus(); Invalidate(); }
        bool CommitIfValid()
        {
            if (active == null || buf == "") return true;
            double v; if (!double.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { Err(Lx.T("数値として読めません", "Not a number", "无法解析为数值")); return false; }
            if (active.IsInt && (v != Math.Floor(v) || v < 0 || v > 134)) { Err(Lx.T("index は 0〜134 の整数", "Index must be an integer 0–134", "index 须为 0～134 的整数")); return false; }
            double old = active.Get(); active.Set(v);
            string bad = DsDoc.Validate(Cur);
            if (bad != null && !active.IsInt) { active.Set(old); Err(bad); return false; }
            buf = ""; Touch(); return true;
        }
        void Err(string s) { err = s; errAt = DateTime.Now; Invalidate(); Timer t = new Timer(); t.Interval = 4200; t.Tick += delegate { t.Stop(); t.Dispose(); Invalidate(); }; t.Start(); }
        public void Key(string k)
        {
            if (k == "TAB") { Next(1); return; }
            if (active == null) return;
            switch (k)
            {
                case "BS": if (buf.Length > 0) buf = buf.Substring(0, buf.Length - 1); break;
                case "C": buf = ""; break;
                case "ENTER": if (CommitIfValid()) { active = null; } break;
                case "ESC": buf = ""; active = null; break;
                case "±": if (buf == "") buf = "-"; else if (buf.StartsWith("-")) buf = buf.Substring(1); else buf = "-" + buf; break;
                case ".": if (!active.IsInt && buf.IndexOf('.') < 0) buf += (buf == "" || buf == "-") ? "0." : "."; break;
                case "UP": Step(active.IsInt ? 1 : 0.1); break;
                case "DOWN": Step(active.IsInt ? -1 : -0.1); break;
                default: if (k.Length == 1 && char.IsDigit(k[0]) && buf.Length < 10) buf += k; break;
            }
            Invalidate();
        }
        void Step(double d) { double old = active.Get(); double v = Math.Round((buf == "" ? old : Parse(buf, old)) + d, 3); active.Set(v); if (DsDoc.Validate(Cur) != null && !active.IsInt) { active.Set(old); Err(DsDoc.Validate(Cur) ?? ""); return; } buf = ""; Touch(); }
        static double Parse(string s, double def) { double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        void Next(int dir)
        {
            if (fields.Count == 0) return; if (!CommitIfValid()) return;
            int i = active == null ? -1 : fields.IndexOf(active); i = (i + dir + fields.Count) % fields.Count; active = fields[i]; buf = ""; Invalidate();
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Tab || k == Keys.Up || k == Keys.Down || k == Keys.Enter || k == Keys.Escape || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Enter: Key("ENTER"); e.Handled = true; break;
                case Keys.Escape: Key("ESC"); e.Handled = true; break;
                case Keys.Tab: Next(e.Shift ? -1 : 1); e.Handled = true; break;
                case Keys.Back: Key("BS"); e.Handled = true; break;
                case Keys.Delete: Key("C"); e.Handled = true; break;
                case Keys.Up: Key("UP"); e.Handled = true; break;
                case Keys.Down: Key("DOWN"); e.Handled = true; break;
            }
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e); char ch = e.KeyChar;
            if (char.IsDigit(ch)) { Key(ch.ToString()); e.Handled = true; }
            else if (ch == '.' || ch == ',') { Key("."); e.Handled = true; }
            else if (ch == '-' || ch == '+') { Key("±"); e.Handled = true; }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(e.Location)) { hits[i].A(); return; }
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); mouse = e.Location; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-1, -1); Invalidate(); }

        // ---------- サブダイアログ ----------
        void PickColor(Action<Color> set, Color cur) { using (ColorDialog d = new ColorDialog()) { d.Color = cur; d.FullOpen = true; if (d.ShowDialog(this) == DialogResult.OK) set(d.Color); } }
        void PickIndex(Action<int> set, int cur)
        {
            using (Form f = new Form())
            {
                f.Text = Lx.T("stcMCValue 一覧から選ぶ", "Pick from stcMCValue list", "从 stcMCValue 列表选择"); f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false; f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(520, 560); f.BackColor = BG; f.ForeColor = WH; f.Font = new Font(UiFont, 11f);
                ListView lv = new ListView(); lv.View = View.Details; lv.FullRowSelect = true; lv.HideSelection = false; lv.BackColor = FIELD; lv.ForeColor = WH; lv.Font = new Font(UiFont, 11f); lv.SetBounds(12, 12, 496, 490); lv.MultiSelect = false;
                lv.Columns.Add("index", 60); lv.Columns.Add(Lx.T("項目名", "Caption", "项目名"), 300); lv.Columns.Add(Lx.T("単位", "Unit", "单位"), 60); lv.Columns.Add("div", 60);
                for (int i = 0; i <= 134; i++) { string[] t = M.Tag(i); if (t[0] == "" && t[1] == "" && McMeta.Get(i) == null) continue; ListViewItem it = new ListViewItem(i.ToString()); it.SubItems.Add(t[M.S.Lang]); it.SubItems.Add(t[3]); it.SubItems.Add(McMeta.DivOf(i).ToString()); it.Tag = i; lv.Items.Add(it); if (i == cur) it.Selected = true; }
                f.Controls.Add(lv);
                Button ok = new Button(); ok.Text = Lx.T("選択", "Select", "选择"); ok.SetBounds(300, 512, 100, 38); ok.DialogResult = DialogResult.OK; ok.FlatStyle = FlatStyle.Flat; ok.BackColor = CY; ok.ForeColor = DK; f.Controls.Add(ok);
                Button ca = new Button(); ca.Text = Lx.T("キャンセル", "Cancel", "取消"); ca.SetBounds(408, 512, 100, 38); ca.DialogResult = DialogResult.Cancel; ca.FlatStyle = FlatStyle.Flat; ca.BackColor = BTN; ca.ForeColor = WH; f.Controls.Add(ca);
                lv.DoubleClick += delegate { if (lv.SelectedItems.Count > 0) f.DialogResult = DialogResult.OK; };
                f.AcceptButton = ok; f.CancelButton = ca;
                if (lv.SelectedItems.Count > 0) lv.EnsureVisible(lv.SelectedItems[0].Index);
                if (f.ShowDialog(this) == DialogResult.OK && lv.SelectedItems.Count > 0) set((int)lv.SelectedItems[0].Tag);
            }
        }
    }
}

namespace PullerView
{
    /// <summary>設定ウィンドウ（タブ: ウィンドウ／表示、グラフ設定 DataSetting.ini）。ヘッダー・フッターは自作描画。</summary>
    public class SettingsForm : Form
    {
        WindowSettingsPanel win; GraphSettingsPanel gp; DsDoc doc; int tab = 1;
        const int HeadH = 48, FootH = 58;
        class Btn { public Rectangle R; public string Text; public Color Bg, Fg; public Action A; }
        List<Btn> btns = new List<Btn>(); Rectangle[] tabR = new Rectangle[2]; Point mouse = new Point(-1, -1); string msg = ""; Color msgColor;
        public SettingsForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = "PullerView " + Lx.T("設定", "Settings", "设置"); FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(1000, 720); BackColor = GraphSettingsPanel.BG; Font = new Font(GraphSettingsPanel.UiFont, 9f); KeyPreview = false;
            doc = DsDoc.Load(M.IniPath ?? Ini.InExeDir("DataSetting.ini"));
            win = new WindowSettingsPanel(); win.Location = new Point(290, HeadH + 20); win.Visible = false; Controls.Add(win);
            gp = new GraphSettingsPanel(doc); gp.Bounds = new Rectangle(0, HeadH, 1000, 720 - HeadH - FootH); Controls.Add(gp);
            gp.Changed += delegate { Invalidate(new Rectangle(0, 720 - FootH, 1000, FootH)); };
            using (GraphicsPath p = Rounded(new Rectangle(0, 0, 1000, 720), 10)) Region = new Region(p);
            Shown += delegate { gp.Focus(); };
        }
        /// <summary>PullerView の右側に出し、画面からはみ出す場合は作業領域内に収める。</summary>
        public static Point PlaceNear(Form pv)
        {
            Rectangle wa = Screen.FromControl(pv).WorkingArea; int x = pv.Right + 10, y = pv.Top;
            if (x + 1000 > wa.Right) x = Math.Max(wa.Left, wa.Right - 1000); if (y + 720 > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - 720);
            return new Point(x, y);
        }
        static GraphicsPath Rounded(Rectangle r, int rad) { GraphicsPath p = new GraphicsPath(); int d = rad * 2; p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
        void SetTab(int i) { tab = i; win.Visible = i == 0; gp.Visible = i == 1; if (i == 1) gp.Focus(); Invalidate(); }
        void Say(string s, Color c) { msg = s; msgColor = c; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Color DK = GraphSettingsPanel.DK, WH = GraphSettingsPanel.WH, GR = GraphSettingsPanel.GR, CY = GraphSettingsPanel.CY, CARD = GraphSettingsPanel.CARD, BTN = GraphSettingsPanel.BTN, AMB = GraphSettingsPanel.AMB;
            // ---- ヘッダー ----
            using (SolidBrush b = new SolidBrush(DK)) g.FillRectangle(b, 0, 0, Width, HeadH);
            using (Font f = new Font(GraphSettingsPanel.UiFont, 13f, FontStyle.Bold)) using (SolidBrush b = new SolidBrush(WH)) g.DrawString("PullerView " + Lx.T("設定", "Settings", "设置"), f, b, 16, 12);
            string[] names = { Lx.T("ウィンドウ / 表示", "Window / Display", "窗口 / 显示"), Lx.T("グラフ設定（DataSetting.ini）", "Graph settings (DataSetting.ini)", "图表设置（DataSetting.ini）") };
            int x = 230;
            using (Font f = new Font(GraphSettingsPanel.UiFont, 10f))
            {
                for (int i = 0; i < 2; i++)
                {
                    int w = (int)g.MeasureString(names[i], f).Width + 30; tabR[i] = new Rectangle(x, 8, w, 32); bool act = i == tab;
                    if (act) { using (GraphicsPath p = Rounded(tabR[i], 6)) using (SolidBrush b = new SolidBrush(CARD)) g.FillPath(b, p); using (SolidBrush b = new SolidBrush(CY)) g.FillRectangle(b, x, HeadH - 3, w, 3); }
                    else if (tabR[i].Contains(mouse)) using (GraphicsPath p = Rounded(tabR[i], 6)) using (SolidBrush b = new SolidBrush(Color.FromArgb(0x2A, 0x2A, 0x32))) g.FillPath(b, p);
                    using (SolidBrush b = new SolidBrush(act ? WH : GR)) g.DrawString(names[i], f, b, x + 15, 16);
                    x += w + 8;
                }
                using (SolidBrush b = new SolidBrush(GR)) g.DrawString("×", new Font(GraphSettingsPanel.UiFont, 14f), b, Width - 34, 10);
            }
            // ---- フッター ----
            Rectangle foot = new Rectangle(0, Height - FootH, Width, FootH); using (SolidBrush b = new SolidBrush(DK)) g.FillRectangle(b, foot);
            string info = doc.Path + "   " + Lx.T("最終保存", "Saved", "最后保存") + " " + (doc.Saved == DateTime.MinValue ? "---" : doc.Saved.ToString("yyyy-MM-dd HH:mm"));
            if (doc.Dirty) info += "   ●" + Lx.T("未保存の変更 " + doc.DirtyCount + " 件", doc.DirtyCount + " unsaved changes", "未保存的更改 " + doc.DirtyCount + " 项");
            using (Font f = new Font(GraphSettingsPanel.UiFont, 8.5f)) using (SolidBrush b = new SolidBrush(doc.Dirty ? AMB : GR)) g.DrawString(info, f, b, 16, foot.Y + 10);
            if (msg != "") using (Font f = new Font(GraphSettingsPanel.UiFont, 8.5f)) using (SolidBrush b = new SolidBrush(msgColor)) g.DrawString(msg, f, b, 16, foot.Y + 30);
            btns.Clear();
            string[] bn = { Lx.T("再読込", "Reload", "重新读取"), Lx.T("適用（プレビュー）", "Apply (preview)", "应用（预览）"), Lx.T("保存", "Save", "保存"), Lx.T("閉じる", "Close", "关闭") };
            Action[] ba = { Reload, Apply, Save, CloseClicked }; Color[] bg = { BTN, BTN, CY, BTN }; Color[] fg = { WH, WH, DK, WH };
            int bx = Width - 16;
            using (Font f = new Font(GraphSettingsPanel.UiFont, 10f))
                for (int i = 3; i >= 0; i--)
                {
                    int w = Math.Max(84, (int)g.MeasureString(bn[i], f).Width + 36); bx -= w; Rectangle r = new Rectangle(bx, foot.Y + 10, w, 38);
                    Btn bt = new Btn(); bt.R = r; bt.Text = bn[i]; bt.A = ba[i]; btns.Add(bt);
                    Color c = bg[i]; if (r.Contains(mouse)) c = ControlPaint.Light(c, 0.2f);
                    using (GraphicsPath p = Rounded(r, 8)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
                    StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush b = new SolidBrush(fg[i])) g.DrawString(bn[i], f, b, r, sf);
                    bx -= 10;
                }
            // 外枠
            using (Pen p = new Pen(Color.FromArgb(0x60, 0x60, 0x6A))) using (GraphicsPath gp2 = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10)) g.DrawPath(p, gp2);
        }
        // ---- フッター操作 ----
        void Reload()
        {
            if (doc.Dirty && MessageBox.Show(this, Lx.T("未保存の変更を破棄してファイルから読み直しますか？", "Discard unsaved changes and reload from file?", "放弃未保存的更改并重新读取文件？"), "PullerView", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            doc = DsDoc.Load(doc.Path); gp.Doc = doc; M.LoadIni(doc.Path); gp.Invalidate(); Say(Lx.T("再読込しました（グラフはクリアされました）", "Reloaded (graphs cleared)", "已重新读取（图表已清除）"), GraphSettingsPanel.GRN);
        }
        void Apply() { doc.ApplyToModel(); Say(Lx.T("保存せずにグラフへ反映しました（プレビュー）", "Applied to graphs without saving (preview)", "已应用到图表（未保存，预览）"), GraphSettingsPanel.GRN); }
        void Save()
        {
            try
            {
                for (int p = 0; p < 13; p++) for (int n = 0; n < 4; n++) { GraphCfg c = doc.C[p, n]; string bad = DsDoc.Validate(c); if (bad != null && c.Enable) { Say(Lx.T("保存できません: ", "Cannot save: ", "无法保存: ") + M.Procs[p] + " " + Lx.T("グラフ", "Graph", "图表") + (n + 1) + " — " + bad, GraphSettingsPanel.RED); gp.Select(p); return; } }
                doc.Save(); doc.ApplyToModel(); M.S.Save();
                Say(Lx.T("保存しました: DataSetting.ini / PullerView.ini", "Saved: DataSetting.ini / PullerView.ini", "已保存: DataSetting.ini / PullerView.ini"), GraphSettingsPanel.GRN);
            }
            catch (Exception ex) { Say(Lx.T("保存失敗: ", "Save failed: ", "保存失败: ") + ex.Message, GraphSettingsPanel.RED); M.Log("DataSetting.ini 保存失敗: " + ex); }
        }
        void CloseClicked()
        {
            if (doc.Dirty)
            {
                DialogResult r = MessageBox.Show(this, Lx.T("DataSetting.ini に未保存の変更があります。保存して閉じますか？\n「いいえ」で破棄、「キャンセル」で戻ります。", "DataSetting.ini has unsaved changes. Save and close?\nNo = discard, Cancel = go back.", "DataSetting.ini 有未保存的更改。保存并关闭？\n否 = 放弃，取消 = 返回。"), "PullerView", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) { Save(); if (doc.Dirty) return; }
                else { M.LoadIni(doc.Path); }   // 破棄: ファイルの内容に戻す（プレビュー反映分も戻る）
            }
            M.S.Save(); DialogResult = DialogResult.OK; Close();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); mouse = e.Location;
            if (drag) { Point d = Control.MousePosition; Location = new Point(formStart.X + d.X - dragStart.X, formStart.Y + d.Y - dragStart.Y); return; }
            if (e.Y < HeadH || e.Y > Height - FootH) Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < 2; i++) if (tabR[i].Contains(e.Location)) { SetTab(i); return; }
            if (e.Y < HeadH && e.X > Width - 44) { CloseClicked(); return; }
            foreach (Btn b in btns) if (b.R.Contains(e.Location)) { b.A(); return; }
            if (e.Y < HeadH) { drag = true; dragStart = Control.MousePosition; formStart = Location; }
        }
        bool drag; Point dragStart, formStart;
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); drag = false; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { if (keyData == Keys.Escape && tab == 0) { CloseClicked(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
    }
}
