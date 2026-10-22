// PullerView UIモック — モデル（設定・シミュレーション・判定）
// ※ モックです。VB_FERROTEC とは一切通信しません（値はすべて擬似データ）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

public enum St { Normal, CtrlHi, CtrlLo, SpecHi, SpecLo, Invalid }

public class Settings
{
    public Color BgColor = ColorTranslator.FromHtml("#4D4D4D");
    public int OpacityPct = 65;     // 透過率（背景のみ透過モードでは背景の不透明度）
    public bool BgOnly = true;       // true=背景のみ透過（線・文字は不透明） false=ウィンドウ全体を半透明
    public Color CmtColor = ColorTranslator.FromHtml("#FFD54F");
    public int CmtSize = 9;
    public bool CmtLeft = true;
    public int CmtSpeed = 4;
    public int JudgeSec = 2;
    public int Lang = 0;           // 0=日本語 1=English 2=中文
    public bool AutoY = false;
    public int Logo = 1;            // 0=テキスト, 1..5=ロゴ案
    public bool Standalone = true;  // true=VB_FERROTEC.exe／frmAutoStatus が無くても PullerView を表示（単体起動）。false=式样どおり非表示
    public double XMax = 3500;      // グラフ X 軸（結晶長さ）の最大値 [mm]。投入量により変わるため設定画面で変更し PullerView.ini に保存
    public double XSpan = 0;        // X 軸の目盛間隔 [mm]。0=自動（最大値から 5〜8 本になるよう選択）
    public Settings Clone() { return (Settings)MemberwiseClone(); }

    // ---- PullerView.ini（ウィンドウ設定・X 軸設定の永続化。DataSetting.ini とは別ファイル）----
    public static string IniFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PullerView.ini"); } }
    static string Hex(Color c) { return c.R.ToString("000") + c.G.ToString("000") + c.B.ToString("000"); }
    public void Save()
    {
        try
        {
            string[] ls = new string[] {
                "; PullerView.ini — PullerView のウィンドウ設定（設定画面の OK で自動保存。手動編集も可）",
                "[Graph]",
                "; xaxis_max_mm  : X 軸（結晶長さ）の最大値 [mm]。投入量に合わせて変更（例: 2500 / 3500）",
                "; xaxis_span_mm : X 軸の目盛間隔 [mm]。0=自動",
                "xaxis_max_mm=" + XMax.ToString("0"),
                "xaxis_span_mm=" + XSpan.ToString("0"),
                "auto_y=" + (AutoY ? 1 : 0),
                "judge_sec=" + JudgeSec,
                "[Window]",
                "bgcolorRGB=" + Hex(BgColor),
                "opacity_pct=" + OpacityPct,
                "bg_only=" + (BgOnly ? 1 : 0),
                "logo=" + Logo,
                "lang=" + Lang,
                "standalone=" + (Standalone ? 1 : 0),
                "[Comment]",
                "colorRGB=" + Hex(CmtColor),
                "size_pt=" + CmtSize,
                "scroll_left=" + (CmtLeft ? 1 : 0),
                "speed=" + CmtSpeed };
            File.WriteAllLines(IniFile, ls, new UTF8Encoding(true));
        }
        catch (Exception ex) { M.Log("PullerView.ini 保存失敗: " + ex.Message); }
    }
    public static Settings Load()
    {
        Settings s = new Settings();
        try
        {
            if (!File.Exists(IniFile)) return s;
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string raw in File.ReadAllLines(IniFile, Encoding.UTF8))
            {
                string l = raw.Trim(); if (l == "" || l.StartsWith(";") || l.StartsWith("#") || l.StartsWith("[")) continue;
                int i = l.IndexOf('='); if (i <= 0) continue; d[l.Substring(0, i).Trim().ToLower()] = l.Substring(i + 1).Trim();
            }
            double dv; int iv; string v;
            if (d.TryGetValue("xaxis_max_mm", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) && dv >= 100) s.XMax = dv;
            if (d.TryGetValue("xaxis_span_mm", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) && dv >= 0) s.XSpan = dv;
            if (d.TryGetValue("auto_y", out v)) s.AutoY = v == "1";
            if (d.TryGetValue("judge_sec", out v) && int.TryParse(v, out iv) && iv >= 1 && iv <= 10) s.JudgeSec = iv;
            if (d.TryGetValue("bgcolorrgb", out v)) { Color c = M.Hex(v, s.BgColor); s.BgColor = c; }
            if (d.TryGetValue("opacity_pct", out v) && int.TryParse(v, out iv) && iv >= 0 && iv <= 100) s.OpacityPct = iv;
            if (d.TryGetValue("bg_only", out v)) s.BgOnly = v != "0";
            if (d.TryGetValue("logo", out v) && int.TryParse(v, out iv) && iv >= 0 && iv <= 5) s.Logo = iv;
            if (d.TryGetValue("lang", out v) && int.TryParse(v, out iv) && iv >= 0 && iv <= 2) s.Lang = iv;
            if (d.TryGetValue("standalone", out v)) s.Standalone = v != "0";
            if (d.TryGetValue("colorrgb", out v)) s.CmtColor = M.Hex(v, s.CmtColor);
            if (d.TryGetValue("size_pt", out v) && int.TryParse(v, out iv) && iv >= 8 && iv <= 24) s.CmtSize = iv;
            if (d.TryGetValue("scroll_left", out v)) s.CmtLeft = v != "0";
            if (d.TryGetValue("speed", out v) && int.TryParse(v, out iv) && iv >= 1 && iv <= 10) s.CmtSpeed = iv;
            M.Log("PullerView.ini 読込: X軸最大=" + s.XMax + "mm 目盛=" + (s.XSpan > 0 ? s.XSpan + "mm" : "自動"));
        }
        catch (Exception ex) { M.Log("PullerView.ini 読込失敗（既定値を使用）: " + ex.Message); }
        return s;
    }
    // 自動目盛: 最大値を 5〜8 分割する「きりの良い」間隔
    public double SpanFor(double xmax)
    {
        if (XSpan > 0) return XSpan;
        double[] nice = { 50, 100, 200, 250, 500, 1000, 2000 };
        foreach (double n in nice) if (xmax / n <= 8) return n;
        return 2000;
    }
}

