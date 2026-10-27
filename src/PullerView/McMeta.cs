// McMeta.cs — VB_FERROTEC から受信した stcMCValue（項目名・単位・除数）のキャッシュ（McMeta.ini）
// stcMCValue は VB_FERROTEC.exe 起動後の初回接続時にしか送られない（PoC 2026-10-21 確認）ため、
// 受信時に EXE と同じフォルダの McMeta.ini へ保存し、未受信時はそれを代用する。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PullerView
{
    public class McItem
    {
        public int Index; public long Div = 1; public string CaptionJ = "", CaptionE = "", CaptionC = "", Caption = "", Unit = "", Format = ""; public int DispNo;
        /// <summary>言語別の項目名（0=日本語 1=英語 2=中国語）。該当言語の名称が無ければ sCaption（VB_FERROTEC が PCINFData.csv から読んだ表示中言語の名称）。</summary>
        public string Name(int lang)
        {
            string s = lang == 0 ? CaptionJ : lang == 2 ? CaptionC : CaptionE;
            if (s != null && s.Trim() != "") return s.Trim();
            return Caption == null ? "" : Caption.Trim();
        }
    }

    public static class McMeta
    {
        public static string FileName = "McMeta.ini";
        public static Dictionary<int, McItem> Items = new Dictionary<int, McItem>();
        public static string Source = "none";          // none / file / remoting
        public static DateTime ReceivedAt = DateTime.MinValue;

        public static McItem Get(int idx) { McItem m; return Items.TryGetValue(idx, out m) ? m : null; }
        public static long DivOf(int idx) { McItem m = Get(idx); return m == null || m.Div <= 0 ? 0 : m.Div; }
        /// <summary>項目名の共通取得: McMeta.ini（VB_FERROTEC から受信したデータリスト）の名称を優先し、無い場合のみ内蔵の既定名（M.Tag）。</summary>
        public static string NameOf(int idx, int lang)
        {
            McItem m = Get(idx); if (m != null) { string s = m.Name(lang); if (s != "") return s; }
            return M.Tag(idx)[lang];
        }
        /// <summary>単位の共通取得: McMeta.ini の単位を優先し、無い場合のみ内蔵の既定値。</summary>
        public static string UnitOf(int idx)
        {
            McItem m = Get(idx); if (m != null && m.Unit != null && m.Unit.Trim() != "") return m.Unit.Trim();
            return M.Tag(idx)[3];
        }
        /// <summary>一覧表示用の index 上限（McMeta.ini の最大 index と 134 の大きい方）。</summary>
        public static int MaxIndex { get { int mx = 134; foreach (int k in Items.Keys) if (k > mx) mx = k; return mx; } }

        public static void Load()
        {
            try
            {
                Ini ini = Ini.Load(Ini.InExeDir(FileName));
                Items.Clear();
                foreach (string sec in ini.Sections)
                {
                    if (!sec.StartsWith("Item", StringComparison.OrdinalIgnoreCase)) continue;
                    int n; if (!int.TryParse(sec.Substring(4), out n)) continue;
                    McItem m = new McItem(); m.Index = n; m.Div = (long)ini.GetDbl(sec, "div", 1);
                    m.CaptionJ = ini.Get(sec, "caption_j", ""); m.CaptionE = ini.Get(sec, "caption_e", ""); m.CaptionC = ini.Get(sec, "caption_c", ""); m.Caption = ini.Get(sec, "caption", ""); m.Unit = ini.Get(sec, "unit", "");
                    m.Format = ini.Get(sec, "format", ""); m.DispNo = ini.GetInt(sec, "dispno", 0);
                    Items[n] = m;
                }
                if (Items.Count > 0) { Source = "file"; Log.Write("McMeta.ini 読込: " + Items.Count + " 項目（受信日時 " + ini.Get("Meta", "received", "-") + "）"); }
            }
            catch (Exception ex) { Log.Write("McMeta.ini 読込失敗: " + ex.Message); }
        }

        /// <summary>Remoting で受信した stcMCValue 配列（TP_MC_VALUE[]）を反映し、McMeta.ini に保存する。</summary>
        public static void Update(Array mc)
        {
            if (mc == null || mc.Length == 0) return;
            Dictionary<int, McItem> d = new Dictionary<int, McItem>();
            for (int n = 0; n < mc.Length; n++)
            {
                object e = mc.GetValue(n); McItem m = new McItem(); m.Index = n;
                m.Div = Reflect.L(e, "lDivVal"); m.Caption = Reflect.S(e, "sCaption"); m.CaptionE = Reflect.S(e, "sCaption_e"); m.CaptionJ = Reflect.S(e, "sCaption_j"); m.CaptionC = Reflect.S(e, "sCaption_c");
                m.Unit = Reflect.S(e, "sUnit"); m.Format = Reflect.S(e, "sFormat"); m.DispNo = (int)Reflect.L(e, "iDispNo");
                d[n] = m;
            }
            Items = d; Source = "remoting"; ReceivedAt = DateTime.Now;
            try
            {
                Ini ini = new Ini();
                ini.Set("Meta", "received", ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                ini.Set("Meta", "count", d.Count);
                foreach (KeyValuePair<int, McItem> kv in d)
                {
                    string s = "Item" + kv.Key; McItem m = kv.Value;
                    ini.Set(s, "div", m.Div); ini.Set(s, "caption", m.Caption); ini.Set(s, "caption_e", m.CaptionE); ini.Set(s, "caption_j", m.CaptionJ); ini.Set(s, "caption_c", m.CaptionC);
                    ini.Set(s, "unit", m.Unit); ini.Set(s, "format", m.Format); ini.Set(s, "dispno", m.DispNo);
                }
                ini.Save(Ini.InExeDir(FileName), new string[] {
                    "; McMeta.ini — VB_FERROTEC.exe から受信したデータリスト（項目名・単位・除数・表示番号）のキャッシュ。自動生成（手動編集不要）",
                    "; データリストは VB_FERROTEC.exe 起動後の初回接続時にしか送られないため、未受信時は本ファイルを代用する。",
                    "; caption   = VB_FERROTEC が表示中言語で読み込んだ項目名（PCINFData.csv）。PullerView の項目名表示はこの値を基本とする",
                    "; caption_e / caption_j / caption_c = 英語／日本語／中国語の項目名（VB_FERROTEC 側で設定されている場合のみ。空なら caption を使用）" });
                Log.Write("データリスト（stcMCValue）受信 " + d.Count + " 項目 → " + FileName + " に保存");
            }
            catch (Exception ex) { Log.Write("McMeta.ini 保存失敗: " + ex.Message); }
        }
    }

    /// <summary>VB_FERROTEC_Common の構造体をリフレクションで読む（DLL のバージョン差に強くするため型を直接参照しない）。</summary>
    public static class Reflect
    {
        public static object P(object o, string name)
        {
            if (o == null) return null;
            System.Reflection.PropertyInfo p = o.GetType().GetProperty(name); if (p != null) return p.GetValue(o, null);
            System.Reflection.FieldInfo f = o.GetType().GetField(name); return f == null ? null : f.GetValue(o);
        }
        public static void Set(object o, string name, object v) { o.GetType().GetProperty(name).SetValue(o, v, null); }
        public static string S(object o, string name) { object v = P(o, name); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static long L(object o, string name) { object v = P(o, name); long r; return v != null && long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out r) ? r : 0; }
        public static bool B(object o, string name) { object v = P(o, name); return v is bool && (bool)v; }
    }
}
