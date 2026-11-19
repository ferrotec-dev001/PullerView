// VisionView（晶線解析）の本体: VisionView.ini の読み書き、画像の入力（カメラ TCP／ファイル再生／模擬カメラ）、
// 「シールドで隠れている場合は判定しない」ルール、真偽度と OK／NG の判定、イベント。解析そのものは VisionAnalyzer.cs。
// VB_FERROTEC.exe・カメラのどちらとも接続せずに、ファイル再生だけで受信以外の全機能が動く。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Ferrotec.CvImageSaver40;

namespace PullerView
{
    /// <summary>VisionView.ini（EXE と同じフォルダ）の読み書き。値は VisionCfg／CameraCfg と、このクラスの補助項目に入る。</summary>
    public static class VisionIni
    {
        public static bool Pane = true; public static double Gain = 3.4, Gamma = 0.55, PairTol = 10, Aspect = 0.93; public static string Loaded = "";
        public static bool[] ProcOn = { true, true, true, true, false };   // NECK CROWN SHOULDER BODY TAIL（TAIL は検出方式が未定のため既定 OFF）
        static readonly string[] SrcN = { "camera", "file", "sim" }, PsN = { "auto", "manual", "filename" }, OrN = { "100", "111", "110" }, CtN = { "brightness", "edge", "ridge" }, HdN = { "off", "hold", "predict" };
        public static string File { get { return Ini.InExeDir("VisionView.ini"); } }
        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static int Idx(string[] names, string v, int def) { for (int i = 0; i < names.Length; i++) if (string.Equals(names[i], v, StringComparison.OrdinalIgnoreCase)) return i; return def; }

