"""PullerView PoC-A: pythonnet で VB_FERROTEC_SERVER の .NET Remoting から読取り専用で値を取得する。

実行場所: 装置PC（VB_FERROTEC.exe が稼働し、ホストに接続済みの Windows 10 x64）
前提    : Python 3.12 x64、pip install pythonnet psutil、.NET Framework 4.8、
          VB_FERROTEC.exe と同じフォルダの VB_FERROTEC_Common.dll（同一ビルドのものを使うこと）

例:
  python remoting_poc.py --host 172.16.87.27 --port 10030 ^
      --dll "C:\\VB_FERROTEC\\VB_FERROTEC_Common.dll" --indices 65,92,115,116,117,118,119 --seconds 60

本 PoC は Getter しか呼ばない（Setter・fnc_GetRight・stcSendPLC は使用しない）。
"""
from __future__ import annotations

import argparse
import csv
import socket
import statistics
import sys
import time
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from poc.mc_value import IOBIT_LEN, index_offset, read_mc  # noqa: E402

LENGTH_INDEX = {"NECK": 115, "CROWN": 116, "SHOULDER": 117, "BODY": 118, "TAIL": 119}


def local_ips() -> list[str]:
    ips = set()
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ips.add(info[4][0])
    except OSError:
        pass
    return sorted(ips)


def load_clr(dll: Path):
    import pythonnet  # type: ignore

    pythonnet.load("netfx")  # Remoting は .NET Framework のみ。必ず import clr の前に呼ぶ
    import clr  # type: ignore

    clr.AddReference(str(dll))
    import System  # type: ignore
    from System.Collections import Hashtable  # type: ignore
    from System.Runtime.Remoting.Channels import (  # type: ignore
        BinaryClientFormatterSinkProvider,
        BinaryServerFormatterSinkProvider,
        ChannelServices,
    )
    from System.Runtime.Remoting.Channels.Tcp import TcpChannel  # type: ignore
    from System.Runtime.Serialization.Formatters import TypeFilterLevel  # type: ignore
    import VB_FERROTEC_Common as vbc  # type: ignore

    # VB クライアント(mdlLocalService.sub_Start_Service)と同じ構成: port=0, Binary, TypeFilterLevel.Full
    props = Hashtable()
    props["port"] = 0
    props["name"] = "PullerViewPoC"
    srv = BinaryServerFormatterSinkProvider()
    srv.TypeFilterLevel = TypeFilterLevel.Full
    ChannelServices.RegisterChannel(TcpChannel(props, BinaryClientFormatterSinkProvider(), srv), False)
    return System, vbc


