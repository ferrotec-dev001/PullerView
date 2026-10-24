// Trend.cs — 表示用トレンド集約（ピクセル列ごとの min / max / last ビン）
// BODY 工程は 0.45〜1.0 mm/min で進むため、1 s サンプルは 1 px（≒12.5 mm）あたり 750〜1,700 点になる。
// 生データ（GraphState.X/Pv/Sv）はそのまま保持し、描画は X 軸をプロット幅 px で分割したビンへ逐次集約（1 点 O(1)）して
// min〜max の包絡線（帯）＋ last 折れ線で描く。X 軸範囲やプロット幅が変わったときだけ生データから再集約する。
using System;
using System.Collections.Generic;

namespace PullerView
{
    public class TrendBins
    {
        public struct Bin { public int N; public double Min, Max, Last, SvMin, SvMax, SvLast; }
        public Bin[] B = new Bin[0];
        public double X0, X1; public int Count { get { return B.Length; } }
        public double BinW { get { return B.Length == 0 ? 0 : (X1 - X0) / B.Length; } }
        public double Min = double.MaxValue, Max = double.MinValue;   // 範囲内の全体 min/max（Y 自動スケール用）

        /// <summary>ビン数・範囲が変わっていれば生データから再集約する。変わっていなければ何もしない。</summary>
        public void Ensure(int n, double x0, double x1, List<double> xs, List<double> pv, List<double> sv)
        {
            if (n < 1) n = 1;
            if (B.Length == n && X0 == x0 && X1 == x1) return;
            B = new Bin[n]; X0 = x0; X1 = x1; Min = double.MaxValue; Max = double.MinValue;
            for (int i = 0; i < xs.Count; i++) Add(xs[i], pv[i], sv[i]);
        }
        /// <summary>1 サンプルをビンへ加える（範囲外は無視）。</summary>
        public void Add(double x, double pv, double sv)
        {
            if (B.Length == 0 || X1 <= X0) return;
            int i = (int)((x - X0) / (X1 - X0) * B.Length); if (i < 0 || x > X1) return; if (i >= B.Length) i = B.Length - 1;
            Bin b = B[i];
            if (b.N == 0) { b.Min = b.Max = b.Last = pv; b.SvMin = b.SvMax = b.SvLast = sv; }
            else
            {
                if (pv < b.Min) b.Min = pv; if (pv > b.Max) b.Max = pv; b.Last = pv;
                if (!double.IsNaN(sv)) { if (double.IsNaN(b.SvMin) || sv < b.SvMin) b.SvMin = sv; if (double.IsNaN(b.SvMax) || sv > b.SvMax) b.SvMax = sv; b.SvLast = sv; }
            }
            b.N++; B[i] = b;
            if (pv < Min) Min = pv; if (pv > Max) Max = pv;
            if (!double.IsNaN(sv)) { if (sv < Min) Min = sv; if (sv > Max) Max = sv; }
        }
        public void Clear() { B = new Bin[0]; X0 = X1 = 0; Min = double.MaxValue; Max = double.MinValue; }
        /// <summary>ビン i の X 中心値。</summary>
        public double Xc(int i) { return X0 + (i + 0.5) * BinW; }
    }
}
