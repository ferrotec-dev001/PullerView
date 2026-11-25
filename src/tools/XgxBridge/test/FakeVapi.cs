// 試験用の模擬 Vapi.Net.dll（KEYENCE の Vapi.Net.dll ではない）。PullerView.XgxBridge のリフレクション呼び出しを、XG-X 実機なしで通すためのもの。
// 型・メンバーの名前と形（構造体／プロパティ／入れ子の列挙型・デリゲート／out 引数）は、実物 3.7.0.0 のメタデータに合わせてある。
// ビルド: mcs -target:library -out:Vapi.Net.dll FakeVapi.cs（出力先に空の libEGL.dll・libGLESv2.dll も置く）。製品には含めない。
// 動作の指定（環境変数）: FAKEVAPI_FIND=NOT_FOUND（検出失敗）／FAKEVAPI_SRC=BMP のあるフォルダ／FAKEVAPI_INTERVAL_MS=画像の間隔（既定 1000）／FAKEVAPI_COUNT=枚数（既定 0＝無限）
//                         FAKEVAPI_STOP_AFTER=枚数（その枚数のあと XG-X 側から停止通知）
//                         FAKEVAPI_CRASH_AFTER=枚数（その枚数のあと、模擬 DLL の内部で未処理の例外を起こしてプロセスごと異常終了する）
using System;
using System.IO;
using System.Threading;

