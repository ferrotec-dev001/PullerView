// DataLog.cs — 生データ CSV ログ（単結晶炉ログ相当のフォーマット）
// 1 s ごとの受信サンプル（日時・工程・結晶長さ・stcMCValue 全項目）を日付ごとの CSV へ追記する。
// ファイル: <EXE>\log\PV_yyyyMMdd.csv（PullerView.ini [Log] で変更可）。既定エンコード UTF-8（BOM 付き）。全言語の OS で読めるよう Unicode とする（2026-10-24 中村氏指示）。utf-16（BOM 付き UTF-16LE）も選択可。
// ※ 列構成（ヘッダー行の形）は実機の炉ログを入手後に合わせ込む前提の暫定版。列順は stcMCValue の index 昇順。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PullerView
{
    public static class DataLog
    {
        static StreamWriter w; static string curFile = ""; static List<int> idx = new List<int>(); static DateTime lastFlush = DateTime.MinValue; static int errCount;

        static Encoding Enc()
        {
            string e = M.S.CsvEnc.ToLower().Replace("_", "-"); try { if (e == "utf-8" || e == "utf8" || e == "unicode") return new UTF8Encoding(true); if (e == "utf-16" || e == "utf16") return new UnicodeEncoding(false, true); return Encoding.GetEncoding(M.S.CsvEnc); }
            catch (Exception) { return new UTF8Encoding(true); }
        }
        public static string Folder { get { string Dir = M.S.CsvDir; return Path.IsPathRooted(Dir) ? Dir : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Dir); } }

        /// <summary>毎秒サンプルを 1 行追記する。日付が変わればファイルを切り替える。</summary>
        public static void Write(HostSample sm)
        {
            if (!M.S.CsvLog || sm == null) { if (w != null) Close(); return; }
            try
            {
                string f = Path.Combine(Folder, "PV_" + sm.Time.ToString("yyyyMMdd") + ".csv");
                if (f != curFile || w == null) Open(f);
                StringBuilder sb = new StringBuilder(2048);
                sb.Append(sm.Time.ToString("yyyy/MM/dd")).Append(',').Append(sm.Time.ToString("HH:mm:ss")).Append(',');
                sb.Append(sm.Process).Append(',').Append(sm.IngotLen.ToString("0.0", CultureInfo.InvariantCulture));
                foreach (int i in idx)
                {
                    sb.Append(',');
                    double? v = sm.Value(i); if (v != null) sb.Append(Fmt(v.Value, i));
                }
                w.WriteLine(sb.ToString()); w.Flush();   // 1 行/秒なので毎行フラッシュ（異常終了時に行が途中で切れて次回起動の行と連結するのを防ぐ）
            }
            catch (Exception ex) { if (errCount++ < 3) M.Log("CSV ログ書込失敗: " + ex.Message); }
        }
        static string Fmt(double v, int i)
        {
            McItem m = McMeta.Get(i); int dec = 0; long d = m == null ? 1 : m.Div; while (d > 1 && dec < 6) { d /= 10; dec++; }
            return v.ToString(dec == 0 ? "0" : "0." + new string('0', dec), CultureInfo.InvariantCulture);
        }
        static void Open(string f)
        {
            Close();
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            bool isNew = !File.Exists(f) || new FileInfo(f).Length == 0;
            if (!isNew) { try { using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { fs.Seek(-1, SeekOrigin.End); if (fs.ReadByte() != '\n') { fs.Seek(0, SeekOrigin.End); fs.WriteByte((byte)'\r'); fs.WriteByte((byte)'\n'); M.Log("CSV ログ: 前回の最終行が途中で終わっていたため改行を補った"); } } } catch (Exception) { } }
            idx.Clear(); foreach (int i in McMeta.Items.Keys) idx.Add(i); idx.Sort();
            w = new StreamWriter(f, true, Enc()); curFile = f; lastFlush = DateTime.Now;
            if (isNew)
            {
                // ヘッダー 3 行: index / 項目名 / 単位（炉ログ相当。実機ログ入手後に合わせる）
                StringBuilder h1 = new StringBuilder("Date,Time,Process,IngotLength"), h2 = new StringBuilder("日付,時刻,工程,結晶長さ"), h3 = new StringBuilder(",,,mm");
                foreach (int i in idx) { McItem m = McMeta.Get(i); h1.Append(",[").Append(i).Append(']'); h2.Append(',').Append(Q(m == null ? "" : (m.CaptionJ != "" ? m.CaptionJ : m.Caption))); h3.Append(',').Append(Q(m == null ? "" : m.Unit)); }
                w.WriteLine(h1.ToString()); w.WriteLine(h2.ToString()); w.WriteLine(h3.ToString());
            }
            M.Log("CSV ログ: " + f + (isNew ? "（新規）" : "（追記）") + " 項目数=" + idx.Count);
        }
        static string Q(string s) { return s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        public static void Close() { try { if (w != null) { w.Flush(); w.Close(); } } catch (Exception) { } w = null; curFile = ""; }
    }
}
