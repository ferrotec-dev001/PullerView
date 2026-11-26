// ZrMap.cs — stcRealData.intIOBit（ZR110000〜ZR112299）のアドレス別データ定義（ZrMap.ini）
// グラフの PV / SV は従来のデータリスト index（0〜149。stcMCValue の配列位置）に加えて、ZR アドレス（110000 以上）でも指定できる。
// VB_FERROTEC（12 インチ FT-CZ3212）mdlPcObjRWPLC.sub_Update_RealTimeData: stcRealData.intIOBit = ZR110000〜112299（2,300 ワード）。
// 読取り専用（PullerView から VB_FERROTEC / PLC へは書き込まない）。
using System;
using System.Collections.Generic;

namespace PullerView
{
    public static class ZrMap
    {
        public const int Base = 110000;          // intIOBit[0] の ZR アドレス
        public const int Words = 2300;           // intIOBit の長さ（ZR110000〜112299）
        public const int IndexThreshold = 100000; // これ以上の index は ZR アドレスとして扱う
        public static string FileName = "ZrMap.ini";
        public static Dictionary<int, McItem> Items = new Dictionary<int, McItem>();
        public static List<int> Order = new List<int>();   // 一覧表示順（アドレス昇順）

        public static bool IsZr(int idx) { return idx >= IndexThreshold; }
        public static bool InRange(int idx) { return idx >= Base && idx < Base + Words; }
        public static int Last { get { return Base + Words - 1; } }

        /// <summary>ZR アドレスの定義。ZrMap.ini に無いアドレスは 1 ワード・除数 1・名称なしで扱う。範囲外は null。</summary>
        public static McItem Get(int addr)
        {
            McItem m; if (Items.TryGetValue(addr, out m)) return m;
            if (!InRange(addr)) return null;
            m = new McItem(); m.Index = addr; m.Div = 1; m.Words = 1; m.DivSrc = "none"; return m;
        }
        public static string Label(int addr) { return "ZR" + addr; }

        /// <summary>intIOBit から ZR アドレスの生値を取り出す。1 ワード=16bit、2 ワード=下位・上位の順で 32bit（VB_FERROTEC fnc_CalcDoubleVal と同じ）。</summary>
        public static long? Raw(int[] io, int addr)
        {
            McItem m = Get(addr); if (m == null || io == null) return null;
            int off = addr - Base; int w = m.Words == 2 ? 2 : 1; if (off < 0 || io.Length < off + w) return null;
            if (w == 2) { long v = HostLink.CalcRaw(io[off], io[off + 1]); return m.Signed ? v : (v & 0xFFFFFFFFL); }
            int u = io[off] & 0xFFFF; return m.Signed ? (long)(short)u : (long)u;
        }

        public static void Load()
        {
            Dictionary<int, McItem> d = new Dictionary<int, McItem>(); List<int> o = new List<int>();
            try
            {
                string path = Ini.InExeDir(FileName);
                if (!System.IO.File.Exists(path)) { Items = d; Order = o; Log.Write("ZrMap.ini なし — ZR アドレス指定は 1 ワード・除数 1 で扱う"); return; }
                Ini ini = Ini.Load(path); int bad = 0;
                foreach (string sec in ini.Sections)
                {
                    if (!sec.StartsWith("ZR", StringComparison.OrdinalIgnoreCase)) continue;
                    int a; if (!int.TryParse(sec.Substring(2), out a) || !InRange(a)) { bad++; continue; }
                    McItem m = new McItem(); m.Index = a; m.Words = ini.GetInt(sec, "words", 1) == 2 ? 2 : 1; m.Signed = ini.GetInt(sec, "signed", 1) != 0;
                    double dv = ini.GetDbl(sec, "div", 1); m.Div = dv >= 1 ? (long)dv : 1;
                    m.Caption = ini.Get(sec, "caption", ""); m.CaptionE = ini.Get(sec, "caption_e", ""); m.CaptionJ = ini.Get(sec, "caption_j", ""); m.CaptionC = ini.Get(sec, "caption_c", "");
                    m.Unit = ini.Get(sec, "unit", ""); m.DivSrc = ini.Get(sec, "div_src", "").Trim().ToLowerInvariant();
                    if (a + m.Words > Base + Words) { bad++; continue; }
                    if (!d.ContainsKey(a)) o.Add(a); d[a] = m;
                }
                o.Sort(); Items = d; Order = o;
                Log.Write("ZrMap.ini 読込: " + d.Count + " 項目（ZR" + Base + "〜ZR" + Last + "）" + (bad > 0 ? " 無視 " + bad + " 件（範囲外・書式不正）" : ""));
            }
            catch (Exception ex) { Items = d; Order = o; Log.Write("ZrMap.ini 読込失敗: " + ex.Message); }
        }
    }
}
