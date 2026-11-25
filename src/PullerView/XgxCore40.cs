// CvImageSaver v1.4.2 の XG-X 画像取り込み部（XgxBridgeProtocol / XgxSpoolWatcher / XgxBridgeHost）を .NET Framework 4.0 向けに移植したもの。
// 元: CvImageSaver v1.4.2（.NET 8 / netstandard2.0）。async/await・Task・C# 6 以降の構文を使わず、専用スレッドで動かす。
// 仕組み: XG-X の画像は Vapi.Net.dll（KEYENCE XG-X VisionTerminal 付属）が「受信フォルダ」へ BMP として保存する。
//         本体はそのフォルダを監視し、書込みが終わった BMP を取り込んで受信フォルダから削除する（XgxSpoolWatcher）。
//         Vapi.Net.dll は別プロセス（XgxBridge）で動かし、標準出力の通知だけを受け取る（XgxBridgeHost）。
// 変更点（PullerView V2 向け）:
//   ・OK/NG の判別（ファイル名キーワード・画像出力ユニット番号）は移植していない。判定は VisionView の晶線解析が行い、画像は 1 フォルダへ保存する。
//   ・保存間隔（10 分ごとに 10 枚）は移植していない。保存は [Image] の 全数／間引き に従う。
//   ・XgxBridge は exe へ内蔵せず、PullerView.exe と同じフォルダの XgxBridge\ に置く（ビルド時にコピー）。
//   ・起動した時点で受信フォルダに残っていた BMP は「持ち越し」として扱い、保存はするが解析へは渡さない（古い画像で判定を乱さないため）。
//   ・受信フォルダは、画像保存フォルダ・再生フォルダ・EXE のフォルダと重ねられない（取り込み後に削除するため）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Ferrotec.CvImageSaver40
{
    /// <summary>XgxBridge が標準出力へ書く通知 1 行分。形式はタブ区切り（元の XgxBridgeProtocol と同じ）。</summary>
    public sealed class XgxBridgeMessage
    {
        public const int Unknown = 0, State = 1, File = 2, Header = 3, Log = 4;
        public const string StateLogging = "LOGGING", StateConnecting = "CONNECTING", StateError = "ERROR", StateStopped = "STOPPED", StopCommand = "STOP", LogDebug = "DEBUG";
        public int Kind; public string Name = "", Text = ""; public int UnitNo;

        /// <summary>STATE\t状態名\t説明 ／ FILE\tユニット番号\t保存パス ／ HEADER\t説明 ／ LOG\tレベル\t本文。合わない行は Unknown。</summary>
        public static XgxBridgeMessage Parse(string line)
        {
            XgxBridgeMessage m = new XgxBridgeMessage(); string text = line ?? ""; string[] f = text.Split(new char[] { '\t' }, 3); int unit;
            if (f[0] == "STATE" && f.Length >= 2) { m.Kind = State; m.Name = f[1]; m.Text = f.Length >= 3 ? f[2] : ""; }
            else if (f[0] == "FILE" && f.Length >= 3 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out unit) && f[2].Length > 0) { m.Kind = File; m.Text = f[2]; m.UnitNo = unit; }
            else if (f[0] == "HEADER" && f.Length >= 2) { m.Kind = Header; m.Text = string.Join(" ", f, 1, f.Length - 1); }
            else if (f[0] == "LOG" && f.Length >= 3) { m.Kind = Log; m.Name = f[1]; m.Text = f[2]; }
            else { m.Kind = Unknown; m.Text = text; }
            return m;
        }
    }

    /// <summary>XG-X 取り込みの設定（VisionView.ini [Xgx]）。フォルダはすべて絶対パスで渡す。</summary>
    public sealed class XgxSettings
    {
        public const int ModeBridge = 0, ModeFolder = 1, SpoolMaxBytes = 160;   // V2-v3: 受信フォルダの上限（ANSI のバイト数）。Vapi.Net.dll は保存パスを 256 バイトの領域で返すため、配下のフォルダ名＋ファイル名の分として 95 バイトを残す
        public const string DefaultBridgeExe = @"XgxBridge\PullerView.XgxBridge.exe";
        public int Mode = ModeBridge; public string Ip = "192.168.0.10", SpoolFolder = "", VapiFolder = "", BridgeExe = "", BaseDir = "";
        public int StableMs = 1000, ScanMs = 500, ReconnectSec = 5; public bool Detailed;
        /// <summary>受信フォルダと重ねてはいけないフォルダ（画像保存フォルダ・再生フォルダ）。同一・親子のどちらも不可。</summary>
        public string[] Protected = new string[0];

        static string Norm(string p) { string s = (p ?? "").Trim().Replace('/', '\\'); try { if (s != "") s = Path.GetFullPath(s).Replace('/', '\\'); } catch (Exception) { } return s.TrimEnd('\\'); }
        /// <summary>2 つのフォルダが同一、またはどちらかがもう一方の配下にあるか。</summary>
        public static bool IsSameOrNested(string a, string b) { string x = Norm(a) + "\\", y = Norm(b) + "\\"; return x.StartsWith(y, StringComparison.OrdinalIgnoreCase) || y.StartsWith(x, StringComparison.OrdinalIgnoreCase); }
        /// <summary>parent が child と同一、または child の上位フォルダか。</summary>
        public static bool IsSameOrParent(string parent, string child) { string x = Norm(parent) + "\\", y = Norm(child) + "\\"; return y.StartsWith(x, StringComparison.OrdinalIgnoreCase); }
        public string BridgeExePath { get { string p = string.IsNullOrEmpty(BridgeExe) || BridgeExe.Trim() == "" ? DefaultBridgeExe : BridgeExe.Trim(); return Path.IsPathRooted(p) ? p : Path.Combine(BaseDir, p); } }

        /// <summary>Vapi.Net.dll へ渡すときの文字コード（Windows の ANSI コードページ。日本語 Windows は Shift_JIS で全角 1 文字＝2 バイト）でのバイト数。</summary>
        public static int AnsiBytes(string s) { return Encoding.Default.GetByteCount(s ?? ""); }
        /// <summary>受信フォルダ（絶対パスに直したもの）の ANSI でのバイト数。</summary>
        public int SpoolBytes { get { return AnsiBytes(Norm(SpoolFolder)); } }
        /// <summary>ANSI コードページで表せる文字だけか。表せない文字は Vapi.Net.dll へ渡すときに「?」へ変わり、別のフォルダになる。</summary>
        public static bool AnsiSafe(string s) { if (string.IsNullOrEmpty(s)) return true; try { Encoding e = Encoding.Default; return e.GetString(e.GetBytes(s)) == s; } catch (Exception) { return false; } }
        /// <summary>受信フォルダの検査。0＝問題なし 1＝未設定 2＝ドライブ直下 3＝長すぎる 4＝画像保存・再生フォルダと重なる 5＝EXE のフォルダ（またはその上位） 6＝ANSI で表せない文字を含む。</summary>
        public int SpoolErrorCode()
        {
            if (string.IsNullOrEmpty(SpoolFolder) || SpoolFolder.Trim() == "") return 1;
            string sp = Norm(SpoolFolder);
            if (sp.Length <= 3 || sp.EndsWith(":")) return 2;
            if (Mode == ModeBridge && !AnsiSafe(sp)) return 6;
            if (Mode == ModeBridge && AnsiBytes(sp) > SpoolMaxBytes) return 3;
            foreach (string p in Protected) if (!string.IsNullOrEmpty(p) && IsSameOrNested(sp, p)) return 4;
            if (!string.IsNullOrEmpty(BaseDir) && IsSameOrParent(sp, BaseDir)) return 5;
            return 0;
        }
        /// <summary>受信フォルダの検査（ログ用の文言）。null＝問題なし。問題があるときは監視（＝取り込み後の削除）を一切行わない。</summary>
        public string SpoolError()
        {
            int c = SpoolErrorCode(); string sp = Norm(SpoolFolder);
            switch (c)
            {
                case 0: return null;
                case 1: return "受信フォルダが未設定です";
                case 2: return "受信フォルダにドライブ直下は指定できません: " + sp;
                case 3: return "受信フォルダのパスが長すぎます（現在 " + AnsiBytes(sp) + " バイト、上限 " + SpoolMaxBytes + " バイト。全角文字は 2 バイト）: " + sp;
                case 6: return "受信フォルダのパスに、この PC の文字コード（ANSI）で表せない文字があります: " + sp;
                case 4: return "受信フォルダは画像保存フォルダ・再生フォルダと別の場所にしてください（親子関係も不可）: " + sp;
                default: return "受信フォルダに PullerView.exe のフォルダ、またはその上位フォルダは指定できません: " + sp;
            }
        }
        public List<string> Validate()
        {
            List<string> e = new List<string>(); string se = SpoolError(); if (se != null) e.Add(se);
            if (StableMs < 0 || StableMs > 60000) e.Add("stable_ms は 0〜60000"); if (ScanMs < 100 || ScanMs > 60000) e.Add("scan_ms は 100〜60000"); if (ReconnectSec < 1) e.Add("reconnect_sec は 1 以上");
            if (Mode == ModeBridge) { System.Net.IPAddress a; if (!System.Net.IPAddress.TryParse(Ip ?? "", out a)) e.Add("XG-X の IP アドレスの形式が正しくありません: " + Ip); }
            return e;
        }
    }

    /// <summary>
    /// XG-X が PC へ出力した BMP を受信フォルダから取り込む（出力する手段は問わない: XgxBridge／XG-X VisionTerminal／FTP 出力）。
    /// サイズと更新時刻が 2 回の確認で変わらず、更新から StableMs 経過し、共有なしで開けるファイルだけを取り込み、取り込んだら受信フォルダから削除する。
    /// </summary>
    public sealed class XgxSpoolWatcher
    {
        const int RetryAfterFailureSec = 30;
        sealed class Seen { public long Length; public DateTime LastWriteUtc; public DateTime RetryAtUtc = DateTime.MinValue; public bool Done, Backlog; }
        /// <summary>取り込んだ画像の引き渡し先。引数: 画像データ、出力時刻（ファイルの更新時刻）、受信フォルダからの相対パス、持ち越し（起動前からあった画像）か。戻り値 false＝保存先へ書けなかった（受信フォルダに残して再試行）。</summary>
        public delegate bool TakeHandler(byte[] data, DateTime time, string relative, bool backlog);

        readonly Func<XgxSettings> settings; readonly FileLog log; readonly TakeHandler take;
        readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase); readonly HashSet<string> warnedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Thread th; volatile bool stop; readonly ManualResetEvent wake = new ManualResetEvent(false); bool firstScan = true; string stateMsg = "";
        public volatile bool Watching; public long FilesTaken, BacklogTaken; public int PendingFiles; public string LastFile; public DateTime LastFileAt = DateTime.MinValue;
        public string StateMessage { get { return stateMsg; } }
        public Func<DateTime> UtcNow = delegate { return DateTime.UtcNow; };

        public XgxSpoolWatcher(Func<XgxSettings> settings, FileLog log, TakeHandler take) { this.settings = settings; this.log = log; this.take = take; }
        public void Start() { if (th != null) return; stop = false; wake.Reset(); seen.Clear(); warnedExt.Clear(); firstScan = true; th = new Thread(Run); th.IsBackground = true; th.Name = "XgxSpoolWatcher"; th.Start(); }
        public void Stop() { stop = true; wake.Set(); Thread t = th; if (t != null) { t.Join(5000); th = null; } Watching = false; }
        void Run()
        {
            while (!stop)
            {
                try { ScanOnce(); } catch (Exception ex) { log.Error("XG-X 受信フォルダの確認で例外: " + ex.Message); }
                wake.WaitOne(Math.Max(100, settings().ScanMs));
            }
            Set(false, "停止", false);
        }

        /// <summary>受信フォルダを 1 回確認し、書込みが完了している BMP を取り込む。戻り値＝今回取り込んだ件数。</summary>
        public int ScanOnce()
        {
            XgxSettings s = settings(); string err = s.SpoolError();
            if (err != null) { Set(false, "XG-X 受信フォルダの設定が不正です: " + err, true); return 0; }   // 取り込み後に削除するため、不正な設定では絶対に動かさない
            string root = s.SpoolFolder; FileInfo[] files;
            try { Directory.CreateDirectory(root); files = new DirectoryInfo(root).GetFiles("*", SearchOption.AllDirectories); }
            catch (Exception ex) { Set(false, "XG-X 受信フォルダにアクセスできません: " + ex.Message, true); return 0; }
            Set(true, "XG-X 受信フォルダを監視中: " + root, false);
            Array.Sort(files, delegate (FileInfo a, FileInfo b) { int c = SafeTime(a).CompareTo(SafeTime(b)); return c != 0 ? c : string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase); });
            DateTime nowUtc = UtcNow(); HashSet<string> alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int taken = 0, pending = 0; bool first = firstScan; firstScan = false;
            foreach (FileInfo f in files)
            {
                if (stop) break;
                if (!string.Equals(f.Extension, ".bmp", StringComparison.OrdinalIgnoreCase)) { WarnOther(f.Extension, f.FullName); continue; }
                alive.Add(f.FullName); pending++;
                if (TryTake(f, root, s, nowUtc, first)) { taken++; pending--; }
            }
            List<string> gone = new List<string>(); foreach (string k in seen.Keys) if (!alive.Contains(k)) gone.Add(k); foreach (string k in gone) seen.Remove(k);
            PendingFiles = pending; return taken;
        }
        static DateTime SafeTime(FileInfo f) { try { return f.LastWriteTimeUtc; } catch (Exception) { return DateTime.MinValue; } }

        bool TryTake(FileInfo f, string root, XgxSettings s, DateTime nowUtc, bool first)
        {
            long length; DateTime lwUtc, lwLocal;
            try { f.Refresh(); if (!f.Exists) return false; length = f.Length; lwUtc = f.LastWriteTimeUtc; lwLocal = f.LastWriteTime; } catch (IOException) { return false; }   // 確認中に消えた・移動された
            Seen sn;
            if (!seen.TryGetValue(f.FullName, out sn)) { sn = new Seen(); sn.Length = length; sn.LastWriteUtc = lwUtc; sn.Backlog = first; seen[f.FullName] = sn; return false; }   // 初回は記録のみ。次回の確認で変化が無ければ取り込む
            if (sn.Done) { TryDelete(f.FullName, false); return false; }   // 取り込み済みで削除だけ失敗していたもの
            if (sn.Length != length || sn.LastWriteUtc != lwUtc) { sn.Length = length; sn.LastWriteUtc = lwUtc; return false; }   // まだ書込み中
            if (length == 0) return false;
            double ageMs = (nowUtc - lwUtc).TotalMilliseconds; if (ageMs >= 0 && ageMs < s.StableMs) return false;   // 更新時刻が未来（時刻合わせ等）のときは待ち続けない
            if (nowUtc < sn.RetryAtUtc) return false;
            byte[] data;
            try
            {
                // 共有なしで開けることを、出力側が書込みを終えてファイルを閉じたことの確認に使う
                using (FileStream fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    data = new byte[fs.Length]; int read = 0; while (read < data.Length) { int n = fs.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                    if (read != data.Length) return false;
                }
            }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
            string rootFull = Path.GetFullPath(root); string rel = f.FullName.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? f.FullName.Substring(rootFull.Length).TrimStart('\\', '/') : f.Name;
            bool ok; try { ok = take(data, lwLocal, rel, sn.Backlog); } catch (Exception ex) { log.Error("XG-X 取り込み処理で例外: " + rel + " " + ex.Message); ok = false; }
            if (!ok) { sn.RetryAtUtc = nowUtc.AddSeconds(RetryAfterFailureSec); log.Warn("XG-X 取り込み失敗（受信フォルダに残して " + RetryAfterFailureSec + " 秒後に再試行します）: " + rel); return false; }
            Interlocked.Increment(ref FilesTaken); if (sn.Backlog) Interlocked.Increment(ref BacklogTaken); LastFile = rel; LastFileAt = DateTime.Now;
            if (s.Detailed) log.Info("XG-X 取り込み: " + rel + " bytes=" + data.Length + (sn.Backlog ? "（持ち越し: 解析しない）" : ""));
            if (!TryDelete(f.FullName, true)) sn.Done = true;   // 二重取り込みを防ぐ
            return true;
        }
        bool TryDelete(string path, bool logFailure) { try { File.Delete(path); return true; } catch (Exception ex) { if (logFailure) log.Warn("XG-X 受信フォルダのファイルを削除できません: " + path + " " + ex.Message); return false; } }
        void WarnOther(string ext, string path)
        {
            string e = (ext ?? "").ToLowerInvariant(); if (e != ".jpg" && e != ".jpeg" && e != ".png") return; if (!warnedExt.Add(e)) return;
            log.Warn("XG-X 受信フォルダに BMP 以外の画像があります（取り込まず残します）。XG-X の画像出力形式を BMP にしてください: " + path);
        }
        void Set(bool watching, string msg, bool warn) { if (Watching == watching && stateMsg == msg) return; Watching = watching; stateMsg = msg; if (warn) log.Warn("XG-X: " + msg); else if (settings().Detailed) log.Info("XG-X: " + msg); }
    }

    /// <summary>
    /// XgxBridge（別プロセス）を起動・監視する。ブリッジが終了した場合は再接続間隔を置いて起動し直す。
    /// 停止は標準入力へ STOP を送り、ブリッジ側で画像ロギングの停止・切断を行わせる（応答しなければ強制終了）。
    /// </summary>
    public sealed class XgxBridgeHost
    {
        public const int DetailBufferLines = 60;
        readonly Func<XgxSettings> settings; readonly FileLog log; Thread th; volatile bool stop; readonly ManualResetEvent wake = new ManualResetEvent(false); Process cur; readonly object plk = new object();
        volatile string lastError; string stateMsg = ""; readonly Queue<string> detail = new Queue<string>(); string lastDump;
        public volatile bool Connected; public long FilesReported; public int StartCount; public volatile string LastHeader; public int LastExitCode = int.MinValue;
        public string Message { get { return stateMsg; } }
        /// <summary>起動情報の生成方法。テストで差し替える（null を返すと起動しない）。</summary>
        public Func<XgxSettings, ProcessStartInfo> StartInfoFactory;

        public XgxBridgeHost(Func<XgxSettings> settings, FileLog log) { this.settings = settings; this.log = log; }
        public void Start() { if (th != null) return; stop = false; wake.Reset(); th = new Thread(Run); th.IsBackground = true; th.Name = "XgxBridgeHost"; th.Start(); }
        /// <summary>ブリッジを止める。STOP を送って最長 waitMs 待ち、終わらなければ強制終了する（戻った時点でブリッジは残っていない）。</summary>
        public void Stop(int waitMs)
        {
            stop = true; wake.Set(); Process p; lock (plk) p = cur;
            if (p != null) { try { p.StandardInput.WriteLine(XgxBridgeMessage.StopCommand); p.StandardInput.Flush(); p.StandardInput.Close(); } catch (Exception) { } }
            Thread t = th;
            if (t != null && !t.Join(waitMs))
            {
                lock (plk) p = cur;
                if (p != null) { try { if (!p.HasExited) { log.Warn("XgxBridge が停止要求に応答しないため強制終了します"); p.Kill(); } } catch (Exception) { } }
                t.Join(3000);
            }
            th = null; Connected = false;
        }

        // 末尾の \ は閉じ引用符をエスケープしてしまうため取り除く
        static string Quote(string v) { return "\"" + (v ?? "").Trim().TrimEnd('\\', '/').Replace("\"", "") + "\""; }
        public static string BuildArguments(XgxSettings s) { StringBuilder b = new StringBuilder(); b.Append("--ip ").Append(Quote(s.Ip)).Append(" --root ").Append(Quote(s.SpoolFolder)); if (!string.IsNullOrEmpty(s.VapiFolder) && s.VapiFolder.Trim() != "") b.Append(" --vapi-dir ").Append(Quote(s.VapiFolder)); return b.ToString(); }
        /// <summary>ブリッジの終了コードが「設定の誤り」（引数・32bit・Vapi.Net.dll の場所や読み込み・版の不一致）を表すか。</summary>
        public static bool IsConfigurationError(int code) { return code >= 2 && code <= 6; }
        /// <summary>V2-v4: Windows が異常終了させたときの終了コードか（0xC0000005＝アクセス違反 など）。ブリッジが検知したアクセス違反（40）も含む。</summary>
        public static bool IsCrash(int code) { return unchecked((uint)code) >= 0xC0000000u || code == 40; }
        /// <summary>異常終了が続いたときに再起動を空ける回数と秒数。</summary>
        public const int CrashBackoffCount = 3, CrashBackoffSec = 60;
        public static string CrashName(int code) { uint u = unchecked((uint)code); return code == 40 ? "40＝Vapi.Net.dll の内部でアクセス違反" : "0x" + u.ToString("X8") + (u == 0xC0000005u ? "＝アクセス違反" : u == 0xC0000409u ? "＝スタック破壊の検出" : u == 0xC00000FDu ? "＝スタック不足" : u == 0xC0000135u ? "＝DLL が見つからない" : ""); }
        int crashRun; string lastStep = "", lastWarn;
        /// <summary>異常終了が続いている回数（画面表示・試験用）。</summary>
        public int CrashRun { get { return crashRun; } }

        /// <summary>
        /// Windows のイベントログ（アプリケーション）から、ブリッジの異常終了の記録（障害が起きたモジュール名・オフセット）を探す。
        /// 取得できない環境では空を返す。読み取りが止まっても本体を止めないよう、別スレッドで実行して最大 4 秒だけ待つ。
        /// </summary>
        public static string[] ReadCrashEvents(string exeName, DateTime since)
        {
            List<string> r = new List<string>();
            Thread t = new Thread(delegate ()
            {
                try
                {
                    using (EventLog ev = new EventLog("Application"))
                    {
                        EventLogEntryCollection es = ev.Entries; int n = es.Count;
                        for (int i = n - 1; i >= 0 && i >= n - 400; i--)
                        {
                            EventLogEntry e = es[i]; if (e.TimeGenerated < since) break;
                            string src = e.Source ?? ""; if (src != "Application Error" && src != ".NET Runtime") continue;
                            string msg = e.Message ?? ""; if (msg.IndexOf(exeName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            msg = msg.Replace("\r", " ").Replace("\n", " "); while (msg.Contains("  ")) msg = msg.Replace("  ", " ");
                            lock (r) { if (r.Count < 2) r.Add(src + "（ID " + (e.InstanceId & 0xFFFF) + "）: " + (msg.Length > 900 ? msg.Substring(0, 900) + "…" : msg)); }
                        }
                    }
                }
                catch (Exception) { /* イベントログを読めない環境。記録なしとして扱う */ }
            });
            t.IsBackground = true; t.Start(); t.Join(4000);
            lock (r) return r.ToArray();
        }
        /// <summary>再起動までの待ち時間。設定の誤りは待っても直らないため、ログが同じエラーで埋まらないよう 30 秒以上空ける。</summary>
        public static int RetryDelaySeconds(int code, XgxSettings s) { int n = Math.Max(1, s.ReconnectSec); return IsConfigurationError(code) ? Math.Max(30, n) : n; }

        void Run()
        {
            while (!stop)
            {
                XgxSettings s = settings(); int delay = RetryDelaySeconds(0, s); ProcessStartInfo psi = null; string serr = s.SpoolError();
                if (serr != null) Set(false, "XG-X 受信フォルダの設定が不正です: " + serr, true, false);
                else
                {
                    try { psi = CreateStartInfo(s); } catch (Exception ex) { Set(false, "XgxBridge を起動できません: " + ex.Message, true, false); }
                }
                if (psi != null)
                {
                    try
                    {
                        DateTime started = DateTime.Now.AddSeconds(-2); int code = RunOnce(psi); LastExitCode = code;
                        if (!stop && IsCrash(code))
                        {   // V2-v4: Vapi.Net.dll の内部などで異常終了した。原因の手がかりを残し、続くときは再起動の間隔を空ける
                            crashRun++; delay = crashRun >= CrashBackoffCount ? Math.Max(CrashBackoffSec, RetryDelaySeconds(code, s)) : RetryDelaySeconds(code, s);
                            if (crashRun == 1 || crashRun % 10 == 0)
                            {
                                Thread.Sleep(1500);   // Windows がイベントログへ書くのを待つ
                                string[] evs = ReadCrashEvents(Path.GetFileName(psi.FileName), started);
                                if (evs.Length == 0) log.Warn("XgxBridge の異常終了: Windows のイベントログ（アプリケーション）に記録が見つかりません（または読めません）");
                                foreach (string e in evs) log.Warn("XgxBridge の異常終了（Windows のイベントログ）: " + e);
                            }
                            Set(false, "XgxBridge が異常終了しました（終了コード " + CrashName(code) + "、連続 " + crashRun + " 回）。直前の呼び出し: " + (lastStep == "" ? "（不明）" : lastStep) + "。" + delay + " 秒後に再起動します。", true, false);
                        }
                        else if (!stop) { crashRun = 0; delay = RetryDelaySeconds(code, s); string le = lastError; Set(false, string.IsNullOrEmpty(le) ? "XgxBridge が終了しました（終了コード " + code + "）。" + delay + " 秒後に再起動します。" : le + "（終了コード " + code + "。" + delay + " 秒後に再試行します）", le != null, false); }
                    }
                    catch (Exception ex) { log.Error("XgxBridge の実行で例外: " + ex.Message); Set(false, "XgxBridge を起動できません: " + ex.Message, true, false); }
                }
                if (stop) break;
                wake.WaitOne(delay * 1000);
            }
            Connected = false; stateMsg = "停止";
        }
        ProcessStartInfo CreateStartInfo(XgxSettings s)
        {
            if (StartInfoFactory != null) return StartInfoFactory(s);
            string exe = s.BridgeExePath;
            if (!File.Exists(exe)) { Set(false, "XgxBridge が見つかりません: " + exe, true, false); return null; }
            ProcessStartInfo psi = new ProcessStartInfo(exe, BuildArguments(s)); psi.WorkingDirectory = Path.GetDirectoryName(exe) ?? s.BaseDir; return psi;
        }
        int RunOnce(ProcessStartInfo psi)
        {
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            using (Process p = new Process())
            {
                p.StartInfo = psi; Set(false, "XgxBridge を起動中", false, true); lastError = null; lastStep = ""; lock (detail) detail.Clear();
                p.Start(); lock (plk) cur = p; StartCount++;
                try
                {
                    log.Info("XgxBridge 起動 pid=" + p.Id + " " + psi.FileName + " " + psi.Arguments);
                    p.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) log.Warn("XgxBridge(stderr): " + e.Data); };
                    p.BeginErrorReadLine();
                    if (stop) { try { p.StandardInput.WriteLine(XgxBridgeMessage.StopCommand); p.StandardInput.Close(); } catch (Exception) { } }   // 起動と停止要求が重なった場合
                    string line; while ((line = p.StandardOutput.ReadLine()) != null) Handle(line);
                    if (!p.WaitForExit(5000)) { log.Warn("XgxBridge が終了しないため強制終了します pid=" + p.Id); try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { } }
                    int code = -1; try { code = p.ExitCode; } catch (Exception) { }
                    Connected = false; log.Info("XgxBridge 終了 code=" + code); if (code != 0) DumpDetail(code);
                    return code;
                }
                finally { lock (plk) cur = null; }
            }
        }

        /// <summary>ブリッジの通知 1 行を処理する（テストからも呼ぶ）。</summary>
        public void Handle(string line)
        {
            XgxBridgeMessage m = XgxBridgeMessage.Parse(line);
            switch (m.Kind)
            {
                case XgxBridgeMessage.State:
                    if (m.Name == XgxBridgeMessage.StateLogging) Set(true, m.Text, false, false);
                    else { bool error = m.Name == XgxBridgeMessage.StateError; if (error) lastError = m.Text; Set(false, m.Name + ": " + m.Text, error, !error); }   // 途中経過・停止は詳細ログ、エラーは警告
                    break;
                case XgxBridgeMessage.File: Interlocked.Increment(ref FilesReported); if (settings().Detailed) log.Info("XgxBridge: 保存通知 unit=" + m.UnitNo + " path=" + m.Text); break;
                case XgxBridgeMessage.Header: LastHeader = m.Text; log.Info("XgxBridge: 検査設定 " + m.Text); break;
                case XgxBridgeMessage.Log:
                    if (m.Name == "ERROR") log.Error("XgxBridge: " + m.Text); else if (m.Name == "WARN") { if (m.Text == lastWarn) Detail(m.Text); else { lastWarn = m.Text; log.Warn("XgxBridge: " + m.Text); } } else if (m.Name == XgxBridgeMessage.LogDebug) Detail(m.Text); else log.Info("XgxBridge: " + m.Text);
                    break;
                default: if (m.Text.Trim() != "" && settings().Detailed) log.Info("XgxBridge(出力): " + m.Text); break;
            }
        }
        // ブリッジの詳細ログ（接続の各段階）。通常はログに出さず、異常終了したときだけまとめて出力する
        void Detail(string text) { if (text != null && text.StartsWith("呼び出し: ")) lastStep = text.Substring(6); if (settings().Detailed) log.Info("XgxBridge: " + text); lock (detail) { detail.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text); while (detail.Count > DetailBufferLines) detail.Dequeue(); } }
        /// <summary>異常終了時に直前の詳細ログを警告として出力する。同じ内容で繰り返し終了する場合は最初の 1 回だけ。</summary>
        void DumpDetail(int code)
        {
            string[] lines; lock (detail) { lines = detail.ToArray(); detail.Clear(); }
            if (lines.Length == 0) return;
            StringBuilder body = new StringBuilder().Append(code); foreach (string l in lines) body.Append('\n').Append(l.Length > 13 ? l.Substring(13) : l);   // 時刻（先頭 13 文字）を除いて比べる
            string key = body.ToString(); if (key == lastDump) return; lastDump = key;
            log.Warn("XgxBridge が終了コード " + code + " で終了しました。直前の詳細ログ " + lines.Length + " 行:"); foreach (string l in lines) log.Warn("  XgxBridge(詳細) " + l);
        }
        void Set(bool connected, string msg, bool warn, bool isDetail)
        {
            if (Connected == connected && stateMsg == msg) return; Connected = connected; stateMsg = msg;
            if (warn) log.Warn("XgxBridge: " + msg); else if (isDetail) Detail(msg); else log.Info("XgxBridge: " + msg);
        }
    }
}