def pick_pc(System, vbc, server, my_ips: list[str], force_ip: str | None):
    """自PCの clsPcObj を選ぶ。GetPcObject(IP) → ConnectPc 走査の順。"""
    cands = [force_ip] if force_ip else my_ips
    for ip in cands:
        pc = server.GetPcObject(ip)
        if pc is not None and str(pc.IPAddress) == ip:
            return pc, "GetPcObject"
    for pc in server.ConnectPc:
        addr = str(pc.IPAddress)
        if any(addr == ip or ip in addr for ip in cands):
            return pc, "ConnectPc"
    return None, ""


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True, help="ホスト(VB_FERROTEC_SERVER)の IP。Application.ini [Server] HostIP")
    ap.add_argument("--port", type=int, default=10030, help="RemotePort（既定 10030）")
    ap.add_argument("--dll", required=True, help="VB_FERROTEC_Common.dll のパス")
    ap.add_argument("--indices", default="65,92,115,116,117,118,119")
    ap.add_argument("--seconds", type=int, default=30)
    ap.add_argument("--ip", default=None, help="自PCの IP を明示する場合")
    ap.add_argument("--csv", default="poc_result.csv")
    a = ap.parse_args()
    indices = [int(x) for x in a.indices.split(",") if x.strip()]

    print(f"[1] CLR load: {a.dll}")
    System, vbc = load_clr(Path(a.dll))
    try:
        import psutil  # type: ignore

        proc = psutil.Process()
    except Exception:  # noqa: BLE001
        proc = None

    url = f"tcp://{a.host}:{a.port}/{vbc.Enum_FormID.eServer}"
    print(f"[2] GetObject(clsServerObj) {url}")
    server = System.Activator.GetObject(vbc.clsServerObj, url)
    t0 = time.perf_counter()
    print(f"    ServerName={server.ServerName} ServerIP={server.ServerIP} RemotePort={server.RemotePort} "
          f"SocketPort={server.SocketPort} ({(time.perf_counter()-t0)*1000:.1f} ms)")
    pcs = list(server.ConnectPc)
    print(f"    ConnectPc={len(pcs)}")
    for pc in pcs:
        print(f"      - IP={pc.IPAddress} Name={pc.PcName} InitFlag={pc.InitFlag}")

    my_ips = local_ips()
    print(f"[3] 自PC IP={my_ips} (--ip={a.ip})")
    pc, how = pick_pc(System, vbc, server, my_ips, a.ip)
    if pc is None:
        print("    NG: 自PCに対応する clsPcObj が見つかりません。VB_FERROTEC.exe がホストに接続済みか、--ip を確認してください")
        return 2
    print(f"    OK: {how} → IP={pc.IPAddress} Name={pc.PcName}")

    print("[4] stcPCINF_DATA（1回）")
    t0 = time.perf_counter()
    mc = pc.stcPCINF_DATA.stcMCValue
    print(f"    stcMCValue len={len(mc)} ({(time.perf_counter()-t0)*1000:.1f} ms)")
    meta = {}
    for n in indices:
        m = mc[n]
        meta[n] = dict(cap=str(m.sCaption_j or m.sCaption), unit=str(m.sUnit), div=int(m.lDivVal), fmt=str(m.sFormat),
                       dispno=int(m.iDispNo))
        print(f"    [{n:3d}] {meta[n]['cap']} [{meta[n]['unit']}] lDivVal={meta[n]['div']} fmt={meta[n]['fmt']} "
              f"iDispNo={meta[n]['dispno']} lValue(init)={int(m.lValue)}")

    print(f"[5] 毎秒読取り {a.seconds} 秒 → {a.csv}")
    lat, fails = [], 0
    with open(a.csv, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["time", "latency_ms", "iobit_len", "process", "plc", "ingot_len", "proc_len"] + [f"idx{n}" for n in indices])
        for i in range(a.seconds):
            t0 = time.perf_counter()
            try:
                rd = pc.stcRealData
                info = pc.stcPcInfo
                iob = rd.intIOBit
                n_io = 0 if iob is None else len(iob)
                vals = []
                for n in indices:
                    d = meta[n]["div"]
                    vals.append(read_mc(iob, n, d) if d > 0 else None)
                proc_name = str(info.strPROCESS).upper()
                proc_len = {"NECK": info.strNECK_LENGTH, "CROWN": info.strCROWN_LENGTH, "SHOULDER": info.strSHOULDER_LENGTH,
                            "BODY": info.strBODY_LENGTH, "TAIL": info.strTAIL_LENGTH}.get(proc_name, "")
                ms = (time.perf_counter() - t0) * 1000
                lat.append(ms)
                row = [datetime.now().strftime("%H:%M:%S.%f")[:-3], f"{ms:.1f}", n_io, proc_name, bool(info.bStatePlc),
                       str(info.strINGOT_LENGTH), str(proc_len)] + ["" if v is None else f"{v:.4f}" for v in vals]
                w.writerow(row)
                mem = f" mem={proc.memory_info().rss/1e6:.0f}MB" if proc else ""
                print(f"  {row[0]} {ms:6.1f}ms io={n_io} proc={proc_name} plc={row[4]} len={row[5]}/{row[6]} "
                      + " ".join(f"{n}={row[7+k]}" for k, n in enumerate(indices)) + mem)
                if n_io != IOBIT_LEN:
                    print(f"    WARN intIOBit len={n_io} (期待 {IOBIT_LEN})")
            except Exception as e:  # noqa: BLE001
                fails += 1
                print(f"  ERR {type(e).__name__}: {e}")
            time.sleep(max(0.0, 1.0 - (time.perf_counter() - t0)))

    print("[6] 結果")
    if lat:
        print(f"    latency avg={statistics.mean(lat):.1f}ms max={max(lat):.1f}ms p95={sorted(lat)[int(len(lat)*0.95)-1]:.1f}ms")
    print(f"    reads={len(lat)} errors={fails}")
    return 0 if lat and fails == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
