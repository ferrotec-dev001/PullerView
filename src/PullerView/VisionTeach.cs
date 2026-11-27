// VisionTeach.cs — 教示画像の保存（V2-v8）
// オペレータが「この画像は実際は NG／OK」と印を付けた画像を、通常の受信画像（[Image]。最新 max_files 枚で自動削除）とは別のフォルダへ残す。
// 保存するもの: BMP（受信したままのデータ）と、teach.csv の 1 行（教示日時・教示内容・工程・そのときの判定結果・閾値・検出範囲など）。
// ここでは保存だけを行う。教示画像からの設定値の自動提案（再解析）は未実装。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PullerView
{
    public static class TeachCfg
    {
        public static bool Enable = true;
        public static string Folder = "NgTeach";   // 相対は EXE 基準
        public static int MaxFiles = 200;          // 教示画像の上限枚数（0=無制限）
        public static int OnFull = 0;              // 上限に達したとき: 0=stop（保存しない。古い画像を残す）／1=delete_oldest（古い画像から削除して保存）
        public static readonly string[] OnFullN = { "stop", "delete_oldest" };
    }

    public static class VisionTeach
    {
        public const string CsvName = "teach.csv";
        const string Header = "teach_time,label,file,image_time,source_name,process,judge,frame_status,detected,confidence_pct,hit,count,contour,judge_mode,seam_threshold,seam_threshold_strong,detect_range,seam_deg,seam_d,comment";
        static readonly object lk = new object();
        public static string FolderPath { get { string f = TeachCfg.Folder; return Path.IsPathRooted(f) ? f : Path.Combine(Ini.ExeDir, f); } }
        static string N(double v) { return double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string Q(string s) { s = s ?? ""; return s.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        public static int Count() { try { return Directory.Exists(FolderPath) ? Directory.GetFiles(FolderPath, "*.bmp").Length : 0; } catch (Exception) { return 0; } }

        /// <summary>教示画像を保存する。f は印を付けた時点の画像（VV.Cur を控えたもの）、judge・conf・hit・cnt はその時点の判定。戻り値 true＝保存した。msg は画面・ログ用の結果。</summary>
        public static bool Save(VFrame f, bool asNg, string judge, double conf, int hit, int cnt, out string msg)
        {
            msg = "";
            if (!TeachCfg.Enable) { msg = Lx.T("教示画像の保存は無効です（VisionView.ini [Teach] enable=0）", "Teaching image saving is disabled (VisionView.ini [Teach] enable=0)", "示教图像保存已禁用（VisionView.ini [Teach] enable=0）"); return false; }
            if (f == null || f.Raw == null || f.Raw.Length == 0) { msg = Lx.T("保存できる画像がありません", "No image to save", "没有可保存的图像"); return false; }
            lock (lk)
            {
                try
                {
                    string dir = FolderPath; Directory.CreateDirectory(dir);
                    string[] have = Directory.GetFiles(dir, "*.bmp"); string removed = "";
                    if (TeachCfg.MaxFiles > 0 && have.Length >= TeachCfg.MaxFiles)
                    {
                        if (TeachCfg.OnFull != 1)
                        {
                            msg = Lx.T("教示画像が上限 " + TeachCfg.MaxFiles + " 枚に達しているため保存しませんでした。不要な画像を " + dir + " から移動・削除するか、VisionView.ini [Teach] max_files を増やしてください。",
                                "Not saved: the teaching image limit (" + TeachCfg.MaxFiles + ") has been reached. Move or delete images in " + dir + ", or increase max_files in VisionView.ini [Teach].",
                                "示教图像已达上限 " + TeachCfg.MaxFiles + " 张，未保存。请移走或删除 " + dir + " 中的图像，或增大 VisionView.ini [Teach] max_files。");
                            M.Log("VisionView 教示: " + msg); return false;
                        }
                        // 古い順（更新日時）に、上限未満になるまで削除する。teach.csv の行は残す
                        Array.Sort(have, delegate (string a, string b) { int c = File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)); return c != 0 ? c : string.CompareOrdinal(a, b); });
                        for (int i = 0; i <= have.Length - TeachCfg.MaxFiles; i++) { File.Delete(have[i]); removed += (removed == "" ? "" : " ") + Path.GetFileName(have[i]); }
                        M.Log("VisionView 教示: 上限 " + TeachCfg.MaxFiles + " 枚のため古い教示画像を削除 — " + removed);
                    }
                    DateTime now = DateTime.Now; string label = asNg ? "NG" : "OK"; string proc = f.ProcName == "" ? "NA" : f.ProcName;
                    string stem = now.ToString("yyyyMMddHHmmss") + "_" + proc + "_TEACH-" + label, path = Path.Combine(dir, stem + ".bmp");
                    for (int k = 2; File.Exists(path); k++) path = Path.Combine(dir, stem + "_" + k + ".bmp");
                    string tmp = path + ".tmp"; File.WriteAllBytes(tmp, f.Raw); File.Move(tmp, path);
                    // 記録（teach.csv。UTF-8 BOM）
                    string thr = "", thrS = "", range = "", contour = "", jmode = "";
                    if (f.Proc >= 0) { VisionProcCfg p = VisionCfg.P[f.Proc]; thr = N(p.Thr); thrS = p.Judge == 1 ? N(p.ThrStrong) : ""; range = N(p.R1a) + ":" + N(p.R1b) + (p.Range2 ? " " + N(p.R2a) + ":" + N(p.R2b) : ""); contour = p.Contour == 0 ? "brightness" : p.Contour == 1 ? "edge" : "ridge"; jmode = p.Judge == 1 ? "pair" : "single"; }
                    string sd = "", sv = ""; bool det = false;
                    if (f.R != null) { det = f.R.Detected; foreach (VSeam s in f.R.Seams) { sd += (sd == "" ? "" : " ") + N(s.Deg); sv += (sv == "" ? "" : " ") + N(s.D); } }
                    string csv = Path.Combine(dir, CsvName); bool isNew = !File.Exists(csv);
                    string line = string.Join(",", new string[] { now.ToString("yyyy-MM-dd HH:mm:ss"), label, Q(Path.GetFileName(path)), f.Time.ToString("yyyy-MM-dd HH:mm:ss"), Q(Path.GetFileName(f.Name ?? "")), proc, judge ?? "", f.St == ' ' ? "" : f.St.ToString(), f.R == null ? "" : (det ? "1" : "0"), N(conf), hit.ToString(), cnt.ToString(), contour, jmode, thr, thrS, Q(range), Q(sd), Q(sv), Q(f.Comment) });
                    using (StreamWriter w = new StreamWriter(csv, true, new UTF8Encoding(isNew))) { if (isNew) w.Write(Header + "\r\n"); w.Write(line + "\r\n"); }
                    int total = have.Length - (removed == "" ? 0 : removed.Split(' ').Length) + 1;
                    msg = Lx.T("教示画像を保存しました（実際は " + label + "）: " + Path.GetFileName(path) + "（" + total + (TeachCfg.MaxFiles > 0 ? "／" + TeachCfg.MaxFiles : "") + " 枚）",
                        "Teaching image saved (actual: " + label + "): " + Path.GetFileName(path) + " (" + total + (TeachCfg.MaxFiles > 0 ? "/" + TeachCfg.MaxFiles : "") + ")",
                        "已保存示教图像（实际为 " + label + "）: " + Path.GetFileName(path) + "（" + total + (TeachCfg.MaxFiles > 0 ? "／" + TeachCfg.MaxFiles : "") + " 张）");
                    M.Log("VisionView 教示: " + label + " " + path + " 工程=" + proc + " 判定=" + judge + " 閾値=" + thr + (removed == "" ? "" : " 削除=" + removed));
                    return true;
                }
                catch (Exception ex) { msg = Lx.T("教示画像を保存できませんでした: ", "Could not save the teaching image: ", "无法保存示教图像: ") + ex.Message; M.Log("VisionView 教示: 保存失敗 — " + ex); return false; }
            }
        }
    }
}
