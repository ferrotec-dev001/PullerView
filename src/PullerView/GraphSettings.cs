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
        static readonly string[] Known = { "enable", "pv_pcinfodata_index", "caption", "caption_text", "unit", "unit_text", "sv_pcinfodata_index", "limit_mode", "judge_cond", "usl", "ucl", "lcl", "lsl", "warningcolor", "alarmcolor", "warningcolorrgb", "alarmcolorrgb" };

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
                string sn = Ini.SecName(l);
                if (sn != null)
                {
                    inHeader = false; curName = sn.ToUpper(); cur = new Dictionary<string, string>();
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
                c.Usl = D(s, "usl", c.Usl); c.Ucl = D(s, "ucl", c.Ucl); c.Lcl = D(s, "lcl", c.Lcl); c.Lsl = D(s, "lsl", c.Lsl); c.DevMode = G(s, "limit_mode", "abs").Trim().ToLower() == "dev"; c.JudgeCond = G(s, "judge_cond", "").Trim();
                c.Warn = M.Hex(G(s, "warningcolorrgb", G(s, "warningcolor", "")), c.Warn); c.Alarm = M.Hex(G(s, "alarmcolorrgb", G(s, "alarmcolor", "")), c.Alarm);
            }
            if (d.Header.Count == 0) d.Header.AddRange(new string[] { "; limit_mode=abs: usl/ucl/lcl/lsl は PV の絶対値   limit_mode=dev: SV からの偏差（符号付き。例 usl=5 ucl=3 lcl=-3 lsl=-5）", "; judge_cond=上下限判定を行う制御モード条件（空=常時）。例 MAN14.0=R104014 BIT00 温度制御  MAN14.1=BIT01 電力制御  MAN14.2=BIT02 液面温度制御  MAN19.0=R104019 BIT00 APC圧力制御。一覧は PullerView.ini [JudgeCond]", "; PullerView DataSetting.ini — 工程ごとのグラフ設定（PullerView の設定画面から編集・保存される）", "; セクション: [<工程略称><n>] n=1..4   enable / pv_pcinfodata_index / sv_pcinfodata_index / caption / caption_text / unit / unit_text / usl / ucl / lcl / lsl / warningcolor / alarmcolor" });
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
                o.Add("limit_mode=" + (c.DevMode ? "dev" : "abs")); o.Add("judge_cond=" + c.JudgeCond); o.Add("usl=" + Num(c.Usl)); o.Add("ucl=" + Num(c.Ucl)); o.Add("lcl=" + Num(c.Lcl)); o.Add("lsl=" + Num(c.Lsl));
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
    /// <summary>ダーク配色の簡易イミディエイトモード UI 基底（ヒット領域・数値フィールド・テンキー・キーボード入力・描画ヘルパー）。
    /// グラフ設定タブとウィンドウ／表示タブで共用する。</summary>
    public abstract class DarkPanel : Control
    {
        public static readonly Color BG = Color.FromArgb(0x2B, 0x2B, 0x30), CARD = Color.FromArgb(0x3A, 0x3A, 0x42), FIELD = Color.FromArgb(0x24, 0x24, 0x2A), CY = Color.FromArgb(0x4F, 0xC3, 0xF7),
            AMB = Color.FromArgb(0xFF, 0xC1, 0x07), GRN = Color.FromArgb(0x4C, 0xD9, 0x64), RED = Color.FromArgb(0xFF, 0x5A, 0x5A), BLU = Color.FromArgb(0x3F, 0xA9, 0xF5), GR = Color.FromArgb(0x9A, 0x9A, 0xA5),
            WH = Color.FromArgb(240, 240, 240), DK = Color.FromArgb(0x1E, 0x1E, 0x24), SEL = Color.FromArgb(0x1F, 0x4B, 0x5E), BTN = Color.FromArgb(0x4A, 0x4A, 0x55), BTNR = Color.FromArgb(0x5A, 0x3A, 0x3A), LINE = Color.FromArgb(0x50, 0x50, 0x58);
        protected class Hit { public Rectangle R; public Action A; public string Tip; }
        protected List<Hit> hits = new List<Hit>();
        public class NumField { public string Key; public Func<string> Label; public Func<double> Get; public Action<double> Set; public bool IsInt; public Rectangle R; public Func<string> Range; public double Min = double.MinValue, Max = double.MaxValue, Step = 0.1; }
        protected List<NumField> fields = new List<NumField>(); protected NumField active; protected string buf = ""; protected string err = ""; protected DateTime errAt;
        protected Point mouse = new Point(-1, -1);
        public event Action Changed;
        protected DarkPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = BG; TabStop = true;
        }
        public static string UiFont { get { return M.FontName(); } }
        static bool FontExists(string n) { try { using (Font f = new Font(n, 9f)) return f.Name == n; } catch (Exception) { return false; } }
        /// <summary>値確定後の派生側フック（Dirty 管理など）。</summary>
        protected virtual void OnTouched() { }
        /// <summary>フィールド確定時の追加検証（null=OK、文字列=エラー表示して元に戻す）。</summary>
        protected virtual string ValidateAfterSet(NumField f) { return null; }
        protected void Touch() { OnTouched(); if (Changed != null) Changed(); Invalidate(); }

        // ---------- 描画ヘルパー ----------
        protected Font F(float pt, bool bold) { return new Font(UiFont, pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
        public static GraphicsPath RR(RectangleF r, float rad) { GraphicsPath p = new GraphicsPath(); float d = rad * 2; p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
        protected void Box(Graphics g, Rectangle r, Color fill, Color? outline, int rad, float w) { using (GraphicsPath p = RR(r, rad)) { using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p); if (outline != null) using (Pen pn = new Pen(outline.Value, w)) g.DrawPath(pn, p); } }
        protected void Txt(Graphics g, string s, float pt, Color c, float x, float y, bool bold) { using (Font f = F(pt, bold)) using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, x, y); }
        protected void TxtC(Graphics g, string s, float pt, Color c, RectangleF r, bool bold) { using (Font f = F(pt, bold)) using (SolidBrush b = new SolidBrush(c)) { StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap; g.DrawString(s, f, b, r, sf); } }
        protected void TxtR(Graphics g, string s, float pt, Color c, float right, float y) { using (Font f = F(pt, false)) using (SolidBrush b = new SolidBrush(c)) { SizeF sz = g.MeasureString(s, f); g.DrawString(s, f, b, right - sz.Width, y); } }
        protected float TW(Graphics g, string s, float pt) { using (Font f = F(pt, false)) return g.MeasureString(s, f).Width; }
        protected void Toggle(Graphics g, Rectangle r, bool on) { Box(g, r, on ? GRN : Color.FromArgb(0x60, 0x60, 0x68), null, r.Height / 2, 1); int d = r.Height - 4; using (SolidBrush b = new SolidBrush(WH)) g.FillEllipse(b, on ? r.Right - d - 2 : r.X + 2, r.Y + 2, d, d); }
        protected void Add(Rectangle r, Action a, string tip) { Hit h = new Hit(); h.R = r; h.A = a; h.Tip = tip; hits.Add(h); }
        protected bool Hover(Rectangle r) { return r.Contains(mouse); }
        /// <summary>セグメント（排他選択）ボタン列。各ボタンはテキスト幅に追従。戻り値は全体幅。</summary>
        protected int Segment(Graphics g, int x, int y, int h, string[] items, int sel, Action<int> set, float pt) { return Segment(g, x, y, h, items, sel, set, pt, 26); }
        protected int Segment(Graphics g, int x, int y, int h, string[] items, int sel, Action<int> set, float pt, int pad)
        {
            int cx = x;
            for (int i = 0; i < items.Length; i++)
            {
                int w = (int)TW(g, items[i], pt) + pad; Rectangle r = new Rectangle(cx, y, w, h); bool on = i == sel; int ix = i;
                Box(g, r, on ? CY : Hover(r) ? Color.FromArgb(0x5A, 0x5A, 0x66) : FIELD, on ? (Color?)null : LINE, h / 2, 1); TxtC(g, items[i], pt, on ? DK : WH, r, on); Add(r, delegate { set(ix); }, null); cx += w + 4;
            }
            return cx - x;
        }
        /// <summary>ラベル付きトグル行。</summary>
        protected void ToggleRow(Graphics g, int x, int y, int w, string label, bool on, Action<bool> set)
        {
            Txt(g, label, 9f, WH, x, y + 6, false); Rectangle tr = new Rectangle(x + w - 58, y, 58, 28); Toggle(g, tr, on);
            Rectangle hr = new Rectangle(x, y - 4, w, 36); Add(hr, delegate { set(!on); }, null);
        }
        /// <summary>色見本ボタン（クリックで ColorDialog）。</summary>
        protected void ColorBtn(Graphics g, Rectangle r, Color c, Action<Color> set)
        {
            Box(g, r, c, Hover(r) ? CY : LINE, 7, Hover(r) ? 2 : 1);
            Txt(g, "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"), 8f, c.GetBrightness() > 0.55f ? DK : WH, r.X + 8, r.Y + r.Height / 2 - 7, false);
            Add(r, delegate { PickColor(set, c); }, null);
        }
        /// <summary>数値フィールド（描画＋ヒット登録）。アクティブ時は入力バッファを表示。</summary>
        protected void NumBox(Graphics g, Rectangle r, NumField f, string suffix)
        {
            bool act = active == f; Box(g, r, FIELD, act ? CY : LINE, 7, act ? 2 : 1);
            string s = act ? buf + "_" : (f.IsInt ? ((int)f.Get()).ToString() : Fmt(f.Get()));
            Txt(g, s, 11f, act ? CY : WH, r.X + 10, r.Y + r.Height / 2 - 10, false);
            if (suffix != null && !act) TxtR(g, suffix, 8f, GR, r.Right - 8, r.Y + r.Height / 2 - 7);
        }
        /// <summary>スライダー（クリック／ドラッグ対応）。</summary>
        protected void Slider(Graphics g, Rectangle r, double v, double min, double max, Action<double> set, Color col)
        {
            int cy = r.Y + r.Height / 2; Rectangle track = new Rectangle(r.X + 10, cy - 3, r.Width - 20, 6); Box(g, track, FIELD, null, 3, 1);
            double frac = max > min ? (Math.Max(min, Math.Min(max, v)) - min) / (max - min) : 0; int fx = track.X + (int)(frac * track.Width);
            Box(g, new Rectangle(track.X, track.Y, Math.Max(0, fx - track.X), 6), col, null, 3, 1);
            using (SolidBrush b = new SolidBrush(WH)) g.FillEllipse(b, fx - 9, cy - 9, 18, 18);
            Action<Point> f = delegate (Point p) { double nv = min + (max - min) * (p.X - track.X) / (double)track.Width; set(Math.Max(min, Math.Min(max, nv))); };
            sliders.Add(new SliderHit(r, f)); Add(r, delegate { f(mouse); }, null);
        }
        class SliderHit { public Rectangle R; public Action<Point> F; public SliderHit(Rectangle r, Action<Point> f) { R = r; F = f; } }
        List<SliderHit> sliders = new List<SliderHit>(); SliderHit dragging;

        protected static string Fmt(double v) { return v.ToString("0.0##", CultureInfo.InvariantCulture); }
        // ---------- 数値フィールド＋テンキー ----------
        protected NumField Field(string key, string label, Func<double> get, Action<double> set, bool isInt, Rectangle r, Func<string> range)
        {
            NumField f = new NumField(); f.Key = key; f.Label = delegate { return label; }; f.Get = get; f.Set = set; f.IsInt = isInt; f.R = r; f.Range = range; fields.Add(f);
            if (active != null && active.Key == key) { active = f; }   // 再描画後も同じ論理フィールドを維持
            Add(r, delegate { Activate(f); }, null); return f;
        }
        protected NumField Field(string key, string label, Func<double> get, Action<double> set, bool isInt, Rectangle r, double min, double max, double step)
        {
            NumField f = Field(key, label, get, set, isInt, r, null); f.Min = min; f.Max = max; f.Step = step;
            f.Range = delegate { return Lx.T("範囲 ", "Range ", "范围 ") + Fmt(min) + " ～ " + Fmt(max) + (isInt ? Lx.T("（整数）", " (integer)", "（整数）") : ""); }; return f;
        }
        protected void Activate(NumField f) { if (active == f) return; CommitIfValid(); active = f; buf = ""; Focus(); Invalidate(); }
        protected bool CommitIfValid()
        {
            if (active == null || buf == "") return true;
            double v; if (!double.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { Err(Lx.T("数値として読めません", "Not a number", "无法解析为数值")); return false; }
            if (active.IsInt && v != Math.Floor(v)) { Err(Lx.T("整数で入力してください", "Integer only", "请输入整数")); return false; }
            if (v < active.Min || v > active.Max) { Err(Lx.T("範囲外: ", "Out of range: ", "超出范围: ") + Fmt(active.Min) + " ～ " + Fmt(active.Max)); return false; }
            double old = active.Get(); active.Set(v);
            string bad = ValidateAfterSet(active);
            if (bad != null) { active.Set(old); Err(bad); return false; }
            buf = ""; Touch(); return true;
        }
        protected void Err(string s) { err = s; errAt = DateTime.Now; Invalidate(); Timer t = new Timer(); t.Interval = 4200; t.Tick += delegate { t.Stop(); t.Dispose(); Invalidate(); }; t.Start(); }
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
                case "UP": Step(active.IsInt ? Math.Max(1, active.Step) : active.Step); break;
                case "DOWN": Step(-(active.IsInt ? Math.Max(1, active.Step) : active.Step)); break;
                default: if (k.Length == 1 && char.IsDigit(k[0]) && buf.Length < 10) buf += k; break;
            }
            Invalidate();
        }
        void Step(double d)
        {
            double old = active.Get(); double v = Math.Round((buf == "" ? old : Parse(buf, old)) + d, 3);
            if (v < active.Min || v > active.Max) return;
            active.Set(v); string bad = ValidateAfterSet(active); if (bad != null) { active.Set(old); Err(bad); return; } buf = ""; Touch();
        }
        protected static double Parse(string s, double def) { double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        void Next(int dir)
        {
            if (fields.Count == 0) return; if (!CommitIfValid()) return;
            int i = active == null ? -1 : fields.IndexOf(active); i = (i + dir + fields.Count) % fields.Count; active = fields[i]; buf = ""; Invalidate();
        }
        /// <summary>右カラムのテンキー（幅 276）。extraHint は注記 3 行目（null 可）。</summary>
        protected void DrawNumPad(Graphics g, int rx, string extraHint)
        {
            int rw = 276; Rectangle K = new Rectangle(rx, 12, rw, Height - 24); Box(g, K, CARD, null, 10, 1);
            string head = Lx.T("テンキー  — 入力中: ", "Keypad  — Editing: ", "数字键盘  — 输入中: "); Txt(g, head, 8.5f, GR, rx + 16, 20, false);
            Txt(g, active == null ? Lx.T("（数値欄を選択）", "(select a field)", "（请选择数值栏）") : active.Label(), 8.5f, CY, rx + 16 + TW(g, head, 8.5f), 20, false);
            Rectangle disp = new Rectangle(rx + 16, 40, rw - 32, 52); Box(g, disp, Color.FromArgb(0x14, 0x14, 0x18), active != null ? CY : LINE, 8, 2);
            TxtR(g, active == null ? "" : (buf == "" ? Fmt(active.Get()) : buf), 18f, active == null ? GR : WH, disp.Right - 14, disp.Y + 6);
            if (active != null && active.Range != null) Txt(g, active.Range(), 7.5f, GR, disp.X + 8, disp.Bottom - 16, false);
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
            Txt(g, Lx.T("  Esc 取消 / Tab 次の項目 / ↑↓ で増減）", "  Esc cancel / Tab next field / Up-Down step)", "  Esc 取消 / Tab 下一项 / 上下键 增减）"), 7.5f, GR, rx + 16, yy + 15, false);
            if (extraHint != null) Txt(g, extraHint, 7.5f, GR, rx + 16, yy + 36, false);
        }
        // ---------- 入力 ----------
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
            base.OnMouseDown(e); Focus(); dragging = null;
            foreach (SliderHit s in sliders) if (s.R.Contains(e.Location)) { dragging = s; s.F(e.Location); Invalidate(); return; }
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(e.Location)) { hits[i].A(); return; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); mouse = e.Location;
            if (dragging != null && e.Button == MouseButtons.Left) dragging.F(e.Location);
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = null; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-1, -1); Invalidate(); }
        protected void PickColor(Action<Color> set, Color cur) { using (ColorDialog d = new ColorDialog()) { d.Color = cur; d.FullOpen = true; if (d.ShowDialog(this) == DialogResult.OK) set(d.Color); } }
        /// <summary>ペイント前の共通初期化（ヒット領域・フィールド再構築）。</summary>
        protected void BeginPaint(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = M.TextHint(false); g.Clear(BG); hits.Clear(); fields.Clear(); sliders.Clear(); }
    }

    /// <summary>DataSetting.ini 編集タブ。</summary>
    public class GraphSettingsPanel : DarkPanel
    {
        public DsDoc Doc; int proc = 8, slot = 0;                   // 選択中の工程（既定 BODY）とグラフ枠
        TextBox capBox, unitBox;                                     // 任意入力（表示名・単位）は実 TextBox
        public GraphSettingsPanel(DsDoc doc)
        {
            Doc = doc;
            capBox = MakeBox(); unitBox = MakeBox();
            capBox.TextChanged += delegate { if (!loading) { Cur.CapText = capBox.Text; Touch(); } };
            unitBox.TextChanged += delegate { if (!loading) { Cur.UnitText = unitBox.Text; Touch(); } };
            proc = Math.Max(0, Math.Min(12, M.Proc));
        }
        bool loading;
        TextBox MakeBox() { TextBox b = new TextBox(); b.BorderStyle = BorderStyle.None; /* 枠線なし: 角丸 Box を背面に描くため四角枠を出さない */ b.BackColor = FIELD; b.ForeColor = WH; b.Font = new Font(UiFont, 11f); Controls.Add(b); return b; }
        GraphCfg Cur { get { return Doc.C[proc, slot]; } }
        protected override void OnTouched() { Doc.Dirty = true; Doc.DirtyCount++; }
        protected override string ValidateAfterSet(NumField f) { return f.IsInt ? null : DsDoc.Validate(Cur); }
        public void Select(int p) { proc = p; slot = 0; active = null; buf = ""; Invalidate(); }

        // ---------- 描画 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; BeginPaint(g);
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
            Txt(g, Lx.T("グラフ背景色", "Graph background", "图表背景色") + "  " + Lx.T("（管理外 / 規格外）", "(out of control / out of spec)", "（超出管理限 / 超出规格限）"), 8.5f, GR, lx, y0 + 318, false);
            int half = lw / 2;   // 2 色を 1 行に並べる（判定条件の行を確保）
            Rectangle sw1 = new Rectangle(lx, y0 + 340, 22, 22); Box(g, sw1, c.Warn, LINE, 4, 1); Txt(g, "UCL/LCL  " + Hx6(c.Warn), 8.5f, WH, lx + 28, y0 + 343, false);
            Add(new Rectangle(lx, y0 + 338, half - 4, 26), delegate { PickColor(delegate(Color k) { c.Warn = k; Touch(); }, c.Warn); }, null);
            Rectangle sw2 = new Rectangle(lx + half, y0 + 340, 22, 22); Box(g, sw2, c.Alarm, LINE, 4, 1); Txt(g, "USL/LSL  " + Hx6(c.Alarm), 8.5f, WH, lx + half + 28, y0 + 343, false);
            Add(new Rectangle(lx + half, y0 + 338, half, 26), delegate { PickColor(delegate(Color k) { c.Alarm = k; Touch(); }, c.Alarm); }, null);
            // 判定条件（制御モード）
            using (Pen pn = new Pen(LINE)) g.DrawLine(pn, lx, y0 + 370, lx + lw, y0 + 370);
            Txt(g, Lx.T("上下限判定を行う条件（制御モード）", "Judge only when (control mode)", "进行上下限判定的条件（控制模式）"), 8.5f, GR, lx, y0 + 376, false);
            Rectangle jb = new Rectangle(lx, y0 + 394, lw, 34); Box(g, jb, Hover(jb) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, LINE, 7, 1);
            using (Font jf = F(8.5f, false)) using (SolidBrush b = new SolidBrush(WH)) { StringFormat sf = new StringFormat(); sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap; g.DrawString(M.CondName(c.JudgeCond) + "  ▾", jf, b, new RectangleF(jb.X + 10, jb.Y, jb.Width - 14, jb.Height), sf); }
            Add(jb, delegate { PickCond(delegate(string v) { c.JudgeCond = v; Touch(); }, c.JudgeCond); }, null);
            // ---- 右カラム: 上下限（縦配置） ----
            int rx0 = cx + 288, rw0 = cw - 288 - 16;
            Txt(g, Lx.T("上下限", "Limits", "上下限"), 9f, WH, rx0 + 24, y0 + 10, false);
            Segment(g, rx0 + 24 + 50, y0 + 4, 26, new[] { Lx.T("絶対値", "Absolute", "绝对值"), Lx.T("SV偏差", "SV ± dev", "SV偏差") }, c.DevMode ? 1 : 0, delegate(int i) { c.DevMode = i == 1; Touch(); }, 8f, 10);
            int railTop = y0 + 36, railBot = y0 + 352;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0x55, 0x55, 0x60))) g.FillRectangle(b, rx0 + 6, railTop, 4, railBot - railTop);
            using (SolidBrush b = new SolidBrush(RED)) { g.FillPolygon(b, new Point[] { new Point(rx0 + 8, railTop - 4), new Point(rx0 + 2, railTop + 8), new Point(rx0 + 14, railTop + 8) }); g.FillPolygon(b, new Point[] { new Point(rx0 + 8, railBot + 4), new Point(rx0 + 2, railBot - 8), new Point(rx0 + 14, railBot - 8) }); }
            Txt(g, Lx.T("上限側", "upper", "上限侧"), 7f, GR, rx0 + 18, railTop - 2, false); Txt(g, Lx.T("下限側", "lower", "下限侧"), 7f, GR, rx0 + 18, railBot - 10, false);
            if (c.DevMode) { int my = (railTop + railBot) / 2; using (Pen pn = new Pen(WH, 1f)) g.DrawLine(pn, rx0 + 2, my, rx0 + 14, my); Txt(g, "SV = 0", 7f, WH, rx0 + 18, my - 6, false); }
            string[] lk = { "usl", "ucl", "lcl", "lsl" }; string[] ln0 = { Lx.T("USL 規格上限", "USL  Spec upper", "USL 规格上限"), Lx.T("UCL 管理上限", "UCL  Control upper", "UCL 管理上限"), Lx.T("LCL 管理下限", "LCL  Control lower", "LCL 管理下限"), Lx.T("LSL 規格下限", "LSL  Spec lower", "LSL 规格下限") };
            string[] ln = new string[4]; for (int i = 0; i < 4; i++) ln[i] = c.DevMode ? ln0[i] + Lx.T("（SV からの偏差）", " (dev. from SV)", "（与 SV 的偏差）") : ln0[i];
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
                Txt(g, ln[i], 8f, GR, r.X + 16, r.Y + 6, false); Txt(g, act ? buf + "_" : (c.DevMode && get[i]() > 0 ? "+" : "") + Fmt(get[i]()), 13f, act ? CY : WH, r.X + 14, r.Y + 24, true); TxtR(g, unit, 8f, GR, r.Right - 10, r.Y + 32);
            }
            Rectangle curR = new Rectangle(rx0 + 24, y0 + 186, rw0 - 24, 26); Box(g, curR, SEL, null, 13, 1);
            string curTxt = CurrentValueText(c); TxtC(g, curTxt, 8f, Color.FromArgb(190, 235, 255), curR, false); using (SolidBrush b = new SolidBrush(GRN)) g.FillEllipse(b, rx0 + 3, y0 + 194, 10, 10);
            string vmsg = DsDoc.Validate(c);
            Txt(g, vmsg == null ? "OK  USL ≥ UCL > LCL ≥ LSL" : "✖ " + vmsg, 8f, vmsg == null ? GRN : RED, rx0 + 24, y0 + 360, false);
            // プレビュー帯
            Rectangle pv = new Rectangle(lx, card.Bottom - 80, cw - 32, 34); Box(g, pv, Color.FromArgb(0x3C, 0x3C, 0x3C), null, 6, 1); DrawPreview(g, pv, c);
            Txt(g, Lx.T("※ 数値欄をタップするとテンキーで入力できます。キーボードからも入力可", "※ Tap a numeric field to edit with the keypad; keyboard also works", "※ 点击数值栏可用数字键盘输入，也可使用键盘"), 7.5f, GR, lx, card.Bottom - 40, false);
            if (err != "" && (DateTime.Now - errAt).TotalSeconds < 4) Txt(g, err, 8.5f, RED, lx, card.Bottom - 22, true);

            // ===== 右: テンキー =====
            DrawNumPad(g, 712, Lx.T("・index 欄は整数のみ。範囲外は赤表示で確定不可", "- Index fields: integer only. Out of range = rejected", "・index 栏仅限整数。超范围不可确定"));
        }
        static string Hx6(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
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
        void DrawIndexRow(Graphics g, int lx, int lw, int y, string label, string key, Func<double> get, Action<double> set)
        {
            Txt(g, label, 8.5f, GR, lx, y, false);
            Rectangle r = new Rectangle(lx, y + 16, 76, 36); NumField f = Field(key, label, get, set, true, r, delegate { return Lx.T("範囲 0〜134（整数）", "Range 0–134 (integer)", "范围 0～134（整数）"); });
            bool act = active == f; Box(g, r, FIELD, act ? CY : LINE, 7, act ? 2 : 1); Txt(g, act ? buf + "_" : ((int)get()).ToString(), 11f, act ? CY : WH, r.X + 10, r.Y + 8, false);
            int idx = (int)get(); string[] tag = M.Tag(idx);
            Rectangle pb = new Rectangle(lx + lw - 86, y + 16, 86, 36);
            using (Font tf = F(8.5f, false)) using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 235, 255))) { StringFormat sf = new StringFormat(); sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap; g.DrawString(tag[M.S.Lang] + (tag[3] != "" ? " [" + tag[3] + "]" : ""), tf, b, new RectangleF(lx + 82, y + 16, pb.X - lx - 86, 36), sf); }   // 長い項目名は … で省略（英語名対策）
            Box(g, pb, Hover(pb) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 7, 1); TxtC(g, Lx.T("一覧から選ぶ", "Pick from list", "从列表选择"), 8f, WH, pb, false);
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
            loading = true; Rectangle nb = new Rectangle(tb.X + 8, tb.Y + 8, tb.Width - 16, 20); if (box.Bounds != nb) box.Bounds = nb; box.Enabled = custom; string nt = custom ? text : autoText; if (box.Text != nt) box.Text = nt; box.BackColor = custom ? FIELD : Color.FromArgb(0x2E, 0x2E, 0x35); box.ForeColor = custom ? WH : Color.FromArgb(140, 140, 150); loading = false;
            Box(g, tb, custom ? FIELD : Color.FromArgb(0x2E, 0x2E, 0x35), LINE, 7, 1);
        }
        void DrawPreview(Graphics g, Rectangle r, GraphCfg c)
        {
            double hi = c.Usl, lo = c.Lsl, span = hi - lo; if (span <= 0) span = 1; hi += span * 0.15; lo -= span * 0.15; span = hi - lo;   // 偏差モードは SV=0 基準の相対表示
            Func<double, float> Y = delegate(double v) { return (float)(r.Bottom - 3 - (v - lo) / span * (r.Height - 6)); };
            Color[] cs = { RED, BLU, BLU, RED }; double[] vs = { c.Usl, c.Ucl, c.Lcl, c.Lsl };
            for (int i = 0; i < 4; i++) using (Pen p = new Pen(cs[i], 1f)) g.DrawLine(p, r.X + 4, Y(vs[i]), r.Right - 70, Y(vs[i]));
            double mid = (c.Ucl + c.Lcl) / 2; using (Pen p = new Pen(GRN, 2f)) g.DrawLine(p, r.X + 14, Y(mid - span * 0.03), r.Right - 80, Y(mid + span * 0.02));
            if (c.DevMode) { using (Pen p = new Pen(WH, 1f)) { p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; g.DrawLine(p, r.X + 4, Y(0), r.Right - 70, Y(0)); } Txt(g, "SV", 7f, WH, r.X + 6, (int)Y(0) - 11, false); }
            TxtR(g, Lx.T("プレビュー", "Preview", "预览"), 7.5f, GR, r.Right - 8, r.Y + 9);
        }

        // ---------- 入力 ----------
        // ---------- サブダイアログ ----------
        void PickCond(Action<string> set, string cur)
        {
            using (Form f = new Form())
            {
                f.Text = Lx.T("判定条件を選ぶ", "Pick judge condition", "选择判定条件"); f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = f.MinimizeBox = false; f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(560, 440); f.BackColor = BG; f.ForeColor = WH; f.Font = new Font(UiFont, 11f);
                ListView lv = new ListView(); lv.View = View.Details; lv.FullRowSelect = true; lv.HideSelection = false; lv.BackColor = FIELD; lv.ForeColor = WH; lv.Font = new Font(UiFont, 11f); lv.SetBounds(12, 12, 536, 370); lv.MultiSelect = false;
                lv.Columns.Add(Lx.T("条件式", "Expr", "条件式"), 110); lv.Columns.Add(Lx.T("名称", "Name", "名称"), 400);
                ListViewItem it0 = new ListViewItem(""); it0.SubItems.Add(M.CondName("")); it0.Tag = ""; lv.Items.Add(it0); if (cur == null || cur.Trim() == "") it0.Selected = true;
                foreach (M.CondDef cd in M.Conds) { ListViewItem it = new ListViewItem(cd.Expr); it.SubItems.Add(cd.Name(M.S.Lang)); it.Tag = cd.Expr; lv.Items.Add(it); if (cur != null && string.Compare(cd.Expr, cur.Trim(), StringComparison.OrdinalIgnoreCase) == 0) it.Selected = true; }
                if (lv.SelectedItems.Count == 0 && cur != null && cur.Trim() != "") { ListViewItem it = new ListViewItem(cur.Trim()); it.SubItems.Add(cur.Trim()); it.Tag = cur.Trim(); lv.Items.Add(it); it.Selected = true; }
                f.Controls.Add(lv);
                Button ok = new Button(); ok.Text = Lx.T("選択", "Select", "选择"); ok.SetBounds(340, 392, 100, 38); ok.DialogResult = DialogResult.OK; ok.FlatStyle = FlatStyle.Flat; ok.BackColor = CY; ok.ForeColor = DK; f.Controls.Add(ok);
                Button ca = new Button(); ca.Text = Lx.T("キャンセル", "Cancel", "取消"); ca.SetBounds(448, 392, 100, 38); ca.DialogResult = DialogResult.Cancel; ca.FlatStyle = FlatStyle.Flat; ca.BackColor = BTN; ca.ForeColor = WH; f.Controls.Add(ca);
                lv.DoubleClick += delegate { if (lv.SelectedItems.Count > 0) f.DialogResult = DialogResult.OK; };
                f.AcceptButton = ok; f.CancelButton = ca;
                if (f.ShowDialog(this) == DialogResult.OK && lv.SelectedItems.Count > 0) set((string)lv.SelectedItems[0].Tag);
            }
        }
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
            win = new WindowSettingsPanel(); win.Bounds = new Rectangle(0, HeadH, 1000, 720 - HeadH - FootH); win.Visible = false; Controls.Add(win);
            win.Changed += delegate { Invalidate(); };   // 言語切替などでヘッダー／フッターも再描画
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
        void SetTab(int i) { tab = i; win.Visible = i == 0; gp.Visible = i == 1; if (i == 1) gp.Focus(); else win.Focus(); Invalidate(); }
        void Say(string s, Color c) { msg = s; msgColor = c; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = M.TextHint(false);
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
