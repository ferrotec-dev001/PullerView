// VisionLocate.cs — 探索枠とメニスカス（明るい弧）の自動検出（V2-v11）
// XG-X の画像は坩堝全体が写り、メニスカスは画面のごく一部しかない。画面全体からリングを探すと坩堝の縁などを拾うため、
// 工程ごとの探索枠（画像に対する % の矩形）の中でメニスカスを探し、その周辺だけを切り出して晶線解析にかける。
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PullerView
{
    public static class VisionLocate
    {
        public const int RefW = 1276;            // 検出に使う縮小画像の基準幅（原画 5104／1/2 2552／1/4 1276 のどれでも同じ見かけの大きさにそろえる）
        public const double DogThr = 6.0;        // 弧とみなす明るさの差（細かいぼかし − 粗いぼかし）
        /// <summary>探索枠（%）を画素の矩形にする（画像内に収める）。</summary>
        public static Rectangle FrameRect(VisionProcCfg p, int w, int h)
        {
            int x = (int)Math.Round(p.Rx / 100.0 * w), y = (int)Math.Round(p.Ry / 100.0 * h), rw = (int)Math.Round(p.Rw / 100.0 * w), rh = (int)Math.Round(p.Rh / 100.0 * h);
            x = Math.Max(0, Math.Min(w - 8, x)); y = Math.Max(0, Math.Min(h - 8, y)); rw = Math.Max(8, Math.Min(w - x, rw)); rh = Math.Max(8, Math.Min(h - y, rh)); return new Rectangle(x, y, rw, rh);
        }
        static float[] Gauss(float[] a, int w, int h, double sigma)
        {
            int r = Math.Max(1, (int)Math.Ceiling(sigma * 3)); float[] k = new float[2 * r + 1]; float s = 0; for (int i = -r; i <= r; i++) { k[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma)); s += k[i + r]; }
            for (int i = 0; i < k.Length; i++) k[i] /= s;
            float[] t = new float[w * h], o = new float[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float v = 0; for (int i = -r; i <= r; i++) { int xx = x + i; if (xx < 0) xx = 0; else if (xx >= w) xx = w - 1; v += a[y * w + xx] * k[i + r]; } t[y * w + x] = v; }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float v = 0; for (int i = -r; i <= r; i++) { int yy = y + i; if (yy < 0) yy = 0; else if (yy >= h) yy = h - 1; v += t[yy * w + x] * k[i + r]; } o[y * w + x] = v; }
            return o;
        }
        /// <summary>探索枠 fr（画素）の中からメニスカス（細い明るい弧のかたまり）を探す。見つかれば、その外接矩形（元画像の画素）を返す。</summary>
        public static bool Find(byte[] px, int off, int w, int h, int stride, bool bottomUp, int bpp, Rectangle fr, out Rectangle box)
        {
            box = Rectangle.Empty; int k = Math.Max(1, (int)Math.Round(w / (double)RefW)), sw = fr.Width / k, sh = fr.Height / k; if (sw < 24 || sh < 12) return false;
            float[] a = new float[sw * sh];
            for (int y = 0; y < sh; y++) for (int x = 0; x < sw; x++)
            {
                int s = 0; for (int yy = 0; yy < k; yy++) { int Y = fr.Y + y * k + yy, row = off + (bottomUp ? h - 1 - Y : Y) * stride + 2; for (int xx = 0; xx < k; xx++) s += px[row + (fr.X + x * k + xx) * bpp]; }
                a[y * sw + x] = s / (float)(k * k);
            }
            float[] g1 = Gauss(a, sw, sh, 1.2), g2 = Gauss(a, sw, sh, 6); int[] lab = new int[sw * sh]; int n = 0; double best = 0; int[] st = new int[sw * sh];
            for (int i0 = 0; i0 < lab.Length; i0++)
            {
                if (lab[i0] != 0 || g1[i0] - g2[i0] <= DogThr) continue;
                n++; int sp = 0; st[sp++] = i0; lab[i0] = n; int x0 = sw, y0 = sh, x1 = -1, y1 = -1, area = 0; double sum = 0;
                while (sp > 0)
                {
                    int i = st[--sp], x = i % sw, y = i / sw; area++; sum += g1[i] - g2[i]; if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= sw || yy >= sh) continue; int j = yy * sw + xx;
                        if (lab[j] == 0 && g1[j] - g2[j] > DogThr) { lab[j] = n; st[sp++] = j; }
                    }
                }
                int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                if (bw >= 20 && bh >= 10 && area >= 40 && area < 0.35 * bw * bh && sum > best) { best = sum; box = new Rectangle(fr.X + x0 * k, fr.Y + y0 * k, bw * k, bh * k); }   // 細い弧だけ（塗りつぶされた明るい面は除く）
            }
            return best > 0;
        }
        /// <summary>メニスカスの外接矩形から、解析に渡す切り出し範囲（元画像の画素。画像内に収める）を決める。</summary>
        public static Rectangle Window(Rectangle box, int w, int h)
        {
            int k = Math.Max(1, (int)Math.Round(w / (double)RefW)); int hw = Math.Max(2 * box.Width, 60 * k), hh = (int)Math.Round(hw * 1025.0 / 1216); hw = Math.Min(hw, w / 2); hh = Math.Min(hh, h / 2);
            int cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2, x = Math.Max(0, Math.Min(w - 2 * hw, cx - hw)), y = Math.Max(0, Math.Min(h - 2 * hh, cy - hh)); return new Rectangle(x, y, 2 * hw, 2 * hh);
        }
        /// <summary>矩形を切り出して、上から下・隙間なしの画素列にする。</summary>
        public static byte[] Crop(byte[] px, int off, int w, int h, int stride, bool bottomUp, int bpp, Rectangle r)
        {
            byte[] o = new byte[r.Width * r.Height * bpp];
            for (int y = 0; y < r.Height; y++) { int Y = r.Y + y; Buffer.BlockCopy(px, off + (bottomUp ? h - 1 - Y : Y) * stride + r.X * bpp, o, y * r.Width * bpp, r.Width * bpp); }
            return o;
        }
    }
}
