// DataLog.cs — 生データ CSV ログ（単結晶炉ログ相当のフォーマット）
// 1 s ごとの受信サンプル（日時・工程・結晶長さ・データリスト（stcMCValue）全項目）を日付ごとの CSV へ追記する。
// ファイル: <EXE>\log\PV_yyyyMMdd.csv（PullerView.ini [Log] で変更可）。既定エンコード UTF-8（BOM 付き）。全言語の OS で読めるよう Unicode とする（2026-10-24 中村氏指示）。utf-16（BOM 付き UTF-16LE）も選択可。
// ※ 列構成（ヘッダー行の形）は実機の炉ログを入手後に合わせ込む前提の暫定版。列順は stcMCValue の index 昇順。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Globalization;

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
                if (f != curFile || w == null) Open(f); else HousekeepIfDue();
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
            Close(); Housekeep();   // 日付が変わって新しい CSV を開くとき: 前日以前の CSV を ZIP 化し、保存期間を過ぎた ZIP を削除（背景スレッド）
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            bool isNew = !File.Exists(f) || new FileInfo(f).Length == 0;
            if (!isNew) { try { using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { fs.Seek(-1, SeekOrigin.End); if (fs.ReadByte() != '\n') { fs.Seek(0, SeekOrigin.End); fs.WriteByte((byte)'\r'); fs.WriteByte((byte)'\n'); M.Log("CSV ログ: 前回の最終行が途中で終わっていたため改行を補った"); } } } catch (Exception) { } }
            idx.Clear(); foreach (int i in McMeta.Items.Keys) idx.Add(i); idx.Sort();
            w = new StreamWriter(f, true, Enc()); curFile = f; lastFlush = DateTime.Now;
            if (isNew)
            {
                // ヘッダー 3 行: index / 項目名 / 単位（炉ログ相当。実機ログ入手後に合わせる）
                StringBuilder h1 = new StringBuilder("Date,Time,Process,IngotLength"), h2 = new StringBuilder("日付,時刻,工程,結晶長さ"), h3 = new StringBuilder(",,,mm");
                foreach (int i in idx) { McItem m = McMeta.Get(i); h1.Append(",[").Append(i).Append(']'); h2.Append(',').Append(Q(m == null ? "" : m.Name(0))); h3.Append(',').Append(Q(m == null ? "" : m.Unit)); }
                w.WriteLine(h1.ToString()); w.WriteLine(h2.ToString()); w.WriteLine(h3.ToString());
            }
            M.Log("CSV ログ: " + f + (isNew ? "（新規）" : "（追記）") + " 項目数=" + idx.Count);
        }
        static string Q(string s) { return s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        // ===== 日次ローテーション: 当日以外の PV_yyyyMMdd.csv → PV_yyyyMMdd.zip（Deflate）→ CSV 削除。csv_keep_days（既定 90 日）より古い ZIP/CSV は削除 =====
        static readonly object hk = new object(); static bool hkRunning; static DateTime lastHk = DateTime.MinValue;
        /// <summary>日次整理を背景スレッドで開始（多重起動しない）。起動時・日付変更時・1 時間ごとに呼ばれる。</summary>
        public static void Housekeep()
        {
            if (!M.S.CsvLog) return;
            lock (hk) { if (hkRunning) return; hkRunning = true; lastHk = DateTime.Now; }
            Thread th = new Thread(delegate ()
            {
                try { HousekeepNow(); }
                catch (Exception ex) { M.Log("CSV ログ整理エラー: " + ex.Message); }
                finally { lock (hk) hkRunning = false; }
            });
            th.IsBackground = true; th.Priority = ThreadPriority.BelowNormal; th.Name = "CsvHousekeep"; th.Start();
        }
        /// <summary>Write() から 1 時間ごとに呼ぶ（日付をまたいでも書込みが無い場合の保険）。</summary>
        public static void HousekeepIfDue() { if ((DateTime.Now - lastHk).TotalMinutes >= 60) Housekeep(); }
        static void HousekeepNow()
        {
            string dir = Folder; if (!Directory.Exists(dir)) return;
            string today = DateTime.Now.ToString("yyyyMMdd"); int keep = Math.Max(1, M.S.CsvKeepDays); DateTime limit = DateTime.Today.AddDays(-keep);
            // 1) 前日以前の CSV を ZIP 化
            if (M.S.CsvZip)
                foreach (string f in Directory.GetFiles(dir, "PV_*.csv"))
                {
                    string stem = Path.GetFileNameWithoutExtension(f); string ymd = stem.Length >= 11 ? stem.Substring(3, 8) : "";
                    if (ymd.Length != 8 || ymd == today || string.Equals(f, curFile, StringComparison.OrdinalIgnoreCase)) continue;   // 当日・書込み中は対象外
                    string zip = Path.Combine(dir, stem + ".zip"), tmp = zip + ".tmp";
                    try
                    {
                        using (ZipWriter zw = new ZipWriter(tmp)) zw.AddFile(f, stem + ".csv");
                        if (File.Exists(zip)) File.Delete(zip); File.Move(tmp, zip);
                        long before = new FileInfo(f).Length, after = new FileInfo(zip).Length; File.Delete(f);
                        M.Log("CSV ログ圧縮: " + stem + ".csv " + (before / 1024) + "KB → " + stem + ".zip " + (after / 1024) + "KB");
                    }
                    catch (Exception ex) { M.Log("CSV ログ圧縮失敗: " + stem + " " + ex.Message); try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
                }
            try { EventLog.Cleanup(dir, limit, keep); } catch (Exception) { }
            // 2) 保存期間を過ぎた ZIP / CSV を削除（ファイル名の日付で判定）
            foreach (string pat in new string[] { "PV_*.zip", "PV_*.csv", "PV_*.jpg" })
                foreach (string f in Directory.GetFiles(dir, pat))
                {
                    string stem = Path.GetFileNameWithoutExtension(f); DateTime d;
                    if (stem.Length < 11 || !DateTime.TryParseExact(stem.Substring(3, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                    if (d < limit && !string.Equals(f, curFile, StringComparison.OrdinalIgnoreCase))
                    { try { File.Delete(f); M.Log("CSV ログ削除（" + keep + " 日超過）: " + Path.GetFileName(f)); } catch (Exception ex) { M.Log("CSV ログ削除失敗: " + Path.GetFileName(f) + " " + ex.Message); } }
                }
        }
        public static void Close() { try { if (w != null) { w.Flush(); w.Close(); } } catch (Exception) { } w = null; curFile = ""; }
    }
}
