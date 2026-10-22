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
        public int Index; public long Div = 1; public string CaptionJ = "", Caption = "", Unit = "", Format = ""; public int DispNo;
    }

    public static class McMeta
    {
        public static string FileName = "McMeta.ini";
        public static Dictionary<int, McItem> Items = new Dictionary<int, McItem>();
        public static string Source = "none";          // none / file / remoting
        public static DateTime ReceivedAt = DateTime.MinValue;

        public static McItem Get(int idx) { McItem m; return Items.TryGetValue(idx, out m) ? m : null; }
        public static long DivOf(int idx) { McItem m = Get(idx); return m == null || m.Div <= 0 ? 0 : m.Div; }

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
                    m.CaptionJ = ini.Get(sec, "caption_j", ""); m.Caption = ini.Get(sec, "caption", ""); m.Unit = ini.Get(sec, "unit", "");
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
                m.Div = Reflect.L(e, "lDivVal"); m.CaptionJ = Reflect.S(e, "sCaption_j"); m.Caption = Reflect.S(e, "sCaption");
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
                    ini.Set(s, "div", m.Div); ini.Set(s, "caption_j", m.CaptionJ); ini.Set(s, "caption", m.Caption);
                    ini.Set(s, "unit", m.Unit); ini.Set(s, "format", m.Format); ini.Set(s, "dispno", m.DispNo);
                }
                ini.Save(Ini.InExeDir(FileName), new string[] {
                    "; McMeta.ini — VB_FERROTEC.exe から受信した stcMCValue（項目名・単位・除数）のキャッシュ。自動生成（手動編集不要）",
                    "; stcMCValue は VB_FERROTEC.exe 起動後の初回接続時にしか送られないため、未受信時は本ファイルを代用する。" });
                Log.Write("stcMCValue 受信 " + d.Count + " 項目 → " + FileName + " に保存");
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
