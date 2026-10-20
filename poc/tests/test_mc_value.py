import pytest
from poc.mc_value import calc_raw, calc_value, index_offset, read_mc, IOBIT_LEN


def test_index_offset():
    assert index_offset(0) == 200
    assert index_offset(65) == 330
    assert index_offset(134) == 468
    with pytest.raises(ValueError):
        index_offset(135)
    with pytest.raises(ValueError):
        index_offset(-1)


def test_calc_raw_positive():
    # VB: sUVal="0001", sBVal="1234" → Convert.ToInt32("00011234",16)
    assert calc_raw(0x1234, 0x0001) == 0x00011234


def test_calc_raw_negative_words():
    # VB は Convert.ToString(-1,16)="ffffffff" の下4桁 "ffff" を使う → 負のワードも下位16bitだけ採用
    assert calc_raw(-1, -1) == -1
    assert calc_raw(0xFFFF, 0xFFFF) == -1
    assert calc_raw(0, 0x8000) == -2147483648


def test_calc_value():
    assert calc_value(21034, 100) == pytest.approx(210.34)
    with pytest.raises(ValueError):
        calc_value(1, 0)


def test_read_mc():
    io = [0] * IOBIT_LEN
    io[330], io[331] = 21034, 0          # index 65
    assert read_mc(io, 65, 100) == pytest.approx(210.34)
    assert read_mc(io[:300], 65, 100) is None   # 配列不足 → 判定不能