        public static string Serialize()
        {
            StringBuilder b = new StringBuilder(); Action<string> L = delegate (string s) { b.Append(s).Append("\r\n"); };
            L("; VisionView.ini — 晶線解析（VisionView）の設定。PullerView.exe と同じフォルダに置く（文字コード UTF-8）。設定画面の「保存」で上書きされる");
            L("[Vision]");
            L("enable=" + (VisionCfg.Enable ? 1 : 0) + "              ; 1=晶線解析を使う 0=使わない（0 のとき PullerView は単体版と同じ動作）");
            L("source=" + SrcN[VisionCfg.Source] + "            ; 画像入力元: camera（TCP）／file（ファイル再生）／sim（模擬カメラ。再生フォルダの画像を本機内で TCP 送信）");
            L("process_source=" + PsN[VisionCfg.ProcSource] + "    ; 工程の取得元: auto（VB_FERROTEC。未接続時は manual）／manual／filename（…_NECK.bmp）");
            L("manual_process=" + VisionCfg.P[VisionCfg.Proc].Name);
            L("crystal_orientation=" + OrN[VisionCfg.Orient] + "  ; 100=4 本・90° / 111=3 本・120° / 110=2 本・180°");
            L("truth_n=" + VisionCfg.TruthN + "             ; 真偽度の測定窓 N（直近 N 回。判定対象外の画像は数えない）");
            L("ng_threshold_pct=" + VisionCfg.NgPct + "    ; 真偽度がこの値を下回ると NG（同じ値は OK）");
            L("judge_delay_sec=" + VisionCfg.DelaySec + "    ; 検出開始時間 [秒]。工程切替からこの時間はカウント・判定しない。ファイル再生での確認は 0 にする");
            L("tolerance_deg=" + VisionCfg.TolDeg + "        ; 期待位置の許容角度 [°]");
            L("pair_tol_deg=" + N(PairTol) + "        ; 組（pair）判定の角度差の許容 [°]");
            L("ring_aspect=" + N(Aspect) + "       ; リングの縦横比 b/a（カメラの見下ろし角で決まる。輪郭方式で使用）");
            L("pane=" + (Pane ? 1 : 0) + "                ; 1=右欄（VisionView）を開く 0=畳む（タイトル行のカメラのアイコンで切替）");
            L("display_gain=" + N(Gain) + "      ; 画面に表示する画像の明るさ補正（解析には影響しない）");
            L("display_gamma=" + N(Gamma));
            L(""); L("[Camera]   ; VB_FERROTEC との接続（PullerView.ini [Host]）とは別の設定");
            L("ip=" + CameraCfg.Ip[0] + "." + CameraCfg.Ip[1] + "." + CameraCfg.Ip[2] + "." + CameraCfg.Ip[3]);
            L("port=" + CameraCfg.Port + "             ; 画像出力ポート番号");
            L("delimiter=" + (CameraCfg.Delimiter == 1 ? "CRLF" : "CR") + "          ; CR / CRLF（受信はどちらも受け付ける。模擬カメラの送信に使用）");
            L("camera_no=" + CameraCfg.CameraNo + "           ; 0=全カメラ 1〜4=そのカメラだけ");
            L("reconnect_sec=" + CameraCfg.ReconnectSec); L("timeout_sec=" + CameraCfg.TimeoutSec);
            L("reject_checksum=" + (CameraCfg.RejectChecksum ? 1 : 0) + "     ; 1=チェックサム不一致の画像を破棄する");
            L("raw_dump=" + (CameraCfg.RawDump ? 1 : 0) + "            ; 1=診断用に受信した生データを Log\\raw-*.bin へ保存する（接続ごとに先頭 2MB）");
            L(""); L("[Image]    ; 受信した画像の保存。OK／NG は判断せず 1 つのフォルダへ保存する");
            L("save=" + (CameraCfg.Save ? 1 : 0)); L("save_mode=" + (CameraCfg.SaveMode == 1 ? "thin" : "all") + "         ; all=全数 thin=間引き（save_every 枚に 1 枚）"); L("save_every=" + CameraCfg.SaveEvery);
            L("folder=" + CameraCfg.Folder + "          ; 相対は EXE 基準。ファイル名 YYYYMMDDHHMMSS_工程名.bmp");
            L("max_files=" + CameraCfg.MaxFiles + "          ; 最大保持枚数（0=無制限。超過分は古い順に削除）"); L("keep_days=" + CameraCfg.KeepDays + "          ; 保持日数（0=無効）"); L("cleanup_hours=" + CameraCfg.CleanupHours);
            L(""); L("[Playback] ; ファイル再生（source=file／sim）");
            L("folder=" + CameraCfg.PlayFolder); L("interval_ms=" + CameraCfg.PlayMs); L("loop=" + (CameraCfg.PlayLoop ? 1 : 0));
            for (int i = 0; i < VisionCfg.P.Length; i++)
            {
                VisionProcCfg p = VisionCfg.P[i]; L(""); L("[" + p.Name + "]" + (p.Decided ? "" : "   ; この工程の設定値は未定（仮の値）"));
                L("enable=" + (ProcOn[i] ? 1 : 0) + "              ; 0=この工程は晶線解析の対象外にする");
                L("contour=" + CtN[p.Contour] + "       ; brightness（輝度方式）／edge・ridge（輪郭方式）"); L("judge=" + (p.Judge == 1 ? "pair" : "single"));
                L("seam_threshold=" + N(p.Thr)); L("seam_threshold_strong=" + N(p.ThrStrong) + "   ; pair のとき、組の強い方に必要な値");
                L("detect_range=" + N(p.R1a) + ":" + N(p.R1b) + (p.Range2 ? "," + N(p.R2a) + ":" + N(p.R2b) : "") + "   ; 検出範囲 [°]。2 区間のときは間がシールド遮蔽範囲");
                L("hidden_rule=" + HdN[p.Hidden] + "      ; 未検出の画像の扱い: off／hold／predict"); L("hide_hold_sec=" + p.HoldSec); L("hide_margin_deg=" + p.MarginDeg); L("hide_lost_sec=" + p.LostSec);
            }
            return b.ToString();
        }
        public static void Save() { string s = Serialize(); System.IO.File.WriteAllText(File, s, new UTF8Encoding(true)); Loaded = s; M.Log("VisionView.ini 保存"); }
        /// <summary>設定画面で値が変わっていれば保存する。戻り値 true＝保存した。</summary>
        public static bool SaveIfChanged() { if (Serialize() == Loaded) return false; Save(); return true; }

        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) { Loaded = Serialize(); M.Log("VisionView.ini なし: 既定値を使用 (" + File + ")"); return; }
                Ini ini = Ini.Load(File); Dictionary<string, string> d = ini.Section("Vision"); int iv; double dv;
                Func<Dictionary<string, string>, string, string> V = delegate (Dictionary<string, string> sec, string k) { string v; if (!sec.TryGetValue(k, out v)) return null; int c = v.IndexOf(';'); if (c >= 0) v = v.Substring(0, c); return v.Trim(); };
                Func<Dictionary<string, string>, string, int, int, int, int> I = delegate (Dictionary<string, string> sec, string k, int def, int lo, int hi) { string v = V(sec, k); int x; return v != null && int.TryParse(v, out x) && x >= lo && x <= hi ? x : def; };
                Func<Dictionary<string, string>, string, double, double> Dn = delegate (Dictionary<string, string> sec, string k, double def) { string v = V(sec, k); double x; return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : def; };
                VisionCfg.Enable = I(d, "enable", VisionCfg.Enable ? 1 : 0, 0, 1) == 1; VisionCfg.Source = Idx(SrcN, V(d, "source"), VisionCfg.Source); VisionCfg.ProcSource = Idx(PsN, V(d, "process_source"), VisionCfg.ProcSource);
                string mp = V(d, "manual_process"); for (int i = 0; i < VisionCfg.P.Length; i++) if (string.Equals(VisionCfg.P[i].Name, mp, StringComparison.OrdinalIgnoreCase)) VisionCfg.Proc = i;
                VisionCfg.Orient = Idx(OrN, V(d, "crystal_orientation"), VisionCfg.Orient); VisionCfg.TruthN = I(d, "truth_n", VisionCfg.TruthN, 1, 1000); VisionCfg.NgPct = I(d, "ng_threshold_pct", VisionCfg.NgPct, 0, 100);
                VisionCfg.DelaySec = I(d, "judge_delay_sec", VisionCfg.DelaySec, 0, 86400); VisionCfg.TolDeg = I(d, "tolerance_deg", VisionCfg.TolDeg, 1, 45); PairTol = Dn(d, "pair_tol_deg", PairTol); Aspect = Dn(d, "ring_aspect", Aspect); if (Aspect < 0.3 || Aspect > 1.5) Aspect = 0.93;
                Pane = I(d, "pane", Pane ? 1 : 0, 0, 1) == 1; Gain = Dn(d, "display_gain", Gain); Gamma = Dn(d, "display_gamma", Gamma); if (Gain <= 0) Gain = 1; if (Gamma <= 0) Gamma = 1;
                d = ini.Section("Camera"); string ip = V(d, "ip"); IPAddress a; if (ip != null && IPAddress.TryParse(ip, out a) && a.AddressFamily == AddressFamily.InterNetwork) { byte[] ab = a.GetAddressBytes(); for (int i = 0; i < 4; i++) CameraCfg.Ip[i] = ab[i]; }
                CameraCfg.Port = I(d, "port", CameraCfg.Port, 1, 65535); string dl = V(d, "delimiter"); if (dl != null) CameraCfg.Delimiter = dl.Replace("+", "").ToUpperInvariant() == "CRLF" ? 1 : 0; CameraCfg.CameraNo = I(d, "camera_no", CameraCfg.CameraNo, 0, 4);
                CameraCfg.ReconnectSec = I(d, "reconnect_sec", CameraCfg.ReconnectSec, 1, 3600); CameraCfg.TimeoutSec = I(d, "timeout_sec", CameraCfg.TimeoutSec, 1, 600); CameraCfg.RejectChecksum = I(d, "reject_checksum", CameraCfg.RejectChecksum ? 1 : 0, 0, 1) == 1; CameraCfg.RawDump = I(d, "raw_dump", CameraCfg.RawDump ? 1 : 0, 0, 1) == 1;
                d = ini.Section("Image"); CameraCfg.Save = I(d, "save", CameraCfg.Save ? 1 : 0, 0, 1) == 1; string sm = V(d, "save_mode"); if (sm != null) CameraCfg.SaveMode = sm.ToLowerInvariant() == "thin" ? 1 : 0; CameraCfg.SaveEvery = I(d, "save_every", CameraCfg.SaveEvery, 1, 100000);
                string fo = V(d, "folder"); if (!string.IsNullOrEmpty(fo)) CameraCfg.Folder = fo; CameraCfg.MaxFiles = I(d, "max_files", CameraCfg.MaxFiles, 0, 10000000); CameraCfg.KeepDays = I(d, "keep_days", CameraCfg.KeepDays, 0, 36500); CameraCfg.CleanupHours = I(d, "cleanup_hours", CameraCfg.CleanupHours, 1, 1000);
                d = ini.Section("Playback"); fo = V(d, "folder"); if (!string.IsNullOrEmpty(fo)) CameraCfg.PlayFolder = fo; CameraCfg.PlayMs = I(d, "interval_ms", CameraCfg.PlayMs, 50, 600000); CameraCfg.PlayLoop = I(d, "loop", CameraCfg.PlayLoop ? 1 : 0, 0, 1) == 1;
                for (int i = 0; i < VisionCfg.P.Length; i++)
                {
                    VisionProcCfg p = VisionCfg.P[i]; d = ini.Section(p.Name); ProcOn[i] = I(d, "enable", ProcOn[i] ? 1 : 0, 0, 1) == 1; p.Contour = Idx(CtN, V(d, "contour"), p.Contour); string jg = V(d, "judge"); if (jg != null) p.Judge = jg.ToLowerInvariant() == "pair" ? 1 : 0;
                    p.Thr = Dn(d, "seam_threshold", p.Thr); p.ThrStrong = Dn(d, "seam_threshold_strong", p.ThrStrong); p.Hidden = Idx(HdN, V(d, "hidden_rule"), p.Hidden); p.HoldSec = I(d, "hide_hold_sec", p.HoldSec, 0, 3600); p.MarginDeg = I(d, "hide_margin_deg", p.MarginDeg, 0, 45); p.LostSec = I(d, "hide_lost_sec", p.LostSec, 0, 3600);
                    string rg = V(d, "detect_range");
                    if (!string.IsNullOrEmpty(rg))
                    {
                        string[] parts = rg.Split(new char[] { ',' }); List<double[]> rs = new List<double[]>();
                        foreach (string part in parts) { string[] ab = part.Split(new char[] { ':' }); double lo, hi; if (ab.Length == 2 && double.TryParse(ab[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lo) && double.TryParse(ab[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out hi) && hi > lo) rs.Add(new double[] { lo, hi }); }
                        if (rs.Count >= 1) { p.R1a = rs[0][0]; p.R1b = rs[0][1]; p.Range2 = rs.Count >= 2; if (p.Range2) { p.R2a = rs[1][0]; p.R2b = rs[1][1]; } }
                    }
                }
                Loaded = Serialize(); M.Log("VisionView.ini 読込: enable=" + VisionCfg.Enable + " source=" + SrcN[VisionCfg.Source] + " process_source=" + PsN[VisionCfg.ProcSource]);
            }
            catch (Exception ex) { M.Log("VisionView.ini 読込失敗: " + ex.Message); }
        }
    }

    /// <summary>「シールドで隠れている場合は判定しない」ルール（hold／predict）。Python 参照実装 hidden_rule.py の移植。</summary>
    public class HiddenRule
    {
        readonly double[][] ranges; readonly bool predict; readonly double hold, margin, period, lost; const int Hist = 12;
        readonly List<double[]> obs = new List<double[]>(); public double? Last; double? t0;
        public HiddenRule(double[][] ranges, bool predict, double holdSec, double margin, double period, double lostSec) { this.ranges = ranges; this.predict = predict; hold = holdSec; this.margin = margin; this.period = period; lost = lostSec; }
        bool InRange(double phi)
        {
            for (int k = -2; k < 5; k++) { double a = phi + period * k; foreach (double[] r in ranges) if (r[0] + margin <= a && a <= r[1] - margin) return true; }
            return false;
        }
        /// <summary>直近の検出から晶線の位置を推算する（0〜period の角度）。推算できないときは null。</summary>
        public double? Predict(double t)
        {
            int n0 = Math.Max(0, obs.Count - Hist), n = obs.Count - n0; if (n < 4) return null;
            double bR = -1, bW = 0, bP = 0;
            for (int i = 0; i < 4500; i++)
            {   // 見かけの回転速度 [°/s]（撮影間隔による折返しを含む）
                double w = -45 + i * 0.02, c = 0, s = 0;
                for (int k = n0; k < obs.Count; k++) { double ph = (obs[k][1] - w * obs[k][0]) * 360.0 / period * Math.PI / 180; c += Math.Cos(ph); s += Math.Sin(ph); }
                c /= n; s /= n; double R = Math.Sqrt(c * c + s * s);
                if (bR < 0 || R > bR + 1e-9 || (Math.Abs(R - bR) <= 1e-9 && Math.Abs(w) < Math.Abs(bW))) { bR = R; bW = w; bP = Math.Atan2(s, c) * 180 / Math.PI * period / 360.0; }
            }
            if (bR < 0.9) return null;
            double v = (bP + bW * t) % period; return v < 0 ? v + period : v;
        }
        /// <summary>戻り値: 'D' 検出／'N' 未検出（NG として数える）／'S' 判定対象外。</summary>
        public char Update(double t, List<double> degs)
        {
            if (t0 == null) t0 = t;
            if (degs.Count > 0) { foreach (double d in degs) obs.Add(new double[] { t, d }); if (obs.Count > 200) obs.RemoveRange(0, obs.Count - 200); Last = t; return 'D'; }
            double rf = Last != null ? Last.Value : t0.Value;
            if (predict && Last != null && t - Last.Value <= lost) { double? phi = Predict(t); if (phi != null) return InRange(phi.Value) ? 'N' : 'S'; }
            return t - rf <= hold ? 'S' : 'N';
        }
    }

    /// <summary>画面に出す 1 枚分の結果。</summary>
    public class VFrame
    {
        public Bitmap Disp; public VResult R; public string Name = ""; public DateTime Time; public int Proc = -1; public string ProcName = ""; public char St = ' '; public string Comment = ""; public double HiddenDeg = double.NaN; public double DispScale = 1;
    }

    /// <summary>VisionView の実行部（静的）。</summary>
    public static class VV
    {
        public static readonly object Lk = new object();
        public static VFrame Cur;                                         // 最新の 1 枚（Lk で保護）
        public static string Judge = "OFF";                               // WAIT／OK／NG／OFF（対象外・機能 OFF）
        public static double Conf = double.NaN; public static int Hit, Cnt; public static long Frames, Dropped; public static double LastMs;
        public static readonly List<char> Hist = new List<char>(); public static readonly List<double> ConfHist = new List<double>(); public const int HistN = 15;
        public static Control Ui; public static string SrcStatus = ""; public static bool CamConnected; public static string TestMsg = "";
        public static readonly string[] ProcNames = { "NECK", "CROWN", "SHOULDER", "BODY", "TAIL" };

        // ---- 判定の状態（工程ごとにリセット）
        static HiddenRule hr, hrShow; static List<bool> q = new List<bool>(); static double? lastDet; static bool lostFlag; static int curProc = -2; static double tAcc; static DateTime prevTime = DateTime.MinValue;
        static double Pitch { get { return VisionCfg.Orient == 1 ? 120.0 : VisionCfg.Orient == 2 ? 180.0 : 90.0; } }
        public static double[][] Ranges(VisionProcCfg p) { return p.Range2 ? new double[][] { new double[] { p.R1a, p.R1b }, new double[] { p.R2a, p.R2b } } : new double[][] { new double[] { p.R1a, p.R1b } }; }
        public static VParam Param(VisionProcCfg p) { VParam c = new VParam(); c.Contour = p.Contour; c.Pair = p.Judge == 1; c.Thr = p.Thr; c.ThrStrong = p.ThrStrong; c.Expect = Pitch; c.PairTol = VisionIni.PairTol; c.Aspect = VisionIni.Aspect; c.Ranges = Ranges(p); return c; }
        static void ResetJudge(int proc)
        {
            curProc = proc; q = new List<bool>(); lastDet = null; lostFlag = false; tAcc = 0; prevTime = DateTime.MinValue; Hist.Clear(); ConfHist.Clear(); Conf = double.NaN; Hit = Cnt = 0;
            if (proc >= 0) { VisionProcCfg p = VisionCfg.P[proc]; double[][] rg = Ranges(p); hr = new HiddenRule(rg, p.Hidden == 2, p.Hidden == 0 ? -1.0 : p.HoldSec, p.MarginDeg, Pitch, p.LostSec); hrShow = new HiddenRule(rg, true, 0, 0, Pitch, p.LostSec); Judge = "WAIT"; }
            else { hr = hrShow = null; Judge = "OFF"; }
        }
        /// <summary>現在の工程（0=NECK … 4=TAIL、−1=対象外）。nameProc はファイル名から取った工程名（無ければ null）。</summary>
        public static int CurrentProc(string nameProc)
        {
            if (VisionCfg.ProcSource == 2 && nameProc != null) { int k = Array.IndexOf(ProcNames, nameProc.ToUpperInvariant()); if (k >= 0) return k; }
            if (VisionCfg.ProcSource == 0 && M.DataOk && !M.Sim) return Array.IndexOf(ProcNames, M.Procs[M.Proc]);   // VB_FERROTEC の工程（NECK〜TAIL 以外は −1＝対象外）
            return VisionCfg.Proc;
        }
        static string Dev { get { return EventLog.Dev(); } }
        static readonly List<PvEvent> evq = new List<PvEvent>();
        // イベントは解析スレッドで作り、画面スレッドで EventLog へ渡す（EventLog.Added が画面を描き直すため）
        static void Ev(string text, int tone, string proc) { PvEvent e = new PvEvent(); e.Time = DateTime.Now; e.Text = text; e.Proc = proc; e.X = M.CurX; e.LenAxis = M.LengthAxis; e.Tone = tone; lock (evq) evq.Add(e); }
        public static void FlushEvents() { PvEvent[] a; lock (evq) { a = evq.ToArray(); evq.Clear(); } foreach (PvEvent e in a) EventLog.Add(e); }
        public static string JudgeText(string j) { return j == "OK" ? "OK" : j == "NG" ? "NG" : j == "WAIT" ? Lx.T("待機", "Wait", "待机") : Lx.T("対象外", "N/A", "对象外"); }

        /// <summary>1 枚分の判定（Python 参照実装 vision_judge.py と同じ数え方）。</summary>
        static void JudgeFrame(VFrame f, int interval)
        {
            int proc = f.Proc; if (proc != curProc) ResetJudge(proc);
            if (proc < 0) { f.Comment = Lx.T("この工程は晶線解析の対象外です", "This process is not subject to seam analysis", "该工序不在晶线解析范围内"); return; }
            VisionProcCfg p = VisionCfg.P[proc]; string pn = p.Name;
            // 時刻: 画像の時刻を使う。再生の折返しなどで時刻が戻ったときは再生間隔だけ進める
            if (prevTime != DateTime.MinValue) { double dt = (f.Time - prevTime).TotalSeconds; if (dt < 0 || dt > 3600) dt = interval / 1000.0; tAcc += dt; }
            prevTime = f.Time; double t = tAcc;
            List<VSeam> seams = (f.R != null && f.R.Valid && f.R.Detected) ? f.R.Seams : new List<VSeam>(); List<double> degs = new List<double>(); foreach (VSeam s in seams) degs.Add(s.Deg);
            // シールドの陰の晶線の位置（表示用の推算）
            f.HiddenDeg = double.NaN;
            if (p.Range2 && degs.Count == 0 && hrShow.Last != null) { double? phi = hrShow.Predict(t); if (phi != null) for (int k = -2; k < 5; k++) { double a = phi.Value + Pitch * k; if (a >= p.R1b && a <= p.R2a) { f.HiddenDeg = a; break; } } }
            hrShow.Update(t, degs);
            char st = hr.Update(t, degs); f.St = st; int meas = 0, hit = 0; bool counting = t >= VisionCfg.DelaySec; double margin = p.MarginDeg, tol = VisionCfg.TolDeg; double[][] rg = Ranges(p);
            int secs = (int)Math.Round(t - (lastDet != null ? lastDet.Value : 0));
            if (st == 'D')
            {
                VSeam s0 = seams[0]; foreach (VSeam s in seams) if (s.D > s0.D) s0 = s;
                List<double> exp = new List<double>(); exp.Add(s0.Deg);
                foreach (int k in new int[] { -3, -2, -1, 1, 2, 3 }) { double a = s0.Deg + Pitch * k; bool inr = false; foreach (double[] r in rg) if (r[0] + margin <= a && a <= r[1] - margin) inr = true; if (inr) exp.Add(a); }
                meas = exp.Count; foreach (double e in exp) { bool any = false; foreach (VSeam s in seams) if (Math.Abs(s.Deg - e) <= tol) any = true; if (any) hit++; }
                StringBuilder ds = new StringBuilder(); foreach (VSeam s in seams) { if (ds.Length > 0) ds.Append("・"); ds.Append(Math.Round(s.Deg).ToString("0") + "°"); }
                f.Comment = Lx.T("晶線を検出しました（" + ds + "）", "Seam line detected (" + ds + ")", "已检出晶线（" + ds + "）");
                if (lostFlag) { Ev(Dev + Lx.T(pn + "の晶線を再び検出しました。", pn + ": the seam line has been detected again.", pn + "再次检出晶线。"), 0, pn); lostFlag = false; }
                lastDet = t;
            }
            else if (st == 'N')
            {
                meas = 1; f.Comment = Lx.T("検出範囲内で晶線を検出できません", "No seam line detected within the detection range", "检出范围内未检出晶线");
                if (!lostFlag && p.Hidden != 0)
                {
                    bool miss = p.Hidden == 2 && lastDet != null && t - lastDet.Value <= p.HoldSec;
                    Ev(Dev + (miss ? Lx.T(pn + "の晶線が検出範囲内にあるはずですが、検出できません。未検出として数えます。", pn + ": the seam line should be within the detection range but was not detected. Counting it as not detected.", pn + "的晶线应在检出范围内，但未检出。按未检出计数。")
                                   : Lx.T(pn + "の晶線を" + secs + "秒間検出できていません。シールドによる判定対象外の時間を超えたため、未検出として数えます。", pn + ": no seam line has been detected for " + secs + " s. The heat-shield exclusion time has passed, so images are now counted as not detected.", pn + "已连续" + secs + "秒未检出晶线。已超过热屏遮挡的不判定时间，开始按未检出计数。")), 1, pn);
                    lostFlag = true;
                }
            }
            else f.Comment = lastDet != null ? Lx.T("晶線がシールドに隠れているため判定していません（最後の検出から " + secs + " 秒）", "Not judged: the seam line is behind the heat shield (" + secs + " s since last detection)", "晶线被热屏遮挡，暂不判定（距上次检出 " + secs + " 秒）")
                                             : Lx.T("晶線がシールドに隠れているため判定していません（検出待ち " + secs + " 秒）", "Not judged: the seam line is behind the heat shield (waiting for detection, " + secs + " s)", "晶线被热屏遮挡，暂不判定（等待检出 " + secs + " 秒）");
            if (counting && meas > 0) { for (int i = 0; i < meas; i++) q.Add(i < hit); int n = Math.Max(1, VisionCfg.TruthN); if (q.Count > n) q.RemoveRange(0, q.Count - n); }
            if (!counting) { int rem = (int)Math.Ceiling(VisionCfg.DelaySec - t); string ts = (rem / 60) + ":" + (rem % 60).ToString("00"); f.Comment = Lx.T("待機中（検出開始まで残り " + ts + "）", "Waiting (" + ts + " until judgement starts)", "待机中（距检出开始还有 " + ts + "）") + "　" + f.Comment; }
            int h = 0; foreach (bool b in q) if (b) h++; Hit = h; Cnt = q.Count; Conf = q.Count > 0 ? 100.0 * h / q.Count : double.NaN;
            string nw = Judge; if (q.Count >= Math.Max(1, VisionCfg.TruthN)) nw = Conf >= VisionCfg.NgPct ? "OK" : "NG";   // N 回に満たない間は途中値のみ（NG 判定しない）
            if (nw != Judge)
            {
                string cf = Conf.ToString("0"), th = VisionCfg.NgPct.ToString();
                if (Judge == "WAIT" && nw == "OK") Ev(Dev + Lx.T(pn + "の晶線判定を開始しました。真偽度は" + cf + "%です。", pn + ": seam-line judgment has started. Confidence is " + cf + "%.", pn + "的晶线判定已开始。真伪度为" + cf + "%。"), 3, pn);
                else if (nw == "NG") Ev(Dev + Lx.T(pn + "の晶線の真偽度が" + cf + "%に下がりました。閾値" + th + "%を下回っています（検出" + Hit + "回／測定" + Cnt + "回）。", pn + ": seam-line confidence has dropped to " + cf + "%, below the " + th + "% threshold (" + Hit + " detected / " + Cnt + " measured).", pn + "的晶线真伪度已降到" + cf + "%，低于阈值" + th + "%（检出" + Hit + "次／测定" + Cnt + "次）。"), 2, pn);
                else Ev(Dev + Lx.T(pn + "の晶線の真偽度が" + cf + "%に戻りました（検出" + Hit + "回／測定" + Cnt + "回）。", pn + ": seam-line confidence has returned to " + cf + "% (" + Hit + " detected / " + Cnt + " measured).", pn + "的晶线真伪度已恢复到" + cf + "%（检出" + Hit + "次／测定" + Cnt + "次）。"), 3, pn);
                Judge = nw;
            }
            Hist.Add(!counting ? '-' : st == 'D' ? 'o' : st == 'N' ? 'n' : '-'); ConfHist.Add(Conf); while (Hist.Count > HistN) { Hist.RemoveAt(0); ConfHist.RemoveAt(0); }
        }

        // ---- BMP の読取りと表示用画像
        static int LE(byte[] b, int o) { return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24); }
        /// <summary>BMP（24／32bit 無圧縮）を読む。それ以外の形式は GDI+ で 24bit に変換する。</summary>
        static bool Decode(byte[] bmp, out byte[] px, out int off, out int w, out int h, out int stride, out bool bottomUp, out int bpp)
        {
            px = bmp; off = 0; w = h = stride = 0; bottomUp = false; bpp = 3;
            if (bmp.Length > 54 && bmp[0] == (byte)'B' && bmp[1] == (byte)'M')
            {
                int bits = bmp[28] | (bmp[29] << 8), comp = LE(bmp, 30); w = LE(bmp, 18); h = LE(bmp, 22); off = LE(bmp, 10); bottomUp = h > 0; h = Math.Abs(h);
                if (comp == 0 && (bits == 24 || bits == 32) && w > 0 && h > 0) { bpp = bits / 8; stride = ((w * bits + 31) / 32) * 4; if ((long)off + (long)stride * h <= bmp.Length) return true; }
            }
            using (MemoryStream ms = new MemoryStream(bmp)) using (Bitmap b = new Bitmap(ms))
            {
                w = b.Width; h = b.Height; BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try { stride = bd.Stride; px = new byte[stride * h]; Marshal.Copy(bd.Scan0, px, 0, px.Length); } finally { b.UnlockBits(bd); }
                off = 0; bottomUp = false; bpp = 3; return true;
            }
        }
        const int DispW = 370;
        static Bitmap MakeDisp(byte[] px, int off, int w, int h, int stride, bool bottomUp, int bpp, out double scale)
        {
            int dw = DispW, dh = Math.Max(1, (int)Math.Round(dw * (double)h / w)); scale = dw / (double)w; byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++) lut[i] = (byte)Math.Round(255 * Math.Pow(Math.Min(1.0, i / 255.0 * VisionIni.Gain), VisionIni.Gamma));
            int ds = ((dw * 3 + 3) / 4) * 4; byte[] o = new byte[ds * dh];
            for (int y = 0; y < dh; y++)
            {
                int y0 = y * h / dh, y1 = Math.Max(y0 + 1, (y + 1) * h / dh);
                for (int x = 0; x < dw; x++)
                {
                    int x0 = x * w / dw, x1 = Math.Max(x0 + 1, (x + 1) * w / dw), sb = 0, sg = 0, sr = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++) { int row = off + (bottomUp ? h - 1 - yy : yy) * stride; for (int xx = x0; xx < x1; xx++) { int k = row + xx * bpp; sb += px[k]; sg += px[k + 1]; sr += px[k + 2]; n++; } }
                    int d = y * ds + x * 3; o[d] = lut[sb / n]; o[d + 1] = lut[sg / n]; o[d + 2] = lut[sr / n];
                }
            }
            Bitmap b = new Bitmap(dw, dh, PixelFormat.Format24bppRgb); BitmapData bd = b.LockBits(new Rectangle(0, 0, dw, dh), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try { for (int y = 0; y < dh; y++) Marshal.Copy(o, y * ds, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), dw * 3); } finally { b.UnlockBits(bd); }
            return b;
        }
        static string ProcFromName(string name)
        {
            string n = Path.GetFileNameWithoutExtension(name).ToUpperInvariant(); int u = n.LastIndexOf('_'); if (u < 0) return null; string s = n.Substring(u + 1); return Array.IndexOf(ProcNames, s) >= 0 ? s : null;
        }
        static DateTime TimeFromName(string name, DateTime def)
        {
            string n = Path.GetFileName(name); DateTime t; return n.Length >= 14 && DateTime.TryParseExact(n.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : def;
        }

        /// <summary>画像 1 枚を処理する（解析スレッドから呼ぶ。検証モードでは直接呼ぶ）。</summary>
        public static VFrame ProcessImage(byte[] bmp, string name, DateTime time, bool makeDisp)
        {
            VFrame f = new VFrame(); f.Name = name; f.Time = time;
            byte[] px; int off, w, h, stride, bpp; bool bu;
            try { Decode(bmp, out px, out off, out w, out h, out stride, out bu, out bpp); }
            catch (Exception ex) { M.Log("VisionView: 画像を読めません " + name + " — " + ex.Message); return null; }
            int proc = CurrentProc(ProcFromName(name)); if (proc >= 0 && !VisionIni.ProcOn[proc]) proc = -1;
            f.Proc = proc; f.ProcName = proc >= 0 ? ProcNames[proc] : "";
            if (proc >= 0) { int ow, oh; double sc; byte[] red = VisionAnalyzer.Red(px, off, w, h, stride, bu, bpp, out ow, out oh, out sc); f.R = VisionAnalyzer.Analyze(red, ow, oh, sc, Param(VisionCfg.P[proc])); LastMs = f.R.Ms; }
            Bitmap disp = null; double dsc = 1; if (makeDisp) { try { disp = MakeDisp(px, off, w, h, stride, bu, bpp, out dsc); } catch (Exception ex) { M.Log("VisionView: 表示用画像の作成に失敗 — " + ex.Message); } }
            f.DispScale = dsc;
            lock (Lk)
            {
                JudgeFrame(f, CameraCfg.PlayMs); f.Disp = disp; VFrame old = Cur; Cur = f; Frames++;
                if (old != null && old.Disp != null) { try { old.Disp.Dispose(); } catch (Exception) { } }
            }
            return f;
        }

        // ---- 解析スレッド（最新の 1 枚だけを持つ。解析が間に合わないときは古い画像を飛ばす）
        class Job { public byte[] Bmp; public string Name; public DateTime Time; }
        static Job pending; static readonly object jobLk = new object(); static readonly AutoResetEvent jobEv = new AutoResetEvent(false); static Thread worker, feeder; static volatile bool stop; static ImageReceiver rx; static ImageStore store; static TcpListener simL;
        public static void Submit(byte[] bmp, string name, DateTime time) { lock (jobLk) { if (pending != null) Dropped++; Job j = new Job(); j.Bmp = bmp; j.Name = name; j.Time = time; pending = j; } jobEv.Set(); }
        static void Notify() { Control c = Ui; try { if (c != null && c.IsHandleCreated && !c.IsDisposed) c.BeginInvoke(new Action(delegate { FlushEvents(); M.Fire(); })); else FlushEvents(); } catch (Exception) { } }
        static void Work()
        {
            while (!stop)
            {
                jobEv.WaitOne(500); Job j; lock (jobLk) { j = pending; pending = null; }
                if (j == null || stop) continue;
                try { ProcessImage(j.Bmp, j.Name, j.Time, true); } catch (Exception ex) { M.Log("VisionView: 画像処理で例外 — " + ex); }
                if (Frames % 50 == 1) M.Log("VisionView: 処理 " + Frames + " 枚（解析 " + LastMs.ToString("0") + " ms、解析待ちで飛ばした画像 " + Dropped + " 枚）判定 " + Judge + (double.IsNaN(Conf) ? "" : " 真偽度 " + Conf.ToString("0") + "%"));
                Notify();
            }
        }
        static string Abs(string p) { return Path.IsPathRooted(p) ? p : Path.Combine(Ini.ExeDir, p); }
        static string[] PlayFiles() { string d = Abs(CameraCfg.PlayFolder); if (!Directory.Exists(d)) return new string[0]; List<string> l = new List<string>(); foreach (string f in Directory.GetFiles(d)) if (f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) l.Add(f); l.Sort(StringComparer.OrdinalIgnoreCase); return l.ToArray(); }
        static void Sleep(int ms) { DateTime e = DateTime.Now.AddMilliseconds(ms); while (!stop && DateTime.Now < e) Thread.Sleep(Math.Min(50, Math.Max(1, (int)(e - DateTime.Now).TotalMilliseconds))); }
        /// <summary>ファイル再生: 再生フォルダの BMP をファイル名順に一定間隔で解析へ渡す。</summary>
        static void PlayLoop()
        {
            int i = 0; string[] fs = PlayFiles();
            while (!stop)
            {
                if (fs.Length == 0 || i >= fs.Length)
                {
                    if (fs.Length > 0 && !CameraCfg.PlayLoop) { SrcStatus = Lx.T("再生終了", "Playback finished", "回放结束"); Sleep(500); continue; }
                    fs = PlayFiles(); i = 0; if (fs.Length == 0) { SrcStatus = Lx.T("再生フォルダに BMP がありません: ", "No BMP in playback folder: ", "回放文件夹中没有 BMP: ") + CameraCfg.PlayFolder; Notify(); Sleep(2000); continue; }
                }
                try { byte[] b = System.IO.File.ReadAllBytes(fs[i]); string n = Path.GetFileName(fs[i]); Submit(b, n, TimeFromName(n, DateTime.Now)); SrcStatus = Lx.T("ファイル再生 ", "Playback ", "文件回放 ") + (i + 1) + "/" + fs.Length; }
                catch (Exception ex) { M.Log("VisionView: 再生ファイルを読めません " + fs[i] + " — " + ex.Message); }
                i++; Sleep(CameraCfg.PlayMs);
            }
        }
        /// <summary>模擬カメラ: 再生フォルダの BMP を、画像出力ポートと同じ T1 電文で本機内（127.0.0.1）へ送る。</summary>
        static void SimLoop()
        {
            long n = 0;
            while (!stop)
            {
                try
                {
                    using (TcpClient c = simL.AcceptTcpClient())
                    {
                        NetworkStream st = c.GetStream(); string[] fs = PlayFiles(); int i = 0;
                        while (!stop && fs.Length > 0)
                        {
                            if (i >= fs.Length) { if (!CameraCfg.PlayLoop) { Sleep(500); continue; } i = 0; }
                            byte[] bmp = System.IO.File.ReadAllBytes(fs[i++]); int len = bmp.Length + 1; n++;
                            byte[] hd = Encoding.ASCII.GetBytes("T1," + Math.Max(1, CameraCfg.CameraNo) + ",0," + n.ToString("D10", CultureInfo.InvariantCulture) + "," + len.ToString("D7", CultureInfo.InvariantCulture) + ","), dl = Encoding.ASCII.GetBytes(CameraCfg.Delimiter == 1 ? "\r\n" : "\r");
                            st.Write(hd, 0, hd.Length); st.Write(bmp, 0, bmp.Length); st.WriteByte(Checksum.Calculate(bmp, 0, bmp.Length)); st.Write(dl, 0, dl.Length); Sleep(CameraCfg.PlayMs);
                        }
                        if (fs.Length == 0) Sleep(2000);
                    }
                }
                catch (Exception) { if (!stop) Sleep(500); }
            }
        }
        static CvSettings CamSettings(bool sim)
        {
            CvSettings s = new CvSettings(); s.Ip = sim ? "127.0.0.1" : CameraCfg.Ip[0] + "." + CameraCfg.Ip[1] + "." + CameraCfg.Ip[2] + "." + CameraCfg.Ip[3]; s.Port = CameraCfg.Port; s.CameraNo = CameraCfg.CameraNo; s.ReconnectSec = CameraCfg.ReconnectSec; s.TimeoutSec = CameraCfg.TimeoutSec;
            s.RejectChecksum = CameraCfg.RejectChecksum; s.RawDump = CameraCfg.RawDump; s.Save = CameraCfg.Save; s.Folder = CameraCfg.Folder; s.SaveEvery = CameraCfg.SaveMode == 1 ? Math.Max(1, CameraCfg.SaveEvery) : 1; s.MaxFiles = CameraCfg.MaxFiles; s.KeepDays = CameraCfg.KeepDays; s.CleanupHours = CameraCfg.CleanupHours;
            s.LogFolder = DataLog.Folder; int p = CurrentProc(null); s.Process = p >= 0 ? ProcNames[p] : ""; return s;
        }
        public static bool Running { get { return worker != null; } }
        public static void Start()
        {
            if (worker != null || !VisionCfg.Enable) { if (!VisionCfg.Enable) { Judge = "OFF"; SrcStatus = ""; } return; }
            stop = false; lock (Lk) { ResetJudge(-2); curProc = -2; Judge = "WAIT"; }
            worker = new Thread(Work); worker.IsBackground = true; worker.Name = "VisionWorker"; worker.Start();
            int src = VisionCfg.Source;
            if (src == 1) { feeder = new Thread(PlayLoop); feeder.IsBackground = true; feeder.Name = "VisionPlayback"; feeder.Start(); }
            else
            {
                bool sim = src == 2; FileLog lg = new FileLog(null); lg.Sink = delegate (string lv, string m) { M.Log("VisionView カメラ: " + m); };
                if (sim) { try { simL = new TcpListener(IPAddress.Loopback, CameraCfg.Port); simL.Start(); feeder = new Thread(SimLoop); feeder.IsBackground = true; feeder.Name = "VisionSimCam"; feeder.Start(); } catch (Exception ex) { M.Log("VisionView: 模擬カメラを開始できません（ポート " + CameraCfg.Port + "）— " + ex.Message); } }
                store = new ImageStore(delegate { return CamSettings(sim); }, lg, Ini.ExeDir); rx = new ImageReceiver(delegate { return CamSettings(sim); }, lg, Ini.ExeDir);
                rx.FrameReceived += delegate (ImageFrame fr)
                {
                    CvSettings s = CamSettings(sim); if (s.CameraNo != 0 && fr.CameraNo != s.CameraNo) return; if (!fr.ChecksumMatches && s.RejectChecksum) { store.Save(fr); return; }
                    string path = store.Save(fr); DateTime now = DateTime.Now; string name = path != null ? Path.GetFileName(path) : now.ToString("yyyyMMddHHmmss") + (s.Process == "" ? "" : "_" + s.Process) + ".bmp";
                    Submit(fr.ImageData, name, now);
                };
                rx.Start();
            }
            M.Log("VisionView 開始: source=" + src + " process_source=" + VisionCfg.ProcSource);
        }
        public static void Stop()
        {
            stop = true; jobEv.Set();
            try { if (rx != null) rx.Stop(); } catch (Exception) { } rx = null; store = null;
            try { if (simL != null) simL.Stop(); } catch (Exception) { } simL = null;
            try { if (feeder != null) feeder.Join(1500); } catch (Exception) { } feeder = null;
            try { if (worker != null) worker.Join(3000); } catch (Exception) { } worker = null; CamConnected = false;
        }
        public static void Restart() { Stop(); lock (Lk) { if (Cur != null && Cur.Disp != null) { try { Cur.Disp.Dispose(); } catch (Exception) { } } Cur = null; } Start(); }
        /// <summary>入力元の状態（設定画面・画面のコメント用）。</summary>
        public static string Status()
        {
            if (!VisionCfg.Enable) return Lx.T("晶線解析: OFF", "Seam analysis: OFF", "晶线解析: OFF");
            if (VisionCfg.Source == 1) return SrcStatus;
            ImageReceiver r = rx; CamConnected = r != null && r.Connected; string ep = (VisionCfg.Source == 2 ? "127.0.0.1" : CameraCfg.Ip[0] + "." + CameraCfg.Ip[1] + "." + CameraCfg.Ip[2] + "." + CameraCfg.Ip[3]) + ":" + CameraCfg.Port;
            return (CamConnected ? Lx.T("接続中  ", "Connected  ", "已连接  ") : Lx.T("未接続  ", "Not connected  ", "未连接  ")) + ep + (r != null ? Lx.T("　受信 ", "  received ", "　接收 ") + r.FramesReceived : "");
        }
        /// <summary>接続テスト（設定画面）。別スレッドで接続を試し、終わったら done を呼ぶ。受信中のカメラには接続しない。</summary>
        public static void TestConnect(Action done)
        {
            if (VisionCfg.Source == 0 && rx != null && rx.Connected) { TestMsg = Lx.T("受信中のため接続済みです", "Already connected (receiving)", "正在接收，已连接"); done(); return; }
            string ip = CameraCfg.Ip[0] + "." + CameraCfg.Ip[1] + "." + CameraCfg.Ip[2] + "." + CameraCfg.Ip[3]; int port = CameraCfg.Port, to = CameraCfg.TimeoutSec; TestMsg = Lx.T("接続テスト中…", "Testing…", "连接测试中…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (TcpClient c = new TcpClient())
                    {
                        IAsyncResult ar = c.BeginConnect(ip, port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(to * 1000)) { c.Close(); TestMsg = Lx.T("接続できません（タイムアウト " + to + " 秒）", "Cannot connect (timeout " + to + " s)", "无法连接（超时 " + to + " 秒）"); }
                        else { c.EndConnect(ar); TestMsg = Lx.T("接続できました ", "Connected ", "连接成功 ") + ip + ":" + port; }
                    }
                }
                catch (Exception ex) { TestMsg = Lx.T("接続できません: ", "Cannot connect: ", "无法连接: ") + ex.Message; }
                try { done(); } catch (Exception) { }
            });
        }
    }
}
