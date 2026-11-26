// PullerView.XgxBridge — XG-X 画像ロギング ブリッジ（32bit / .NET Framework 4.0）。
// CvImageSaver v1.4.2 の tools/CvImageSaver.XgxBridge を移植したもの。Vapi.Net.dll（KEYENCE XG-X VisionTerminal 付属）で XG-X を検出・接続し、
// 画像ロギングを開始する。画像ファイルの書込みは Vapi.Net.dll 自身が --root のフォルダへ行う。本プログラムは状態と保存通知を標準出力へ 1 行ずつ書く。
//
// 元との違い: Vapi.Net.dll をビルド時に参照せず、実行時にリフレクションで呼び出す。
//   理由 1) Vapi.Net.dll は KEYENCE の著作物のためリポジトリに置けない。ビルド時参照にすると、DLL を持たない PC ではソリューションがビルドできない。
//   理由 2) Vapi.Net.dll は .NET Framework 4.7.2 向けにビルドされており、.NET Framework 4.0 のプロジェクトからは通常の参照ができない。
//   呼び出す API・順序・引数は元（v1.4.2。2026-11-25 に実機で確認された手順）と同じ。起動時に、使う API がすべてあるかを確認してから始める（Vapi.CheckApi）。
//
// 通知の形式（タブ区切り 1 行。本体側 XgxCore40.cs の XgxBridgeMessage と合わせること）:
//   STATE\t状態名\t説明 ／ FILE\tユニット番号\t保存パス ／ HEADER\t説明 ／ LOG\tレベル\t本文。停止要求は標準入力の STOP。
// 終了コード: 0 正常 / 2 引数 / 3 32bit でない / 4 Vapi.Net.dll が無い / 5 Vapi.Net.dll を読み込めない / 6 Vapi.Net.dll の版が想定と違う（API が無い）
//             10 予期しない例外 / 20 検出失敗 / 21 createSession / 22 connect / 23 startLog / 24 エンジン起動 / 30〜33 ロギング中の異常 / 40 Vapi 内部のアクセス違反
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

namespace PullerView.XgxBridge
{
    /// <summary>起動引数。</summary>
    internal sealed class Options
    {
        public string Ip = "", Root = "", VapiDir;
        /// <summary>コントローラ検出の待ち時間（ミリ秒）。Vapi.Net.dll が受け付ける範囲は 1000〜60000。</summary>
        public int FindTimeoutMs = 10000;
        /// <summary>Vapi エンジンの起動時に渡すアプリ種別（VapiWinSimServices.ApplicationId。1＝APP_ID_VISION_REMOCON）。-1 で起動を省略する。</summary>
        public int EngineAppId = 1;
        public int EngineTimeoutMs = 60000;
        public bool ListApi;
        /// <summary>V2-v4: Vapi.Net.dll と、それが必要とする DLL を読み込めるかだけを調べて終了する。</summary>
        public bool Check;

