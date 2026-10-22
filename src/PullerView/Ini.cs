// Ini.cs — .ini 読み書きユーティリティ（設定値はすべて EXE と同じフォルダの .ini に集約する。要件 2026-10-21 追加 6）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PullerView
{
    /// <summary>単純な INI（[section] / key=value / ';' '#' コメント）。キーは大文字小文字を区別しない。</summary>
    public class Ini
    {
        readonly Dictionary<string, Dictionary<string, string>> secs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> order = new List<string>();
        public string Path = "";

        public static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string InExeDir(string name) { return System.IO.Path.Combine(ExeDir, name); }

        public static Ini Load(string path)
        {
            Ini ini = new Ini(); ini.Path = path;
            if (!File.Exists(path)) return ini;
            Dictionary<string, string> cur = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string l = raw.Trim();
                if (l == "" || l.StartsWith(";") || l.StartsWith("#")) continue;
                if (l.StartsWith("[") && l.EndsWith("]")) { cur = ini.Section(l.Substring(1, l.Length - 2).Trim()); continue; }
                int e = l.IndexOf('='); if (e <= 0 || cur == null) continue;
                cur[l.Substring(0, e).Trim()] = l.Substring(e + 1).Trim();
            }
            return ini;
        }

        public Dictionary<string, string> Section(string name)
        {
            Dictionary<string, string> d;
            if (!secs.TryGetValue(name, out d)) { d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); secs[name] = d; order.Add(name); }
            return d;
        }
        public bool Has(string sec) { return secs.ContainsKey(sec); }
        public IEnumerable<string> Sections { get { return order; } }

        public string Get(string sec, string key, string def)
        {
            Dictionary<string, string> d; string v;
            return secs.TryGetValue(sec, out d) && d.TryGetValue(key, out v) ? v : def;
        }
        public int GetInt(string sec, string key, int def) { int v; return int.TryParse(Get(sec, key, ""), out v) ? v : def; }
        public double GetDbl(string sec, string key, double def) { double v; return double.TryParse(Get(sec, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        public bool GetBool(string sec, string key, bool def) { string v = Get(sec, key, ""); return v == "" ? def : v != "0" && !v.Equals("false", StringComparison.OrdinalIgnoreCase); }
        public void Set(string sec, string key, object val) { Section(sec)[key] = Convert.ToString(val, CultureInfo.InvariantCulture); }

        /// <summary>UTF-8(BOM) で保存。先頭コメントは呼び出し側が渡す。</summary>
        public void Save(string path, string[] header)
        {
            StringBuilder sb = new StringBuilder();
            if (header != null) foreach (string h in header) sb.AppendLine(h);
            foreach (string s in order)
            {
                sb.AppendLine("[" + s + "]");
                foreach (KeyValuePair<string, string> kv in secs[s]) sb.AppendLine(kv.Key + "=" + kv.Value);
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
