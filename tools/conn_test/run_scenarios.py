#!/usr/bin/env python3
# run_scenarios.py — PullerView（待受側）と FakeFerrotec（VB_FERROTEC 模擬クライアント）の接続安定性シナリオを自動実行する
#   使い方: python run_scenarios.py [--dir <PullerView.exe のあるフォルダ>] [--mono] [--only s1,s3]
#   Windows では PullerView.exe --headless を直接起動、Linux(Mono) では --mono を付ける。
#   各シナリオの結果を stdout に要約し、ログを <dir>/conn_test_logs/ に残す。
import subprocess, time, os, re, signal, sys, argparse

ap = argparse.ArgumentParser(); ap.add_argument('--dir', default='.'); ap.add_argument('--mono', action='store_true'); ap.add_argument('--only', default='')
A = ap.parse_args(); os.chdir(A.dir); os.makedirs('conn_test_logs', exist_ok=True)
PRE = ['mono'] if A.mono else []
HOST = PRE + ['PullerView.exe', '--headless']; FAKE = PRE + ['FakeFerrotec.exe']

def run(name, host_sec, fakes, delays, kill=None, host_delay=0):
    t0 = time.time(); procs = []; host = None; started = [False] * len(fakes)
    while time.time() - t0 < host_sec + host_delay + 2:
        el = time.time() - t0
        if host is None and el >= host_delay:
            host = subprocess.Popen(HOST + [str(host_sec)], stdout=open(f'conn_test_logs/h_{name}.log', 'w'), stderr=subprocess.STDOUT)
        for i, (a, d) in enumerate(zip(fakes, delays)):
            if not started[i] and el >= d:
                procs.append(subprocess.Popen(FAKE + a, stdout=open(f'conn_test_logs/f_{name}_{i}.log', 'w'), stderr=subprocess.STDOUT)); started[i] = True
        if kill:
            idx, at, sig = kill
            if el >= at and idx < len(procs) and procs[idx].poll() is None and not getattr(procs[idx], 'k', False):
                procs[idx].send_signal(sig); procs[idx].k = True
        if host is not None and host.poll() is not None and all(started): break
        time.sleep(0.2)
    for p in procs:
        if p.poll() is None: p.terminate()
    if host and host.poll() is None: host.terminate()
    time.sleep(0.5)
    return open(f'conn_test_logs/h_{name}.log', encoding='utf-8-sig').read(), [open(f'conn_test_logs/f_{name}_{i}.log', encoding='utf-8-sig').read() for i in range(len(fakes))]

def timeline(h):
    out = []
    for l in h.splitlines():
        m = re.search(r'conn=(\d) comm=(\d) .*fresh=(\d)', l)
        if m: out.append(m.group(1) + m.group(2) + m.group(3))
    return ' '.join(out)

def report(name, title, h, fs, expect):
    ev = [l for l in h.splitlines() if '[STAT]' not in l and '読込' not in l and '起動' not in l and '終了' not in l]
    print(f'\n===== {name}: {title} =====\n期待: {expect}\n--- host events ---'); print('\n'.join(ev[:40]))
    print('--- timeline (接続,通信OK,鮮度 /秒) ---'); print(timeline(h))
    for i, f in enumerate(fs): print(f'--- fake{i} ---'); print('\n'.join([l for l in f.splitlines() if 'proc=' not in l][-8:]))

KILL = getattr(signal, 'SIGKILL', signal.SIGTERM)
S = {
 's1':  ('正常: 待受→4 秒後にクライアント接続', lambda: run('s1', 25, [['--duration', '18']], [4]), '接続後すぐ 111 が続き、切断で 000'),
 's2':  ('クライアント先行起動（待受は 6 秒後）', lambda: run('s2', 22, [['--duration', '24']], [0], host_delay=6), 'クライアントが Connection refused を再試行し、待受開始後に接続'),
 's3':  ('正常切断→4 秒後に再接続', lambda: run('s3', 28, [['--duration', '26', '--disconnect-at', '8', '--reconnect-after', '4']], [2]), '切断で 000、再接続で新 SessionID を採用し 111 に復帰'),
 's4':  ('クライアント強制終了（kill）→ 別クライアント接続', lambda: run('s4', 26, [['--duration', '40'], ['--duration', '12']], [2, 16], kill=(0, 12, KILL)), 'kill 直後に切断検知、後続クライアントを採用'),
 's5':  ('半開ソケット（書込みも TextID=00 も停止、ソケットは開いたまま）', lambda: run('s5', 34, [['--duration', '32', '--freeze-at', '4']], [1]), 'stale_sec 後に通信断扱い(100)、heartbeat_timeout_sec 後にソケットを閉じ再接続を受け入れる'),
 's6':  ('データ更新停止 8 秒（ソケット・TextID=00 は維持）', lambda: run('s6', 24, [['--duration', '22', '--silent-at', '6', '--silent-for', '8', '--heartbeat', '3']], [1]), '停止中は 100（古い値を表示・記録しない）、再開で 111'),
 's7':  ('接続/切断の繰り返し ×8', lambda: run('s7', 32, [['--flap', '8']], [1]), '毎回新 SessionID、例外なし'),
 's8':  ('二重接続（後発が切れたら先発へ戻る）', lambda: run('s8', 28, [['--duration', '26'], ['--duration', '10']], [1, 8]), 'B 接続で B を採用、B 切断で A へ戻り 111 を維持'),
 's9':  ('不正電文（ゴミ／BCC 不正／ETX 無し長文／1 バイトずつ送信）', lambda: run('s9', 14, [['--duration', '11', '--garbage']], [1]), 'BCC 不一致は破棄、最後の STX で再同期して正常ハンドシェイク'),
 's11': ('高頻度書込み 100ms', lambda: run('s11', 22, [['--duration', '20', '--interval', '100']], [1]), '読取りは 1 回/秒を維持、fails=0'),
 's13': ('接続/切断 ×25（リーク確認）', lambda: run('s13', 95, [['--flap', '25']], [1]), 'mem/thr の増加が収束する（thr は実機で要確認）'),
 's14': ('連続 150 秒 1Hz', lambda: run('s14', 152, [['--duration', '148', '--proc-sec', '40']], [1]), '欠落なし、[遅延] なし、CSV に 1 行/秒'),
}
only = [x for x in A.only.split(',') if x]
for k, (title, fn, expect) in S.items():
    if only and k not in only: continue
    h, fs = fn(); report(k, title, h, fs, expect)
