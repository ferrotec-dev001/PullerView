// VisionView（晶線解析）の解析部。OpenCV を使わない C# だけの実装（.NET Framework 4.0／x86）。
// 画面・通信・判定から独立させてあり、OpenCV 版へ差し替える場合はこのファイルだけが対象になる。
//   輪郭方式 contour（edge／ridge）: Python 参照実装 contour_reference.py（2026-11-19）を移植。CROWN 中後期・BODY で使用
//   輝度方式 brightness             : VisionProbe v0.3（OpenCvSharp）の SeamAnalyzer を移植。NECK で使用
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PullerView
{
    /// <summary>晶線候補 1 本。Deg=特徴量の山の角度、Mark=×を付ける角度、X/Y=×の画像座標（元画像の画素）。</summary>
    public class VSeam { public double Deg, Mark, R, D; public double X, Y; }
    /// <summary>1 枚の解析結果。</summary>
    public class VResult
    {
        public bool Valid, Detected; public int Method;                 // Method: 0=brightness 1=edge 2=ridge
        public double Cx, Cy, A, B, Rot, Ms, Scale = 1;                 // リング（楕円）の中心・半径（元画像の画素）。Rot は brightness のみ
        public double[] Ang, D;                                        // 座標展開グラフ用: 角度 [°] と特徴量
        public List<VSeam> Seams = new List<VSeam>();                  // 判定に使う晶線候補
        public VSeam P1, P2;                                           // pair 判定で組になった 2 本
        public int W, H;
        /// <summary>画面に表示する晶線（pair なら組の 2 本、それ以外は候補すべて）。</summary>
        public List<VSeam> Shown() { List<VSeam> l = new List<VSeam>(); if (P1 != null) { l.Add(P1); l.Add(P2); } else if (Detected) l.AddRange(Seams); return l; }
    }
    /// <summary>解析の設定（工程ごと）。</summary>
    public class VParam
    {
        public int Contour = 1; public bool Pair = true; public double Thr = 0.6, ThrStrong = 0.8, Expect = 90, PairTol = 10;
        public double[][] Ranges = { new double[] { -6, 186 } };
        public bool Sigma, Top; public double PartnerRatio = 0.6;      // V2-v14（brightness）: Sigma=特徴量を画像ごとのばらつき σ で割る／Top=最大の山と、そこから晶線間隔だけ離れた山だけを採用
        public double Aspect = 0.93;                                   // リングの縦横比 b/a（カメラの見下ろし角）。contour 方式で固定値として使う
    }

    public static class VisionAnalyzer
    {
        const double STEP = 0.5; const int RefWidth = 1216;
        // ---------------------------------------------------------------- 画像の基本処理
        /// <summary>24bit BGR（下から上の BMP 画素列でも可）から赤チャンネルを取り出す。幅が基準の 1.5 倍を超える画像は 2×2 画素を平均して 1/2 にする。</summary>
        public static byte[] Red(byte[] bgr, int off, int w, int h, int stride, bool bottomUp, int bytesPp, out int ow, out int oh, out double scale)
        {
            bool bin = w > RefWidth * 1.5; scale = bin ? 2 : 1; ow = bin ? w / 2 : w; oh = bin ? h / 2 : h; byte[] r = new byte[ow * oh];
            for (int y = 0; y < oh; y++)
            {
                if (!bin) { int row = off + (bottomUp ? h - 1 - y : y) * stride + 2; for (int x = 0; x < ow; x++) r[y * ow + x] = bgr[row + x * bytesPp]; }
                else
                {
                    int r0 = off + (bottomUp ? h - 1 - 2 * y : 2 * y) * stride + 2, r1 = off + (bottomUp ? h - 2 - 2 * y : 2 * y + 1) * stride + 2;
                    for (int x = 0; x < ow; x++) { int o = 2 * x * bytesPp; r[y * ow + x] = (byte)((bgr[r0 + o] + bgr[r0 + o + bytesPp] + bgr[r1 + o] + bgr[r1 + o + bytesPp] + 2) >> 2); }
                }
            }
            return r;
        }
        static int Refl(int i, int n) { if (i < 0) i = -i; if (i >= n) i = 2 * n - 2 - i; return i < 0 ? 0 : i; }
        /// <summary>5×5 ガウシアン（[1 4 6 4 1]/16、端は折返し）。</summary>
        public static byte[] Blur5(byte[] s, int w, int h)
        {
            int[] t = new int[w * h]; byte[] d = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int o = y * w;
                for (int x = 2; x < w - 2; x++) t[o + x] = s[o + x - 2] + 4 * s[o + x - 1] + 6 * s[o + x] + 4 * s[o + x + 1] + s[o + x + 2];
                for (int k = 0; k < 4; k++) { int x = k < 2 ? k : w - 4 + k; if (x < 0 || x >= w) continue; t[o + x] = s[o + Refl(x - 2, w)] + 4 * s[o + Refl(x - 1, w)] + 6 * s[o + x] + 4 * s[o + Refl(x + 1, w)] + s[o + Refl(x + 2, w)]; }
            }
            for (int y = 0; y < h; y++)
            {
                int o = y * w, a = Refl(y - 2, h) * w, b = Refl(y - 1, h) * w, c = Refl(y + 1, h) * w, e = Refl(y + 2, h) * w;
                for (int x = 0; x < w; x++) d[o + x] = (byte)((t[a + x] + 4 * t[b + x] + 6 * t[o + x] + 4 * t[c + x] + t[e + x] + 128) >> 8);
            }
            return d;
        }
        // 横方向の移動最小／最大（窓 ±hw。画像の外は無視）
        // src の y0 行目から H 行分を処理し、dst の 0 行目から書く
        static void HMinMax(byte[] src, int y0, byte[] dst, int W, int H, int hw, bool isMin)
        {
            if (hw == 0) { Buffer.BlockCopy(src, y0 * W, dst, 0, W * H); return; }
            int w = 2 * hw + 1, n = W + 2 * hw; byte id = isMin ? (byte)255 : (byte)0; byte[] p = new byte[n], g = new byte[n], hh = new byte[n];
            for (int i = 0; i < n; i++) p[i] = id;
            for (int y = 0; y < H; y++)
            {
                Buffer.BlockCopy(src, (y0 + y) * W, p, hw, W);
                if (isMin)
                {
                    for (int i = 0; i < n; i++) g[i] = (i % w == 0 || p[i] < g[i - 1]) ? p[i] : g[i - 1];
                    for (int i = n - 1; i >= 0; i--) hh[i] = (i == n - 1 || i % w == w - 1 || p[i] < hh[i + 1]) ? p[i] : hh[i + 1];
                    int o = y * W; for (int x = 0; x < W; x++) { byte a = hh[x], b = g[x + w - 1]; dst[o + x] = a < b ? a : b; }
                }
                else
                {
                    for (int i = 0; i < n; i++) g[i] = (i % w == 0 || p[i] > g[i - 1]) ? p[i] : g[i - 1];
                    for (int i = n - 1; i >= 0; i--) hh[i] = (i == n - 1 || i % w == w - 1 || p[i] > hh[i + 1]) ? p[i] : hh[i + 1];
                    int o = y * W; for (int x = 0; x < W; x++) { byte a = hh[x], b = g[x + w - 1]; dst[o + x] = a > b ? a : b; }
                }
            }
        }
        /// <summary>楕円形の構造要素（直径 k）による収縮（isMin）／膨張。行ごとの横幅が違う移動最小／最大を縦に合成する。</summary>
        static byte[] Morph(byte[] src, int W, int H, int k, bool isMin)
        {
            int r = k / 2; Dictionary<int, List<int>> rows = new Dictionary<int, List<int>>();
            for (int dy = -r; dy <= r; dy++) { int dx = (int)Math.Round(r * Math.Sqrt((r * r - dy * dy) / (double)(r * r))); if (!rows.ContainsKey(dx)) rows[dx] = new List<int>(); rows[dx].Add(dy); }
            byte[] dst = new byte[W * H]; byte id = isMin ? (byte)255 : (byte)0;
            // 画像を横長の帯に分け、帯ごとに並列で処理する（結果は分けない場合と同じ）
            int strips = Math.Max(1, Math.Min(Environment.ProcessorCount, 8)), sh = (H + strips - 1) / strips;
            System.Threading.Tasks.Parallel.For(0, strips, delegate (int si)
            {
                int ys = si * sh, ye = Math.Min(H, ys + sh); if (ys >= ye) return;
                int t0 = Math.Max(0, ys - r), t1 = Math.Min(H, ye + r), th = t1 - t0; byte[] tmp = new byte[W * th];
                for (int i = ys * W; i < ye * W; i++) dst[i] = id;
                foreach (KeyValuePair<int, List<int>> kv in rows)
                {
                    HMinMax(src, t0, tmp, W, th, kv.Key, isMin);
                    foreach (int dy in kv.Value)
                    {
                        int y0 = Math.Max(ys, -dy), y1 = Math.Min(ye, H - dy);
                        for (int y = y0; y < y1; y++)
                        {
                            int o = y * W, q = (y + dy - t0) * W;
                            if (isMin) { for (int x = 0; x < W; x++) if (tmp[q + x] < dst[o + x]) dst[o + x] = tmp[q + x]; }
                            else { for (int x = 0; x < W; x++) if (tmp[q + x] > dst[o + x]) dst[o + x] = tmp[q + x]; }
                        }
                    }
                }
            });
            return dst;
        }
        /// <summary>トップハット（元画像 − オープニング）。細い明線（リング）を取り出す。</summary>
        public static byte[] TopHat(byte[] g, int W, int H, int k)
        {
            byte[] o = Morph(Morph(g, W, H, k, true), W, H, k, false), t = new byte[W * H];
            for (int i = 0; i < t.Length; i++) { int v = g[i] - o[i]; t[i] = (byte)(v < 0 ? 0 : v); }
            return t;
        }
        /// <summary>8 連結の連結成分。lab に 1 から始まるラベル、戻り値は各ラベルの画素数（[0] は未使用）。</summary>
        static List<int> Label(bool[] bw, int W, int H, int[] lab)
        {
            List<int> area = new List<int>(); area.Add(0); int[] st = new int[W * H]; 
            for (int s = 0; s < bw.Length; s++)
            {
                if (!bw[s] || lab[s] != 0) continue;
                int id = area.Count, n = 0, sp = 0; st[sp++] = s; lab[s] = id;
                while (sp > 0)
                {
                    int p = st[--sp]; n++; int px = p % W, py = p / W;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = px + dx, y = py + dy; if (x < 0 || y < 0 || x >= W || y >= H) continue; int q = y * W + x;
                        if (bw[q] && lab[q] == 0) { lab[q] = id; st[sp++] = q; }
                    }
                }
                area.Add(n);
            }
            return area;
        }
        static bool Solve(double[,] m, double[] v, int n)
        {
            for (int c = 0; c < n; c++)
            {
                int pv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[pv, c])) pv = r;
                if (Math.Abs(m[pv, c]) < 1e-300) return false;
                if (pv != c) { for (int k = 0; k < n; k++) { double t = m[c, k]; m[c, k] = m[pv, k]; m[pv, k] = t; } double t2 = v[c]; v[c] = v[pv]; v[pv] = t2; }
                for (int r = 0; r < n; r++) { if (r == c) continue; double f = m[r, c] / m[c, c]; if (f == 0) continue; for (int k = c; k < n; k++) m[r, k] -= f * m[c, k]; v[r] -= f * v[c]; }
            }
            for (int c = 0; c < n; c++) v[c] /= m[c, c];
            return true;
        }
        /// <summary>最小二乗（正規方程式）。rows[i] が 1 行分の係数、rhs[i] が右辺。</summary>
        static double[] Lsq(List<double[]> rows, List<double> rhs, int n)
        {
            double[,] m = new double[n, n]; double[] v = new double[n];
            for (int i = 0; i < rows.Count; i++) { double[] a = rows[i]; for (int r = 0; r < n; r++) { v[r] += a[r] * rhs[i]; for (int c = 0; c < n; c++) m[r, c] += a[r] * a[c]; } }
            return Solve(m, v, n) ? v : null;
        }
        static double Median(List<double> v) { if (v.Count == 0) return double.NaN; double[] s = v.ToArray(); Array.Sort(s); int n = s.Length; return n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]); }
        static double Percentile(double[] v, double p) { double[] s = (double[])v.Clone(); Array.Sort(s); double pos = (s.Length - 1) * p / 100.0; int i = (int)Math.Floor(pos); double f = pos - i; return i + 1 < s.Length ? s[i] + (s[i + 1] - s[i]) * f : s[i]; }

        // ---------------------------------------------------------------- 輪郭方式（contour）
        class Pol { public double[] Ang, R; public float[] V; public bool[] Ins; public int Na, Nr; }
        /// <summary>縦横比を固定した楕円（x 方向の半径 a、y 方向 a×q）のあてはめ。</summary>
        static bool Fit(List<double> xs, List<double> ys, double q, out double cx, out double cy, out double a)
        {
            cx = cy = a = 0; int n = xs.Count; if (n < 3) return false; double x0 = 0, y0 = 0;
            for (int i = 0; i < n; i++) { x0 += xs[i]; y0 += ys[i] / q; } x0 /= n; y0 /= n;
            List<double[]> rows = new List<double[]>(n); List<double> rhs = new List<double>(n);
            for (int i = 0; i < n; i++) { double xa = xs[i] - x0, ya = ys[i] / q - y0; rows.Add(new double[] { xa, ya, 1 }); rhs.Add(xa * xa + ya * ya); }
            double[] p = Lsq(rows, rhs, 3); if (p == null) return false;
            double c0 = p[0] / 2, c1 = p[1] / 2; a = Math.Sqrt(Math.Max(p[2] + c0 * c0 + c1 * c1, 1)); cx = c0 + x0; cy = (c1 + y0) * q; return true;
        }
        /// <summary>リングに沿った極座標展開（角度 −60〜240°・0.5°刻み × 相対半径 1±band）。双一次補間、画像の外は 0。</summary>
        static Pol Unwrap(byte[] img, int W, int H, double cx, double cy, double a, double b, double band, int nr)
        {
            Pol P = new Pol(); int na = 601; P.Na = na; P.Nr = nr; P.Ang = new double[na]; P.R = new double[nr]; P.V = new float[na * nr]; P.Ins = new bool[na * nr];
            double[] cs = new double[na], sn = new double[na];
            for (int j = 0; j < na; j++) { P.Ang[j] = -60 + j * STEP; double d = P.Ang[j] * Math.PI / 180; cs[j] = Math.Cos(d); sn[j] = Math.Sin(d); }
            for (int i = 0; i < nr; i++) P.R[i] = (1 - band) + 2 * band * i / (nr - 1);
            for (int i = 0; i < nr; i++) for (int j = 0; j < na; j++)
            {
                float mx = (float)(cx + cs[j] * a * P.R[i]), my = (float)(cy + sn[j] * b * P.R[i]); int k = i * na + j;
                P.Ins[k] = mx >= 1 && mx < W - 2 && my >= 1 && my < H - 2;
                int ix = (int)Math.Floor(mx), iy = (int)Math.Floor(my); double fx = mx - ix, fy = my - iy, v = 0;   // 双一次補間（参照実装が使った OpenCV 5.0 の remap と同じく、座標は丸めない）
                if (ix >= 0 && iy >= 0 && ix < W && iy < H) v += img[iy * W + ix] * (1 - fx) * (1 - fy);
                if (ix + 1 >= 0 && iy >= 0 && ix + 1 < W && iy < H) v += img[iy * W + ix + 1] * fx * (1 - fy);
                if (ix >= 0 && iy + 1 >= 0 && ix < W && iy + 1 < H) v += img[(iy + 1) * W + ix] * (1 - fx) * fy;
                if (ix + 1 >= 0 && iy + 1 >= 0 && ix + 1 < W && iy + 1 < H) v += img[(iy + 1) * W + ix + 1] * fx * fy;
                P.V[k] = (float)Math.Floor(v + 0.5);
            }
            return P;
        }
        /// <summary>ridge: 明線の重心の相対半径。</summary>
        static void Ridge(byte[] th, int W, int H, double cx, double cy, double a, double b, double band, int nr, out double[] ang, out double[] rr, out bool[] ok)
        {
            Pol P = Unwrap(th, W, H, cx, cy, a, b, band, nr); int na = P.Na; ang = P.Ang; rr = new double[na]; ok = new bool[na]; double[] pk = new double[na]; bool[] all = new bool[na];
            for (int j = 0; j < na; j++)
            {
                double m = 0; bool al = true; for (int i = 0; i < nr; i++) { int k = i * na + j; if (!P.Ins[k]) { al = false; P.V[k] = 0; } else if (P.V[k] > m) m = P.V[k]; }
                double sw = 0, sr = 0; for (int i = 0; i < nr; i++) { double w = P.V[i * na + j] - 0.5 * m; if (w > 0) { sw += w; sr += w * P.R[i]; } }
                pk[j] = m; all[j] = al; rr[j] = sr / Math.Max(sw, 1e-6);
            }
            double lim = 0.3 * Percentile(pk, 90); for (int j = 0; j < na; j++) ok[j] = pk[j] >= lim && all[j];
        }
        static readonly double[] G7 = { 0.03125, 0.109375, 0.21875, 0.28125, 0.21875, 0.109375, 0.03125 };
        /// <summary>edge: リング外側の縁（外向きに明るさが最も急に落ちる位置）の相対半径。</summary>
        static void Edge(byte[] g, int W, int H, double cx, double cy, double a, double b, double band, int nr, out double[] ang, out double[] rr, out bool[] ok)
        {
            Pol P = Unwrap(g, W, H, cx, cy, a, b, band, nr); int na = P.Na; ang = P.Ang; rr = new double[na]; ok = new bool[na]; double[] st = new double[na]; int[] im = new int[na]; bool[] all = new bool[na];
            double[] col = new double[nr], neg = new double[nr];
            for (int j = 0; j < na; j++)
            {
                bool al = true;
                for (int i = 0; i < nr; i++) { double s = 0; for (int t = -3; t <= 3; t++) s += G7[t + 3] * P.V[Refl(i + t, nr) * na + j]; col[i] = s; if (!P.Ins[i * na + j]) al = false; }
                int bi = 0; double bv = 0;
                for (int i = 0; i < nr; i++) { double gr = (i == 0 || i == nr - 1) ? 0 : (col[i + 1] - col[i - 1]) * 0.5; double v = (-gr > 0 && P.Ins[i * na + j]) ? -gr : 0; neg[i] = v; if (v > bv) { bv = v; bi = i; } }
                double sw = 0, sr = 0; for (int i = Math.Max(0, bi - 4); i <= Math.Min(nr - 1, bi + 4); i++) { sw += neg[i]; sr += neg[i] * P.R[i]; }
                st[j] = bv; im[j] = bi; all[j] = al; rr[j] = sr / Math.Max(sw, 1e-6);
            }
            double lim = 0.3 * Percentile(st, 90); for (int j = 0; j < na; j++) ok[j] = all[j] && st[j] >= lim && im[j] > 4 && im[j] < nr - 5;
        }
        /// <summary>段差特徴量 D(θ)[%] = | d(θ) − 周囲 ±8°の中央値 |、d(θ) = 右 2°の平均半径 − 左 2°の平均半径。</summary>
        static double[] DFeat(double[] rr, bool[] ok)
        {
            int n = rr.Length; double[] D = new double[n]; for (int i = 0; i < n; i++) D[i] = double.NaN;
            int s = (int)(2.0 / STEP), g = (int)(0.5 / STEP), h = (int)(8.0 / STEP), t = Math.Max((int)(2.0 / STEP), g + s);
            int i0 = 0;
            while (i0 < n)
            {
                if (!ok[i0]) { i0++; continue; }
                int i1 = i0; while (i1 < n && ok[i1]) i1++;
                int m = i1 - i0;
                if (m * STEP >= 12.0)
                {
                    double[] c = new double[m + 1], d = new double[m]; for (int i = 0; i < m; i++) { c[i + 1] = c[i] + rr[i0 + i] * 100; d[i] = double.NaN; }
                    for (int i = g + s; i < m - g - s; i++) d[i] = (c[i + g + s + 1] - c[i + g + 1]) / s - (c[i - g] - c[i - g - s]) / s;
                    List<double> v = new List<double>();
                    for (int i = t; i < m - t; i++)
                    {
                        v.Clear(); for (int k = Math.Max(0, i - h); k < Math.Min(m, i + h + 1); k++) if (!double.IsNaN(d[k])) v.Add(d[k]);
                        if (v.Count >= h) D[i0 + i] = Math.Abs(d[i] - Median(v));
                    }
                }
                i0 = i1;
            }
            return D;
        }
        class Loc { public byte[] G, Th; public int W, H; public double Cx, Cy, A, B; }
        static Loc Locate(byte[] red, int W, int H, double q)
        {
            Loc L = new Loc(); L.W = W; L.H = H; L.G = Blur5(red, W, H); L.Th = TopHat(L.G, W, H, 31);
            int mx = 0; for (int i = 0; i < L.Th.Length; i++) if (L.Th[i] > mx) mx = L.Th[i];
            int t = (int)Math.Floor(Math.Max(2.0, mx * 0.35)); bool[] bw = new bool[W * H]; for (int i = 0; i < bw.Length; i++) bw[i] = L.Th[i] > t;
            int[] lab = new int[W * H]; List<int> area = Label(bw, W, H, lab); if (area.Count < 2) return null;
            int am = 0; for (int i = 1; i < area.Count; i++) if (area[i] > am) am = area[i];
            List<double> xs = new List<double>(), ys = new List<double>();
            for (int i = 0; i < lab.Length; i++) if (lab[i] > 0 && area[lab[i]] >= 0.2 * am) { xs.Add(i % W); ys.Add(i / W); }
            if (xs.Count < 20) return null;
            double cx, cy, a; if (!Fit(xs, ys, q, out cx, out cy, out a)) return null; double b = a * q;
            for (int it = 0; it < 3; it++)
            {
                double[] ang, rr; bool[] ok; Ridge(L.Th, W, H, cx, cy, a, b, it == 0 ? 0.12 : 0.06, 121, out ang, out rr, out ok);
                List<double> r = new List<double>(); List<int> ix = new List<int>(); for (int j = 0; j < ang.Length; j++) if (ok[j]) { r.Add(rr[j]); ix.Add(j); }
                if (r.Count < 20) break;
                double med = Median(r); List<double> ad = new List<double>(); foreach (double v in r) ad.Add(Math.Abs(v - med)); double lim = 3.5 * 1.4826 * Median(ad) + 0.002;
                xs.Clear(); ys.Clear();
                for (int k = 0; k < r.Count; k++) if (Math.Abs(r[k] - med) < lim) { double d = ang[ix[k]] * Math.PI / 180; xs.Add(cx + Math.Cos(d) * a * r[k]); ys.Add(cy + Math.Sin(d) * b * r[k]); }
                double nx, ny, na; if (!Fit(xs, ys, q, out nx, out ny, out na)) break; cx = nx; cy = ny; a = na; b = a * q;
            }
            L.Cx = cx; L.Cy = cy; L.A = a; L.B = b; return L;
        }
        static VResult Contour(byte[] red, int W, int H, VParam c)
        {
            VResult R = new VResult(); R.Method = c.Contour; Loc L = Locate(red, W, H, c.Aspect); if (L == null) return R;
            double[] ang, rr; bool[] ok; int n;
            if (c.Contour == 1)
            {
                Edge(L.G, W, H, L.Cx, L.Cy, L.A, L.B, 0.06, 121, out ang, out rr, out ok); n = ang.Length;
                List<double> v = new List<double>(); for (int j = 0; j < n; j++) if (ok[j]) v.Add(rr[j]);
                if (v.Count > 0) { double med = Median(v); for (int j = 0; j < n; j++) rr[j] /= med; }
                for (int j = 0; j < n; j++) ok[j] = ok[j] && rr[j] > 0.985 && rr[j] < 1.06;
            }
            else { Ridge(L.Th, W, H, L.Cx, L.Cy, L.A, L.B, 0.06, 121, out ang, out rr, out ok); n = ang.Length; for (int j = 0; j < n; j++) ok[j] = ok[j] && rr[j] > 0.988 && rr[j] < 1.05; }
            double[] D = DFeat(rr, ok);
            for (int j = 0; j < n; j++) { bool inr = false; foreach (double[] rg in c.Ranges) if (ang[j] >= rg[0] && ang[j] <= rg[1]) inr = true; if (!inr) D[j] = double.NaN; }
            int[] ord = new int[n]; double[] key = new double[n]; for (int j = 0; j < n; j++) { ord[j] = j; key[j] = double.IsNaN(D[j]) ? 9 : -D[j]; }
            Array.Sort(key, ord);
            foreach (int j in ord)
            {
                if (double.IsNaN(D[j]) || D[j] < c.Thr) break;
                bool far = true; foreach (VSeam s in R.Seams) if (Math.Abs(ang[j] - s.Deg) <= 8.0) far = false; if (!far) continue;
                int k0 = Math.Max(0, j - 6), k1 = Math.Min(n - 1, j + 6), k = j;
                if (c.Contour == 1) { double best = double.NegativeInfinity; for (int i = k0; i <= k1; i++) if (ok[i] && rr[i] > best) { best = rr[i]; k = i; } }
                else
                {   // ridge: 山の近く ±3°で、輪郭が周囲 ±8°の中央値から最も離れている点＝凹凸の頂点
                    List<double> v = new List<double>(); for (int i = Math.Max(0, j - 16); i <= Math.Min(n - 1, j + 16); i++) if (ok[i]) v.Add(rr[i]);
                    double bs = v.Count > 0 ? Median(v) : rr[j], best = -1; for (int i = k0; i <= k1; i++) { double dv = ok[i] ? Math.Abs(rr[i] - bs) : -1.0; if (dv > best) { best = dv; k = i; } }
                }
                VSeam sm = new VSeam(); sm.Deg = ang[j]; sm.Mark = ang[k]; sm.R = rr[k]; sm.D = D[j]; double dd = sm.Mark * Math.PI / 180; sm.X = L.Cx + Math.Cos(dd) * L.A * sm.R; sm.Y = L.Cy + Math.Sin(dd) * L.B * sm.R; R.Seams.Add(sm);
            }
            R.Seams.Sort(delegate (VSeam x, VSeam y) { return x.Deg.CompareTo(y.Deg); });
            double ts = c.ThrStrong > 0 ? c.ThrStrong : c.Thr, bestSum = -1;
            for (int i = 0; i < R.Seams.Count; i++) for (int u = i + 1; u < R.Seams.Count; u++)
            {
                VSeam s = R.Seams[i], w = R.Seams[u];
                if (Math.Abs(w.Deg - s.Deg - c.Expect) <= c.PairTol && Math.Max(s.D, w.D) >= ts && s.D + w.D > bestSum) { bestSum = s.D + w.D; if (c.Pair) { R.P1 = s; R.P2 = w; } }
            }
            R.Detected = c.Pair ? bestSum >= 0 : R.Seams.Count > 0;
            R.Valid = true; R.Cx = L.Cx; R.Cy = L.Cy; R.A = L.A; R.B = L.B; R.Ang = ang; R.D = D; return R;
        }

        // ---------------------------------------------------------------- 輝度方式（brightness）
        static int Otsu(byte[] g, int W, int x0, int y0, int x1, int y1)
        {
            double[] hst = new double[256]; double n = 0; for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { hst[g[y * W + x]]++; n++; }
            double mu = 0; for (int i = 0; i < 256; i++) mu += i * hst[i] / n; double q1 = 0, mu1 = 0, best = 0; int thr = 0; const double eps = 1.1920929e-7;
            for (int i = 0; i < 256; i++)
            {   // OpenCV の大津の方法（getThreshVal_Otsu_8u）と同じ手順
                double p = hst[i] / n; mu1 *= q1; q1 += p; double q2 = 1 - q1; if (Math.Min(q1, q2) < eps || Math.Max(q1, q2) > 1 - eps) continue;
                mu1 = (mu1 + i * p) / q1; double mu2 = (mu - q1 * mu1) / q2, sg = q1 * q2 * (mu1 - mu2) * (mu1 - mu2); if (sg > best) { best = sg; thr = i; }
            }
            return thr;
        }
        static double Cross(double[] o, double[] a, double[] b) { return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]); }
        /// <summary>楕円のあてはめ（OpenCV の fitEllipse と同じ 2 段の最小二乗）。戻り値 false＝あてはめ不能。</summary>
        static bool FitEllipse(List<double[]> pts, out double cx, out double cy, out double a, out double b, out double rot)
        {
            cx = cy = a = b = rot = 0; int n = pts.Count; if (n < 5) return false; double mx = 0, my = 0; foreach (double[] p in pts) { mx += p[0]; my += p[1]; } mx /= n; my /= n;
            List<double[]> rows = new List<double[]>(); List<double> rhs = new List<double>();
            foreach (double[] p in pts) { double x = p[0] - mx, y = p[1] - my; rows.Add(new double[] { -x * x, -y * y, -x * y, x, y }); rhs.Add(10000.0); }
            double[] g = Lsq(rows, rhs, 5); if (g == null) return false;
            double[,] m2 = { { 2 * g[0], g[2] }, { g[2], 2 * g[1] } }; double[] c = { g[3], g[4] }; if (!Solve(m2, c, 2)) return false;
            rows.Clear(); rhs.Clear(); foreach (double[] p in pts) { double x = p[0] - mx - c[0], y = p[1] - my - c[1]; rows.Add(new double[] { x * x, y * y, x * y }); rhs.Add(1.0); }
            double[] f = Lsq(rows, rhs, 3); if (f == null) return false;
            double r4 = -0.5 * Math.Atan2(f[2], f[1] - f[0]), t = Math.Abs(f[2]) > 1e-8 ? f[2] / Math.Sin(-2.0 * r4) : f[1] - f[0];
            double r2 = Math.Abs(f[0] + f[1] - t); if (r2 > 1e-8) r2 = Math.Sqrt(2.0 / r2); double r3 = Math.Abs(f[0] + f[1] + t); if (r3 > 1e-8) r3 = Math.Sqrt(2.0 / r3);
            cx = c[0] + mx; cy = c[1] + my; double w = r2 * 2, h = r3 * 2; rot = r4 * 180 / Math.PI;
            if (w > h) { double s = w; w = h; h = s; rot = 90 + r4 * 180 / Math.PI; }
            if (rot < -180) rot += 360; if (rot > 360) rot -= 360;
            a = w / 2; b = h / 2; return a > 1 && b > 1 && !double.IsNaN(a) && !double.IsNaN(b);
        }
        static VResult Brightness(byte[] red, int W, int H, VParam c)
        {
            VResult R = new VResult(); R.Method = 0; byte[] g = Blur5(red, W, H); int N = W * H;
            long[] hist = new long[256]; for (int i = 0; i < N; i++) hist[g[i]]++;
            int med = 255, top = 255; long cs = 0; bool m1 = false, m2 = false;
            for (int v = 0; v < 256; v++) { cs += hist[v]; if (!m1 && cs >= N * 0.5) { med = v; m1 = true; } if (!m2 && cs >= N * 0.999) { top = v; m2 = true; } }
            int t0 = (int)Math.Floor((med + top) / 2.0); bool[] bw = new bool[N]; for (int i = 0; i < N; i++) bw[i] = g[i] > t0;
            int[] lab = new int[N]; List<int> area = Label(bw, W, H, lab); if (area.Count < 2) return R;
            int id = 1; for (int i = 2; i < area.Count; i++) if (area[i] > area[id]) id = i;
            int bx0 = W, by0 = H, bx1 = -1, by1 = -1; for (int i = 0; i < N; i++) if (lab[i] == id) { int x = i % W, y = i / W; if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; if (y < by0) by0 = y; if (y > by1) by1 = y; }
            int bwid = bx1 - bx0 + 1, bhei = by1 - by0 + 1, mg = (int)(Math.Max(bwid, bhei) * 0.25);
            int X0 = Math.Max(0, bx0 - mg), Y0 = Math.Max(0, by0 - mg), X1 = Math.Min(W, bx0 + bwid + mg), Y1 = Math.Min(H, by0 + bhei + mg);
            int ot = Otsu(g, W, X0, Y0, X1, Y1); int rw = X1 - X0, rh = Y1 - Y0; bool[] b2 = new bool[rw * rh]; for (int y = 0; y < rh; y++) for (int x = 0; x < rw; x++) b2[y * rw + x] = g[(Y0 + y) * W + X0 + x] > ot;
            int[] l2 = new int[rw * rh]; List<int> a2 = Label(b2, rw, rh, l2); if (a2.Count < 2) return R; id = 1; for (int i = 2; i < a2.Count; i++) if (a2[i] > a2[id]) id = i;
            // リング（最大の成分）の外形: 行ごとの左端・右端 → 凸包
            List<double[]> bd = new List<double[]>(); int cy0 = rh, cy1 = -1;
            for (int y = 0; y < rh; y++) { int lo = -1, hi = -1; for (int x = 0; x < rw; x++) if (l2[y * rw + x] == id) { if (lo < 0) lo = x; hi = x; } if (lo >= 0) { bd.Add(new double[] { X0 + lo, Y0 + y }); if (hi != lo) bd.Add(new double[] { X0 + hi, Y0 + y }); if (y < cy0) cy0 = y; if (y > cy1) cy1 = y; } }
            if (bd.Count < 5) return R;
            bd.Sort(delegate (double[] p, double[] q) { int k = p[0].CompareTo(q[0]); return k != 0 ? k : p[1].CompareTo(q[1]); });
            List<double[]> hull = new List<double[]>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < bd.Count; i++) { double[] p = pass == 0 ? bd[i] : bd[bd.Count - 1 - i]; while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
                hull.RemoveAt(hull.Count - 1);
            }
            double ycut = (Y0 + cy0) + (cy1 - cy0 + 1) * 0.25; List<double[]> hp = new List<double[]>(); foreach (double[] p in hull) if (p[1] > ycut) hp.Add(p);
            double ecx, ecy, ea, eb, rot; if (!FitEllipse(hp, out ecx, out ecy, out ea, out eb, out rot)) return R;
            R.Cx = ecx; R.Cy = ecy; R.A = ea; R.B = eb; R.Rot = rot;
            double th = rot * Math.PI / 180.0; double[] ux = new double[360], uy = new double[360], rad = new double[360];
            for (int d = 0; d < 360; d++) { double t = d * Math.PI / 180.0; ux[d] = Math.Cos(t); uy[d] = Math.Sin(t); double vx = ux[d] * Math.Cos(th) + uy[d] * Math.Sin(th), vy = -ux[d] * Math.Sin(th) + uy[d] * Math.Cos(th); rad[d] = 1.0 / Math.Sqrt((vx / ea) * (vx / ea) + (vy / eb) * (vy / eb)); }
            const int nr = 96, TangWin = 31, Smooth = 5, BaseWin = 61, MinWidth = 2; const double RIn = 0.62, ROut = 1.12;
            byte[] pol = new byte[nr * 360]; bool[] pb = new bool[nr * 360];
            for (int i = 0; i < nr; i++)
            {
                double Rr = RIn + (ROut - RIn) * i / (nr - 1);
                for (int d = 0; d < 360; d++)
                {
                    float mx = (float)(ecx + ux[d] * rad[d] * Rr), my = (float)(ecy + uy[d] * rad[d] * Rr);
                    int ix = (int)Math.Floor(mx), iy = (int)Math.Floor(my); double fx = mx - ix, fy = my - iy, v = 0;
                    if (ix >= 0 && iy >= 0 && ix < W && iy < H) v += g[iy * W + ix] * (1 - fx) * (1 - fy);
                    if (ix + 1 >= 0 && iy >= 0 && ix + 1 < W && iy < H) v += g[iy * W + ix + 1] * fx * (1 - fy);
                    if (ix >= 0 && iy + 1 >= 0 && ix < W && iy + 1 < H) v += g[(iy + 1) * W + ix] * (1 - fx) * fy;
                    if (ix + 1 >= 0 && iy + 1 >= 0 && ix + 1 < W && iy + 1 < H) v += g[(iy + 1) * W + ix + 1] * fx * fy;
                    pol[i * 360 + d] = (byte)Math.Floor(v + 0.5);
                    int nx = (int)Math.Round((double)mx) - X0, ny = (int)Math.Round((double)my) - Y0; pb[i * 360 + d] = nx >= 0 && ny >= 0 && nx < rw && ny < rh && l2[ny * rw + nx] == id;
                }
            }
            double lo0 = c.Ranges.Length > 0 ? c.Ranges[0][0] : 20, hi0 = c.Ranges.Length > 0 ? c.Ranges[0][1] : 175; int a0 = (int)Math.Round(lo0), a1 = (int)Math.Round(hi0), n = a1 - a0 + 1, hw = TangWin / 2; if (n < 3) return R;
            double[] F = new double[n]; int[] angI = new int[n];
            for (int k = 0; k < n; k++)
            {
                int ang = a0 + k, am = ((ang % 360) + 360) % 360; angI[k] = ang; double ss = 0; int cnt = 0;
                for (int i = 0; i < nr; i++)
                {
                    if (!pb[i * 360 + am]) continue;
                    double mean = 0; for (int j = -hw; j <= hw; j++) mean += pol[i * 360 + (((ang + j) % 360) + 360) % 360]; mean /= TangWin;
                    double res = pol[i * 360 + am] - mean; ss += res * res; cnt++;
                }
                F[k] = Math.Sqrt(ss / Math.Max(cnt, 1));
            }
            double[] Fs = new double[n], Bs = new double[n], Dv = new double[n], An = new double[n]; int hs = Smooth / 2, hb = BaseWin / 2; double[] win = new double[BaseWin];
            for (int k = 0; k < n; k++) { double s = 0; for (int j = -hs; j <= hs; j++) s += F[Math.Min(n - 1, Math.Max(0, k + j))]; Fs[k] = s / Smooth; }
            for (int k = 0; k < n; k++) { for (int j = -hb; j <= hb; j++) win[j + hb] = Fs[Math.Min(n - 1, Math.Max(0, k + j))]; double[] w2 = (double[])win.Clone(); Array.Sort(w2); Bs[k] = w2[hb]; Dv[k] = Fs[k] - Bs[k]; An[k] = angI[k]; }
            if (c.Sigma)
            {   // 偏差で評価: 範囲内の中央値からの差を、中央絶対偏差から求めた σ で割る（画像ごとにノイズの大きさが違っても同じ閾値で比べられる）
                double[] t1 = (double[])Dv.Clone(); Array.Sort(t1); double m0 = t1[n / 2]; for (int k = 0; k < n; k++) t1[k] = Math.Abs(Dv[k] - m0); Array.Sort(t1); double sg = Math.Max(1.4826 * t1[n / 2], 0.05);
                for (int k = 0; k < n; k++) Dv[k] = (Dv[k] - m0) / sg;
            }
            double lowThr = c.Top ? c.Thr * Math.Max(0.1, Math.Min(1.0, c.PartnerRatio)) : c.Thr;
            for (int k = 0; k < n;)
            {
                if (Dv[k] > lowThr)
                {
                    int j = k, km = k; while (j < n && Dv[j] > lowThr) { if (Dv[j] > Dv[km]) km = j; j++; }
                    if (j - k >= MinWidth) { VSeam s = new VSeam(); s.Deg = s.Mark = angI[km]; s.D = Dv[km]; s.R = 1; int dd = ((angI[km] % 360) + 360) % 360; s.X = ecx + ux[dd] * rad[dd]; s.Y = ecy + uy[dd] * rad[dd]; R.Seams.Add(s); }
                    k = j;
                }
                else k++;
            }
            if (c.Top && R.Seams.Count > 0)
            {   // 最大の山が閾値以上のときだけ検出とし、ほかの山は「最大の山から晶線間隔の整数倍 ± 許容」にあるものだけ残す
                VSeam tp = R.Seams[0]; foreach (VSeam s in R.Seams) if (s.D > tp.D) tp = s; List<VSeam> keep = new List<VSeam>();
                if (tp.D > c.Thr) foreach (VSeam s in R.Seams) { if (s == tp) { keep.Add(s); continue; } double dl = Math.Abs(s.Deg - tp.Deg), m = c.Expect > 0 ? Math.Round(dl / c.Expect) : 0; if (m >= 1 && Math.Abs(dl - m * c.Expect) <= c.PairTol) keep.Add(s); }
                R.Seams = keep;
            }
            R.Ang = An; R.D = Dv; R.Valid = true; R.Detected = R.Seams.Count > 0; return R;
        }

        /// <summary>1 枚を解析する。red は赤チャンネル（Red() の出力）、scale は元画像に対する縮小率（結果の座標は元画像の画素に戻す）。</summary>
        public static VResult Analyze(byte[] red, int W, int H, double scale, VParam c)
        {
            Stopwatch sw = Stopwatch.StartNew(); VResult R;
            try { R = c.Contour == 0 ? Brightness(red, W, H, c) : Contour(red, W, H, c); }
            catch (Exception ex) { R = new VResult(); R.Method = c.Contour; M.Log("VisionView 解析で例外: " + ex.Message); }
            R.W = (int)(W * scale); R.H = (int)(H * scale); R.Scale = scale;
            if (scale != 1) { R.Cx *= scale; R.Cy *= scale; R.A *= scale; R.B *= scale; foreach (VSeam s in R.Seams) { s.X *= scale; s.Y *= scale; } }
            sw.Stop(); R.Ms = sw.Elapsed.TotalMilliseconds; return R;
        }
    }
}