public class GraphCfg
{
    public string Section, Abbr; public int N; public bool Enable = true;
    public int Pv, Sv; public bool CapCustom, UnitCustom; public string CapText = "", UnitText = "";
    public double Usl, Ucl, Lcl, Lsl, Length = 2300, Span = 100;
    public Color Warn = ColorTranslator.FromHtml("#000066"), Alarm = ColorTranslator.FromHtml("#800000");
    public string Caption(int lang)
    {
        if (CapCustom && CapText != "") return CapText;
        string[] t = M.Tag(Pv); return t[lang];
    }
    public string Unit() { if (UnitCustom && UnitText != "") return UnitText; return M.Tag(Pv)[3]; }
}

public class GraphState
{
    public List<double> X = new List<double>(), Pv = new List<double>(), Sv = new List<double>();
    public St Raw = St.Normal, Pending = St.Normal, Confirmed = St.Normal; public int PendCount; public bool Valid = true;
    public void Clear() { X.Clear(); Pv.Clear(); Sv.Clear(); Raw = Pending = Confirmed = St.Normal; PendCount = 0; Valid = true; }
}

public static class M
{
    // 工程（13 状態）。0〜11 = 自動モードの工程（VB_FERROTEC の g_strAutoLogStatus と同じ文字列）、12 = OTHER（AUTO/MANUAL/WAITMATERIALS/MAINTENANCE をまとめた状態。2026-10-21 中村氏要件）
    public static readonly string[] Procs = { "STATUS_CHECK", "VACUUM", "LEAK_CHECK", "MELT", "STABILIZE", "NECK", "CROWN", "SHOULDER", "BODY", "TAIL", "ANEEL", "SHUTDOWN", "OTHER" };
    public static readonly string[] Abbrs = { "STCK", "VACM", "LKCK", "MELT", "STAB", "NECK", "CRWN", "SHLD", "BODY", "TAIL", "ANEL", "SHDW", "OTHER" };
    public const int OTHER = 12;
    // VB_FERROTEC の strPROCESS → 工程 index。NECK 等は同名、AUTO/MANUAL/WAITMATERIALS/MAINTENANCE/空/不明 は OTHER
    public static int ProcOf(string strProcess)
    {
        string s = (strProcess ?? "").Trim().ToUpper(); int i = Array.IndexOf(Procs, s);
        return i >= 0 && i < OTHER ? i : OTHER;
    }
    public static readonly string[] KindJa = { "管理上限超過", "管理下限超過", "規格上限超過", "規格下限超過" };
    public static readonly string[] KindEn = { "UCL exceeded", "LCL exceeded", "USL exceeded", "LSL exceeded" };
    public static readonly string[] KindZh = { "超出管理上限", "超出管理下限", "超出规格上限", "超出规格下限" };

