// CvImageSaver の受信・保存部を .NET Framework 4.0 向けに移植したもの。poc/CvRecv40/src/CvCore40.cs を基に、
// PullerView へ組み込むためにログの出力先（FileLog.Sink）を差し替えられるようにし、XG-X 取り込み（XgxCore40.cs）用に保存時刻を指定できるようにしてある。
// 元: CvImageSaver v1.1.0（.NET 8 / netstandard2.0）。async/await・System.Text.Json・Serilog・C# 8 構文を使わない。
// 変更点: OK/NG のフォルダ分けをやめて 1 フォルダへ保存、カメラ番号 0＝全カメラ、ファイル名に工程名を付加可。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Ferrotec.CvImageSaver40
{
    /// <summary>CV シリーズのバイナリ電文チェックサム（全データの XOR をビット反転した 1 バイト）。</summary>
    public static class Checksum
    {
        public static byte Calculate(byte[] data, int offset, int count) { byte x = 0; int end = offset + count; for (int i = offset; i < end; i++) x ^= data[i]; return (byte)(~x); }
    }

    /// <summary>画像 1 枚分の電文: T1 , c , t , nnnnnnnnnn , ddddddd , [画像][チェックサム] CR（BR 応答は先頭が BR）。</summary>
    public sealed class ImageFrame
    {
        public string Command; public int CameraNo; public string RawJudgement; public long MeasurementCount; public int DeclaredLength; public byte[] ImageData; public byte ChecksumByte; public bool ChecksumMatches;
        public bool LooksLikeBmp { get { return ImageData.Length >= 2 && ImageData[0] == (byte)'B' && ImageData[1] == (byte)'M'; } }
        public override string ToString() { return Command + " cam=" + CameraNo + " t=" + RawJudgement + " count=" + MeasurementCount + " len=" + DeclaredLength + " chk=" + (ChecksumMatches ? "OK" : "NG"); }
    }

    /// <summary>受信バイト列を逐次解析して ImageFrame を切り出す（元の FrameParser と同じ手順）。</summary>
    public sealed class FrameParser
    {
        const byte Cr = 0x0D, Lf = 0x0A, Comma = (byte)',';
        byte[] buf = new byte[64 * 1024]; int start, end;
        public int MaxHeaderLength = 64, MaxTextLineLength = 4096, MaxImageLength = 64 * 1024 * 1024;
        public event Action<string> TextLineReceived; public event Action<int> Resynchronized;
        public int WaitingForBytes; public string LastHeader;
        public int PendingBytes { get { return end - start; } }
        public void Reset() { start = 0; end = 0; WaitingForBytes = 0; }

        public List<ImageFrame> Feed(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException("data");
            Append(data, offset, count);
            List<ImageFrame> frames = new List<ImageFrame>(); ImageFrame f;
            while (TryParseOne(out f)) { if (f != null) frames.Add(f); }
            Compact(); return frames;
        }
        void Append(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            if (end + count > buf.Length) { Compact(); if (end + count > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, end + count)); }
            Buffer.BlockCopy(data, offset, buf, end, count); end += count;
        }
        void Compact() { if (start == 0) return; int len = end - start; if (len > 0) Buffer.BlockCopy(buf, start, buf, 0, len); start = 0; end = len; }

        bool TryParseOne(out ImageFrame frame)
        {
            frame = null;
            while (start < end && (buf[start] == Cr || buf[start] == Lf)) start++;
            int avail = end - start; if (avail < 3) return false;
            bool isHeader = (buf[start] == (byte)'T' && buf[start + 1] == (byte)'1' && buf[start + 2] == Comma) || (buf[start] == (byte)'B' && buf[start + 1] == (byte)'R' && buf[start + 2] == Comma);
            if (!isHeader) return ConsumeTextLineOrResync();
            int commas = 0, headerEnd = -1, limit = Math.Min(avail, MaxHeaderLength);
            for (int i = 0; i < limit; i++)
            {
                byte b = buf[start + i];
                if (b == Cr) return ConsumeTextLineOrResync();
                if (b == Comma) { commas++; if (commas == 5) { headerEnd = i; break; } }
            }
            if (headerEnd < 0) { if (avail >= MaxHeaderLength) return ResyncToNextHeader(); return false; }
            string header = Encoding.ASCII.GetString(buf, start, headerEnd); string[] tok = header.Split(',');
            int cameraNo, dataLen; long count;
            if (tok.Length != 5 || !int.TryParse(tok[1], NumberStyles.None, CultureInfo.InvariantCulture, out cameraNo) || !long.TryParse(tok[3], NumberStyles.None, CultureInfo.InvariantCulture, out count)
                || !int.TryParse(tok[4], NumberStyles.None, CultureInfo.InvariantCulture, out dataLen) || dataLen <= 0 || dataLen > MaxImageLength)
                return ResyncToNextHeader();
            int payloadStart = start + headerEnd + 1;
            if (WaitingForBytes != dataLen || LastHeader != header) { LastHeader = header; WaitingForBytes = dataLen; }
            if (end - payloadStart < dataLen) return false;
            WaitingForBytes = 0;
            int imageLen = dataLen - 1; byte[] image = new byte[imageLen]; Buffer.BlockCopy(buf, payloadStart, image, 0, imageLen);
            byte chk = buf[payloadStart + imageLen];
            frame = new ImageFrame(); frame.Command = tok[0]; frame.CameraNo = cameraNo; frame.RawJudgement = tok[2]; frame.MeasurementCount = count; frame.DeclaredLength = dataLen; frame.ImageData = image;
            frame.ChecksumByte = chk; frame.ChecksumMatches = Checksum.Calculate(image, 0, imageLen) == chk;
            start = payloadStart + dataLen; return true;
        }
        bool ConsumeTextLineOrResync()
        {
            int avail = end - start, limit = Math.Min(avail, MaxTextLineLength), crIndex = -1; bool printable = true;
            for (int i = 0; i < limit; i++) { byte b = buf[start + i]; if (b == Cr) { crIndex = i; break; } if (b == Lf) continue; if (b < 0x20 || b > 0x7E) { printable = false; break; } }
            if (printable && crIndex >= 0)
            {
                string line = Encoding.ASCII.GetString(buf, start, crIndex).Trim('\n'); start += crIndex + 1; if (start < end && buf[start] == Lf) start++;
                if (line.Length > 0 && TextLineReceived != null) TextLineReceived(line);
                return true;
            }
            if (printable) { if (avail >= MaxTextLineLength) return ResyncToNextHeader(); return false; }
            return ResyncToNextHeader();
        }
        bool ResyncToNextHeader()
        {
            int avail = end - start;
            for (int i = 1; i < avail; i++)
            {
                if (buf[start + i] == Cr) { Drop(i + 1); return true; }
                if (i + 2 >= avail) break;
                byte b0 = buf[start + i], b1 = buf[start + i + 1], b2 = buf[start + i + 2];
                if (b2 == Comma && ((b0 == (byte)'T' && b1 == (byte)'1') || (b0 == (byte)'B' && b1 == (byte)'R'))) { Drop(i); return true; }
            }
            if (avail > 2) { Drop(avail - 2); return true; }
            return false;
        }
        void Drop(int n) { start += n; if (Resynchronized != null) Resynchronized(n); }
    }

    /// <summary>設定（CvImageSaver の settings.json に対応。INI から読む）。</summary>
    public sealed class CvSettings
    {
        public string Ip = "192.168.0.10"; public int Port = 8501, CameraNo = 1 /*0=全カメラ*/, ReconnectSec = 5, TimeoutSec = 5; public bool RejectChecksum = false, RawDump = false; public int RawDumpMaxKb = 2048;
        public bool Save = true; public string Folder = "Image", Process = ""; public int SaveEvery = 1, MaxFiles = 10, KeepDays = 30, CleanupHours = 1;
        public string LogFolder = "Log";

        public static CvSettings Load(string path)
        {
            CvSettings s = new CvSettings(); if (!File.Exists(path)) return s;
            string sec = "";
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string l = raw; int c = l.IndexOf(';'); if (c >= 0) l = l.Substring(0, c); l = l.Trim(); if (l == "") continue;
                if (l.StartsWith("[")) { int e = l.IndexOf(']'); sec = e > 0 ? l.Substring(1, e - 1).Trim().ToLowerInvariant() : ""; continue; }
                int q = l.IndexOf('='); if (q < 0) continue; string k = sec + "." + l.Substring(0, q).Trim().ToLowerInvariant(), v = l.Substring(q + 1).Trim(); int n; bool isN = int.TryParse(v, out n);
                switch (k)
                {
                    case "camera.ip": s.Ip = v; break; case "camera.port": if (isN) s.Port = n; break; case "camera.camera_no": if (isN) s.CameraNo = n; break;
                    case "camera.reconnect_sec": if (isN) s.ReconnectSec = n; break; case "camera.timeout_sec": if (isN) s.TimeoutSec = n; break;
                    case "camera.reject_checksum": s.RejectChecksum = v == "1"; break; case "camera.raw_dump": s.RawDump = v == "1"; break;
                    case "image.save": s.Save = v != "0"; break; case "image.folder": if (v != "") s.Folder = v; break; case "image.process": s.Process = v; break;
                    case "image.save_every": if (isN) s.SaveEvery = n; break; case "image.max_files": if (isN) s.MaxFiles = n; break; case "image.keep_days": if (isN) s.KeepDays = n; break; case "image.cleanup_hours": if (isN) s.CleanupHours = n; break;
                    case "log.folder": if (v != "") s.LogFolder = v; break;
                }
            }
            return s;
        }
        public List<string> Validate()
        {
            List<string> e = new List<string>(); System.Net.IPAddress a;
            if (!System.Net.IPAddress.TryParse(Ip, out a)) e.Add("ip の形式が正しくありません: " + Ip);
            if (Port < 1 || Port > 65535) e.Add("port は 1〜65535"); if (CameraNo < 0 || CameraNo > 4) e.Add("camera_no は 0（全カメラ）または 1〜4");
            if (ReconnectSec < 1) e.Add("reconnect_sec は 1 以上"); if (TimeoutSec < 1) e.Add("timeout_sec は 1 以上"); if (SaveEvery < 1) e.Add("save_every は 1 以上");
            if (MaxFiles < 0) e.Add("max_files は 0 以上"); if (KeepDays < 0) e.Add("keep_days は 0 以上"); if (CleanupHours < 1) e.Add("cleanup_hours は 1 以上");
            return e;
        }
    }

    /// <summary>日付ごとのテキストログ（Serilog の代わり）。</summary>
    public sealed class FileLog
    {
        readonly string folder; readonly object lk = new object(); public bool Echo; public Action<string, string> Sink;   // Sink を設定すると、ファイルではなく Sink(level, msg) へ出力する
        public FileLog(string folder) { this.folder = folder; if (folder != null) Directory.CreateDirectory(folder); }
        public void Write(string level, string msg)
        {
            if (Sink != null) { try { Sink(level, msg); } catch (Exception) { } return; }
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg;
            lock (lk) { try { File.AppendAllText(Path.Combine(folder, "CvRecv-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + "\r\n", new UTF8Encoding(true)); } catch (Exception) { } if (Echo) Console.WriteLine(line); }
        }
        public void Info(string m) { Write("INF", m); } public void Warn(string m) { Write("WRN", m); } public void Error(string m) { Write("ERR", m); }
    }

    /// <summary>画像の保存（1 フォルダ。OK/NG は判断しない）と、枚数・日数による自動削除。</summary>
    /// <summary>画像ファイルの形式（BMP／JPEG）。保存は受け取ったデータをそのまま書き、拡張子だけ中身に合わせる（再圧縮しない）。</summary>
    public static class ImgFmt
    {
        public static bool IsImage(string path) { string e = (Path.GetExtension(path) ?? "").ToLowerInvariant(); return e == ".bmp" || e == ".jpg" || e == ".jpeg"; }
        /// <summary>データの先頭から拡張子を決める: JPEG（FF D8 FF）→ .jpg、それ以外 → .bmp。</summary>
        public static string ExtOf(byte[] d) { return d != null && d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF ? ".jpg" : ".bmp"; }
        /// <summary>フォルダ直下の画像ファイル（BMP／JPG）。</summary>
        public static FileInfo[] Files(string dir) { List<FileInfo> l = new List<FileInfo>(); if (Directory.Exists(dir)) foreach (FileInfo f in new DirectoryInfo(dir).GetFiles()) if (IsImage(f.Name)) l.Add(f); return l.ToArray(); }
        public static string[] Paths(string dir) { FileInfo[] fs = Files(dir); string[] r = new string[fs.Length]; for (int i = 0; i < fs.Length; i++) r[i] = fs[i].FullName; return r; }
    }

    public sealed class ImageStore
    {
        readonly Func<CvSettings> settings; readonly FileLog log; readonly string baseDir; readonly object lk = new object(); long seq; DateTime lastCleanup = DateTime.MinValue;
        public long Saved, Skipped, Deleted;
        /// <summary>直前の Save が「保存先へ書けなかった」ために null を返したとき true（保存対象外で null のときは false）。</summary>
        public volatile bool LastWriteFailed;
        public ImageStore(Func<CvSettings> settings, FileLog log, string baseDir) { this.settings = settings; this.log = log; this.baseDir = baseDir; }
        public string FolderPath { get { string f = settings().Folder; return Path.IsPathRooted(f) ? f : Path.Combine(baseDir, f); } }

        /// <summary>保存したファイルのパス。保存しなかった場合は null。</summary>
        public string Save(ImageFrame f) { return Save(f, DateTime.Now); }
        /// <summary>time をファイル名の日時に使って保存する（XG-X: 画像が出力された時刻）。</summary>
        public string Save(ImageFrame f, DateTime time)
        {
            LastWriteFailed = false; CvSettings s = settings();
            if (s.CameraNo != 0 && f.CameraNo != s.CameraNo) { Skipped++; return null; }
            if (!f.ChecksumMatches) { if (s.RejectChecksum) { Skipped++; log.Warn("チェックサム不一致のため破棄: " + f); return null; } log.Warn("チェックサム不一致（保存する）: " + f); }
            long n = Interlocked.Increment(ref seq);
            if (!s.Save || (s.SaveEvery > 1 && (n - 1) % s.SaveEvery != 0)) { Skipped++; return null; }
            try
            {
                lock (lk)
                {
                    string dir = FolderPath; Directory.CreateDirectory(dir);
                    string stem = time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), tail = (s.Process == "" ? "" : "_" + s.Process) + ImgFmt.ExtOf(f.ImageData);
                    string path = Path.Combine(dir, stem + tail);
                    for (int i = 1; File.Exists(path) && i <= 9999; i++) path = Path.Combine(dir, stem + "-" + i + tail);   // 同じ秒に複数枚届いたときの枝番
                    string tmp = path + ".tmp"; File.WriteAllBytes(tmp, f.ImageData); File.Move(tmp, path);
                    Saved++; log.Info("保存: " + path + " cam=" + f.CameraNo + " count=" + f.MeasurementCount + " bytes=" + f.ImageData.Length);
                    TrimCount(dir, s.MaxFiles);
                    if (s.KeepDays > 0 && (DateTime.Now - lastCleanup).TotalHours >= s.CleanupHours) { lastCleanup = DateTime.Now; TrimDays(dir, s.KeepDays); }
                    return path;
                }
            }
            catch (Exception ex) { LastWriteFailed = true; log.Error("画像ファイル書込み失敗: " + ex.Message); return null; }
        }
        void TrimCount(string dir, int max)
        {
            if (max <= 0) return;
            FileInfo[] fs = ImgFmt.Files(dir); if (fs.Length <= max) return;
            Array.Sort(fs, delegate (FileInfo a, FileInfo b) { int c = a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc); return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name); });
            for (int i = 0; i < fs.Length - max; i++) { try { fs[i].Delete(); Deleted++; } catch (Exception ex) { log.Warn("削除失敗: " + fs[i].Name + " " + ex.Message); } }
        }
        void TrimDays(string dir, int days)
        {
            DateTime limit = DateTime.Now.AddDays(-days);
            foreach (FileInfo fi in ImgFmt.Files(dir)) if (fi.LastWriteTime < limit) { try { fi.Delete(); Deleted++; } catch (Exception) { } }
        }
    }

    /// <summary>画像出力ポートへ接続して受信し続ける（専用スレッド。切断時は自動で再接続）。</summary>
    public sealed class ImageReceiver
    {
        readonly Func<CvSettings> settings; readonly FileLog log; readonly FrameParser parser = new FrameParser(); readonly string baseDir;
        Thread th; volatile bool stop; TcpClient cur; readonly ManualResetEvent wake = new ManualResetEvent(false);
        public long BytesReceived, FramesReceived, Connects; public volatile bool Connected; public event Action<ImageFrame> FrameReceived;
        public ImageReceiver(Func<CvSettings> settings, FileLog log, string baseDir)
        {
            this.settings = settings; this.log = log; this.baseDir = baseDir;
            parser.TextLineReceived += delegate (string l) { log.Info("テキスト行受信: " + l); }; parser.Resynchronized += delegate (int n) { log.Warn("解析不能データ " + n + " バイトを破棄して再同期"); };
        }
        public void Start() { if (th != null) return; stop = false; wake.Reset(); th = new Thread(Run); th.IsBackground = true; th.Name = "CvImageReceiver"; th.Start(); }
        public void Stop() { stop = true; wake.Set(); TcpClient c = cur; if (c != null) { try { c.Close(); } catch (Exception) { } } Thread t = th; if (t != null) { t.Join(3000); th = null; } }

        void Run()
        {
            while (!stop)
            {
                CvSettings s = settings();
                try { Session(s); }
                catch (Exception ex) { if (!stop) log.Warn("接続/受信エラー: " + ex.Message); }
                Connected = false; if (stop) break;
                wake.WaitOne(s.ReconnectSec * 1000);
            }
            log.Info("受信停止");
        }
        void Session(CvSettings s)
        {
            using (TcpClient client = new TcpClient())
            {
                cur = client; client.ReceiveBufferSize = 256 * 1024; client.NoDelay = true;
                IAsyncResult ar = client.BeginConnect(s.Ip, s.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(s.TimeoutSec * 1000)) { client.Close(); throw new TimeoutException("接続タイムアウト (" + s.TimeoutSec + "s) " + s.Ip + ":" + s.Port); }
                client.EndConnect(ar);
                parser.Reset(); Connected = true; Connects++; log.Info("接続確立 " + s.Ip + ":" + s.Port);
                byte[] buffer = new byte[64 * 1024]; FileStream dump = null; long dumped = 0, dumpMax = s.RawDumpMaxKb * 1024L;
                if (s.RawDump) { try { string d = Path.IsPathRooted(s.LogFolder) ? s.LogFolder : Path.Combine(baseDir, s.LogFolder); Directory.CreateDirectory(d); dump = new FileStream(Path.Combine(d, "raw-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin"), FileMode.Create, FileAccess.Write, FileShare.Read); } catch (Exception ex) { log.Warn("生データ保存ファイルを作成できません: " + ex.Message); } }
                try
                {
                    NetworkStream stream = client.GetStream();
                    while (!stop)
                    {
                        int n = stream.Read(buffer, 0, buffer.Length);
                        if (n == 0) { log.Warn("装置側から切断されました"); return; }
                        BytesReceived += n;
                        if (dump != null && dumped < dumpMax) { int w = (int)Math.Min(n, dumpMax - dumped); dump.Write(buffer, 0, w); dumped += w; }
                        foreach (ImageFrame f in parser.Feed(buffer, 0, n))
                        {
                            FramesReceived++;
                            try { if (FrameReceived != null) FrameReceived(f); } catch (Exception ex) { log.Error("フレーム処理中の例外: " + ex.Message); }
                        }
                    }
                }
                finally
                {
                    if (dump != null) dump.Dispose(); cur = null;
                    if (parser.WaitingForBytes > 0 && !stop) log.Warn("切断時点で画像データ待ち（宣言 " + parser.WaitingForBytes + " バイト、受信済 " + parser.PendingBytes + " バイト）");
                }
            }
        }
    }
}
