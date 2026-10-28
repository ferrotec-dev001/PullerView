using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PullerView
{
    /// <summary>
    /// .NET Framework 4.0 で動く最小 ZIP ライター（System.IO.Compression.ZipArchive は 4.5 以降のため自前実装）。
    /// Deflate（DeflateStream）＋ CRC32、ファイル名は UTF-8（汎用ビット 11）。Windows エクスプローラー／7-Zip 等で展開可。
    /// 用途: 日次 CSV ログ（PV_yyyyMMdd.csv）の圧縮。ZIP64 非対応（4GB 未満）。
    /// </summary>
    public sealed class ZipWriter : IDisposable
    {
        class Entry { public string Name; public uint Crc; public long Comp, Uncomp, Offset; public DateTime Time; }
        readonly FileStream o; readonly List<Entry> entries = new List<Entry>(); bool closed;
        static readonly uint[] CrcTable = MakeTable();
        static uint[] MakeTable() { uint[] t = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; } return t; }
        static uint CrcUpdate(uint crc, byte[] buf, int len) { uint c = crc ^ 0xFFFFFFFFu; for (int i = 0; i < len; i++) c = CrcTable[(c ^ buf[i]) & 0xFF] ^ (c >> 8); return c ^ 0xFFFFFFFFu; }

        public ZipWriter(string zipPath) { o = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None); }

        /// <summary>ファイル 1 本を Deflate 圧縮して追加。読み取りは FileShare.ReadWrite（書き込み中ファイルも読める）。</summary>
        public void AddFile(string path, string entryName)
        {
            Entry e = new Entry(); e.Name = entryName; e.Time = File.GetLastWriteTime(path); e.Offset = o.Position;
            byte[] name = Encoding.UTF8.GetBytes(entryName);
            WriteLocalHeader(e, name);                                  // サイズ・CRC は後で書き戻す
            long dataStart = o.Position; uint crc = 0; long total = 0;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (DeflateStream ds = new DeflateStream(o, CompressionMode.Compress, true))
            {
                byte[] buf = new byte[64 * 1024]; int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0) { crc = CrcUpdate(crc, buf, n); ds.Write(buf, 0, n); total += n; }
            }
            e.Crc = crc; e.Uncomp = total; e.Comp = o.Position - dataStart;
            long end = o.Position; o.Position = e.Offset + 14; W32(e.Crc); W32((uint)e.Comp); W32((uint)e.Uncomp); o.Position = end;
            entries.Add(e);
        }
        void WriteLocalHeader(Entry e, byte[] name)
        {
            W32(0x04034b50); W16(20); W16(0x0800); W16(8); W16(DosTime(e.Time)); W16(DosDate(e.Time));
            W32(0); W32(0); W32(0); W16((ushort)name.Length); W16(0); o.Write(name, 0, name.Length);
        }
        public void Close()
        {
            if (closed) return; closed = true;
            long cdStart = o.Position;
            foreach (Entry e in entries)
            {
                byte[] name = Encoding.UTF8.GetBytes(e.Name);
                W32(0x02014b50); W16(20); W16(20); W16(0x0800); W16(8); W16(DosTime(e.Time)); W16(DosDate(e.Time));
                W32(e.Crc); W32((uint)e.Comp); W32((uint)e.Uncomp); W16((ushort)name.Length); W16(0); W16(0); W16(0); W16(0); W32(0); W32((uint)e.Offset);
                o.Write(name, 0, name.Length);
            }
            long cdSize = o.Position - cdStart;
            W32(0x06054b50); W16(0); W16(0); W16((ushort)entries.Count); W16((ushort)entries.Count); W32((uint)cdSize); W32((uint)cdStart); W16(0);
            o.Flush(); o.Close();
        }
        public void Dispose() { Close(); }
        void W16(int v) { o.WriteByte((byte)(v & 0xFF)); o.WriteByte((byte)((v >> 8) & 0xFF)); }
        void W32(uint v) { for (int i = 0; i < 4; i++) o.WriteByte((byte)((v >> (8 * i)) & 0xFF)); }
        static int DosTime(DateTime t) { return (t.Hour << 11) | (t.Minute << 5) | (t.Second / 2); }
        static int DosDate(DateTime t) { int y = Math.Max(1980, t.Year); return ((y - 1980) << 9) | (t.Month << 5) | t.Day; }
    }
}
