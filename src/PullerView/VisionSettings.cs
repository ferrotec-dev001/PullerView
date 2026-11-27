using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PullerView
{
    /// <summary>晶線解析（VisionView）の工程ごとの設定値。</summary>
    public class VisionProcCfg
    {
        public string Name; public int Contour;   // 0=brightness 1=edge 2=ridge
        public int Judge;                         // 0=single 1=pair
        public double Thr, ThrStrong, R1a, R1b, R2a, R2b; public bool Range2;
        public int Hidden;                        // 0=off 1=hold 2=predict
        public int HoldSec = 20, MarginDeg = 5, LostSec = 60; public bool Decided;
        public int Score = 1, Peak = 1; public double PartnerRatio = 0.6;   // V2-v14（輝度方式）: Score 0=abs 1=sigma（偏差）／Peak 0=all 1=top（最大の山＋晶線間隔の相手のみ）
        public double Sharp = 0, SharpSigma = 1.5;   // 鮮鋭化（V2-v13）: 強さ（0=なし）と、ぼかしの大きさ [幅 1276 換算の画素]
        public bool Roi, RoiAuto = true;          // 探索枠を使う／枠の中でメニスカスを自動検出する（V2-v11）
        public double Rx = 40, Ry = 40, Rw = 20, Rh = 20;   // 探索枠 [画像の幅・高さに対する %]
    }
    /// <summary>晶線解析の設定。VisionView.ini の読み書きは VisionIni（VisionView.cs）、解析は VisionAnalyzer.cs。既定は OFF（VisionView.ini の enable=1 で有効）。</summary>
    public static class VisionCfg
    {
        public static bool Enable = false; public static int Source = 1, ProcSource = 0, Orient = 0, TruthN = 10, NgPct = 50, DelaySec = 300, TolDeg = 8, Proc = 3;
        public static VisionProcCfg[] P = {
            Mk("NECK", 0, 0, 2.5, 2.5, 20, 175, 0, 0, false, 0, true), Mk("CROWN", 1, 1, 0.6, 0.8, -6, 186, 0, 0, false, 0, true), Mk("SHOULDER", 2, 0, 0.2, 0.2, 18, 62, 122, 160, true, 1, false),
            Mk("BODY", 2, 0, 0.2, 0.2, 18, 62, 122, 160, true, 1, true), Mk("TAIL", 2, 0, 0.2, 0.2, 18, 62, 122, 160, true, 1, false) };
        static VisionProcCfg Mk(string n, int c, int j, double t, double ts, double a, double b, double a2, double b2, bool r2, int h, bool dec)
        { VisionProcCfg p = new VisionProcCfg(); p.Name = n; p.Contour = c; p.Judge = j; p.Thr = t; p.ThrStrong = ts; p.R1a = a; p.R1b = b; p.R2a = a2; p.R2b = b2; p.Range2 = r2; p.Hidden = h; p.Decided = dec; return p; }
    }

    /// <summary>設定タブ「晶線解析設定」: 全体設定、真偽度・判定、工程ごとの検出設定。</summary>
    public class VisionSettingsPanel : DarkPanel
    {
        protected override void OnTouched() { M.Fire(); }
        void Set(Action a) { a(); Touch(); }
        void Card(Graphics g, Rectangle r, string title) { Box(g, r, CARD, null, 10, 1); Txt(g, title, 10f, WH, r.X + 16, r.Y + 10, true); using (Pen p = new Pen(LINE)) g.DrawLine(p, r.X + 16, r.Y + 32, r.Right - 16, r.Y + 32); }
        void Label(Graphics g, string s, int x, int y) { Txt(g, s, 8.5f, GR, x, y, false); }
        void TxtW(Graphics g, string s, float pt, Color c, Rectangle r) { using (Font f = F(pt, false)) using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r); }
        void StepBtns(Graphics g, int x, int y, NumField f)
        {
            string[] t = { "−", "＋" }; double[] d = { -1, 1 };
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = new Rectangle(x + i * 40, y, 36, 34); Box(g, r, Hover(r) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 8, 1); TxtC(g, t[i], 12f, WH, r, false);
                double dd = d[i] * f.Step;
                Add(r, delegate { double v = Math.Round(f.Get() + dd, 3); if (v < f.Min || v > f.Max) return; if (active == f) buf = ""; f.Set(v); Touch(); }, null);
            }
        }
        NumField Num(Graphics g, string key, string label, Func<double> get, Action<double> set, bool isInt, Rectangle r, double min, double max, double step, string suffix, bool enabled)
        {
            if (!enabled) { Box(g, r, CARD, LINE, 7, 1); Txt(g, "—", 11f, GR, r.X + 10, r.Y + r.Height / 2 - 10, false); return null; }
            NumField f = Field(key, label, get, set, isInt, r, min, max, step); NumBox(g, r, f, suffix); return f;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; BeginPaint(g);
            int colW = 340, midX = 364, midW = 336; bool en = VisionCfg.Enable;
            // ===== 左上: 晶線解析（全体） =====
            Rectangle c1 = new Rectangle(12, 12, colW, 288); Card(g, c1, Lx.T("晶線解析（全体）", "Seam analysis (general)", "晶线解析（全体）"));
            int x = c1.X + 16, y = c1.Y;
            Txt(g, Lx.T("晶線解析を使う", "Use seam analysis", "使用晶线解析"), 9.5f, WH, x, y + 44, false);
            Rectangle tg = new Rectangle(c1.Right - 16 - 56, y + 42, 56, 26); Toggle(g, tg, en); Add(tg, delegate { Set(delegate { VisionCfg.Enable = !VisionCfg.Enable; }); }, null);
            Label(g, Lx.T("画像入力元", "Image source", "图像输入源"), x, y + 80);
            Segment(g, x, y + 98, 32, new[] { Lx.T("カメラ", "Camera", "相机"), Lx.T("ファイル再生", "File playback", "文件回放"), Lx.T("模擬カメラ", "Simulator", "模拟相机") }, VisionCfg.Source, delegate (int i) { Set(delegate { VisionCfg.Source = i; }); }, 9f, 14);
            Label(g, Lx.T("工程情報", "Process source", "工程信息"), x, y + 140);
            Segment(g, x, y + 158, 32, new[] { Lx.T("自動（Remoting）", "Auto (Remoting)", "自动（Remoting）"), Lx.T("手動", "Manual", "手动"), Lx.T("ファイル名", "File name", "文件名") }, VisionCfg.ProcSource, delegate (int i) { Set(delegate { VisionCfg.ProcSource = i; }); }, 9f, 14);
            Label(g, Lx.T("結晶方位（晶線の本数と間隔）", "Crystal orientation (seam count / pitch)", "晶向（晶线的条数与间隔）"), x, y + 200);
            int sw = Segment(g, x, y + 218, 32, new[] { "<100>", "<111>", "<110>" }, VisionCfg.Orient, delegate (int i) { Set(delegate { VisionCfg.Orient = i; }); }, 9f, 16);
            string[] ot = { Lx.T("4 本・90°", "4 seams, 90°", "4 条・90°"), Lx.T("3 本・120°", "3 seams, 120°", "3 条・120°"), Lx.T("2 本・180°", "2 seams, 180°", "2 条・180°") };
            Txt(g, ot[VisionCfg.Orient], 9f, CY, x + sw + 8, y + 226, false);
            Txt(g, Lx.T("※ オフにすると右欄と判定表示を出しません", "※ Off: right pane and judgement are hidden", "※ 关闭时不显示右栏与判定"), 7.5f, GR, x, y + 262, false);
            // ===== 左下: 真偽度・判定 =====
            Rectangle c2 = new Rectangle(12, 312, colW, Height - 12 - 312); Card(g, c2, Lx.T("真偽度・判定", "Truth rate / judgement", "真伪度・判定"));
            y = c2.Y;
            Label(g, Lx.T("測定窓 N [回]", "Window N [times]", "测定窗口 N [次]"), x, y + 40); Label(g, Lx.T("NG 判定閾値 [%]", "NG threshold [%]", "NG 判定阈值 [%]"), x + 192, y + 40);
            NumField fn = Num(g, "vn", Lx.T("測定窓 N [回]", "Window N", "测定窗口 N [次]"), delegate { return VisionCfg.TruthN; }, delegate (double v) { VisionCfg.TruthN = (int)v; }, true, new Rectangle(x, y + 58, 96, 32), 1, 100, 1, Lx.T("回", "", "次"), true);
            StepBtns(g, x + 102, y + 58, fn);
            Num(g, "vng", Lx.T("NG 判定閾値 [%]", "NG threshold [%]", "NG 判定阈值 [%]"), delegate { return VisionCfg.NgPct; }, delegate (double v) { VisionCfg.NgPct = (int)v; }, true, new Rectangle(x + 192, y + 58, 112, 32), 0, 100, 5, "%", true);
            Label(g, Lx.T("検出開始時間 [秒]", "Start delay [s]", "检出开始时间 [秒]"), x, y + 102); Label(g, Lx.T("許容角度 [°]", "Tolerance [°]", "允许角度 [°]"), x + 192, y + 102);
            NumField fd = Num(g, "vdl", Lx.T("検出開始時間 [秒]", "Start delay [s]", "检出开始时间 [秒]"), delegate { return VisionCfg.DelaySec; }, delegate (double v) { VisionCfg.DelaySec = (int)v; }, true, new Rectangle(x, y + 120, 96, 32), 0, 3600, 30, "s", true);
            StepBtns(g, x + 102, y + 120, fd);
            Num(g, "vtol", Lx.T("許容角度 [°]", "Tolerance [°]", "允许角度 [°]"), delegate { return VisionCfg.TolDeg; }, delegate (double v) { VisionCfg.TolDeg = (int)v; }, true, new Rectangle(x + 192, y + 120, 112, 32), 1, 45, 1, "°", true);
            // 例（判定履歴のコマと同じ表現）
            Rectangle ex = new Rectangle(x, y + 166, colW - 32, 58); Box(g, ex, FIELD, null, 8, 1);
            int n = Math.Min(VisionCfg.TruthN, 20), okc = Math.Max(0, n - 1); float cw = (ex.Width - 16f) / Math.Max(10, n);
            for (int i = 0; i < n; i++) { Rectangle r = new Rectangle((int)(ex.X + 8 + cw * i), ex.Y + 8, (int)cw - 3, 16); Box(g, r, i == 1 && n > 1 ? RED : GRN, null, 3, 1); }
            int pct = n == 0 ? 0 : (int)Math.Round(100.0 * (n > 1 ? okc : n) / n); bool ok = pct >= VisionCfg.NgPct;
            Txt(g, Lx.T("例: 検出 ", "e.g. detected ", "例: 检出 ") + (n > 1 ? okc : n) + "/" + n + " → " + Lx.T("真偽度 ", "truth ", "真伪度 ") + pct + "% → " + (ok ? "OK" : "NG"), 8.5f, ok ? GRN : RED, ex.X + 8, ex.Y + 32, false);
            TxtW(g, Lx.T("真偽度＝直近 N 回の測定のうち検出できた割合。閾値を下回ると NG。工程切替でクリアし、検出開始時間が過ぎるまでは「待機」。", "Truth rate = share of detections in the last N measurements. Below the threshold = NG. Cleared on process change; “Standby” until the start delay elapses.", "真伪度＝最近 N 次测定中检出的比例。低于阈值为 NG。工程切换时清零，检出开始时间内显示「待机」。"), 7.5f, GR, new Rectangle(x, y + 232, colW - 32, 50));
            // ===== 中列: 工程ごとの検出設定 =====
            Rectangle c3 = new Rectangle(midX, 12, midW, Height - 24); Card(g, c3, Lx.T("工程ごとの検出設定", "Detection settings per process", "各工程的检出设定"));
            x = c3.X + 16; y = c3.Y; int pi = VisionCfg.Proc; VisionProcCfg pc = VisionCfg.P[pi]; string pk = "p" + pi;
            string[] pn = new string[VisionCfg.P.Length]; for (int i = 0; i < pn.Length; i++) pn[i] = VisionCfg.P[i].Name;
            Segment(g, x, y + 42, 30, pn, pi, delegate (int i) { active = null; VisionCfg.Proc = i; Invalidate(); }, 8.5f, 12);
            {   // 探索枠（V2-v11）: 別画面で設定する
                string rb = Lx.T("探索枠…", "Search frame…", "搜索框…") + (pc.Roi ? " ON" : " OFF"); int rbw = (int)TW(g, rb, 9f) + 22; Rectangle rr = new Rectangle(c3.Right - 16 - rbw, y + 5, rbw, 24);
                Box(g, rr, Hover(rr) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, pc.Roi ? CY : (Color?)null, 7, 1); TxtC(g, rb, 9f, pc.Roi ? CY : WH, rr, false);
                Add(rr, delegate { using (RoiForm f = new RoiForm(pi)) f.ShowDialog(FindForm()); Touch(); }, Lx.T("リング（メニスカス）を探す範囲を画像上で指定します", "Set the area in which the ring (meniscus) is searched", "在图像上指定搜索环（光圈）的范围"));
            }
            if (!pc.Decided) Txt(g, Lx.T("※ この工程の設定値は未定（仮の値）", "※ Values for this process are not fixed yet", "※ 该工程的设定值未定（暂定值）"), 7.5f, AMB, x, y + 76, false);
            Label(g, Lx.T("検出方式", "Method", "检出方式"), x, y + 92);
            Segment(g, x, y + 110, 30, new[] { Lx.T("輝度", "Brightness", "亮度"), Lx.T("輪郭 edge", "Contour edge", "轮廓 edge"), Lx.T("輪郭 ridge", "Contour ridge", "轮廓 ridge") }, pc.Contour, delegate (int i) { Set(delegate { pc.Contour = i; }); }, 9f, 16);
            if (pc.Contour == 0)
            {   // V2-v15: 輝度方式の評価方法（偏差）と山の選び方（最大の山＋晶線間隔の相手）
                string s1 = Lx.T("偏差（σ）で評価", "Score in σ", "按偏差（σ）评价"), s2 = Lx.T("最大の山と相手のみ", "Top peak + partner", "仅最大峰及对应峰");
                Txt(g, s1, 8.5f, GR, c3.Right - 16 - 46 - TW(g, s1, 8.5f) - 4, y + 92, false); Rectangle tA = new Rectangle(c3.Right - 16 - 44, y + 88, 44, 20); Toggle(g, tA, pc.Score == 1);
                Add(tA, delegate { Set(delegate { pc.Score = pc.Score == 1 ? 0 : 1; pc.Thr = pc.Score == 1 ? 2.5 : 1.0; }); }, Lx.T("検出値を画像ごとのばらつき σ の倍数で評価します。切り替えると検出閾値を目安の値（偏差 2.5／そのまま 1.0）に戻します", "Evaluates the feature in multiples of each image's spread σ. Switching resets the threshold to the guide value (σ 2.5 / raw 1.0)", "以每张图像的离散度 σ 的倍数评价检出值。切换时检出阈值恢复为参考值（偏差 2.5／原值 1.0）"));
                Txt(g, s2, 8.5f, GR, c3.Right - 16 - 46 - TW(g, s2, 8.5f) - 4, y + 150, false); Rectangle tB = new Rectangle(c3.Right - 16 - 44, y + 146, 44, 20); Toggle(g, tB, pc.Peak == 1);
                Add(tB, delegate { Set(delegate { pc.Peak = pc.Peak == 1 ? 0 : 1; }); }, Lx.T("最大の山が閾値以上のときだけ検出とし、ほかの山は晶線間隔だけ離れたものだけ残します", "Detects only when the highest peak exceeds the threshold; other peaks are kept only at the seam pitch from it", "仅当最大峰超过阈值时判为检出，其他峰只保留与其相隔晶线间隔的峰"));
            }
            Label(g, Lx.T("「晶線あり」の条件", "Seam-found condition", "「有晶线」的条件"), x, y + 150);
            Segment(g, x, y + 168, 30, new[] { Lx.T("1 本以上（single）", "1 or more (single)", "1 条以上（single）"), Lx.T("約 90° の 2 本（pair）", "2 at ~90° (pair)", "约 90° 的 2 条（pair）") }, pc.Judge, delegate (int i) { Set(delegate { pc.Judge = i; }); }, 9f, 14);
            Label(g, Lx.T("検出閾値", "Threshold", "检出阈值"), x, y + 208); bool prt = pc.Contour == 0 && pc.Peak == 1 && pc.Judge != 1; Label(g, prt ? Lx.T("相手の山の倍率", "Partner ratio", "对应峰倍率") : Lx.T("強い方の閾値（pair）", "Strong side (pair)", "强侧阈值（pair）"), x + 192, y + 208);
            NumField ft = Num(g, pk + "thr", Lx.T("検出閾値", "Threshold", "检出阈值"), delegate { return pc.Thr; }, delegate (double v) { pc.Thr = v; }, false, new Rectangle(x, y + 226, 96, 32), 0, 20, 0.1, null, true);
            StepBtns(g, x + 102, y + 226, ft);
            if (prt) Num(g, pk + "prt", Lx.T("相手の山の倍率", "Partner ratio", "对应峰倍率"), delegate { return pc.PartnerRatio; }, delegate (double v) { pc.PartnerRatio = v; }, false, new Rectangle(x + 192, y + 226, 112, 32), 0.1, 1, 0.1, null, true);
            else Num(g, pk + "ths", Lx.T("強い方の閾値", "Strong-side threshold", "强侧阈值"), delegate { return pc.ThrStrong; }, delegate (double v) { pc.ThrStrong = v; }, false, new Rectangle(x + 192, y + 226, 112, 32), 0, 20, 0.1, null, pc.Judge == 1);
            Label(g, Lx.T("検出範囲 [°]（開始 – 終了）", "Detection range [°] (start – end)", "检出范围 [°]（开始 – 结束）"), x, y + 270);
            Txt(g, Lx.T("2 区間", "2 ranges", "2 段"), 8.5f, GR, c3.Right - 16 - 46 - TW(g, Lx.T("2 区間", "2 ranges", "2 段"), 8.5f) - 4, y + 270, false);
            Rectangle t2 = new Rectangle(c3.Right - 16 - 44, y + 266, 44, 20); Toggle(g, t2, pc.Range2); Add(t2, delegate { Set(delegate { pc.Range2 = !pc.Range2; }); }, null);
            Num(g, pk + "r1a", Lx.T("範囲 1 開始 [°]", "Range 1 start", "范围 1 开始 [°]"), delegate { return pc.R1a; }, delegate (double v) { pc.R1a = v; }, true, new Rectangle(x, y + 290, 64, 32), -10, 190, 1, null, true);
            Txt(g, "–", 10f, GR, x + 66, y + 296, false);
            Num(g, pk + "r1b", Lx.T("範囲 1 終了 [°]", "Range 1 end", "范围 1 结束 [°]"), delegate { return pc.R1b; }, delegate (double v) { pc.R1b = v; }, true, new Rectangle(x + 78, y + 290, 64, 32), -10, 190, 1, null, true);
            Num(g, pk + "r2a", Lx.T("範囲 2 開始 [°]", "Range 2 start", "范围 2 开始 [°]"), delegate { return pc.R2a; }, delegate (double v) { pc.R2a = v; }, true, new Rectangle(x + 162, y + 290, 64, 32), -10, 190, 1, null, pc.Range2);
            Txt(g, "–", 10f, GR, x + 228, y + 296, false);
            Num(g, pk + "r2b", Lx.T("範囲 2 終了 [°]", "Range 2 end", "范围 2 结束 [°]"), delegate { return pc.R2b; }, delegate (double v) { pc.R2b = v; }, true, new Rectangle(x + 240, y + 290, 64, 32), -10, 190, 1, null, pc.Range2);
            // 検出範囲のプレビュー（左右は画像と同じ: 左が 180°）
            Rectangle pr = new Rectangle(x, y + 330, midW - 32, 52); Box(g, pr, FIELD, null, 8, 1);
            Func<double, float> X = delegate (double a) { return pr.X + 12 + (pr.Width - 24) * (float)((190 - a) / 200.0); };
            using (Pen p = new Pen(GR)) g.DrawLine(p, X(190), pr.Y + 30, X(-10), pr.Y + 30);
            for (int a = 0; a <= 180; a += 30) { using (Pen p = new Pen(GR)) g.DrawLine(p, X(a), pr.Y + 27, X(a), pr.Y + 33); TxtC(g, a.ToString(), 7f, GR, new RectangleF(X(a) - 16, pr.Y + 34, 32, 14), false); }
            double[][] rg = pc.Range2 ? new[] { new[] { pc.R1a, pc.R1b }, new[] { pc.R2a, pc.R2b } } : new[] { new[] { pc.R1a, pc.R1b } };
            if (pc.Range2) { float sx = X(Math.Max(pc.R1a, pc.R2a)), exx = X(Math.Min(pc.R1b, pc.R2b)); if (exx > sx) { using (SolidBrush b = new SolidBrush(Color.FromArgb(0x55, 0x55, 0x5C))) g.FillRectangle(b, sx, pr.Y + 10, exx - sx, 14); TxtC(g, Lx.T("解析しない", "not analysed", "不解析"), 7f, GR, new RectangleF(sx, pr.Y + 10, exx - sx, 14), false); } }
            foreach (double[] q in rg) { float a0 = X(Math.Max(q[0], q[1])), a1 = X(Math.Min(q[0], q[1])); using (SolidBrush b = new SolidBrush(CY)) g.FillRectangle(b, a0, pr.Y + 10, Math.Max(2, a1 - a0), 14); TxtC(g, q[0] + "–" + q[1], 7f, DK, new RectangleF(a0 - 20, pr.Y + 10, a1 - a0 + 40, 14), true); }
            Label(g, Lx.T("未検出の画像の扱い（シールド遮蔽）", "Undetected images (shield)", "未检出图像的处理（热屏遮挡）"), x, y + 392);
            Segment(g, x, y + 410, 30, new[] { Lx.T("NG に数える", "Count as NG", "计为 NG"), "hold", "predict" }, pc.Hidden, delegate (int i) { Set(delegate { pc.Hidden = i; }); }, 9f, 18);
            Label(g, Lx.T("保持時間 [秒]", "Hold [s]", "保持时间 [秒]"), x, y + 450); Label(g, Lx.T("端の余裕 [°]", "Edge margin [°]", "端部余量 [°]"), x + 104, y + 450); Label(g, Lx.T("推算打切り [秒]", "Predict limit [s]", "推算中止 [秒]"), x + 208, y + 450);
            Num(g, pk + "hold", Lx.T("保持時間 [秒]", "Hold [s]", "保持时间 [秒]"), delegate { return pc.HoldSec; }, delegate (double v) { pc.HoldSec = (int)v; }, true, new Rectangle(x, y + 468, 96, 32), 0, 600, 1, "s", pc.Hidden != 0);
            Num(g, pk + "mrg", Lx.T("端の余裕 [°]", "Edge margin [°]", "端部余量 [°]"), delegate { return pc.MarginDeg; }, delegate (double v) { pc.MarginDeg = (int)v; }, true, new Rectangle(x + 104, y + 468, 96, 32), 0, 45, 1, "°", pc.Hidden != 0);
            Num(g, pk + "lost", Lx.T("推算打切り [秒]", "Predict limit [s]", "推算中止 [秒]"), delegate { return pc.LostSec; }, delegate (double v) { pc.LostSec = (int)v; }, true, new Rectangle(x + 208, y + 468, 96, 32), 0, 3600, 5, "s", pc.Hidden == 2);
            TxtW(g, Lx.T("hold＝最後の検出から保持時間内の未検出は判定対象外（測定回数に数えない）。predict＝晶線の位置を推算し、遮蔽範囲内なら判定対象外。VisionView.ini [" + pc.Name + "]（キー名は仮）", "hold = undetected images within the hold time are excluded (not counted). predict = the seam position is estimated; excluded while inside the shielded range. VisionView.ini [" + pc.Name + "] (key names provisional)", "hold＝最后一次检出后保持时间内的未检出不判定（不计入测定次数）。predict＝推算晶线位置，位于遮挡范围内则不判定。VisionView.ini [" + pc.Name + "]（键名暂定）"), 7.5f, GR, new Rectangle(x, y + 510, midW - 32, 66));
            // ===== 右列: テンキー =====
            DrawNumPad(g, 712, Lx.T("・工程を切り替えると工程ごとの値を表示", "- Values are shown per selected process", "・切换工程后显示该工程的值"));
        }
    }
}
