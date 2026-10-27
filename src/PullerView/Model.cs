// PullerView UIモック — モデル（設定・シミュレーション・判定）
// ※ モックです。VB_FERROTEC とは一切通信しません（値はすべて擬似データ）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace PullerView
{

public enum St { Normal, CtrlHi, CtrlLo, SpecHi, SpecLo, Invalid, Skip }   // Skip=判定対象外（制御モード条件を満たさない）

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
    public string FontName = "";   // 空=言語ごとの自動選択（ja: Meiryo UI → Yu Gothic UI / zh: Microsoft YaHei UI → Microsoft YaHei / en: Segoe UI）
    public int FontRender = 0;     // 0=なめらか(AntiAliasGridFit) 1=くっきり(ClearType。透過ウィンドウでは AA に自動切替) 2=非アンチエイリアス(SingleBitPerPixelGridFit)
    public bool AutoY = false;
    public int Logo = 1;            // ロゴは案1（インゴット＋トレンド）固定（2026-10-24 中村氏決定。設定画面の選択は廃止、ini の logo キーは無視）
    public bool Standalone = true;  // true=VB_FERROTEC.exe／frmAutoStatus が無くても PullerView を表示（単体起動）。false=式样どおり非表示
    public double XMax = 3500;      // グラフ X 軸（結晶長さ）の最大値 [mm]。投入量により変わるため設定画面で変更し PullerView.ini に保存
    public double XSpan = 0;        // X 軸の目盛間隔 [mm]。0=自動（最大値から 5〜8 本になるよう選択）
    public double TMax = 120;       // 結晶長さの無い工程の X 軸（工程開始からの経過時間）の最大 [分]。超過したらグラフをスクロール
    public double TSpan = 0;        // 時間軸の目盛間隔 [分]。0=自動
    public string LengthProcs = "NECK,CROWN,SHOULDER,BODY,TAIL";   // X 軸 = 結晶長さ とする工程（それ以外は時間軸）
    public bool CsvLog = true; public string CsvDir = "log"; public string CsvEnc = "utf-8";   // 生データ CSV ログ（utf-8=BOM 付き UTF-8。全言語 OS の Excel/メモ帳で文字化けしない。utf-16 も可）
    public int PosX = 300, PosY = 67;   // frmAutoStatus 左上からの表示位置（ドラッグで変更 → 保存）
    public bool ProcAuto = true;        // 工程追従: true=自動（Remoting の strPROCESS に追従） false=手動（ManualProc を表示）
    public int ManualProc = 8;          // 手動モードで表示する工程（Procs の index。既定 BODY=8）
    public Settings Clone() { return (Settings)MemberwiseClone(); }

    // ---- PullerView.ini（ウィンドウ設定・X 軸設定の永続化。DataSetting.ini とは別ファイル）----
    public static string IniFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PullerView.ini"); } }
    static string Hex(Color c) { return c.R.ToString("000") + c.G.ToString("000") + c.B.ToString("000"); }
    public void Save()
    {
        try
        {
            Ini ini = Ini.Load(IniFile);   // [Host] [Ment] など手動設定のセクションは保持する
            M.LoadConds(ini);
            ini.Set("Graph", "xaxis_max_mm", XMax.ToString("0")); ini.Set("Graph", "xaxis_span_mm", XSpan.ToString("0"));
            ini.Set("Graph", "taxis_max_min", TMax.ToString("0")); ini.Set("Graph", "taxis_span_min", TSpan.ToString("0")); ini.Set("Graph", "length_procs", LengthProcs);
            ini.Set("Log", "csv", CsvLog ? 1 : 0); ini.Set("Log", "csv_dir", CsvDir); ini.Set("Log", "csv_encoding", CsvEnc); ini.Set("Graph", "auto_y", AutoY ? 1 : 0); ini.Set("Graph", "judge_sec", JudgeSec);
            ini.Set("Window", "bgcolorRGB", Hex(BgColor)); ini.Set("Window", "opacity_pct", OpacityPct); ini.Set("Window", "bg_only", BgOnly ? 1 : 0); ini.Set("Window", "lang", Lang); ini.Set("Window", "font", FontName); ini.Set("Window", "font_render", FontRender);
            ini.Set("Window", "standalone", Standalone ? 1 : 0); ini.Set("Window", "pos_x", PosX); ini.Set("Window", "pos_y", PosY);
            ini.Set("Graph", "proc_mode", ProcAuto ? "auto" : "manual"); ini.Set("Graph", "manual_proc", M.Procs[Math.Max(0, Math.Min(M.Procs.Length - 1, ManualProc))]);
            ini.Set("Comment", "colorRGB", Hex(CmtColor)); ini.Set("Comment", "size_pt", CmtSize); ini.Set("Comment", "scroll_left", CmtLeft ? 1 : 0); ini.Set("Comment", "speed", CmtSpeed);
            if (!ini.Has("Host")) { ini.Set("Host", "remote_port", 10030); ini.Set("Host", "socket_port", 10031); ini.Set("Host", "bind_ip", ""); ini.Set("Host", "application_ini", ""); ini.Set("Host", "stale_sec", 5); ini.Set("Host", "heartbeat_timeout_sec", 60); ini.Set("Host", "io_offset_words", 200); ini.Set("Host", "sum_items", "17+101,22+105,23+107,27+111,29+113,19+103,35+123,39+125,42+130,46+132,71+109"); ini.Set("Host", "common_dll", "VB_FERROTEC_Common.dll"); ini.Set("Host", "autostatus_title", "frmAutoStatus"); ini.Set("Host", "autostatus_size", "1126,670"); ini.Set("Host", "process_name", "VB_FERROTEC"); }
            if (!ini.Has("Ment")) { ini.Set("Ment", "stx", 2); ini.Set("Ment", "etx", 3); ini.Set("Ment", "from_id", "02"); ini.Set("Ment", "to_id", "01"); ini.Set("Ment", "worksection_id", "01"); }
            ini.Save(IniFile, new string[] {
                "; PullerView.ini — PullerView の設定（設定画面の OK で自動保存。手動編集も可。EXE と同じフォルダに置く）",
                "; [Graph]  xaxis_max_mm=X 軸（結晶長さ）最大値[mm]  xaxis_span_mm=目盛間隔[mm](0=自動)  auto_y=Y 軸自動  judge_sec=異常確定秒数",
                ";          taxis_max_min=結晶長さの無い工程の時間軸最大[分]（超過でスクロール）  taxis_span_min=時間軸目盛[分](0=自動)  length_procs=結晶長さ軸にする工程（カンマ区切り）",
                "; [Log]    csv=1:生データを CSV に記録  csv_dir=保存フォルダ（相対は EXE 基準）  csv_encoding=utf-8(BOM付き・既定)/utf-16/shift_jis",
                ";          proc_mode=auto:Remoting の工程に追従 / manual:manual_proc の工程を固定表示  manual_proc=手動時の工程名（NECK/BODY など）",
                "; [Window] font=表示フォント名（空=言語ごとに自動: 日本語 Meiryo UI / 中文 Microsoft YaHei UI / English Segoe UI）  font_render=0:なめらか 1:くっきり(ClearType) 2:非アンチエイリアス",
                "; [Window] bgcolorRGB=背景色(RRRGGGBBB)  opacity_pct=不透明度  bg_only=1:背景のみ透過  lang=0:日/1:英/2:中  standalone=1:VB_FERROTEC 無しでも表示  pos_x/pos_y=frmAutoStatus 左上からの位置",
                "; [Comment] colorRGB=文字色  size_pt=文字サイズ  scroll_left=1:左スクロール  speed=速度",
                "; [Host]   remote_port/socket_port=VB_FERROTEC.exe が接続してくるポート（Application.ini [Server] と一致させる）  bind_ip=待受IP(空=全て)",
                ";          io_offset_words=intIOBit 内の値の開始ワード（VB_FERROTEC は 200）  sum_items=2 項目合計で表示する項目（VB_FERROTEC frmMain と同じ）",
                ";          stale_sec=データ更新がこの秒数無ければ通信断扱い（既定5）  heartbeat_timeout_sec=TextID=00 再送もデータ更新も無ければソケットを閉じる秒数（既定60、0=無効）",
                ";          application_ini=指定すると VB_FERROTEC の Application.ini からポートを読む  common_dll=VB_FERROTEC_Common.dll のパス",
                ";          autostatus_title/autostatus_size/autostatus_alt_width/process_name=frmAutoStatus 検出条件  autostatus_marker=frmAutoStatus 内の文字列（| 区切り。既定 AUTO STATUS）",
                "; [JudgeCond] 上下限判定を行う制御モード条件の一覧（設定画面の選択肢）。condN=式;日本語名;English;中文  式: IO〈word〉.〈bit〉=intIOBit(ZR110000起点) MAN〈word〉.〈bit〉=intManual(R104000起点) !=否定 |=OR &=AND",
                "; [Ment]   ソケット電文の STX/ETX/FROM_ID/TO_ID/WORKSECTION_ID（VB_FERROTEC の Ment.ini [Soket Package Element] と一致させる）" });
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
            if (d.TryGetValue("taxis_max_min", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) && dv >= 1) s.TMax = dv;
            if (d.TryGetValue("taxis_span_min", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) && dv >= 0) s.TSpan = dv;
            if (d.TryGetValue("length_procs", out v) && v.Trim() != "") s.LengthProcs = v.Trim();
            if (d.TryGetValue("csv", out v)) s.CsvLog = v == "1";
            if (d.TryGetValue("csv_dir", out v) && v.Trim() != "") s.CsvDir = v.Trim();
            if (d.TryGetValue("csv_encoding", out v) && v.Trim() != "") s.CsvEnc = v.Trim();
            if (d.TryGetValue("auto_y", out v)) s.AutoY = v == "1";
            if (d.TryGetValue("judge_sec", out v) && int.TryParse(v, out iv) && iv >= 1 && iv <= 10) s.JudgeSec = iv;
            if (d.TryGetValue("bgcolorrgb", out v)) { Color c = M.Hex(v, s.BgColor); s.BgColor = c; }
            if (d.TryGetValue("opacity_pct", out v) && int.TryParse(v, out iv) && iv >= 0 && iv <= 100) s.OpacityPct = iv;
            if (d.TryGetValue("bg_only", out v)) s.BgOnly = v != "0";
            // logo キーは旧版互換のため読み飛ばす（案1 固定）
            if (d.TryGetValue("lang", out v) && int.TryParse(v, out iv) && iv >= 0 && iv <= 2) s.Lang = iv;
            if (d.TryGetValue("font", out v)) s.FontName = v.Trim();
            if (d.TryGetValue("font_render", out v) && int.TryParse(v, out iv) && iv >= 0 && iv <= 2) s.FontRender = iv;
            if (d.TryGetValue("standalone", out v)) s.Standalone = v != "0";
            if (d.TryGetValue("pos_x", out v) && int.TryParse(v, out iv)) s.PosX = iv;
            if (d.TryGetValue("pos_y", out v) && int.TryParse(v, out iv)) s.PosY = iv;
            if (d.TryGetValue("proc_mode", out v)) s.ProcAuto = v.Trim().ToLower() != "manual";
            if (d.TryGetValue("manual_proc", out v)) { int ix = Array.IndexOf(M.Procs, v.Trim().ToUpper()); if (ix >= 0) s.ManualProc = ix; }
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
    /// <summary>時間軸の目盛間隔 [分]。0 設定なら 5〜8 本になる間隔を候補から選ぶ。</summary>
    public double TSpanFor(double tmax)
    {
        if (TSpan > 0) return TSpan;
        double[] c = { 1, 2, 5, 10, 15, 30, 60, 120, 240, 480 };
        foreach (double s in c) if (tmax / s <= 8) return s;
        return c[c.Length - 1];
    }
    public bool IsLengthProc(string name) { foreach (string p in LengthProcs.Split(new char[] { ',' })) if (p.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return true; return false; }
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
    /// <summary>true=USL/UCL/LCL/LSL を SV からの偏差（符号付き。例 +5/+3/-3/-5）として扱う。false=絶対値。</summary>
    public bool DevMode;
    /// <summary>上下限判定を行う条件（制御モード）。空=常時判定。書式: 〔!〕IO〈word〉.〈bit〉 / 〔!〕MAN〈word〉.〈bit〉 を | で OR、& で AND。
    /// IO = stcRealData.intIOBit（ZR110000 起点。R110140 → IO140）、MAN = stcRealData.intManual（R104000 起点。R104019 → MAN19）。</summary>
    public string JudgeCond = "";
    /// <summary>現在の SV に対する実効限界値。偏差モードで SV が無ければ NaN。</summary>
    public double EUsl(double sv) { return DevMode ? sv + Usl : Usl; }
    public double EUcl(double sv) { return DevMode ? sv + Ucl : Ucl; }
    public double ELcl(double sv) { return DevMode ? sv + Lcl : Lcl; }
    public double ELsl(double sv) { return DevMode ? sv + Lsl : Lsl; }
    public Color Warn = ColorTranslator.FromHtml("#000066"), Alarm = ColorTranslator.FromHtml("#800000");
    public string Caption(int lang)
    {
        if (CapCustom && CapText != "") return CapText;
        string[] t = M.Tag(Pv); return t[lang];
    }
    public string Unit() { if (UnitCustom && UnitText != "") return UnitText; return M.Tag(Pv)[3]; }
    public GraphCfg CloneCfg() { return (GraphCfg)MemberwiseClone(); }
}

public class GraphState
{
    public List<double> X = new List<double>(), Pv = new List<double>(), Sv = new List<double>();
    public TrendBins Bins = new TrendBins();                 // 表示用集約（min/max/last）。生データは X/Pv/Sv に全て保持
    public St Raw = St.Normal, Pending = St.Normal, Confirmed = St.Normal; public int PendCount; public bool Valid = true;
    public const int RawMax = 400000;                           // 生データ保持上限（約 110 h 分）。超えたら古い 10% を捨てる
    public void Clear() { X.Clear(); Pv.Clear(); Sv.Clear(); Bins.Clear(); Raw = Pending = Confirmed = St.Normal; PendCount = 0; Valid = true; }
    /// <summary>生データへ追加し、ビンにも逐次集約する。lengthAxis=true で X が直前より小さくなった（メルトバック）場合、
    /// 生データを全て削除してから追加する（グラフを最初から新しく描き直す。2026-10-24 中村氏指示）。戻り値 = 削除した点数。</summary>
    public int Add(double x, double pv, double sv, bool lengthAxis)
    {
        int removed = 0;
        if (lengthAxis && X.Count > 0 && x < X[X.Count - 1])
        {
            removed = X.Count; X.Clear(); Pv.Clear(); Sv.Clear(); Bins.Clear();   // 全消去。次の描画で再集約
        }
        X.Add(x); Pv.Add(pv); Sv.Add(sv); Bins.Add(x, pv, sv); return removed;
        if (X.Count > RawMax) { int n = RawMax / 10; X.RemoveRange(0, n); Pv.RemoveRange(0, n); Sv.RemoveRange(0, n); Bins.Clear(); }
    }
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
    // ===== フォント =====
    static string fontCache; static int fontCacheLang = -1; static string fontCacheName;
    static bool FontExists(string n) { try { using (System.Drawing.Font f = new System.Drawing.Font(n, 9f)) return f.Name == n; } catch (Exception) { return false; } }
    /// <summary>表示フォント名。設定が空なら言語ごとに「小さい文字でも太くはっきり見える」フォントを選ぶ。</summary>
    public static string FontName()
    {
        if (fontCache != null && fontCacheLang == S.Lang && fontCacheName == S.FontName) return fontCache;
        string[] pref;
        if (S.FontName != "") pref = new[] { S.FontName, "Meiryo UI", "Yu Gothic UI" };
        else if (S.Lang == 2) pref = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Meiryo UI", "Yu Gothic UI" };
        else if (S.Lang == 1) pref = new[] { "Segoe UI", "Meiryo UI", "Yu Gothic UI" };
        else pref = new[] { "Meiryo UI", "Yu Gothic UI", "MS UI Gothic" };
        string r = System.Drawing.FontFamily.GenericSansSerif.Name; foreach (string n in pref) if (FontExists(n)) { r = n; break; }
        fontCache = r; fontCacheLang = S.Lang; fontCacheName = S.FontName; return r;
    }
    public static System.Drawing.Text.TextRenderingHint TextHint(bool layered)
    {
        if (S.FontRender == 2) return System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        if (S.FontRender == 1 && !layered) return System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;   // per-pixel alpha の透過ウィンドウでは ClearType が使えない
        return System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    }
    public static string SkipLabel() { return S.Lang == 1 ? "Not judged (control mode)" : S.Lang == 2 ? "不判定（控制模式）" : "判定対象外（制御モード）"; }
    public static HostSample LastSample;
    public static List<GraphCfg> Cfg = new List<GraphCfg>();
    public static GraphState[] Slots = { new GraphState(), new GraphState(), new GraphState(), new GraphState() };
    public static int[] Inject = new int[4];   // 0=正常 1=管理上限 2=管理下限 3=規格上限 4=規格下限 5=判定不能
    public static int Proc = 8;
    public static double Len;
    public static bool Running = true, AutoStatusShown = true, Comm = true, PvVisible = true, Maximized = true;
    public static bool AutoAdvance, Paused; public static int ProcSec = 40, SpeedMm = 25, Elapsed;
    public static DateTime ProcStart = DateTime.Now;   // 現工程の開始時刻（時間軸の原点）
    public static double SimMinPerTick = 1.0;          // --sim: 1 tick（1 s）で進める時間軸の分数（時間軸のスクロールを短時間で確認するため）
    static double simMin;
    /// <summary>現工程の X 軸が結晶長さか（false=工程開始からの経過時間 [分]）。</summary>
    public static bool LengthAxis { get { return S.IsLengthProc(Procs[Proc]); } }
    /// <summary>現工程の経過時間 [分]。</summary>
    public static double ElapsedMin { get { return Sim ? simMin : Math.Max(0, (LastSampleAt - ProcStart).TotalMinutes); } }
    /// <summary>現工程の X 値（結晶長さ mm または経過分）。</summary>
    public static double CurX { get { return LengthAxis ? Len : ElapsedMin; } }
    public static int T; static Random rnd = new Random(7);
    public static event Action<string> LogEvent;
    public static event Action Changed;
    public static string IniPath = "";

    public static void Log(string s) { PullerView.Log.Write(s); if (LogEvent != null) LogEvent(s); }
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
    // ===== アイコン表示（2026-10-22 改善項目1）=====
    // frmAutoStatus 未検出時に窓を完全に消すと起動中か分からないため、ロゴアイコンだけの小窓にする。
    // アイコンをクリックすると最小化ウィンドウ（タイトル＋コメント行）に復帰する。frmAutoStatus が再検出されたら元の表示に戻す。
    public static bool IconMode;
    static bool maxBeforeIcon = true;   // アイコン化前の最大化／最小化状態（復帰時に元へ戻す）
    /// <summary>ウィンドウ → アイコン表示。復帰時に元の状態（最大化／最小化）へ戻せるよう記憶する。</summary>
    public static void EnterIcon(string why) { maxBeforeIcon = Maximized; IconMode = true; Log(why + " → アイコン表示（復帰時は" + (maxBeforeIcon ? "最大化" : "最小化") + "）"); Fire(); }
    static bool lastAlive = true;
    /// <summary>毎秒の監視から呼ぶ。frmAutoStatus の検出状態が変わったときだけアイコン表示を切り替える。</summary>
    public static void TrackHost()
    {
        bool alive = HostAlive;
        if (alive == lastAlive) return;
        lastAlive = alive;
        // 単体起動 ON でも VB_FERROTEC.exe 実行中に frmAutoStatus が隠れた（画面切替）ときはアイコン表示にする。VB_FERROTEC 未実行時は単体起動設定に従う
        if (!alive && (!S.Standalone || Running)) { maxBeforeIcon = Maximized; IconMode = true; Log(Running ? "frmAutoStatus 非表示（画面切替）→ アイコン表示" : "VB_FERROTEC.exe 未検出 → アイコン表示"); }
        else if (alive && IconMode) { IconMode = false; Log("frmAutoStatus 検出 → ウィンドウ復帰（" + (Maximized ? "最大化" : "最小化") + "）"); }
        Fire();
    }
    /// <summary>アイコンのクリック: 最小化ウィンドウへ復帰（グラフは出さない）。</summary>
    public static void RestoreFromIcon() { IconMode = false; Maximized = maxBeforeIcon; Log("アイコン → ウィンドウへ復帰（" + (Maximized ? "最大化" : "最小化") + "）"); Fire(); }
    // ===== 工程追従モード（2026-10-22 改善項目2）=====
    public static bool ProcAuto { get { return S.ProcAuto; } }
    public static void SetProcMode(bool auto, int manualProc)
    {
        S.ProcAuto = auto; if (manualProc >= 0) S.ManualProc = manualProc; S.Save();
        if (!auto) { SetProc(S.ManualProc, "手動選択"); Log("工程追従: 手動（" + Procs[S.ManualProc] + " 固定）"); }
        else { Log("工程追従: 自動（Remoting の strPROCESS に追従）"); if (!Sim) { int p = ProcOf(HostProcess); if (p != Proc) SetProc(p, "自動復帰 strPROCESS=" + HostProcess); } }
        Fire();
    }
    public static bool DataOk { get { return Comm && (HostAlive || !Sim); } }                       // データが取れている（実データ時は受信中なら有効。frmAutoStatus 検出は表示制御のみ）
    public static bool OverlayShown { get { return PvVisible; } }    // 未検出時も消さない（単体起動 OFF ならアイコン表示に縮む）

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
    public static string AbbrOf(string sec)
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
            string sn = Ini.SecName(l); if (sn != null) { string n = sn.ToUpper(); cur = new Dictionary<string, string>(); secs[n] = cur; order.Add(n); continue; }
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
                c.Usl = Dbl(d, "usl"); c.Ucl = Dbl(d, "ucl"); c.Lcl = Dbl(d, "lcl"); c.Lsl = Dbl(d, "lsl"); c.DevMode = G(d, "limit_mode", "abs").Trim().ToLower() == "dev"; c.JudgeCond = G(d, "judge_cond", "").Trim();
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
        Proc = p; Len = 0; Elapsed = 0; simMin = 0; ProcStart = Sim || LastSampleAt == DateTime.MinValue ? DateTime.Now : LastSampleAt; foreach (GraphState s in Slots) s.Clear(); Fire();
    }
    public static string[] Kinds() { return S.Lang == 1 ? KindEn : S.Lang == 2 ? KindZh : KindJa; }
    public static string KindName(St s) { string[] k = Kinds(); switch (s) { case St.CtrlHi: return k[0]; case St.CtrlLo: return k[1]; case St.SpecHi: return k[2]; case St.SpecLo: return k[3]; } return ""; }

    // ===== 判定条件（制御モード）=====
    public class CondDef { public string Expr = "", Ja = "", En = "", Zh = ""; public string Name(int lang) { return lang == 1 ? En : lang == 2 ? Zh : Ja; } }
    public static List<CondDef> Conds = DefaultConds();
    static List<CondDef> DefaultConds()
    {
        List<CondDef> l = new List<CondDef>();
        string[][] d = {
            new[] { "MAN14.0",  "温度制御中（R104014 BIT00 TEMP CONTROL SELECT PL）",   "Temp control (R104014 BIT00)",          "温度控制中（R104014 BIT00）" },
            new[] { "MAN14.1",  "電力制御中（R104014 BIT01 POWER CONTROL SELECT PL）",  "Power control (R104014 BIT01)",         "功率控制中（R104014 BIT01）" },
            new[] { "MAN14.2",  "液面温度制御中（R104014 BIT02 LS CONTROL SELECT PL）", "Melt-surface temp control (R104014 BIT02)", "液面温度控制中（R104014 BIT02）" },
            new[] { "MAN19.0",  "APC 圧力制御中（R104019 BIT00 APC PRESSURE PL）",      "APC pressure control (R104019 BIT00)",  "APC 压力控制中（R104019 BIT00）" },
            new[] { "MAN19.1",  "APC 角度制御中（R104019 BIT01 APC ANGLE PL）",         "APC angle control (R104019 BIT01)",     "APC 角度控制中（R104019 BIT01）" },
            new[] { "IO140.6",  "温度制御中（R110140 BIT06 TEMP）",     "Temp control (R110140 BIT06)",       "温度控制中（R110140 BIT06）" },
            new[] { "IO140.7",  "電力制御中（R110140 BIT07 POWER）",    "Power control (R110140 BIT07)",      "功率控制中（R110140 BIT07）" },
            new[] { "IO140.11", "液面温度制御中（R110140 BIT11 LSTEMP）","Melt-surface temp control (BIT11)",  "液面温度控制中（R110140 BIT11）" },
            new[] { "IO66.0",   "電力制御中（ZR110066 Bit0）",          "Power control (ZR110066 Bit0)",      "功率控制中（ZR110066 Bit0）" },
            new[] { "IO66.1",   "温度制御中（ZR110066 Bit1）",          "Temp control (ZR110066 Bit1)",       "温度控制中（ZR110066 Bit1）" },
            new[] { "IO66.2",   "液面温度制御中（ZR110066 Bit2）",      "Melt-surface temp control (Bit2)",   "液面温度控制中（ZR110066 Bit2）" } };
        foreach (string[] x in d) { CondDef c = new CondDef(); c.Expr = x[0]; c.Ja = x[1]; c.En = x[2]; c.Zh = x[3]; l.Add(c); }
        return l;
    }
    /// <summary>PullerView.ini [JudgeCond] cond1=IO140.6;日本語名;English;中文 … があれば一覧を差し替える。</summary>
    public static void LoadConds(Ini ini)
    {
        if (!ini.Has("JudgeCond")) { foreach (CondDef c in Conds) ini.Set("JudgeCond", "cond" + (Conds.IndexOf(c) + 1), c.Expr + ";" + c.Ja + ";" + c.En + ";" + c.Zh); return; }
        List<CondDef> l = new List<CondDef>();
        foreach (KeyValuePair<string, string> kv in ini.Section("JudgeCond")) { string[] a = kv.Value.Split(';'); if (a.Length < 2 || a[0].Trim() == "") continue; CondDef c = new CondDef(); c.Expr = a[0].Trim(); c.Ja = a[1].Trim(); c.En = a.Length > 2 ? a[2].Trim() : c.Ja; c.Zh = a.Length > 3 ? a[3].Trim() : c.Ja; l.Add(c); }
        if (l.Count > 0)
        {   // 既存 ini に無い既定項目（MAN14.x 等）は末尾に追加して書き戻す
            int n = l.Count; foreach (CondDef dc in DefaultConds()) { bool has = false; foreach (CondDef c in l) if (string.Compare(c.Expr, dc.Expr, StringComparison.OrdinalIgnoreCase) == 0) { has = true; break; } if (!has) { l.Add(dc); ini.Set("JudgeCond", "cond" + l.Count, dc.Expr + ";" + dc.Ja + ";" + dc.En + ";" + dc.Zh); } }
            Conds = l;
        }
    }
    public static string CondName(string expr)
    {
        if (expr == null || expr.Trim() == "") return S.Lang == 1 ? "Always" : S.Lang == 2 ? "始终判定" : "常時判定";
        foreach (CondDef c in Conds) if (string.Compare(c.Expr, expr.Trim(), StringComparison.OrdinalIgnoreCase) == 0) return c.Name(S.Lang);
        return expr;
    }
    // 制御モードビットの変化をログに残す（実機での切替確認用）: 式ごとの前回値
    static Dictionary<string, string> condLast = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static bool manMissingLogged;
    public static string CondState(string expr, HostSample sm)
    {
        if (expr == null || expr.Trim() == "" || sm == null) return "";
        List<string> l = new List<string>();
        foreach (string orTerm in expr.Split('|')) foreach (string t0 in orTerm.Split('&')) { string t = t0.Trim().TrimStart('!'); if (t == "") continue; bool? b = sm.Bit(t); l.Add(t + "=" + (b == null ? "?" : b.Value ? "ON" : "OFF")); }
        return string.Join(" ", l.ToArray());
    }
    static void TraceCond(string expr, HostSample sm)
    {
        if (expr == null || expr.Trim() == "" || sm == null) return;
        string st = CondState(expr, sm); string prev; if (condLast.TryGetValue(expr, out prev) && prev == st) return;
        condLast[expr] = st;
        string extra = "";
        if (st.IndexOf("=?") >= 0) extra = "  (intIOBit=" + (sm.IoBit == null ? "null" : sm.IoBit.Length.ToString()) + " intManual=" + (sm.Manual == null ? "null" : sm.Manual.Length.ToString()) + ")";
        Log("[制御モード] " + st + (prev == null ? "（初回）" : "（前回 " + prev + "）") + extra);
        if (sm.Manual == null && expr.IndexOf("MAN", StringComparison.OrdinalIgnoreCase) >= 0 && !manMissingLogged) { manMissingLogged = true; Log("[制御モード] stcRealData.intManual が受信できていません（R104000〜 の条件は判定対象外のままになります）"); }
    }
    /// <summary>条件式を評価。true=判定する。式が空なら常に true。ビットが読めない（配列が無い／短い）ときは判定しない（false）。</summary>
    public static bool EvalCond(string expr, HostSample sm)
    {
        if (expr == null || expr.Trim() == "") return true;
        TraceCond(expr, sm);
        foreach (string orTerm in expr.Split('|'))
        {
            bool all = true, any = false;
            foreach (string t0 in orTerm.Split('&'))
            {
                string t = t0.Trim(); if (t == "") continue; any = true;
                bool neg = t.StartsWith("!"); if (neg) t = t.Substring(1).Trim();
                bool? b = sm == null ? null : sm.Bit(t);
                if (b == null) { all = false; break; }
                if (b.Value == neg) { all = false; break; }
            }
            if (any && all) return true;
        }
        return false;
    }
    static St Classify(double pv, GraphCfg c) { return Classify(pv, double.NaN, c); }
    static St Classify(double pv, double sv, GraphCfg c)
    {
        if (c.DevMode && double.IsNaN(sv)) return St.Invalid;   // 偏差モードは SV が無いと判定できない
        if (pv > c.EUsl(sv)) return St.SpecHi; if (pv < c.ELsl(sv)) return St.SpecLo;
        if (pv > c.EUcl(sv)) return St.CtrlHi; if (pv < c.ELcl(sv)) return St.CtrlLo; return St.Normal;
    }

    // ===== 実データ（HostLink）駆動 =====
    public static bool Sim;                       // true=--sim（模擬データ）。false=VB_FERROTEC.exe からの実データ
    public static DateTime LastSampleAt = DateTime.MinValue;
    public static string HostProcess = "";        // 受信した strPROCESS そのもの（表示・ログ用）
    public static bool PlcOk; public static string HostInfo = ""; public static string HostSession = "";
    /// <summary>HostLink の毎秒サンプルを反映する（UI スレッドで呼ぶこと）。</summary>
    public static void Feed(HostSample sm)
    {
        if (sm.Session != HostSession) { if (HostSession != "") { Log("セッション変更: " + HostSession + " → " + sm.Session + "  グラフをクリア"); foreach (GraphState s in Slots) s.Clear(); Len = 0; } HostSession = sm.Session; }
        LastSampleAt = sm.Time; HostProcess = sm.Process; PlcOk = sm.PlcOk;
        Comm = sm.IoBit != null;                   // 一度も intIOBit が届いていなければ通信断表示
        PvVisible = true;
        if (S.ProcAuto) { int p = ProcOf(sm.Process); if (p != Proc) SetProc(p, "strPROCESS=" + (sm.Process == "" ? "(空)" : sm.Process)); }
        else if (Proc != S.ManualProc) SetProc(S.ManualProc, "手動固定");
        Len = sm.IngotLen;                         // 結晶長さ（strINGOT_LENGTH）
        T++;
        double x = CurX; bool lenAxis = LengthAxis;   // X 軸: 結晶長さ [mm] または工程開始からの経過 [分]
        foreach (GraphCfg c in Active())
        {
            GraphState s = Slots[c.N - 1];
            double? pv = sm.Value(c.Pv), sv = sm.Value(c.Sv);
            if (!Comm || pv == null) { s.Valid = false; s.Raw = St.Invalid; Judge(c, s); continue; }
            s.Valid = true;
            int rm = s.Add(x, pv.Value, sv ?? double.NaN, lenAxis);   // 生データ全保持＋ビン逐次集約（同じ長さが続いても捨てない: 時間は帯の幅として潰れる）
            if (rm > 0 && c.N == 1) Log("結晶長さが戻った（メルトバック）: " + x.ToString("0.0") + " mm。生データ " + rm + " 点を全て削除してグラフを描き直し");
            LastSample = sm; s.Raw = EvalCond(c.JudgeCond, sm) ? Classify(pv.Value, sv ?? double.NaN, c) : St.Skip; Judge(c, s);
        }
        if (sm.IoBit != null) DataLog.Write(sm);   // 生データ CSV（炉ログ相当）
        Fire();
    }
    /// <summary>通信監視（UI タイマーから毎秒）。3 秒以上サンプルが無ければ通信断扱い。</summary>
    public static void Watch(bool hostConnected)
    {
        if (Sim) return;
        bool comm = hostConnected && (DateTime.Now - LastSampleAt).TotalSeconds < 10;   // hostConnected は HostLink の接続＋鮮度（stale_sec）判定込み
        if (comm != Comm) { Comm = comm; if (!comm) { foreach (GraphState s in Slots) { s.Valid = false; s.Raw = St.Invalid; } } Fire(); }
    }

    // ===== 模擬データ（--sim）=====
    public static void Tick()
    {
        if (!Running) return;
        T++;
        TrackHost();
        if (!S.ProcAuto && Proc != S.ManualProc) SetProc(S.ManualProc, "手動固定");
        if (AutoAdvance && S.ProcAuto) { Elapsed++; if (Elapsed >= ProcSec) { int next = Proc + 1; if (next >= Procs.Length) next = 0; SetProc(next, "自動進行"); } }
        if (!Paused && DataOk && IsTarget(Proc)) { Len += SpeedMm; simMin += SimMinPerTick; }
        double x = CurX;
        foreach (GraphCfg c in Active())
        {
            GraphState s = Slots[c.N - 1];
            if (!DataOk || Inject[c.N - 1] == 5) { s.Valid = false; s.Raw = St.Invalid; Judge(c, s); continue; }
            s.Valid = true;
            double mid = (c.EUsl(c.DevMode ? 0 : 0) + c.ELsl(0)) / 2, h = (c.EUcl(0) - c.ELcl(0)) / 2;
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
            s.Add(x, pv, sv, LengthAxis);
            s.Raw = Classify(pv, sv, c); Judge(c, s);
        }
        Fire();
    }

    static void Judge(GraphCfg c, GraphState s)
    {
        string nm = c.Section + " " + c.Caption(S.Lang);
        if (s.Raw == St.Normal || s.Raw == St.Invalid || s.Raw == St.Skip)
        {
            s.Pending = s.Raw; s.PendCount = 0;
            if (s.Confirmed != s.Raw)
            {
                Log(nm + ": " + (s.Raw == St.Normal ? "復帰（正常）" : s.Raw == St.Skip ? "判定対象外（制御モード条件 " + c.JudgeCond + " 不成立）" : "判定不能（---）"));
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
}