    public static Settings S = Settings.Load();
    public static List<GraphCfg> Cfg = new List<GraphCfg>();
    public static GraphState[] Slots = { new GraphState(), new GraphState(), new GraphState(), new GraphState() };
    public static int[] Inject = new int[4];   // 0=正常 1=管理上限 2=管理下限 3=規格上限 4=規格下限 5=判定不能
    public static int Proc = 8;
    public static double Len;
    public static bool Running = true, AutoStatusShown = true, Comm = true, PvVisible = true, Maximized = true;
    public static bool AutoAdvance, Paused; public static int ProcSec = 40, SpeedMm = 25, Elapsed;
    public static int T; static Random rnd = new Random(7);
    public static event Action<string> LogEvent;
    public static event Action Changed;
    public static string IniPath = "";

    public static void Log(string s) { if (LogEvent != null) LogEvent(s); }
    public static void Fire() { if (Changed != null) Changed(); }

    public static string[] Tag(int n)
    {
        switch (n)
        {
            // 実機 stcMCValue（8/12 インチ共通項目）に合わせた表示名
            case 17: return new string[] { "APC圧力 設定", "APC pressure set", "APC压力 设定", "Pa" };
            case 18: return new string[] { "APC圧力", "APC pressure", "APC压力", "Pa" };
            case 19: return new string[] { "Ar流量 設定", "MFC Ar flow set", "Ar流量 设定", "SLM" };
            case 20: return new string[] { "Ar流量", "MFC Ar flow", "Ar流量", "SLM" };
            case 21: return new string[] { "ヒーター温度", "Heater temp", "加热器温度", "deg" };
            case 22: return new string[] { "ヒーター温度 設定", "Heater temp set", "加热器温度 设定", "deg" };
            case 23: return new string[] { "ヒーター電力 設定", "Heater power set", "加热器功率 设定", "kW" };
            case 24: return new string[] { "ヒーター電力", "Heater power", "加热器功率", "kW" };
            case 27: return new string[] { "LS温度 設定", "LS temp set", "LS温度 设定", "deg" };
            case 28: return new string[] { "LS温度", "LS temp", "LS温度", "deg" };
            case 31: return new string[] { "高真空圧力", "High vacuum", "高真空压力", "Pa" };
            case 36: return new string[] { "S/L", "S/L actual", "S/L", "mm/min" };
            case 35: return new string[] { "S/L 設定", "S/L set", "S/L 设定", "mm/min" };
            case 40: return new string[] { "S/R", "S/R actual", "S/R", "rpm" };
            case 39: return new string[] { "S/R 設定", "S/R set", "S/R 设定", "rpm" };
            case 65: return new string[] { "直径", "Ingot dia", "直径", "mm" };
            case 66: return new string[] { "結晶長さ", "Ingot length", "晶棒长度", "mm" };
            case 67: return new string[] { "重量", "Ingot weight", "重量", "kg" };
            case 92: return new string[] { "目標直径", "ADC1 target", "目标直径", "mm" };
            default: string s = "項目[" + n + "]"; return new string[] { s, "Item[" + n + "]", "项目[" + n + "]", "" };
        }
    }
    public static bool IsTarget(int p) { return p >= 0 && p < Procs.Length; }   // 全 13 状態がグラフ対象（設定があれば表示）
    public static string Abbr { get { return Abbrs[Proc]; } }
    public static List<GraphCfg> Active()
    {
        List<GraphCfg> l = new List<GraphCfg>();
        if (!IsTarget(Proc)) return l;
        foreach (GraphCfg c in Cfg) if (c.Abbr == Abbr && c.Enable) l.Add(c);
        l.Sort(delegate(GraphCfg a, GraphCfg b) { return a.N.CompareTo(b.N); });
        return l;
    }
    public static bool HostAlive { get { return Running && AutoStatusShown; } }                    // VB_FERROTEC.exe 実行中 かつ frmAutoStatus 表示中
    public static bool DataOk { get { return HostAlive && Comm; } }                                 // データが取れている
    public static bool OverlayShown { get { return PvVisible && (HostAlive || S.Standalone); } }    // 単体起動許可なら常に表示

    // r3: warningcolorRGB / alarmcolorRGB = 10進 9桁 RRRGGGBBB（例 000000102 = (0,0,102)）。旧 r2 の 6桁16進 RRGGBB も互換で受理
    public static Color Hex(string h, Color def)
    {
        try
        {
            if (h != null && h.Length == 9) return Color.FromArgb(int.Parse(h.Substring(0, 3)), int.Parse(h.Substring(3, 3)), int.Parse(h.Substring(6, 3)));
            if (h != null && h.Length == 6) return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }
        catch (Exception) { }
        return def;
    }
    static string AbbrOf(string sec)
    {
        string s = sec.ToUpper(); int i = s.Length; while (i > 0 && char.IsDigit(s[i - 1])) i--;
        string name = s.Substring(0, i);
        switch (name)
        {
            case "CROWN": return "CRWN"; case "SHOULDER": return "SHLD";
            case "STATUS_CHECK": return "STCK"; case "VACUUM": return "VACM"; case "LEAK_CHECK": return "LKCK"; case "STABILIZE": return "STAB";
            case "ANEEL": return "ANEL"; case "SHUTDOWN": return "SHDW";
            case "AUTO": case "MANUAL": case "WAITMATERIALS": case "MAINTENANCE": case "OTHR": return "OTHER";   // 旧名は OTHER の別名として受理
        }
        return name;
    }

