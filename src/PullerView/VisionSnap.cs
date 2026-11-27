// VisionSnap.cs — 画像の保存（OK_SNAP／NG_SNAP）（V2-v9。V2-v8 の「教示画像の保存」を置き換え）
// 通常の受信画像（[Image]。最新 max_files 枚で自動削除）とは別に、次の画像を残す。
//   NG_SNAP: (1) 自動 — 現在の判定基準で「未検出」と数えた画像  (2) 手動 — 解析画像の下の「NGへ保存」ボタン
//   OK_SNAP: 手動 — 「OKへ保存」ボタン
// 各フォルダに BMP（受信したままのデータ）と記録 snap.csv（保存日時・自動／手動・工程・そのときの判定・閾値・検出範囲など）を置く。
// ここでは保存だけを行う。判定基準（閾値など）は変えない。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PullerView
{
    public static class SnapCfg
    {
        public static bool Enable = true;
        public static bool AutoNg = true;          // 現在の判定基準で未検出と数えた画像を、自動で NG_SNAP へ保存する
        public static string NgFolder = "NG_SNAP", OkFolder = "OK_SNAP";   // 相対は EXE 基準
        public static int MaxFiles = 1000;         // フォルダごとの上限枚数（0=無制限）
        public static int OnFull = 0;              // 上限に達したとき: 0=stop（保存しない。古い画像を残す）／1=delete_oldest（古い画像から削除して保存）
        public static readonly string[] OnFullN = { "stop", "delete_oldest" };
    }

    public static class VisionSnap
    {
        public const string CsvName = "snap.csv";
        const string Header = "save_time,trigger,label,file,image_time,source_name,process,judge,frame_status,detected,confidence_pct,hit,count,contour,judge_mode,seam_threshold,seam_threshold_strong,detect_range,seam_deg,seam_d,comment";
        static readonly object lk = new object();
        static readonly int[] count = { -1, -1 };   // [0]=OK_SNAP [1]=NG_SNAP の枚数（-1=未取得）
        static bool autoFullLogged;
        public static string Dir(bool ng) { string f = ng ? SnapCfg.NgFolder : SnapCfg.OkFolder; return Path.GetFullPath(Path.IsPathRooted(f) ? f : Path.Combine(Ini.ExeDir, f)); }
        static string N(double v) { return double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string Q(string s) { s = s ?? ""; return s.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        /// <summary>フォルダの BMP の枚数（画面表示用。保存のたびに数え直す）。</summary>
        public static int Count(bool ng) { int i = ng ? 1 : 0; if (count[i] < 0) { try { string d = Dir(ng); count[i] = Directory.Exists(d) ? Directory.GetFiles(d, "*.bmp").Length : 0; } catch (Exception) { count[i] = 0; } } return count[i]; }
        public static void Recount() { count[0] = count[1] = -1; autoFullLogged = false; }
        static bool SamePath(string a, string b) { return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }

        /// <summary>自動保存（解析スレッドから、画像 1 枚の判定のあとに呼ぶ）。未検出と数えた画像だけを NG_SNAP へ保存する。
        /// 再生フォルダが OK_SNAP／NG_SNAP のとき（保存した画像を再生しているとき）は保存しない — 再生中のフォルダへ同じ画像を書き足し続けるのを防ぐ。</summary>
        public static void Auto(VFrame f)
        {
            if (f == null || !f.AutoNg || !SnapCfg.Enable || !SnapCfg.AutoNg) return;
            if (VisionCfg.Source != 0) { try { string pf = Path.GetFullPath(Path.IsPathRooted(CameraCfg.PlayFolder) ? CameraCfg.PlayFolder : Path.Combine(Ini.ExeDir, CameraCfg.PlayFolder)); if (SamePath(pf, Dir(true)) || SamePath(pf, Dir(false))) return; } catch (Exception) { return; } }
            string judge; double conf; int hit, cnt; lock (VV.Lk) { judge = VV.Judge; conf = VV.Conf; hit = VV.Hit; cnt = VV.Cnt; }
            string msg; Save(f, true, true, judge, conf, hit, cnt, out msg);
        }

        /// <summary>画像を OK_SNAP（ng=false）または NG_SNAP（ng=true）へ保存する。auto=自動保存。戻り値 true＝保存した。msg は画面・ログ用の結果。</summary>
        public static bool Save(VFrame f, bool ng, bool auto, string judge, double conf, int hit, int cnt, out string msg)
        {
            msg = ""; string label = ng ? "NG" : "OK", fo = ng ? "NG_SNAP" : "OK_SNAP";
            if (!SnapCfg.Enable) { msg = Lx.T("画像の保存は無効です（VisionView.ini [Snap] enable=0）", "Image saving is disabled (VisionView.ini [Snap] enable=0)", "图像保存已禁用（VisionView.ini [Snap] enable=0）"); return false; }
            if (f == null || f.Raw == null || f.Raw.Length == 0) { msg = Lx.T("保存できる画像がありません", "No image to save", "没有可保存的图像"); return false; }
            lock (lk)
            {
                if (ng ? f.SavedNg : f.SavedOk) { msg = Lx.T("この画像は " + fo + " へ保存済みです", "This image has already been saved to " + fo, "此图像已保存到 " + fo); return false; }
                try
                {
                    string dir = Dir(ng); Directory.CreateDirectory(dir);
                    string[] have = Directory.GetFiles(dir, "*.bmp"); string removed = ""; int nRemoved = 0;
                    if (SnapCfg.MaxFiles > 0 && have.Length >= SnapCfg.MaxFiles)
                    {
                        if (SnapCfg.OnFull != 1)
                        {
                            count[ng ? 1 : 0] = have.Length;
                            msg = Lx.T(fo + " が上限 " + SnapCfg.MaxFiles + " 枚に達しているため保存しませんでした。不要な画像を " + dir + " から移動・削除するか、VisionView.ini [Snap] max_files を増やしてください。",
                                "Not saved: " + fo + " has reached its limit (" + SnapCfg.MaxFiles + "). Move or delete images in " + dir + ", or increase max_files in VisionView.ini [Snap].",
                                fo + " 已达上限 " + SnapCfg.MaxFiles + " 张，未保存。请移走或删除 " + dir + " 中的图像，或增大 VisionView.ini [Snap] max_files。");
                            if (!auto || !autoFullLogged) M.Log("VisionView 画像保存: " + msg + (auto ? "（自動保存。以後この記録は省略）" : "")); if (auto) autoFullLogged = true; return false;
                        }
                        // 古い順（更新日時）に、上限未満になるまで削除する。snap.csv の行は残す
                        Array.Sort(have, delegate (string a, string b) { int c = File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)); return c != 0 ? c : string.CompareOrdinal(a, b); });
                        for (int i = 0; i <= have.Length - SnapCfg.MaxFiles; i++) { File.Delete(have[i]); nRemoved++; removed += (removed == "" ? "" : " ") + Path.GetFileName(have[i]); }
                    }
                    autoFullLogged = false;
                    // ファイル名: 画像の時刻_OK|NG_工程.bmp（先頭 14 桁＝時刻、末尾＝工程。ファイル再生で時刻と工程をそのまま読める）
                    DateTime now = DateTime.Now; string proc = f.ProcName == "" ? "NA" : f.ProcName, ts = f.Time.ToString("yyyyMMddHHmmss");
                    string path = Path.Combine(dir, ts + "_" + label + "_" + proc + ".bmp");
                    for (int k = 2; File.Exists(path); k++) path = Path.Combine(dir, ts + "_" + label + k + "_" + proc + ".bmp");
                    string tmp = path + ".tmp"; File.WriteAllBytes(tmp, f.Raw); File.Move(tmp, path);
                    if (ng) f.SavedNg = true; else f.SavedOk = true;
                    string thr = "", thrS = "", range = "", contour = "", jmode = "";
                    if (f.Proc >= 0) { VisionProcCfg p = VisionCfg.P[f.Proc]; thr = N(p.Thr); thrS = p.Judge == 1 ? N(p.ThrStrong) : ""; range = N(p.R1a) + ":" + N(p.R1b) + (p.Range2 ? " " + N(p.R2a) + ":" + N(p.R2b) : ""); contour = p.Contour == 0 ? "brightness" : p.Contour == 1 ? "edge" : "ridge"; jmode = p.Judge == 1 ? "pair" : "single"; }
                    string sd = "", sv = ""; bool det = false;
                    if (f.R != null) { det = f.R.Detected; foreach (VSeam s in f.R.Seams) { sd += (sd == "" ? "" : " ") + N(s.Deg); sv += (sv == "" ? "" : " ") + N(s.D); } }
                    string csv = Path.Combine(dir, CsvName); bool isNew = !File.Exists(csv);
                    string line = string.Join(",", new string[] { now.ToString("yyyy-MM-dd HH:mm:ss"), auto ? "auto" : "manual", label, Q(Path.GetFileName(path)), f.Time.ToString("yyyy-MM-dd HH:mm:ss"), Q(Path.GetFileName(f.Name ?? "")), proc, judge ?? "", f.St == ' ' ? "" : f.St.ToString(), f.R == null ? "" : (det ? "1" : "0"), N(conf), hit.ToString(), cnt.ToString(), contour, jmode, thr, thrS, Q(range), Q(sd), Q(sv), Q(f.Comment) });
                    using (StreamWriter w = new StreamWriter(csv, true, new UTF8Encoding(isNew))) { if (isNew) w.Write(Header + "\r\n"); w.Write(line + "\r\n"); }
                    int total = have.Length - nRemoved + 1; count[ng ? 1 : 0] = total; string of = total + (SnapCfg.MaxFiles > 0 ? "／" + SnapCfg.MaxFiles : "");
                    msg = Lx.T(fo + " へ保存しました: " + Path.GetFileName(path) + "（" + of + " 枚）", "Saved to " + fo + ": " + Path.GetFileName(path) + " (" + of.Replace("／", "/") + ")", "已保存到 " + fo + ": " + Path.GetFileName(path) + "（" + of + " 张）");
                    M.Log("VisionView 画像保存: " + (auto ? "自動 " : "手動 ") + label + " " + path + " 工程=" + proc + " 判定=" + judge + " 閾値=" + thr + (removed == "" ? "" : " 上限のため削除=" + removed));
                    return true;
                }
                catch (Exception ex) { msg = Lx.T("画像を保存できませんでした: ", "Could not save the image: ", "无法保存图像: ") + ex.Message; M.Log("VisionView 画像保存: 失敗 — " + ex); return false; }
            }
        }
    }
}
