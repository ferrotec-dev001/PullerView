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
    public int OpacityPct = 92;
    public Color CmtColor = ColorTranslator.FromHtml("#FFD54F");
    public int CmtSize = 9;
    public bool CmtLeft = true;
    public int CmtSpeed = 4;
    public int JudgeSec = 2;
    public int Lang = 0;           // 0=日本語 1=English 2=中文
    public bool AutoY = false;
    public Settings Clone() { return (Settings)MemberwiseClone(); }
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
    public static readonly string[] Procs = { "STATUS_CHECK", "VACUUM", "LEAK_CHECK", "MELT", "STABILIZE", "NECK", "CROWN", "SHOULDER", "BODY", "TAIL", "ANEEL", "SHUTDOWN", "AUTO", "MANUAL", "WAITMATERIALS", "MAINTENANCE" };
    public static readonly string[] Abbrs = { "STCK", "VACM", "LKCK", "MELT", "STAB", "NECK", "CRWN", "SHLD", "BODY", "TAIL", "ANEL", "SHDW", "AUTO", "MANU", "WAIT", "MENT" };
    public static readonly string[] KindJa = { "管理上限超過", "管理下限超過", "規格上限超過", "規格下限超過" };
    public static readonly string[] KindEn = { "UCL exceeded", "LCL exceeded", "USL exceeded", "LSL exceeded" };
    public static readonly string[] KindZh = { "超出管理上限", "超出管理下限", "超出规格上限", "超出规格下限" };

    public static Settings S = new Settings();
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
            case 65: return new string[] { "直径", "Diameter", "直径", "mm" };
            case 66: return new string[] { "重量", "Weight", "重量", "kg" };
            case 67: return new string[] { "S/L平均", "S/L Ave", "S/L平均", "mm/min" };
            case 68: return new string[] { "ヒーター温度", "Heater temp", "加热器温度", "℃" };
            case 69: return new string[] { "GAP", "GAP", "GAP", "mm" };
            case 70: return new string[] { "回転数", "Rotation", "转速", "rpm" };
            case 71: return new string[] { "PWR", "PWR", "PWR", "kW" };
            default: string s = "項目[" + n + "]"; return new string[] { s, "Item[" + n + "]", "项目[" + n + "]", "" };
        }
    }
    public static bool IsTarget(int p) { return p >= 5 && p <= 9; }
    public static string Abbr { get { return Abbrs[Proc]; } }
    public static List<GraphCfg> Active()
    {
        List<GraphCfg> l = new List<GraphCfg>();
        if (!IsTarget(Proc)) return l;
        foreach (GraphCfg c in Cfg) if (c.Abbr == Abbr && c.Enable) l.Add(c);
        l.Sort(delegate(GraphCfg a, GraphCfg b) { return a.N.CompareTo(b.N); });
        return l;
    }
    public static bool OverlayShown { get { return Running && AutoStatusShown && PvVisible; } }

    public static Color Hex(string h, Color def)
    {
        try { if (h != null && h.Length == 6) return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16)); } catch (Exception) { }
        return def;
    }
    static string AbbrOf(string sec)
    {
        string s = sec.ToUpper(); int i = s.Length; while (i > 0 && char.IsDigit(s[i - 1])) i--;
        string name = s.Substring(0, i);
        if (name == "CROWN") return "CRWN"; if (name == "SHOULDER") return "SHLD";
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
            if (Array.IndexOf(Abbrs, ab) < 5 || Array.IndexOf(Abbrs, ab) > 9) continue; // 対象外工程は読み飛ばし
            Dictionary<string, string> d = secs[name]; GraphCfg c = new GraphCfg(); c.Section = name; c.Abbr = ab; c.N = n;
            try
            {
                c.Enable = G(d, "enable", "0") == "1"; c.Pv = int.Parse(G(d, "pv_pcinfodata_index", "-1")); c.Sv = int.Parse(G(d, "sv_pcinfodata_index", "-1"));
                c.CapCustom = G(d, "caption", "0") == "1"; c.CapText = G(d, "caption_text", ""); c.UnitCustom = G(d, "unit", "0") == "1"; c.UnitText = G(d, "unit_text", "");
                c.Usl = Dbl(d, "usl"); c.Ucl = Dbl(d, "ucl"); c.Lcl = Dbl(d, "lcl"); c.Lsl = Dbl(d, "lsl");
                c.Length = Dbl(d, "length"); c.Span = Dbl(d, "length_span");
                c.Warn = Hex(G(d, "warningcolor", ""), c.Warn); c.Alarm = Hex(G(d, "alarmcolor", ""), c.Alarm);
                if (c.Pv < 0 || c.Pv > 134 || c.Sv < 0 || c.Sv > 134) throw new Exception("index 範囲外(0〜134)");
                if (!(c.Usl >= c.Ucl && c.Ucl > c.Lcl && c.Lcl >= c.Lsl)) throw new Exception("usl>=ucl>lcl>=lsl 違反");
                if (c.Length <= 0 || c.Span <= 0) throw new Exception("length/length_span 不正");
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
        if (AutoAdvance) { Elapsed++; if (Elapsed >= ProcSec) { int next = Proc + 1; if (next > 10) next = 5; SetProc(next, "自動進行"); } }
        if (!Paused && Comm && IsTarget(Proc)) Len += SpeedMm;
        foreach (GraphCfg c in Active())
        {
            GraphState s = Slots[c.N - 1];
            if (!Comm || Inject[c.N - 1] == 5) { s.Valid = false; s.Raw = St.Invalid; Judge(c, s); continue; }
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
