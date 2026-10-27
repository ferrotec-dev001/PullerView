using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PullerView
{
    /// <summary>
    /// SetWinEventHook による即時判定トリガ。
    /// VB_FERROTEC の窓の 表示/非表示/生成/破棄/Z順変更/位置変更/前面化 イベントを受けたら、短い遅延（既定 100 ms）の後に監視処理を 1 回実行する。
    /// 1 秒周期のポーリングは保険としてそのまま残す（イベントを取りこぼしても必ず追従する）。
    /// フックはこのクラスを生成した UI スレッドのメッセージループで配送される（WINEVENT_OUTOFCONTEXT）。.NET Framework 4.0 / Windows 7 で動作。
    /// </summary>
    sealed class WinEventTrigger : IDisposable
    {
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003,
                   EVENT_OBJECT_REORDER = 0x8004, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        readonly WinEventProc proc;             // GC に回収されないようフィールドで保持
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly Timer delay = new Timer();
        readonly Func<int[]> targetPids;        // 監視対象（VB_FERROTEC）の PID 一覧
        readonly Action run;                    // 実行する監視処理
        uint lastEvent; int events, triggers;
        public bool Active { get { return hooks.Count > 0; } }
        public int EventCount { get { return events; } }
        public int TriggerCount { get { return triggers; } }
        /// <summary>直近にトリガとなったイベント名（診断用）</summary>
        public string LastEventName { get { return Name(lastEvent); } }

        public WinEventTrigger(Func<int[]> targetPids, Action run, int delayMs)
        {
            this.targetPids = targetPids; this.run = run;
            delay.Interval = Math.Max(20, delayMs);
            delay.Tick += delegate { delay.Stop(); triggers++; run(); };
            proc = Callback;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                IntPtr h1 = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                IntPtr h2 = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (h1 != IntPtr.Zero) hooks.Add(h1); if (h2 != IntPtr.Zero) hooks.Add(h2);
                Log.Write("WinEvent フック: " + (hooks.Count == 2 ? "有効（即時判定 " + delay.Interval + " ms）" : "一部失敗 (" + hooks.Count + "/2)"));
            }
            catch (Exception ex) { Log.Write("WinEvent フック失敗（周期監視のみで動作）: " + ex.Message); }
        }

        void Callback(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (idObject != 0 /*OBJID_WINDOW*/ || hwnd == IntPtr.Zero) return;   // キャレット・カーソル・子オブジェクトのイベントは無視
                if (ev == EVENT_OBJECT_CREATE || ev == EVENT_OBJECT_DESTROY || ev == EVENT_OBJECT_SHOW || ev == EVENT_OBJECT_HIDE ||
                    ev == EVENT_OBJECT_REORDER || ev == EVENT_OBJECT_LOCATIONCHANGE || ev == EVENT_SYSTEM_FOREGROUND)
                {
                    uint pid; GetWindowThreadProcessId(hwnd, out pid);
                    int[] t = targetPids(); if (t == null || Array.IndexOf(t, (int)pid) < 0) return;   // VB_FERROTEC の窓のみ
                    events++; lastEvent = ev;
                    if (!delay.Enabled) delay.Start();   // 連続イベントは 1 回にまとめる（デバウンス）
                }
            }
            catch (Exception) { }
        }

        static string Name(uint ev)
        {
            switch (ev)
            {
                case EVENT_SYSTEM_FOREGROUND: return "FOREGROUND"; case EVENT_OBJECT_CREATE: return "CREATE"; case EVENT_OBJECT_DESTROY: return "DESTROY";
                case EVENT_OBJECT_SHOW: return "SHOW"; case EVENT_OBJECT_HIDE: return "HIDE"; case EVENT_OBJECT_REORDER: return "REORDER";
                case EVENT_OBJECT_LOCATIONCHANGE: return "LOCATIONCHANGE"; default: return "0x" + ev.ToString("X");
            }
        }

        public void Dispose()
        {
            try { delay.Stop(); delay.Dispose(); } catch (Exception) { }
            foreach (IntPtr h in hooks) { try { UnhookWinEvent(h); } catch (Exception) { } }
            hooks.Clear();
        }
    }
}
