"""stcMCValue ライブ値の算出（VB_FERROTEC frmMain / mdlCommon.fnc_CalcDoubleVal と同じ計算）。

PLC データ領域 R110200〜R110479 は 1 項目 = 2 ワード。
stcRealData.intIOBit は ZR110000 起点の生ワード配列なので、
index n の下位ワードは intIOBit[200 + 2n]、上位ワードは intIOBit[200 + 2n + 1]。
"""
from __future__ import annotations

MC_BASE_OFFSET = 200      # (110200 - 110000)
MC_INDEX_MAX = 134        # 135 項目（R110200〜R110479）
IOBIT_LEN = 2100          # ZR110000〜112099


def index_offset(index: int) -> int:
    """index n → intIOBit 上の下位ワード位置。範囲外は ValueError。"""
    if not 0 <= index <= MC_INDEX_MAX:
        raise ValueError(f"stcMCValue index out of range: {index}")
    return MC_BASE_OFFSET + index * 2


def calc_raw(lower: int, upper: int) -> int:
    """fnc_CalcDoubleVal(iLowerVal, iUpperVal) 相当。上位16bit・下位16bit を連結し符号付き32bit で返す。"""
    v = ((upper & 0xFFFF) << 16) | (lower & 0xFFFF)
    return v - 0x1_0000_0000 if v & 0x8000_0000 else v


def calc_value(raw: int, div_val: int) -> float:
    """表示値 = raw / lDivVal。lDivVal が 0 以下のときは ValueError（VB 側は 0 除算になる設定不正）。"""
    if div_val <= 0:
        raise ValueError(f"invalid lDivVal: {div_val}")
    return raw / div_val


def read_mc(iobit, index: int, div_val: int) -> float | None:
    """intIOBit 配列から index の表示値を得る。配列不足時は None（判定不能）。"""
    off = index_offset(index)
    if iobit is None or len(iobit) < off + 2:
        return None
    return calc_value(calc_raw(int(iobit[off]), int(iobit[off + 1])), div_val)