        public static Options Parse(string[] args, out string error)
        {
            Options o = new Options(); error = "";
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i], value = i + 1 < args.Length ? args[i + 1] : null;
                switch (key)
                {
                    case "--list-api": o.ListApi = true; break;
                    case "--check": o.Check = true; break;
                    case "--ip": if (value == null) { error = "--ip の値がありません"; return null; } o.Ip = value; i++; break;
                    case "--root": if (value == null) { error = "--root の値がありません"; return null; } o.Root = value; i++; break;
                    case "--vapi-dir": if (value == null) { error = "--vapi-dir の値がありません"; return null; } o.VapiDir = value; i++; break;
                    case "--find-timeout": if (!Int(value, 1000, 60000, out o.FindTimeoutMs)) { error = "--find-timeout は 1000〜60000（ミリ秒）で指定してください"; return null; } i++; break;
                    case "--engine-app-id": if (!Int(value, -1, 7, out o.EngineAppId)) { error = "--engine-app-id は -1〜7 で指定してください"; return null; } i++; break;
                    case "--engine-timeout": if (!Int(value, 1000, 300000, out o.EngineTimeoutMs)) { error = "--engine-timeout は 1000〜300000（ミリ秒）で指定してください"; return null; } i++; break;
                    default: error = "不明な引数: " + key; return null;
                }
            }
            if (o.ListApi || o.Check) return o;
            if (o.Ip.Length == 0) { error = "--ip（XG-X の IP アドレス）を指定してください"; return null; }
            if (o.Root.Length == 0) { error = "--root（受信フォルダ）を指定してください"; return null; }
            return o;
        }
        static bool Int(string v, int lo, int hi, out int r) { r = 0; return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) && r >= lo && r <= hi; }
    }

    /// <summary>標準出力への通知（1 行 1 通知、項目はタブ区切り）。</summary>
    internal static class Out
    {
        static readonly object Sync = new object();
        public static void State(string name, string text) { Write("STATE\t" + name + "\t" + Clean(text)); }
        public static void File(int unitNo, string path) { Write("FILE\t" + unitNo.ToString(CultureInfo.InvariantCulture) + "\t" + Clean(path)); }
        public static void Header(string text) { Write("HEADER\t" + Clean(text)); }
        public static void Log(string level, string text) { Write("LOG\t" + level + "\t" + Clean(text)); }
        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
        static void Write(string line) { lock (Sync) { try { Console.Out.WriteLine(line); Console.Out.Flush(); } catch (Exception) { /* 親プロセスが先に終了した場合 */ } } }
    }

    /// <summary>Vapi.Net.dll（名前空間 Keyence.Ve.Interop）のリフレクション呼び出し。</summary>
    internal sealed class Vapi
    {
        public const string Ns = "Keyence.Ve.Interop.";
        /// <summary>使用する API の一覧（型, メンバー, 引数の数。引数の数 -1＝プロパティまたはフィールド、-2＝型だけ）。起動時の確認と、--list-api の出力に使う。</summary>
        public static readonly string[][] Api =
        {
            new[] { "VapiStatus", "val", "-1" },
            new[] { "VapiWinSimServices", "getInstance", "0" }, new[] { "VapiWinSimServices", "startUpByEngineMode", "2" }, new[] { "VapiWinSimServices+ApplicationId", "", "-2" },
            new[] { "VapiStringConvertController", "getInstance", "0" }, new[] { "VapiStringConvertController", "updateConvertLanguage", "1" }, new[] { "VapiLanguageType", "", "-2" },
            new[] { "VapiCommControllerFindService", "getInstance", "0" }, new[] { "VapiCommControllerFindService", "setCallbackFunc", "1" }, new[] { "VapiCommControllerFindService", "clearInfo", "0" },
            new[] { "VapiCommControllerFindService", "findControllerEther", "2" }, new[] { "VapiCommControllerFindService", "cancelFindRequest", "0" }, new[] { "VapiCommControllerFindService", "getControllerInfo", "1" },
            new[] { "VapiCommControllerFindService+VapiCommControllerFindServiceCallbackFuncDelegate", "Invoke", "1" },
            new[] { "VapiCommSystemInfo", "_systemId", "-1" }, new[] { "VapiCommSystemInfo", "_controllerName", "-1" }, new[] { "VapiCommSystemInfo", "_commDeviceType", "-1" }, new[] { "VapiCommSystemInfo", "_addressName", "-1" },
            new[] { "VapiCommSystemInfo", "_portName", "-1" }, new[] { "VapiCommSystemInfo", "getProtocolVersion", "0" },
            new[] { "VapiCommServices", "getInstance", "0" }, new[] { "VapiCommServices", "isSessionAvailable", "1" }, new[] { "VapiCommServices", "createSession", "1" }, new[] { "VapiCommServices", "deleteSession", "1" },
            new[] { "VapiCommImageLoggingService", "getInstance", "0" }, new[] { "VapiCommImageLoggingService", "addCallback", "1" }, new[] { "VapiCommImageLoggingService", "removeCallback", "0" },
            new[] { "VapiCommImageLoggingService", "connect", "1" }, new[] { "VapiCommImageLoggingService", "disconnect", "2" }, new[] { "VapiCommImageLoggingService", "startLog", "3" }, new[] { "VapiCommImageLoggingService", "stopLog", "1" },
            new[] { "VapiCommImageLoggingService", "getHeaderInfo", "2" }, new[] { "VapiCommImageLoggingService", "getSaveFilePath", "3" },
            new[] { "VapiCommImageLoggingService+VapiCommImageLoggingServiceCallbackFuncDelegate", "Invoke", "4" },
            new[] { "VapiImageLogHeader", "unitNum", "-1" }, new[] { "VapiImageLogHeader", "unitNo", "-1" }, new[] { "VapiImageLogHeader", "settingName", "-1" }, new[] { "VapiImageLogHeader", "settingId", "-1" },
            new[] { "VapiImageLogUnitInfo_Array", "get_Item", "1" }, new[] { "VapiImageLogUnitInfo", "unitNo", "-1" }, new[] { "VapiImageLogUnitInfo", "unitName", "-1" },
            new[] { "VapiSettingId", "deviceId", "-1" }, new[] { "VapiSettingId", "settingNumber", "-1" },
            new[] { "VapiCommRemoteDesktopService", "getInstance", "0" }, new[] { "VapiCommRemoteDesktopService", "addCallback", "1" }, new[] { "VapiCommRemoteDesktopService+VapiCommRemoteDesktopServiceCallbackFuncDelegate", "Invoke", "2" },
            new[] { "VapiCommResultLoggingService", "getInstance", "0" }, new[] { "VapiCommResultLoggingService", "addCallback", "1" }, new[] { "VapiCommResultLoggingService+VapiCommResultLoggingServiceCallbackFuncDelegate", "Invoke", "3" },
            new[] { "VapiCommHistoryLoggingService", "getInstance", "0" }, new[] { "VapiCommHistoryLoggingService", "addCallback", "1" }, new[] { "VapiCommHistoryLoggingService+VapiCommHistoryLoggingServiceCallbackFuncDelegate", "Invoke", "3" },
            new[] { "VapiCommRemoteFileService", "getInstance", "0" }, new[] { "VapiCommRemoteFileService", "addCallback", "1" }, new[] { "VapiCommRemoteFileService+VapiCommRemoteFileServiceCallbackFuncDelegate", "Invoke", "3" },
        };
        const BindingFlags All = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        readonly Assembly asm;
        public Vapi(Assembly asm) { this.asm = asm; }

        public Type T(string name) { Type t = asm.GetType(Ns + name, false); if (t == null) throw new MissingMemberException("Vapi.Net.dll に型 " + name + " がありません"); return t; }
        static MethodInfo Find(Type t, string name, int argc)
        {
            MethodInfo hit = null;
            foreach (MethodInfo m in t.GetMethods(All)) { if (m.Name != name || m.GetParameters().Length != argc) continue; if (hit != null) throw new AmbiguousMatchException(t.Name + "." + name + "（引数 " + argc + " 個）が複数あります"); hit = m; }
            if (hit == null) throw new MissingMethodException(t.Name + "." + name + "（引数 " + argc + " 個）がありません");
            return hit;
        }
        /// <summary>使用する API がすべてあるかを確認する。無いものの一覧を返す（空＝問題なし）。</summary>
        public List<string> CheckApi()
        {
            List<string> missing = new List<string>();
            foreach (string[] a in Api)
            {
                Type t = asm.GetType(Ns + a[0], false); int argc = int.Parse(a[2], CultureInfo.InvariantCulture);
                if (t == null) { if (!missing.Contains(a[0])) missing.Add(a[0]); continue; }
                if (argc == -2) continue;
                if (argc == -1) { if (t.GetProperty(a[1], All) == null && t.GetField(a[1], All) == null) missing.Add(a[0] + "." + a[1]); continue; }
                try { Find(t, a[1], argc); } catch (Exception) { missing.Add(a[0] + "." + a[1] + "(" + argc + ")"); }
            }
            return missing;
        }
        /// <summary>メソッド呼び出し。out 引数は args の同じ位置へ書き戻される。列挙型の引数は整数で渡せる。Vapi 内で起きた例外は中身をそのまま投げ直す。</summary>
        public static object CallOn(Type t, object target, string name, params object[] args)
        {
            MethodInfo m = Find(t, name, args.Length); ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++) { Type pt = ps[i].ParameterType; if (pt.IsEnum && args[i] != null && !pt.IsInstanceOfType(args[i])) args[i] = Enum.ToObject(pt, args[i]); }
            try { return m.Invoke(target, args); }
            catch (TargetInvocationException ex) { if (ex.InnerException != null) throw ex.InnerException; throw; }
        }
        public static object Call(object target, string name, params object[] args) { return CallOn(target.GetType(), target, name, args); }
        public object Instance(string type) { return CallOn(T(type), null, "getInstance"); }
        /// <summary>プロパティまたはフィールドの値。</summary>
        public static object Get(object target, string name)
        {
            Type t = target.GetType(); PropertyInfo p = t.GetProperty(name, All); if (p != null) return p.GetValue(target, null);
            FieldInfo f = t.GetField(name, All); if (f != null) return f.GetValue(target);
            throw new MissingMemberException(t.Name + "." + name + " がありません");
        }
        public static int GetInt(object target, string name) { return Convert.ToInt32(Get(target, name), CultureInfo.InvariantCulture); }
        public static string GetStr(object target, string name) { object v = Get(target, name); return v == null ? "" : v.ToString(); }
        /// <summary>VapiStatus の値の名前（SUCCESS / NOT_FOUND など）。</summary>
        public static string St(object status) { if (status == null) return "(null)"; object v = Get(status, "val"); return v == null ? "(null)" : v.ToString(); }
        /// <summary>Vapi のコールバック用デリゲートを作る。引数は object[] にまとめて handler へ渡す。
        /// Vapi.Net.dll はコールバックを delegate.BeginInvoke（非同期呼び出し）で呼ぶ。式ツリーをコンパイルしたデリゲート（DynamicMethod）は BeginInvoke できず、
        /// ArgumentException「オブジェクトはランタイム Reflection オブジェクトでなければなりません」で落ちる（2026-11-26 実機ログ）。
        /// そのため、通常のメソッド（CallbackSink.H1〜H8 を引数の型で閉じたもの）に結び付けたデリゲートを作る。</summary>
        public Delegate MakeDelegate(string delegateType, Action<object[]> handler)
        {
            Type dt = T(delegateType); MethodInfo inv = dt.GetMethod("Invoke"); ParameterInfo[] ps = inv.GetParameters();
            if (inv.ReturnType != typeof(void) || ps.Length < 1 || ps.Length > 8) throw new NotSupportedException(delegateType + ": 対応していないコールバックの形です（戻り値 " + inv.ReturnType.Name + "、引数 " + ps.Length + " 個）");
            Type[] ts = new Type[ps.Length];
            for (int i = 0; i < ps.Length; i++) { ts[i] = ps[i].ParameterType; if (ts[i].IsByRef || ts[i].IsPointer) throw new NotSupportedException(delegateType + ": ref / out / ポインタ引数のコールバックには対応していません（" + ps[i].Name + "）"); }
            MethodInfo m = typeof(CallbackSink).GetMethod("H" + ps.Length).MakeGenericMethod(ts);
            return Delegate.CreateDelegate(dt, new CallbackSink(handler), m);
        }
    }

    /// <summary>Vapi のコールバックを受ける実体。BeginInvoke できるよう、動的生成ではない通常のメソッドにしてある。</summary>
    public sealed class CallbackSink
    {
        readonly Action<object[]> h;
        public CallbackSink(Action<object[]> handler) { h = handler; }
        public void H1<A>(A a) { h(new object[] { a }); }
        public void H2<A, B>(A a, B b) { h(new object[] { a, b }); }
        public void H3<A, B, C>(A a, B b, C c) { h(new object[] { a, b, c }); }
        public void H4<A, B, C, D>(A a, B b, C c, D d) { h(new object[] { a, b, c, d }); }
        public void H5<A, B, C, D, E>(A a, B b, C c, D d, E e) { h(new object[] { a, b, c, d, e }); }
        public void H6<A, B, C, D, E, F>(A a, B b, C c, D d, E e, F f) { h(new object[] { a, b, c, d, e, f }); }
        public void H7<A, B, C, D, E, F, G>(A a, B b, C c, D d, E e, F f, G g) { h(new object[] { a, b, c, d, e, f, g }); }
        public void H8<A, B, C, D, E, F, G, H>(A a, B b, C c, D d, E e, F f, G g, H hh) { h(new object[] { a, b, c, d, e, f, g, hh }); }
    }

    internal static class Program
    {
        /// <summary>設定画面での項目名（メッセージ用）。</summary>
        const string SettingName = "VisionTerminal のインストール先フォルダ";
        /// <summary>Vapi.Net.dll が直接必要とする DLL のうち、Windows 標準ではなく VisionTerminal と同じフォルダにあるもの。</summary>
        static readonly string[] CompanionDlls = { "libEGL.dll", "libGLESv2.dll" };
        /// <summary>Vapi.Net.dll の読み込みに成功したら true。</summary>
        internal static volatile bool VapiLoaded;
        internal const int CrashExitCode = 40;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectory(string lpPathName);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);
        const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x8;
        /// <summary>動作を確認した Vapi.Net.dll の版（先頭 2 桁）。</summary>
        internal const string VerifiedVapiVersion = "3.7.";

        // ===== V2-v4: 読み込めない原因・異常終了の原因を調べるための処理 =====
        /// <summary>DLL のインポート表（通常・遅延）から、必要とする DLL の名前を読む。</summary>
        internal static List<string> PeImports(string path)
        {
            List<string> r = new List<string>();
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (BinaryReader br = new BinaryReader(fs))
            {
                fs.Position = 0x3C; int pe = br.ReadInt32(); fs.Position = pe + 6; int nsec = br.ReadUInt16(); fs.Position = pe + 20; int optSize = br.ReadUInt16();
                long opt = pe + 24; fs.Position = opt; int magic = br.ReadUInt16(); long dd = opt + (magic == 0x10B ? 96 : 112);
                uint[] va = new uint[nsec], vs = new uint[nsec], ra = new uint[nsec];
                for (int i = 0; i < nsec; i++) { fs.Position = opt + optSize + 40L * i + 8; uint vsz = br.ReadUInt32(); va[i] = br.ReadUInt32(); uint rsz = br.ReadUInt32(); ra[i] = br.ReadUInt32(); vs[i] = Math.Max(vsz, rsz); }
                Func<uint, long> off = delegate (uint rva) { for (int i = 0; i < nsec; i++) if (rva >= va[i] && rva < va[i] + vs[i]) return (long)(rva - va[i]) + ra[i]; return -1L; };
                // { データディレクトリの番号, 1 件の大きさ, 名前の RVA の位置 }  1＝インポート表、13＝遅延インポート表
                foreach (int[] t in new[] { new[] { 1, 20, 12 }, new[] { 13, 32, 4 } })
                {
                    fs.Position = dd + 8L * t[0]; uint rva = br.ReadUInt32(); if (rva == 0) continue; long o = off(rva); if (o < 0) continue;
                    for (int n = 0; n < 512; n++)
                    {
                        fs.Position = o + (long)t[1] * n; byte[] e = br.ReadBytes(t[1]); if (e.Length < t[1]) break;
                        uint name = BitConverter.ToUInt32(e, t[2]); if (name == 0) break;
                        long no = off(name); if (no < 0) break; fs.Position = no;
                        StringBuilder sb = new StringBuilder(); for (int k = 0; k < 260; k++) { int c = fs.ReadByte(); if (c <= 0) break; sb.Append((char)c); }
                        if (sb.Length > 0) r.Add(sb.ToString());
                    }
                }
            }
            return r;
        }

        /// <summary>
        /// file が必要とする DLL を 1 つずつ読み込んでみて、読み込めないものを返す。
        /// Vapi.Net.dll と同じフォルダにある DLL はそこから、無いものは Windows の検索順（SetDllDirectory で指定したフォルダを含む）で読み込む。
        /// </summary>
        internal static List<string> FindMissing(string vapiDir, string file, int depth, HashSet<string> seen)
        {
            List<string> bad = new List<string>(); List<string> imports;
            try { imports = PeImports(file); } catch (Exception ex) { bad.Add(Path.GetFileName(file) + "（必要な DLL の一覧を読めません: " + ex.Message + "）"); return bad; }
            foreach (string name in imports)
            {
                if (!seen.Add(name.ToLowerInvariant()) || name.Equals("mscoree.dll", StringComparison.OrdinalIgnoreCase)) continue;
                string local = Path.Combine(vapiDir, name); bool here = File.Exists(local);
                IntPtr h = LoadLibraryEx(here ? local : name, IntPtr.Zero, here ? LOAD_WITH_ALTERED_SEARCH_PATH : 0); int err = h == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                if (h != IntPtr.Zero) continue;
                if (here && err == 126 && depth < 4) { List<string> sub = FindMissing(vapiDir, local, depth + 1, seen); if (sub.Count > 0) { bad.AddRange(sub); continue; } }
                bad.Add(name + "（" + Path.GetFileName(file) + " が必要。" + (err == 126 ? "見つかりません" : err == 193 ? "32bit 用の DLL ではありません" : err == 127 ? "版が古く、必要な関数がありません" : "読み込みエラー " + err) + "）");
            }
            return bad;
        }
        /// <summary>読み込めない DLL の説明文。無ければ空文字。Visual C++ のランタイムが含まれるときは、入れるものを添える。</summary>
        internal static string MissingText(string vapiDir, string vapiDll)
        {
            List<string> bad;
            try { bad = FindMissing(vapiDir, vapiDll, 0, new HashSet<string>()); } catch (Exception ex) { return " 読み込めない DLL の調査に失敗しました: " + ex.Message + "。"; }
            if (bad.Count == 0) return " 必要な DLL を 1 つずつ調べましたが、読み込めないものは見つかりませんでした。";
            string all = string.Join("、", bad.ToArray()), low = all.ToLowerInvariant(); bool vc = false;
            foreach (string k in new[] { "mfc140", "msvcp140", "vcruntime140", "vcomp140", "concrt140", "api-ms-win-crt" }) if (low.Contains(k)) vc = true;
            return " 読み込めない DLL: " + all + "。" + (vc ? "これらは Visual C++ 2015-2022 再頒布可能パッケージ (x86) に含まれます。この PC に x86 版（Vapi.Net.dll 3.7 は 14.44 以降）をインストールしてください。" : "");
        }
        /// <summary>このプロセスに読み込まれている DLL のうち、Windows 標準以外と Visual C++ のランタイムを詳細ログへ出す（異常終了時に、どの DLL がどこから読み込まれたかを残す）。</summary>
        internal static void LogModules()
        {
            try
            {
                string win = (Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows").TrimEnd('\\') + "\\"; int n = 0;
                foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                {
                    string f = m.FileName ?? "", low = Path.GetFileName(f).ToLowerInvariant();
                    bool vc = low.StartsWith("mfc140") || low.StartsWith("msvcp140") || low.StartsWith("vcruntime140") || low.StartsWith("vcomp140") || low.StartsWith("concrt140") || low.StartsWith("ucrtbase") || low.StartsWith("d3dcompiler");
                    if (!vc && f.StartsWith(win, StringComparison.OrdinalIgnoreCase)) continue;
                    string ver = ""; try { ver = m.FileVersionInfo.FileVersion ?? ""; } catch (Exception) { }
                    Out.Log("DEBUG", "読み込み済み: " + Path.GetFileName(f) + " " + ver + "  " + Path.GetDirectoryName(f)); if (++n >= 30) break;
                }
            }
            catch (Exception ex) { Out.Log("DEBUG", "読み込み済み DLL の一覧を取得できません: " + ex.Message); }
        }
        /// <summary>フォルダにファイルを作れるか（Vapi のエンジンは作業フォルダ＝Vapi.Net.dll のフォルダで動く）。</summary>
        internal static bool CanWrite(string dir)
        {
            try { using (new FileStream(Path.Combine(dir, "~pvxgx" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { } return true; }
            catch (Exception) { return false; }
        }
        static string FileVer(string path) { try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? ""; } catch (Exception) { return ""; } }
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
        /// <summary>後始末を行わずにプロセスを即時終了する（Vapi のエンジンのスレッドが残っても終了処理で止まらないようにする）。</summary>
        internal static void Kill(int exitCode) { try { TerminateProcess(GetCurrentProcess(), (uint)exitCode); } catch (Exception) { Environment.Exit(exitCode); } }

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (Exception) { /* コンソールなし */ }
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Out.State("ERROR", "予期しない例外: " + (ex == null ? "(不明)" : ex.GetType().Name + ": " + ex.Message) + "（直前の呼び出し: " + LoggingSession.LastStep + "）");
                if (ex != null) Out.Log("ERROR", ex.ToString());
            };
            try { return Start(args); }
            catch (Exception ex) { Out.State("ERROR", ex.GetType().Name + ": " + ex.Message); Out.Log("ERROR", ex.ToString()); return 10; }
        }

        static int Start(string[] args)
        {
            string error; Options o = Options.Parse(args, out error);
            if (o == null)
            {
                Out.State("ERROR", error);
                Out.Log("INFO", "使い方: PullerView.XgxBridge.exe --ip <XG-XのIP> --root <受信フォルダ> [--vapi-dir <XG-X VisionTerminalのインストール先フォルダ>] [--find-timeout 10000] [--engine-app-id 1] [--engine-timeout 60000] ／ --check [--vapi-dir <フォルダ>] ／ --list-api");
                return 2;
            }
            if (o.ListApi) { foreach (string[] a in Vapi.Api) Console.Out.WriteLine(a[0] + "\t" + a[1] + "\t" + a[2]); return 0; }
            if (IntPtr.Size != 4) { Out.State("ERROR", "32bit プロセスとして起動されていません（Vapi.Net.dll は 32bit 専用です）"); return 3; }

            string vapiDir = string.IsNullOrEmpty(o.VapiDir) || o.VapiDir.Trim() == "" ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(o.VapiDir);
            string vapiDll = Path.Combine(vapiDir, "Vapi.Net.dll");
            if (!File.Exists(vapiDll))
            {
                Out.State("ERROR", "Vapi.Net.dll が見つかりません: " + vapiDll + "。設定「" + SettingName + "」に、XG-X VisionTerminal をインストールしたフォルダ（Vapi.Net.dll のあるフォルダ）を指定してください。");
                return 4;
            }
            List<string> missing = new List<string>(); foreach (string name in CompanionDlls) if (!File.Exists(Path.Combine(vapiDir, name))) missing.Add(name);
            Out.Log("DEBUG", "Vapi.Net.dll のフォルダ: " + vapiDir + DescribeFile(vapiDll) + "、.NET " + Environment.Version);
            if (missing.Count > 0) Out.Log("WARN", "Vapi.Net.dll と同じフォルダに " + string.Join("、", missing.ToArray()) + " がありません。VisionTerminal のインストール先フォルダではない可能性があります。");

            // Vapi.Net.dll と、それが必要とする DLL 群を VisionTerminal のフォルダから読み込めるようにする
            try { SetDllDirectory(vapiDir); } catch (Exception ex) { Out.Log("WARN", "SetDllDirectory: " + ex.Message); }
            Environment.CurrentDirectory = vapiDir;
            AppDomain.CurrentDomain.AssemblyResolve += delegate (object s, ResolveEventArgs e)
            {
                string path = Path.Combine(vapiDir, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };

            Vapi vapi;
            try { vapi = new Vapi(Assembly.LoadFrom(vapiDll)); VapiLoaded = true; }
            catch (Exception ex)
            {
                // Vapi.Net.dll、またはそれが必要とする DLL を読み込めなかった
                // V2-v4: 何が読み込めないのかを 1 つずつ調べて表示する
                Out.State("ERROR", "Vapi.Net.dll を読み込めません（" + vapiDir + "）: " + ex.Message + (missing.Count > 0 ? " 同じフォルダに " + string.Join("、", missing.ToArray()) + " がありません。" : "")
                    + MissingText(vapiDir, vapiDll)
                    + " 設定「" + SettingName + "」に XG-X VisionTerminal のインストール先フォルダそのものを指定する方法もあります。");
                Out.Log("DEBUG", ex.ToString());
                return 5;
            }
            List<string> lack;
            try { lack = vapi.CheckApi(); }
            catch (Exception ex) { Out.State("ERROR", "Vapi.Net.dll の内容を確認できません: " + ex.GetType().Name + ": " + ex.Message); Out.Log("ERROR", ex.ToString()); return 5; }
            if (lack.Count > 0)
            {
                Out.State("ERROR", "Vapi.Net.dll の版が想定と異なります（次の API がありません: " + string.Join(", ", lack.ToArray()) + "）。確認済みの版は 3.7.0.0 です。");
                return 6;
            }

            // V2-v4: 異常終了したときの手がかりを残す（版、読み込まれた DLL、作業フォルダへ書き込めるか）
            string ver = FileVer(vapiDll);
            if (ver != "" && !ver.StartsWith(VerifiedVapiVersion)) Out.Log("WARN", "Vapi.Net.dll のバージョンは " + ver + " です。動作を確認した版は 3.7.0.0 だけです。");
            LogModules();
            if (!CanWrite(vapiDir)) Out.Log("DEBUG", "Vapi.Net.dll のフォルダ（エンジンの作業フォルダ）には書き込めません: " + vapiDir);
            if (o.Check) { Out.State("STOPPED", "確認: Vapi.Net.dll " + ver + " と必要な DLL を読み込めました（" + vapiDir + "）"); return 0; }

            int code = LoggingSession.Run(vapi, o);
            // Vapi の切断処理は終わっている。VisionTerminal と同じく terminateByEngineMode は呼ばないため、
            // エンジンのスレッドが残ったまま終了処理が止まらないよう、ここでプロセスを終了する。
            Kill(code);
            return code;
        }

        /// <summary>ファイルのバージョンとサイズ（ログ用）。取得できない場合は空文字。</summary>
        static string DescribeFile(string path)
        {
            try { System.Diagnostics.FileVersionInfo v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path); return "（バージョン " + (string.IsNullOrEmpty(v.FileVersion) ? "不明" : v.FileVersion) + "、" + new FileInfo(path).Length + " バイト）"; }
            catch (Exception) { return ""; }
        }
    }

    /// <summary>
    /// Vapi.Net.dll を使った XG-X 画像ロギング。呼び出し手順は XG-X VisionTerminal に合わせた CvImageSaver v1.4.2 と同じ。
    /// 1) エンジン起動 startUpByEngineMode(APP_ID_VISION_REMOCON, 60000) → 表示言語の設定
    /// 2) 通知の登録: FindService.setCallbackFunc と 5 つのサービスの addCallback（同じ順序）
    /// 3) 検出: clearInfo → findControllerEther(待ち時間, IP) → 通知待ち → getControllerInfo
    /// 4) セッション: isSessionAvailable でなければ createSession(systemId)
    /// 5) 画像ロギング: connect(systemId) → startLog(systemId, 保存先, true)。通知 HEADER → getHeaderInfo、DATA → getSaveFilePath
    /// 6) 停止: stopLog → STOP_LOG 通知待ち → disconnect → deleteSession → removeCallback
    /// </summary>
    internal static class LoggingSession
    {
        const int StopLogWaitMs = 5000;
        static readonly object Sync = new object();
        static readonly ManualResetEvent StopSignal = new ManualResetEvent(false), FindSignal = new ManualResetEvent(false), StopLogSignal = new ManualResetEvent(false);
        static readonly List<int> UnitNos = new List<int>(); static readonly Dictionary<int, string> LastPath = new Dictionary<int, string>();
        static int systemId = -1, exitCode, callbackLogged; static string root = ""; static volatile bool stopping, stopSet; static volatile string findStatus = "UNDEFINED", step = "(開始前)";
        static Vapi vapi; static object imageSvc;
        // デリゲートは Vapi 側に登録している間、GC に回収されないよう保持する
        static Delegate cbImage, cbFind, cbRdt, cbResult, cbHistory, cbFile;
        /// <summary>直前に呼び出した Vapi の API 名（異常終了時の切り分け用）。</summary>
        internal static string LastStep { get { return step; } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static int Run(Vapi v, Options o)
        {
            vapi = v;
            try
            {
                if (!StartEngine(o)) return 24;
                return RunCore(o);   // VisionTerminal は terminateByEngineMode を呼ばずに終了するため、ここでも呼ばない
            }
            catch (AccessViolationException)
            {
                // 壊れた状態で Vapi の後始末を呼ばないよう、通知だけしてプロセスを即時終了する
                Out.State("ERROR", "Vapi.Net.dll の内部でアクセス違反が発生しました（直前の呼び出し: " + step + "）");
                Program.Kill(Program.CrashExitCode); return Program.CrashExitCode;
            }
        }
        /// <summary>直前に呼び出す API 名を記録し、ログへ出力する。異常終了した場合、ログの最後の「呼び出し:」が発生箇所になる。</summary>
        static void Step(string api) { step = api; Out.Log("DEBUG", "呼び出し: " + api); }
        static void LogResult(string api, object st) { string s = Vapi.St(st); Out.Log(s == "SUCCESS" ? "DEBUG" : "WARN", api + "=" + s); }
        static bool Succeeded(string api, object st) { string s = Vapi.St(st); if (s == "SUCCESS") { Out.Log("DEBUG", api + "=SUCCESS"); return true; } Out.State("ERROR", api + "=" + s); return false; }

        /// <summary>Vapi エンジンを起動する。これを行わずにコントローラ検出を呼ぶとアクセス違反で異常終了する（CvImageSaver v1.3.2 までの不具合）。</summary>
        static bool StartEngine(Options o)
        {
            if (o.EngineAppId < 0) { Out.Log("WARN", "Vapi エンジンの起動を省略します（--engine-app-id " + o.EngineAppId + "）"); return true; }
            Out.State("CONNECTING", "Vapi エンジンを起動中");
            Step("VapiWinSimServices.getInstance"); object sim = vapi.Instance("VapiWinSimServices");
            object appId = Enum.ToObject(vapi.T("VapiWinSimServices+ApplicationId"), o.EngineAppId);
            Step("startUpByEngineMode(" + appId + ", " + o.EngineTimeoutMs + "ms)");
            object st = Vapi.Call(sim, "startUpByEngineMode", appId, o.EngineTimeoutMs); LogResult("startUpByEngineMode", st);
            // VisionTerminal は起動結果を判定する前に表示言語を設定している（LANGUAGE_TYPE_OS = -2）
            try { Step("updateConvertLanguage(LANGUAGE_TYPE_OS)"); Vapi.Call(vapi.Instance("VapiStringConvertController"), "updateConvertLanguage", Enum.Parse(vapi.T("VapiLanguageType"), "LANGUAGE_TYPE_OS")); }
            catch (AccessViolationException) { throw; }
            catch (Exception ex) { Out.Log("WARN", "updateConvertLanguage: " + ex.Message); }
            if (Vapi.St(st) != "SUCCESS") { Out.State("ERROR", "Vapi エンジンを起動できません（startUpByEngineMode=" + Vapi.St(st) + "）"); return false; }
            return true;
        }

        static int RunCore(Options o)
        {
            Directory.CreateDirectory(o.Root); root = Path.GetFullPath(o.Root).TrimEnd('\\');
            Out.State("CONNECTING", "XG-X " + o.Ip + " を検出中");
            // 1. 通知の登録。VisionTerminal と同じ 6 サービスに、同じ順序で登録する（画像ロギング以外の 4 つは何もしない関数）
            Step("ControllerFindService.getInstance"); object find = vapi.Instance("VapiCommControllerFindService");
            cbFind = vapi.MakeDelegate("VapiCommControllerFindService+VapiCommControllerFindServiceCallbackFuncDelegate", OnFind);
            Step("ControllerFindService.setCallbackFunc"); Vapi.Call(find, "setCallbackFunc", cbFind);
            cbRdt = vapi.MakeDelegate("VapiCommRemoteDesktopService+VapiCommRemoteDesktopServiceCallbackFuncDelegate", Nop);
            Step("RemoteDesktopService.addCallback"); Vapi.Call(vapi.Instance("VapiCommRemoteDesktopService"), "addCallback", cbRdt);
            cbResult = vapi.MakeDelegate("VapiCommResultLoggingService+VapiCommResultLoggingServiceCallbackFuncDelegate", Nop);
            Step("ResultLoggingService.addCallback"); Vapi.Call(vapi.Instance("VapiCommResultLoggingService"), "addCallback", cbResult);
            Step("ImageLoggingService.getInstance"); object svc = vapi.Instance("VapiCommImageLoggingService"); imageSvc = svc;
            cbImage = vapi.MakeDelegate("VapiCommImageLoggingService+VapiCommImageLoggingServiceCallbackFuncDelegate", OnCallback);
            Step("ImageLoggingService.addCallback"); Vapi.Call(svc, "addCallback", cbImage);
            cbHistory = vapi.MakeDelegate("VapiCommHistoryLoggingService+VapiCommHistoryLoggingServiceCallbackFuncDelegate", Nop);
            Step("HistoryLoggingService.addCallback"); Vapi.Call(vapi.Instance("VapiCommHistoryLoggingService"), "addCallback", cbHistory);
            cbFile = vapi.MakeDelegate("VapiCommRemoteFileService+VapiCommRemoteFileServiceCallbackFuncDelegate", Nop);
            Step("RemoteFileService.addCallback"); Vapi.Call(vapi.Instance("VapiCommRemoteFileService"), "addCallback", cbFile);
            try
            {
                // 2. コントローラ検出（systemId は検出結果から得る）
                object info; if (!FindController(find, o, out info) || info == null) return 20;
                systemId = Vapi.GetInt(info, "_systemId");
                // 3. セッション確立
                Step("CommServices.getInstance"); object services = vapi.Instance("VapiCommServices");
                Step("isSessionAvailable");
                if ((bool)Vapi.Call(services, "isSessionAvailable", systemId)) Out.Log("DEBUG", "createSession: セッション確立済み");
                else { Step("createSession"); if (!Succeeded("createSession", Vapi.Call(services, "createSession", systemId))) return 21; }
                try
                {
                    // 4. 画像ロギング開始。connect の INVALID_OPERATION（接続済み）は成功として扱う
                    Step("ImageLoggingService.connect"); object st = Vapi.Call(svc, "connect", systemId);
                    if (Vapi.St(st) != "INVALID_OPERATION" && !Succeeded("ImageLoggingService.connect", st)) return 22;
                    try
                    {
                        Step("startLog"); st = Vapi.Call(svc, "startLog", systemId, root, true);
                        if (!Succeeded("startLog", st)) return 23;
                        Out.State("LOGGING", "XG-X " + o.Ip + " 画像ロギング中 → " + root);
                        StartStdinWatcher();
                        try { Console.CancelKeyPress += delegate (object s, ConsoleCancelEventArgs e) { e.Cancel = true; RequestStop(0, "Ctrl+C"); }; } catch (Exception) { }
                        // 停止要求（標準入力の STOP／親プロセス終了）か、異常通知が来るまで待つ。その間、セッションが切れていないかを定期的に確認する
                        while (!StopSignal.WaitOne(5000)) { if (!(bool)Vapi.Call(services, "isSessionAvailable", systemId)) RequestStop(31, "XG-X とのセッションが切断されました"); }
                        // 6. 停止。stopLog が成功したら STOP_LOG 通知を待ってから切断する
                        stopping = true; Step("stopLog"); st = Vapi.Call(svc, "stopLog", systemId); LogResult("stopLog", st);
                        if (Vapi.St(st) == "SUCCESS" && !StopLogSignal.WaitOne(StopLogWaitMs)) Out.Log("WARN", "STOP_LOG 通知が " + StopLogWaitMs + "ms 以内に届きませんでした。切断を続行します。");
                    }
                    finally { try { Step("disconnect"); LogResult("disconnect", Vapi.Call(svc, "disconnect", systemId, false)); } catch (AccessViolationException) { throw; } catch (Exception ex) { Out.Log("WARN", "disconnect: " + ex.Message); } }
                }
                finally
                {
                    try { Step("deleteSession"); if ((bool)Vapi.Call(services, "isSessionAvailable", systemId)) LogResult("deleteSession", Vapi.Call(services, "deleteSession", systemId)); }
                    catch (AccessViolationException) { throw; } catch (Exception ex) { Out.Log("WARN", "deleteSession: " + ex.Message); }
                }
            }
            finally { try { Step("removeCallback"); Vapi.Call(svc, "removeCallback"); } catch (AccessViolationException) { throw; } catch (Exception ex) { Out.Log("WARN", "removeCallback: " + ex.Message); } }
            Out.State("STOPPED", "画像ロギングを停止しました");
            return exitCode;
        }

        /// <summary>IP アドレスを指定してコントローラを検出する。</summary>
        static bool FindController(object find, Options o, out object found)
        {
            found = null;
            Step("clearInfo"); LogResult("clearInfo", Vapi.Call(find, "clearInfo"));
            // 待ち時間はミリ秒。Vapi.Net.dll は 1000〜60000 の範囲外を INVALID_PARAM で拒否する
            Step("findControllerEther(" + o.FindTimeoutMs + "ms, " + o.Ip + ")");
            if (!Succeeded("findControllerEther", Vapi.Call(find, "findControllerEther", o.FindTimeoutMs, o.Ip))) return false;
            if (!FindSignal.WaitOne(o.FindTimeoutMs + 10000))
            {
                try { LogResult("cancelFindRequest", Vapi.Call(find, "cancelFindRequest")); } catch (AccessViolationException) { throw; } catch (Exception ex) { Out.Log("WARN", "cancelFindRequest: " + ex.Message); }
                Out.State("ERROR", "コントローラ検出の結果通知がありません（IP " + o.Ip + "）"); return false;
            }
            if (findStatus != "SUCCESS") { Out.State("ERROR", "XG-X が見つかりません（IP " + o.Ip + ", 検出結果=" + findStatus + "）。IP アドレス・LAN 接続・ファイアウォールを確認してください。"); return false; }
            Step("getControllerInfo"); object[] a = new object[] { null }; object st = Vapi.Call(find, "getControllerInfo", a); Array table = a[0] as Array;
            if (Vapi.St(st) != "SUCCESS" || table == null || table.Length == 0) { Out.State("ERROR", "getControllerInfo=" + Vapi.St(st) + "（検出数 " + (table == null ? 0 : table.Length) + "）"); return false; }
            foreach (object c in table)
            {
                if (c == null) continue; string addr = Vapi.GetStr(c, "_addressName");
                Out.Log("DEBUG", "検出: systemId=" + Vapi.GetInt(c, "_systemId") + " name=" + Vapi.GetStr(c, "_controllerName") + " type=" + Vapi.GetStr(c, "_commDeviceType") + " address=" + addr + " port=" + Vapi.GetStr(c, "_portName") + " protocol=" + Vapi.Call(c, "getProtocolVersion"));
                if (found == null && string.Equals(addr, o.Ip, StringComparison.OrdinalIgnoreCase)) found = c;
            }
            if (found == null && table.Length == 1) found = table.GetValue(0);   // アドレス表記が指定と一致しない場合でも、検出が 1 台だけならそれを使う
            if (found == null) { Out.State("ERROR", "検出した " + table.Length + " 台の中に IP " + o.Ip + " のコントローラがありません"); return false; }
            int id = Vapi.GetInt(found, "_systemId");
            if (id < 0) { Out.State("ERROR", "検出結果の systemId が不正です（" + id + "）"); found = null; return false; }
            Out.Log("INFO", "接続先: systemId=" + id + " " + Vapi.GetStr(found, "_controllerName") + " (" + Vapi.GetStr(found, "_addressName") + ", port " + Vapi.GetStr(found, "_portName") + ")");
            return true;
        }

        static void Nop(object[] a) { }
        /// <summary>コントローラ検出の結果通知（スレッドプールから呼ばれる）。引数: VapiStatus。</summary>
        static void OnFind(object[] a)
        {
            try { string s = Vapi.St(a[0]); findStatus = s; Out.Log(s == "SUCCESS" ? "DEBUG" : "WARN", "コントローラ検出の結果通知: " + s); } catch (Exception ex) { findStatus = "(例外)"; Out.Log("ERROR", "find callback: " + ex); }
            FindSignal.Set();
        }
        static void RequestStop(int code, string reason)
        {
            lock (Sync)
            {
                if (stopSet) return; stopSet = true; exitCode = code;
                if (code != 0) Out.State("ERROR", reason); else Out.Log(reason == "STOP" ? "DEBUG" : "INFO", "停止要求: " + reason);   // 本体からの通常の停止要求は詳細ログ
                StopSignal.Set();
            }
        }
        static void StartStdinWatcher()
        {
            Thread t = new Thread(delegate ()
            {
                try { string line; while ((line = Console.In.ReadLine()) != null) if (line.Trim() == "STOP") { RequestStop(0, "STOP"); return; } RequestStop(0, "標準入力が閉じられました（親プロセス終了）"); }
                catch (Exception ex) { RequestStop(0, "標準入力の読み取り終了: " + ex.Message); }
            });
            t.IsBackground = true; t.Name = "stdin"; t.Start();
        }

        /// <summary>画像ロギングの通知（スレッドプールから呼ばれる）。引数: systemId, eventType, VapiStatus, param。</summary>
        static void OnCallback(object[] a)
        {
            try
            {
                int id = Convert.ToInt32(a[0], CultureInfo.InvariantCulture), param = Convert.ToInt32(a[3], CultureInfo.InvariantCulture); string ev = a[1] == null ? "" : a[1].ToString(), st = Vapi.St(a[2]);
                // param は、DATA 通知では画像を保存した画像出力ユニットの番号。調査用に最初の数回だけ内容を出力する（詳細ログ）
                if (Interlocked.Increment(ref callbackLogged) <= 20) Out.Log("DEBUG", "callback event=" + ev + " status=" + st + " param=" + param + " systemId=" + id);
                if (id < 0 || id != systemId) return;
                if (ev == "CALLBACK_HEADER") { if (st == "SUCCESS") ReportHeader(); }
                else if (ev == "CALLBACK_DATA") { if (st == "SUCCESS") ReportSavedFiles(param); }
                else if (ev == "CALLBACK_STOP_LOG")
                {
                    StopLogSignal.Set(); if (stopping) return;   // こちらから要求した停止の完了通知
                    RequestStop(30, "XG-X 側から画像ロギングの停止が通知されました（status=" + st + "）"); return;
                }
                if (stopping) return;
                // VisionTerminal は FAIL / NO_SPACE でロギングを切断し、TIMEOUT / IO_ERROR でコントローラとの接続を切断する
                if (st == "NO_SPACE") RequestStop(33, "保存先（受信フォルダ）の空き容量が不足しています");
                else if (st == "IO_ERROR" || st == "TIMEOUT" || st == "FAIL") RequestStop(32, "画像ロギングで異常が通知されました（event=" + ev + ", status=" + st + "）");
            }
            catch (Exception ex) { Out.Log("ERROR", "callback: " + ex); }
        }
        /// <summary>検査設定と画像出力ユニットの一覧を取得して通知する。</summary>
        static void ReportHeader()
        {
            object[] a = new object[] { systemId, null }; object st = Vapi.Call(imageSvc, "getHeaderInfo", a); object header = a[1];
            if (Vapi.St(st) != "SUCCESS" || header == null) { Out.Log("WARN", "getHeaderInfo=" + Vapi.St(st)); return; }
            List<string> units = new List<string>(); int n = Vapi.GetInt(header, "unitNum"); object arr = Vapi.Get(header, "unitNo");
            lock (Sync)
            {
                UnitNos.Clear(); LastPath.Clear();   // 検査設定が切り替わると保存先も変わるため、通知し直す
                for (int i = 0; i < n; i++) { object u = Vapi.Call(arr, "get_Item", i); int no = Vapi.GetInt(u, "unitNo"); UnitNos.Add(no); units.Add(no + ":" + Vapi.GetStr(u, "unitName")); }
            }
            object sid = Vapi.Get(header, "settingId");
            Out.Header("setting=" + Vapi.GetStr(sid, "deviceId") + "," + Vapi.GetInt(sid, "settingNumber") + "," + Vapi.GetStr(header, "settingName") + "\tunits=" + (units.Count == 0 ? "(画像出力ユニットなし)" : string.Join(";", units.ToArray())));
        }
        /// <summary>画像を保存したユニットの保存パスを調べ、変化があれば通知する。DATA 通知の param がユニット番号。一覧に無い場合は全ユニットを調べる。</summary>
        static void ReportSavedFiles(int notifiedUnitNo)
        {
            int[] units; lock (Sync) units = UnitNos.ToArray();
            if (units.Length == 0) { ReportHeader(); lock (Sync) units = UnitNos.ToArray(); }
            if (notifiedUnitNo >= 0 && (units.Length == 0 || Array.IndexOf(units, notifiedUnitNo) >= 0)) units = new int[] { notifiedUnitNo };
            foreach (int unitNo in units)
            {
                object[] a = new object[] { systemId, unitNo, null }; object st = Vapi.Call(imageSvc, "getSaveFilePath", a); string path = a[2] as string;
                if (Vapi.St(st) != "SUCCESS" || string.IsNullOrEmpty(path)) continue;
                lock (Sync) { string last; if (LastPath.TryGetValue(unitNo, out last) && last == path) continue; LastPath[unitNo] = path; }
                Out.File(unitNo, Path.IsPathRooted(path) ? path : Path.Combine(root, path));   // 相対パスで返る場合に備えて受信フォルダ基準の絶対パスにそろえる
            }
        }
    }
}
