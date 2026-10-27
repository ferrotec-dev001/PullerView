// Indication.cs — VB_FERROTEC の indication.csv（英語, 日本語, 中国語 の 3 列）による項目名の相互言語変換
// VB_FERROTEC は PCINFData.csv の CAPTION を mdlChangeLanguage.fnc_Change_Captions で indication.csv の 3 列のいずれかと
// 大文字小文字無視・前後空白無視で照合し、表示言語の列へ置き換える。PullerView も同じ規則で照合し、
// McMeta.ini の caption（受信した表示中言語の名称）から caption_e / caption_j / caption_c を補完する。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PullerView
{
    public static class Indication
    {
        public static string FileName = "indication.csv";
        public static string ConfiguredPath = "";            // PullerView.ini [Host] indication_csv（空なら自動探索）
        public static string LoadedPath = "";
        public static int RowCount { get { return rows.Count; } }
        static readonly List<string[]> rows = new List<string[]>();
        static readonly Dictionary<string, string[]> map = new Dictionary<string, string[]>();   // 正規化キー → [e, j, c]
        static string hostDirTried = "";

        static string Key(string s) { return s == null ? "" : s.Trim().ToUpperInvariant(); }   // VB: UCase(Trim(x))

        /// <summary>候補パスを順に探して読み込む: 設定パス → EXE フォルダ → VB_FERROTEC.exe のフォルダ\Caption\。読めたら true。</summary>
        public static bool Load(string hostExeDir)
        {
            List<string> cands = new List<string>();
            if (ConfiguredPath != "") cands.Add(ConfiguredPath);
            cands.Add(Ini.InExeDir(FileName));
            cands.Add(Ini.InExeDir(Path.Combine("Caption", FileName)));
            if (!string.IsNullOrEmpty(hostExeDir)) cands.Add(Path.Combine(Path.Combine(hostExeDir, "Caption"), FileName));
            foreach (string p in cands)
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    if (string.Equals(p, LoadedPath, StringComparison.OrdinalIgnoreCase) && rows.Count > 0) return true;
                    Parse(ReadText(p)); LoadedPath = p;
                    Log.Write("indication.csv 読込: " + rows.Count + " 行（" + p + "）");
                    return rows.Count > 0;
                }
                catch (Exception ex) { Log.Write("indication.csv 読込失敗 " + p + ": " + ex.Message); }
            }
            return rows.Count > 0;
        }

        /// <summary>接続中の VB_FERROTEC.exe のフォルダから indication.csv を探す（未読込時のみ。プロセスごとに 1 回）。</summary>
        public static void TryLoadFromHost(int[] pids)
        {
            if (rows.Count > 0 || pids == null || pids.Length == 0) return;
            try
            {
                using (Process p = Process.GetProcessById(pids[0]))
                {
                    string dir = Path.GetDirectoryName(p.MainModule.FileName);
                    if (dir == hostDirTried) return; hostDirTried = dir; Load(dir);
                }
            }
            catch (Exception) { hostDirTried = "?"; }
        }

        /// <summary>BOM で UTF-16 LE/BE・UTF-8 を判定。BOM 無しは UTF-8 として読めれば UTF-8、だめなら OS 既定（Shift_JIS/GBK）。</summary>
        static string ReadText(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            try { return new UTF8Encoding(false, true).GetString(b); } catch (Exception) { return Encoding.Default.GetString(b); }
        }

        static void Parse(string text)
        {
            rows.Clear(); map.Clear();
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (line.Trim() == "" || line.StartsWith("<<")) continue;
                string[] f = SplitCsv(line); if (f.Length < 2) continue;
                string[] r = new string[] { f[0].Trim(), f.Length > 1 ? f[1].Trim() : "", f.Length > 2 ? f[2].Trim() : "" };
                rows.Add(r);
                foreach (string k in r) { string key = Key(k); if (key != "" && !map.ContainsKey(key)) map[key] = r; }   // 先勝ち（VB も先頭一致で終了）
            }
        }

        static string[] SplitCsv(string line)
        {
            List<string> o = new List<string>(); StringBuilder sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true; else if (c == ',') { o.Add(sb.ToString()); sb.Length = 0; } else sb.Append(c);
            }
            o.Add(sb.ToString()); return o.ToArray();
        }

        /// <summary>名称を指定言語（0=日本語 1=英語 2=中国語）へ変換。見つからない・該当列が空なら null。</summary>
        public static string Translate(string caption, int lang)
        {
            string[] r; if (!map.TryGetValue(Key(caption), out r)) return null;
            string s = lang == 0 ? r[1] : lang == 2 ? r[2] : r[0];
            return s == "" ? null : s;
        }
    }
}