    public static void LoadIni(string path)
    {
        IniPath = path; Cfg.Clear(); int bad = 0;
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (Exception ex) { Log("DataSetting.ini 読込失敗: " + ex.Message + "（グラフなしで継続）"); Fire(); return; }
        Dictionary<string, Dictionary<string, string>> secs = new Dictionary<string, Dictionary<string, string>>();
        List<string> order = new List<string>(); Dictionary<string, string> cur = null;
        foreach (string raw in lines)
        {
            string l = raw.Trim(); if (l == "" || l.StartsWith(";") || l.StartsWith("#")) continue;
            if (l.StartsWith("[") && l.EndsWith("]")) { string n = l.Substring(1, l.Length - 2).Trim().ToUpper(); cur = new Dictionary<string, string>(); secs[n] = cur; order.Add(n); continue; }
            int e = l.IndexOf('='); if (e < 0 || cur == null) continue;
            cur[l.Substring(0, e).Trim().ToLower()] = l.Substring(e + 1).Trim();
        }
        foreach (string name in order)
        {
            string ab = AbbrOf(name); int i = name.Length; while (i > 0 && char.IsDigit(name[i - 1])) i--;
            int n; if (i == name.Length || !int.TryParse(name.Substring(i), out n) || n < 1 || n > 4) { Log("警告: セクション名不正のため無視 [" + name + "]"); bad++; continue; }
            if (Array.IndexOf(Abbrs, ab) < 0) { Log("警告: 工程略称不明のため無視 [" + name + "]"); bad++; continue; }
            Dictionary<string, string> d = secs[name]; GraphCfg c = new GraphCfg(); c.Section = name; c.Abbr = ab; c.N = n;
            try
            {
                c.Enable = G(d, "enable", "0") == "1"; c.Pv = int.Parse(G(d, "pv_pcinfodata_index", "-1")); c.Sv = int.Parse(G(d, "sv_pcinfodata_index", "-1"));
                c.CapCustom = G(d, "caption", "0") == "1"; c.CapText = G(d, "caption_text", ""); c.UnitCustom = G(d, "unit", "0") == "1"; c.UnitText = G(d, "unit_text", "");
                c.Usl = Dbl(d, "usl"); c.Ucl = Dbl(d, "ucl"); c.Lcl = Dbl(d, "lcl"); c.Lsl = Dbl(d, "lsl");
                if (d.ContainsKey("length")) Log("情報: [" + name + "] length/length_span は未使用（X 軸最大値は PullerView.ini の xaxis_max_mm）");
                c.Warn = Hex(G(d, "warningcolorrgb", G(d, "warningcolor", "")), c.Warn); c.Alarm = Hex(G(d, "alarmcolorrgb", G(d, "alarmcolor", "")), c.Alarm);
                if (c.Pv < 0 || c.Pv > 134 || c.Sv < 0 || c.Sv > 134) throw new Exception("index 範囲外(0〜134)");
                if (!(c.Usl >= c.Ucl && c.Ucl > c.Lcl && c.Lcl >= c.Lsl)) throw new Exception("usl>=ucl>lcl>=lsl 違反");

            }
            catch (Exception ex) { Log("警告: [" + name + "] 無効化（" + ex.Message + "）"); c.Enable = false; bad++; }
            Cfg.Add(c);
        }
        foreach (GraphState s in Slots) s.Clear();
        int en = 0; foreach (GraphCfg c in Cfg) if (c.Enable) en++;
        Log("DataSetting.ini 読込: " + Path.GetFileName(path) + " / 有効グラフ " + en + " 本" + (bad > 0 ? " / 警告 " + bad + " 件" : ""));
        Fire();
    }
    static string G(Dictionary<string, string> d, string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
    static double Dbl(Dictionary<string, string> d, string k) { return double.Parse(G(d, k, "x"), CultureInfo.InvariantCulture); }

    public static void SetProc(int p, string why)
    {
        if (p == Proc) return;
        Log("工程切替: " + Procs[Proc] + " → " + Procs[p] + (why != "" ? "（" + why + "）" : "") + (IsTarget(p) ? "  グラフをクリア" : "  グラフ対象外"));
        Proc = p; Len = 0; Elapsed = 0; foreach (GraphState s in Slots) s.Clear(); Fire();
    }
    public static string[] Kinds() { return S.Lang == 1 ? KindEn : S.Lang == 2 ? KindZh : KindJa; }
    public static string KindName(St s) { string[] k = Kinds(); switch (s) { case St.CtrlHi: return k[0]; case St.CtrlLo: return k[1]; case St.SpecHi: return k[2]; case St.SpecLo: return k[3]; } return ""; }

    static St Classify(double pv, GraphCfg c)
    {
        if (pv > c.Usl) return St.SpecHi; if (pv < c.Lsl) return St.SpecLo;
        if (pv > c.Ucl) return St.CtrlHi; if (pv < c.Lcl) return St.CtrlLo; return St.Normal;
    }

    public static void Tick()
    {
        if (!Running) return;
        T++;
        if (AutoAdvance) { Elapsed++; if (Elapsed >= ProcSec) { int next = Proc + 1; if (next >= Procs.Length) next = 0; SetProc(next, "自動進行"); } }
        if (!Paused && DataOk && IsTarget(Proc)) Len += SpeedMm;
        foreach (GraphCfg c in Active())
        {
            GraphState s = Slots[c.N - 1];
            if (!DataOk || Inject[c.N - 1] == 5) { s.Valid = false; s.Raw = St.Invalid; Judge(c, s); continue; }
            s.Valid = true;
            double mid = (c.Usl + c.Lsl) / 2, h = (c.Ucl - c.Lcl) / 2;
            double sv = mid + 0.15 * h * Math.Sin(T * 0.05 + c.N);
            double pv = sv + h * (0.35 * Math.Sin(T * 0.25 + c.N * 2) + 0.15 * Math.Sin(T * 0.7 + c.N) + 0.06 * (rnd.NextDouble() - 0.5));
            double j = 0.04 * (rnd.NextDouble() - 0.5);
            switch (Inject[c.N - 1])
            {
                case 1: pv = c.Ucl + (c.Usl - c.Ucl) * (0.5 + j); if (c.Usl == c.Ucl) pv = c.Ucl + h * 0.1; break;
                case 2: pv = c.Lcl - (c.Lcl - c.Lsl) * (0.5 + j); if (c.Lcl == c.Lsl) pv = c.Lcl - h * 0.1; break;
                case 3: pv = c.Usl + (c.Usl - c.Lsl) * (0.06 + j); break;
                case 4: pv = c.Lsl - (c.Usl - c.Lsl) * (0.06 + j); break;
            }
            if (s.X.Count > 0 && s.X[s.X.Count - 1] == Len) { s.Pv[s.Pv.Count - 1] = pv; s.Sv[s.Sv.Count - 1] = sv; }
            else { s.X.Add(Len); s.Pv.Add(pv); s.Sv.Add(sv); }
            if (s.X.Count > 3000) { s.X.RemoveAt(0); s.Pv.RemoveAt(0); s.Sv.RemoveAt(0); }
            s.Raw = Classify(pv, c); Judge(c, s);
        }
        Fire();
    }

    static void Judge(GraphCfg c, GraphState s)
    {
        string nm = c.Section + " " + c.Caption(S.Lang);
        if (s.Raw == St.Normal || s.Raw == St.Invalid)
        {
            s.Pending = s.Raw; s.PendCount = 0;
            if (s.Confirmed != s.Raw)
            {
                Log(nm + ": " + (s.Raw == St.Normal ? "復帰（正常）" : "判定不能（---）"));
                s.Confirmed = s.Raw;
            }
        }
        else
        {
            if (s.Pending == s.Raw) s.PendCount++; else { s.Pending = s.Raw; s.PendCount = 1; }
            if (s.PendCount >= S.JudgeSec && s.Confirmed != s.Raw) { s.Confirmed = s.Raw; Log(nm + ": " + KindName(s.Raw) + "（確定・連続" + S.JudgeSec + "秒）"); }
        }
    }

    public static List<string> CommentItems()
    {
        List<string> l = new List<string>();
        foreach (GraphCfg c in Active())
        {
            GraphState s = Slots[c.N - 1];
            if (s.Confirmed == St.CtrlHi || s.Confirmed == St.CtrlLo || s.Confirmed == St.SpecHi || s.Confirmed == St.SpecLo)
                l.Add(c.Caption(S.Lang) + " " + KindName(s.Confirmed));
        }
        return l;
    }
}