namespace Keyence.Ve.Interop
{
    public struct VapiStatus
    {
        public enum Status { SUCCESS = 0, FAIL = 1, INVALID_PARAM = 2, NOT_FOUND = 6, NO_SPACE = 7, TIMEOUT = 8, INVALID_OPERATION = 14, IO_ERROR = 15, UNDEFINED = 21 }
        public Status val;
        internal static VapiStatus Of(Status s) { VapiStatus v = new VapiStatus(); v.val = s; return v; }
        internal static VapiStatus Ok { get { return Of(Status.SUCCESS); } }
    }
    public enum VapiLanguageType { LANGUAGE_TYPE_OS = -2, LANGUAGE_TYPE_NONE = -1, LANGUAGE_TYPE_JAPANESE = 0 }
    public enum VapiMemoryCardDriveId { MEMORY_CARD_DRIVE_ID_C = 1, MEMORY_CARD_DRIVE_ID_D = 2 }
    internal static class Fake
    {
        public static string Env(string k, string def) { string v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? def : v; }
        public static bool EngineStarted; public static int Session = -1;
    }
    public class VapiWinSimServices
    {
        public enum ApplicationId { APP_ID_VISION_EDITOR = 0, APP_ID_VISION_REMOCON = 1 }
        static readonly VapiWinSimServices inst = new VapiWinSimServices();
        public static VapiWinSimServices getInstance() { return inst; }
        public VapiStatus startUpByEngineMode(ApplicationId appId, int timeoutMs) { Fake.EngineStarted = appId == ApplicationId.APP_ID_VISION_REMOCON; return Fake.EngineStarted ? VapiStatus.Ok : VapiStatus.Of(VapiStatus.Status.FAIL); }
    }
    public class VapiStringConvertController
    {
        static readonly VapiStringConvertController inst = new VapiStringConvertController();
        public static VapiStringConvertController getInstance() { return inst; }
        public VapiStatus updateConvertLanguage(VapiLanguageType languageType) { return VapiStatus.Ok; }
    }
    public class VapiCommSystemInfo
    {
        public enum CommDeviceType { ETHERNET = 0, USB = 2 }
        internal int id; internal string name, addr, port;
        public int _systemId { get { return id; } }
        public string _controllerName { get { return name; } }
        public CommDeviceType _commDeviceType { get { return CommDeviceType.ETHERNET; } }
        public string _addressName { get { return addr; } }
        public string _portName { get { return port; } }
        public int getProtocolVersion() { return 1018; }
    }
    public class VapiCommControllerFindService
    {
        public delegate void VapiCommControllerFindServiceCallbackFuncDelegate(VapiStatus status);
        static readonly VapiCommControllerFindService inst = new VapiCommControllerFindService();
        VapiCommControllerFindServiceCallbackFuncDelegate cb; string ip; bool found;
        public static VapiCommControllerFindService getInstance() { return inst; }
        public VapiStatus setCallbackFunc(VapiCommControllerFindServiceCallbackFuncDelegate pCallbackFunc) { cb = pCallbackFunc; return VapiStatus.Ok; }
        public VapiStatus clearInfo() { found = false; return VapiStatus.Ok; }
        public VapiStatus cancelFindRequest() { return VapiStatus.Ok; }
        public VapiStatus findControllerEther(int recvTimer, string pDetectIpAddress)
        {
            if (!Fake.EngineStarted) throw new AccessViolationException("engine not started (fake)");
            if (recvTimer < 1000 || recvTimer > 60000) return VapiStatus.Of(VapiStatus.Status.INVALID_PARAM);
            ip = pDetectIpAddress; bool nf = Fake.Env("FAKEVAPI_FIND", "") == "NOT_FOUND";
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(300); found = !nf; if (cb != null) cb(VapiStatus.Of(nf ? VapiStatus.Status.NOT_FOUND : VapiStatus.Status.SUCCESS)); });
            return VapiStatus.Ok;
        }
        public VapiStatus getControllerInfo(out VapiCommSystemInfo[] foundControllerInfoTable)
        {
            if (!found) { foundControllerInfoTable = new VapiCommSystemInfo[0]; return VapiStatus.Of(VapiStatus.Status.NOT_FOUND); }
            VapiCommSystemInfo i = new VapiCommSystemInfo(); i.id = 3; i.name = "FAKE_XGX"; i.addr = ip; i.port = "8502"; foundControllerInfoTable = new VapiCommSystemInfo[] { i }; return VapiStatus.Ok;
        }
    }
    public class VapiCommServices
    {
        static readonly VapiCommServices inst = new VapiCommServices();
        public static VapiCommServices getInstance() { return inst; }
        public bool isSessionAvailable(int systemId) { return Fake.Session == systemId; }
        public VapiStatus createSession(int systemId) { Fake.Session = systemId; return VapiStatus.Ok; }
        public VapiStatus deleteSession(int systemId) { Fake.Session = -1; return VapiStatus.Ok; }
    }
    public class VapiSettingId { public int settingNumber { get { return 0; } } public VapiMemoryCardDriveId deviceId { get { return VapiMemoryCardDriveId.MEMORY_CARD_DRIVE_ID_C; } } }
    public class VapiImageLogUnitInfo { public string unitName { get { return "Image_Stabilize"; } } public int unitNo { get { return 51; } } }
    public class VapiImageLogUnitInfo_Array { readonly VapiImageLogUnitInfo u = new VapiImageLogUnitInfo(); public VapiImageLogUnitInfo this[int i] { get { if (i != 0) throw new IndexOutOfRangeException(); return u; } } }
    public class VapiImageLogHeader
    {
        readonly VapiImageLogUnitInfo_Array a = new VapiImageLogUnitInfo_Array(); readonly VapiSettingId s = new VapiSettingId();
        public VapiImageLogUnitInfo_Array unitNo { get { return a; } } public int unitNum { get { return 1; } } public string settingName { get { return "Melt"; } } public VapiSettingId settingId { get { return s; } }
    }
    public class VapiCommImageLoggingService
    {
        public enum CallbackEventType { CALLBACK_HEADER = 0, CALLBACK_DATA = 1, CALLBACK_STOP_LOG = 2 }
        public delegate void VapiCommImageLoggingServiceCallbackFuncDelegate(int systemId, CallbackEventType eventType, VapiStatus status, int param);
        static readonly VapiCommImageLoggingService inst = new VapiCommImageLoggingService();
        VapiCommImageLoggingServiceCallbackFuncDelegate cb; bool connected; volatile bool logging; Thread th; string dir, last = ""; int sid;
        public static VapiCommImageLoggingService getInstance() { return inst; }
        public void addCallback(VapiCommImageLoggingServiceCallbackFuncDelegate func) { cb = func; }
        public void removeCallback() { cb = null; }
        public VapiStatus connect(int systemId) { if (Fake.Session != systemId) return VapiStatus.Of(VapiStatus.Status.FAIL); if (connected) return VapiStatus.Of(VapiStatus.Status.INVALID_OPERATION); connected = true; return VapiStatus.Ok; }
        public VapiStatus disconnect(int systemId, bool bForce) { connected = false; return VapiStatus.Ok; }
        public VapiStatus startLog(int systemId, string rootDir, bool bNotify)
        {
            if (!connected) return VapiStatus.Of(VapiStatus.Status.INVALID_OPERATION);
            sid = systemId; dir = Path.Combine(Path.Combine(rootDir, "FAKE_XGX"), "Image_Stabilize"); Directory.CreateDirectory(dir); logging = true;
            th = new Thread(Feed); th.IsBackground = true; th.Start(); return VapiStatus.Ok;
        }
        public VapiStatus stopLog(int systemId)
        {
            logging = false; if (th != null) th.Join(3000);
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(100); VapiCommImageLoggingServiceCallbackFuncDelegate c = cb; if (c != null) c(sid, CallbackEventType.CALLBACK_STOP_LOG, VapiStatus.Ok, 0); });
            return VapiStatus.Ok;
        }
        public VapiStatus getHeaderInfo(int systemId, out VapiImageLogHeader pHeader) { pHeader = new VapiImageLogHeader(); return VapiStatus.Ok; }
        public VapiStatus getSaveFilePath(int systemId, int unitNo, out string pFilePath) { pFilePath = last; return last == "" ? VapiStatus.Of(VapiStatus.Status.NOT_FOUND) : VapiStatus.Ok; }
        void Feed()
        {
            string src = Fake.Env("FAKEVAPI_SRC", ""); int ms = int.Parse(Fake.Env("FAKEVAPI_INTERVAL_MS", "1000")), count = int.Parse(Fake.Env("FAKEVAPI_COUNT", "0")), stopAfter = int.Parse(Fake.Env("FAKEVAPI_STOP_AFTER", "0")), crashAfter = int.Parse(Fake.Env("FAKEVAPI_CRASH_AFTER", "0"));
            string[] fs = src != "" && Directory.Exists(src) ? Directory.GetFiles(src, "*.bmp") : new string[0]; Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
            VapiCommImageLoggingServiceCallbackFuncDelegate c = cb; if (c != null) c(sid, CallbackEventType.CALLBACK_HEADER, VapiStatus.Ok, 0);
            int n = 0;
            while (logging && (count == 0 || n < count))
            {
                byte[] data = fs.Length > 0 ? File.ReadAllBytes(fs[n % fs.Length]) : MakeBmp(n);
                string path = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + n.ToString("D4") + ".bmp");
                // 書込み途中の状態を作る: 前半を書いて少し待ち、後半を書いて閉じる
                using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { int half = data.Length / 2; f.Write(data, 0, half); f.Flush(); Thread.Sleep(Math.Min(300, ms / 2)); f.Write(data, half, data.Length - half); }
                last = path; n++; c = cb; if (c != null) c(sid, CallbackEventType.CALLBACK_DATA, VapiStatus.Ok, 51);
                if (crashAfter > 0 && n >= crashAfter) throw new InvalidOperationException("fake crash inside Vapi (test)");
                if (stopAfter > 0 && n >= stopAfter) { logging = false; if (c != null) c(sid, CallbackEventType.CALLBACK_STOP_LOG, VapiStatus.Of(VapiStatus.Status.FAIL), 0); return; }
                for (int w = 0; w < ms && logging; w += 50) Thread.Sleep(50);
            }
        }
        static byte[] MakeBmp(int seed)
        {
            int w = 64, h = 48, stride = w * 3, size = 54 + stride * h; byte[] b = new byte[size]; b[0] = (byte)'B'; b[1] = (byte)'M';
            BitConverter.GetBytes(size).CopyTo(b, 2); BitConverter.GetBytes(54).CopyTo(b, 10); BitConverter.GetBytes(40).CopyTo(b, 14); BitConverter.GetBytes(w).CopyTo(b, 18); BitConverter.GetBytes(h).CopyTo(b, 22);
            b[26] = 1; b[28] = 24; BitConverter.GetBytes(stride * h).CopyTo(b, 34); for (int i = 54; i < size; i++) b[i] = (byte)((i + seed * 7) & 0xFF); return b;
        }
    }
    public class VapiCommRemoteDesktopService
    {
        public delegate void VapiCommRemoteDesktopServiceCallbackFuncDelegate(int systemId, VapiStatus status);
        static readonly VapiCommRemoteDesktopService inst = new VapiCommRemoteDesktopService(); public static VapiCommRemoteDesktopService getInstance() { return inst; }
        public void addCallback(VapiCommRemoteDesktopServiceCallbackFuncDelegate func) { if (func != null) func(-1, VapiStatus.Ok); }
    }
    public class VapiCommResultLoggingService
    {
        public enum CallbackEventType { A = 0 }
        public delegate void VapiCommResultLoggingServiceCallbackFuncDelegate(int systemId, CallbackEventType eventType, VapiStatus status);
        static readonly VapiCommResultLoggingService inst = new VapiCommResultLoggingService(); public static VapiCommResultLoggingService getInstance() { return inst; }
        public void addCallback(VapiCommResultLoggingServiceCallbackFuncDelegate func) { if (func != null) func(-1, CallbackEventType.A, VapiStatus.Ok); }
    }
    public class VapiCommHistoryLoggingService
    {
        public enum CallbackEventType { A = 0 }
        public delegate void VapiCommHistoryLoggingServiceCallbackFuncDelegate(int systemId, CallbackEventType eventType, VapiStatus status);
        static readonly VapiCommHistoryLoggingService inst = new VapiCommHistoryLoggingService(); public static VapiCommHistoryLoggingService getInstance() { return inst; }
        public void addCallback(VapiCommHistoryLoggingServiceCallbackFuncDelegate func) { if (func != null) func(-1, CallbackEventType.A, VapiStatus.Ok); }
    }
    public class VapiCommRemoteFileService
    {
        public enum CallbackEventType { A = 0 }
        public delegate void VapiCommRemoteFileServiceCallbackFuncDelegate(int systemId, CallbackEventType eventType, VapiStatus status);
        static readonly VapiCommRemoteFileService inst = new VapiCommRemoteFileService(); public static VapiCommRemoteFileService getInstance() { return inst; }
        public void addCallback(VapiCommRemoteFileService.VapiCommRemoteFileServiceCallbackFuncDelegate func) { if (func != null) func(-1, CallbackEventType.A, VapiStatus.Ok); }
    }
}
