using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PullerView
{
    /// <summary>監視項目のイベント 1 件（v48）。本文は表示言語の口語文（将来の音声出力用）。</summary>
    public class PvEvent
    {
        public DateTime Time; public string Text = ""; public string Proc = ""; public double? Pv, Sv; public double X; public bool LenAxis; public St Kind = St.Normal; public int Slot = -1; public string Section = "";   // Section = DataSetting.ini セクション（例 BODY1）。空＝全体イベント（工程切替など）
        /// <summary>種別色: 規格外=赤、管理外=琥珀、復帰=緑、判定開始/停止・情報=水色。</summary>
        public int Tone;   // 0=info 1=ctrl 2=spec 3=ok
    }

    /// <summary>イベントログ（v48）: 画面のチャット形式表示用メモリ（最大 EventLog.Max 件）と、Log\yyyyMMdd_event.log への追記（200kB で次ファイル）。</summary>
    public static class EventLog
    {
        public const int Max = 1000;
        public static readonly List<PvEvent> Items = new List<PvEvent>();
        public static event Action Added;
        static readonly object lk = new object(); static string curFile = ""; static string curDay = "";

        public static void Add(PvEvent e)
        {
            lock (lk) { Items.Add(e); if (Items.Count > Max) Items.RemoveRange(0, Items.Count - Max); }
            try { WriteFile(e); } catch (Exception ex) { M.Log("イベントログ書込失敗: " + ex.Message); }
            if (Added != null) Added();
        }
        /// <summary>情報イベント（工程切替など）。</summary>
        public static void Info(string text) { PvEvent e = new PvEvent(); e.Time = DateTime.Now; e.Text = text; e.Proc = M.Procs[M.Proc]; e.X = M.CurX; e.LenAxis = M.LengthAxis; e.Tone = 0; Add(e); }

        /// <summary>装置名称の接頭辞（空なら省略）。</summary>
        public static string Dev() { string d = M.S.Device.Trim(); return d == "" ? "" : d + " "; }

        // ---- ファイル: Log\yyyyMMdd_event.log（タブ区切り: 日時 / 本文 / 工程 / PV / SV / X）。event_max_kb を超えたら yyyyMMdd_event_2.log, _3 … ----
        static void WriteFile(PvEvent e)
        {
            string dir = DataLog.Folder; Directory.CreateDirectory(dir);
            string day = e.Time.ToString("yyyyMMdd"); long maxB = Math.Max(10, M.S.EventMaxKb) * 1024L;
            lock (lk)
            {
                if (curDay != day || curFile == "" || (File.Exists(curFile) && new FileInfo(curFile).Length >= maxB))
                {
                    curDay = day; curFile = Path.Combine(dir, day + "_event.log"); int n = 2;
                    while (File.Exists(curFile) && new FileInfo(curFile).Length >= maxB) { curFile = Path.Combine(dir, day + "_event_" + n + ".log"); n++; }
                    if (!File.Exists(curFile)) File.WriteAllText(curFile, "日時\tイベント\t工程\tPV\tSV\tX（結晶長さ mm／経過 分）\r\n", new UTF8Encoding(true));
                }
                string line = e.Time.ToString("yyyy/MM/dd HH:mm:ss") + "\t" + e.Text.Replace("\t", " ") + "\t" + e.Proc + "\t" + (e.Pv == null ? "" : e.Pv.Value.ToString("0.0##", CultureInfo.InvariantCulture))
                    + "\t" + (e.Sv == null || double.IsNaN(e.Sv.Value) ? "" : e.Sv.Value.ToString("0.0##", CultureInfo.InvariantCulture)) + "\t" + e.X.ToString("0.0", CultureInfo.InvariantCulture) + (e.LenAxis ? "mm" : "min") + "\r\n";
                File.AppendAllText(curFile, line, new UTF8Encoding(true));
            }
        }
        /// <summary>csv_keep_days を超えた yyyyMMdd_event*.log を削除（DataLog.Housekeep から呼ぶ）。</summary>
        public static void Cleanup(string dir, DateTime limit, int keep)
        {
            foreach (string f in Directory.GetFiles(dir, "*_event*.log"))
            {
                string stem = Path.GetFileName(f); DateTime d;
                if (stem.Length < 8 || !DateTime.TryParseExact(stem.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                if (d < limit) { try { File.Delete(f); M.Log("イベントログ削除（" + keep + " 日超過）: " + stem); } catch (Exception) { } }
            }
        }

        // ===== 口語メッセージ生成（表示言語）=====
        static string V(double v) { return v.ToString("0.0##", CultureInfo.InvariantCulture); }
        /// <summary>確定状態の変化から口語イベント文を作る。prev→now。</summary>
        public static void OnConfirmed(GraphCfg c, GraphState s, St prev, St now, double? pv, double? sv)
        {
            int L = M.S.Lang; string cap = c.Caption(L), u = c.Unit(), dev = Dev(); double svv = sv ?? double.NaN;
            PvEvent e = new PvEvent(); e.Time = DateTime.Now; e.Proc = M.Procs[M.Proc]; e.Pv = pv; e.Sv = sv; e.X = M.CurX; e.LenAxis = M.LengthAxis; e.Kind = now; e.Slot = c.N - 1; e.Section = c.Section;
            bool prevOut = prev == St.CtrlHi || prev == St.CtrlLo || prev == St.SpecHi || prev == St.SpecLo;
            string cur = pv == null ? "" : (L == 1 ? " Now " + V(pv.Value) + u + "." : L == 2 ? "当前为" + V(pv.Value) + u + "。" : "現在は" + V(pv.Value) + u + "です。");
            switch (now)
            {
                case St.SpecHi: case St.CtrlHi:
                {
                    string lim = now == St.SpecHi ? "USL" : "UCL"; double lv = now == St.SpecHi ? c.EUsl(svv) : c.EUcl(svv); e.Tone = now == St.SpecHi ? 2 : 1;
                    e.Text = L == 1 ? dev + cap + " exceeded " + lim + " " + V(lv) + u + "." + cur : L == 2 ? dev + cap + "超过了" + lim + V(lv) + u + "。" + cur : dev + cap + "が" + lim + V(lv) + u + "を超えました。" + cur; break;
                }
                case St.SpecLo: case St.CtrlLo:
                {
                    string lim = now == St.SpecLo ? "LSL" : "LCL"; double lv = now == St.SpecLo ? c.ELsl(svv) : c.ELcl(svv); e.Tone = now == St.SpecLo ? 2 : 1;
                    e.Text = L == 1 ? dev + cap + " fell below " + lim + " " + V(lv) + u + "." + cur : L == 2 ? dev + cap + "低于" + lim + V(lv) + u + "。" + cur : dev + cap + "が" + lim + V(lv) + u + "を下回りました。" + cur; break;
                }
                case St.Normal:
                    if (prevOut)
                    {   // 復帰: 直前の逸脱側の管理限界（UCL/LCL）以内へ
                        bool hi = prev == St.SpecHi || prev == St.CtrlHi; string lim = hi ? "UCL" : "LCL"; double lv = hi ? c.EUcl(svv) : c.ELcl(svv); e.Tone = 3;
                        e.Text = L == 1 ? dev + cap + " returned " + (hi ? "below " : "above ") + lim + " " + V(lv) + u + "." + cur
                               : L == 2 ? dev + cap + "已恢复到" + lim + V(lv) + u + (hi ? "以下" : "以上") + "。" + cur
                               : dev + cap + "が" + lim + V(lv) + u + (hi ? "以下" : "以上") + "に復帰しました。" + cur;
                    }
                    else if (prev == St.Skip || prev == St.Invalid)
                    {   // 判定開始（条件到達）
                        e.Tone = 0;
                        e.Text = L == 1 ? dev + cap + " reached the judgement condition. Starting judgement." : L == 2 ? dev + cap + "已达到判定条件，开始判定。" : dev + cap + "の判定条件に達しました。判定を開始します。";
                    }
                    else return;
                    break;
                case St.Skip:
                    if (prev == St.Skip) return; e.Tone = 0;
                    e.Text = L == 1 ? dev + cap + " left the judgement condition. Judgement paused." : L == 2 ? dev + cap + "已脱离判定条件，暂停判定。" : dev + cap + "が判定条件から外れました。判定を停止します。"; break;
                case St.Invalid:
                    if (prev == St.Invalid) return; e.Tone = 1;
                    e.Text = L == 1 ? dev + cap + " value cannot be read. Judgement is not possible." : L == 2 ? "无法读取" + dev + cap + "的值，无法判定。" : dev + cap + "の値が読めません。判定できません。"; break;
                default: return;
            }
            // 逸脱中に判定開始した場合（Skip→SpecHi など）は「判定を開始します」を先に出す
            if ((prev == St.Skip || prev == St.Invalid) && (now == St.SpecHi || now == St.CtrlHi || now == St.SpecLo || now == St.CtrlLo))
            {
                PvEvent b = new PvEvent(); b.Time = e.Time; b.Proc = e.Proc; b.Pv = pv; b.Sv = sv; b.X = e.X; b.LenAxis = e.LenAxis; b.Slot = e.Slot; b.Section = e.Section; b.Tone = 0;
                b.Text = L == 1 ? dev + cap + " reached the judgement condition. Starting judgement." : L == 2 ? dev + cap + "已达到判定条件，开始判定。" : dev + cap + "の判定条件に達しました。判定を開始します。";
                Add(b);
            }
            Add(e);
        }
    }
}
