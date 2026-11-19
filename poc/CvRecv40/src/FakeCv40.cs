// FakeCv40.exe: 模擬カメラ（画像出力ポートの代わり）。フォルダの BMP を T1 電文で送る。.NET Framework 4.0。
// 使い方: FakeCv40.exe --folder <BMP フォルダ> [--port 8501] [--interval 600] [--camera 1|1,2] [--count N] [--crlf] [--chunk N] [--badchk K] [--garbage] [--dropafter N]
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

static class FakeCv
{
    static int Main(string[] args)
    {
        string folder = "."; int port = 8501, interval = 600, count = 0, chunk = 0, badchk = 0, dropAfter = 0; string cams = "1"; bool crlf = false, garbage = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i]; Func<string> nx = delegate { return args[++i]; };
            if (a == "--folder") folder = nx(); else if (a == "--port") port = int.Parse(nx()); else if (a == "--interval") interval = int.Parse(nx()); else if (a == "--camera") cams = nx(); else if (a == "--count") count = int.Parse(nx());
            else if (a == "--chunk") chunk = int.Parse(nx()); else if (a == "--badchk") badchk = int.Parse(nx()); else if (a == "--dropafter") dropAfter = int.Parse(nx()); else if (a == "--crlf") crlf = true; else if (a == "--garbage") garbage = true;
        }
        string[] files = Directory.GetFiles(folder, "*.bmp"); Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        if (files.Length == 0) { Console.WriteLine("BMP がありません: " + folder); return 2; }
        string[] cam = cams.Split(new char[] { ',' });
        TcpListener l = new TcpListener(IPAddress.Any, port); l.Start(); Console.WriteLine("FakeCv40 待機 port=" + port + " files=" + files.Length);
        long n = 0; Random rnd = new Random(1);
        while (count == 0 || n < count)
        {
            using (TcpClient c = l.AcceptTcpClient())
            {
                Console.WriteLine("接続 " + c.Client.RemoteEndPoint); NetworkStream st = c.GetStream(); int sentThis = 0;
                try
                {
                    if (garbage) { byte[] g = new byte[300]; rnd.NextBytes(g); st.Write(g, 0, g.Length); byte[] t = Encoding.ASCII.GetBytes("ER,T1,01\r"); st.Write(t, 0, t.Length); }
                    while (count == 0 || n < count)
                    {
                        byte[] bmp = File.ReadAllBytes(files[n % files.Length]); int dataLen = bmp.Length + 1;
                        string header = "T1," + cam[n % cam.Length] + ",0," + (n + 1).ToString("D10", CultureInfo.InvariantCulture) + "," + dataLen.ToString("D7", CultureInfo.InvariantCulture) + ",";
                        byte[] h = Encoding.ASCII.GetBytes(header), d = Encoding.ASCII.GetBytes(crlf ? "\r\n" : "\r"), fr = new byte[h.Length + dataLen + d.Length];
                        Buffer.BlockCopy(h, 0, fr, 0, h.Length); Buffer.BlockCopy(bmp, 0, fr, h.Length, bmp.Length);
                        byte x = 0; for (int k = 0; k < bmp.Length; k++) x ^= bmp[k]; byte chk = (byte)(~x); if (badchk > 0 && (n + 1) % badchk == 0) chk ^= 0xFF;
                        fr[h.Length + bmp.Length] = chk; Buffer.BlockCopy(d, 0, fr, h.Length + dataLen, d.Length);
                        if (chunk <= 0) st.Write(fr, 0, fr.Length);
                        else for (int p = 0; p < fr.Length;) { int w = Math.Min(fr.Length - p, 1 + rnd.Next(chunk)); st.Write(fr, p, w); p += w; }
                        n++; sentThis++; Console.WriteLine("送信 #" + n + " " + Path.GetFileName(files[(n - 1) % files.Length]) + " cam=" + cam[(n - 1) % cam.Length]);
                        if (dropAfter > 0 && sentThis >= dropAfter) { Console.WriteLine("切断（試験）"); break; }
                        Thread.Sleep(interval);
                    }
                    Thread.Sleep(300);
                }
                catch (Exception ex) { Console.WriteLine("切断: " + ex.Message); }
            }
        }
        l.Stop(); Console.WriteLine("送信完了 " + n); return 0;
    }
}
