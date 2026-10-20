# PoC: VB_FERROTEC_SERVER Remoting 読取り検証

- 文書ID: POC-01 / 版数 0.1 / 2026-10-20
- 目的: 要件定義書 v0.15 の ADR-0008 / ADR-0010（Remoting 実体はホスト、PV/SV は `stcRealData.intIOBit` から算出）を**実機で**検証し、
  Python（pythonnet）から 1 秒周期で読取り専用アクセスできることを確認する。
- 実行場所: **装置PC**（VB_FERROTEC.exe がホストに接続済みの Windows 10 x64）。サンドボックス（Linux）では .NET Remoting を実行できないため、ここではコードと手順のみ用意し、純粋 Python 部分（値の算出）だけ単体テスト済み。

## 構成

| ファイル | 役割 |
| --- | --- |
| `remoting_poc.py` | PoC-A（本命）: pythonnet で `clsServerObj` → `clsPcObj` を取得し、毎秒 `stcRealData`/`stcPcInfo` を読む。CSV 出力・遅延計測 |
| `mc_value.py` | index n → `intIOBit[200+2n], [200+2n+1]` → 符号付き32bit ÷ `lDivVal` の純 Python 実装（本番でも流用） |
| `tests/test_mc_value.py` | 上記の単体テスト（pytest、5件 pass） |
| `bridge/RemotingProbe.cs` + `build.bat` | PoC-B（切り分け用）: 同じ処理を C# で実装。Windows 同梱 `csc.exe` でビルド可（VS 不要）。pythonnet で問題が出たとき「Remoting 自体の問題か pythonnet の問題か」を分離する |

## 手順（装置PC）

1. **DLL を用意**: `VB_FERROTEC.exe` と同じフォルダにある `VB_FERROTEC_Common.dll` を使う（別ビルドだと BinaryFormatter の型解決で失敗する可能性があるため、必ず同一ファイル）。
2. **接続先を確認**: `VB_FERROTEC.exe` の `Application.ini` `[Server]` の `HostIP` と `RemotePort`（開発環境サンプル: 172.16.87.27 / 10030）。
3. **Python 環境**（PoC 用。本番は exe 化）:
   ```bat
   py -3.12 -m venv C:\PullerView\venv
   C:\PullerView\venv\Scripts\pip install -r poc\requirements.txt
   ```
4. **PoC-A 実行**（リポジトリのルートで）:
   ```bat
   C:\PullerView\venv\Scripts\python poc\remoting_poc.py --host <HostIP> --port <RemotePort> ^
       --dll "C:\<VB_FERROTEC のフォルダ>\VB_FERROTEC_Common.dll" --indices 65,92,115,116,117,118,119 --seconds 60
   ```
   自PC の IP が複数ある場合は `--ip <装置PCのIP>` を付ける。
5. **PoC-B（必要時）**: `poc\bridge` に DLL をコピーし `build.bat` → `RemotingProbe.exe <HostIP> <RemotePort> <自PC IP> 60`。

## 合否基準

| # | 確認項目 | 合格条件 |
| --- | --- | --- |
| P-1 | `eServer` 接続 | `ServerName` / `ConnectPc` が表示される |
| P-2 | 自PC の `clsPcObj` 取得 | `GetPcObject(自IP)` または `ConnectPc` 走査で取得できる |
| P-3 | `stcPCINF_DATA` | `stcMCValue` 長 = 150、index 65 の `sCaption_j`/`sUnit`/`lDivVal` が VB 画面の直径表示と整合 |
| P-4 | `stcRealData.intIOBit` | 長さ 2100、毎秒値が変化する（PLC 接続中） |
| P-5 | 値の一致 | index 65 の算出値が VB_FERROTEC.exe メイン画面の表示値（同 `iDispNo`）と一致（丸め差のみ） |
| P-6 | 工程・長さ | `strPROCESS` が 16 工程名のいずれか、工程別長さ（index 115〜119 / `strXXX_LENGTH`）が増加する |
| P-7 | 性能 | 1 回の読取り（`stcRealData`+`stcPcInfo`）の遅延 avg < 100 ms、max < 500 ms。60 秒で errors = 0 |
| P-8 | 常駐 | Python プロセスの RSS が 60 秒で増え続けない（目安 < 150 MB） |
| P-9 | 無害性 | 実行中・終了後に VB_FERROTEC.exe とホスト画面に影響がない（ホストの `dgvPc` 接続数が変わらない） |

## 想定される問題と対処

| 症状 | 原因候補 | 対処 |
| --- | --- | --- |
| `pythonnet.load("netfx")` 失敗 | Python が 32bit、または pythonnet が .NET Core を選択 | Python x64 を使用。`PYTHONNET_RUNTIME=netfx` を環境変数に設定 |
| `GetObject` 後の最初のプロパティ参照で `SocketException` | ファイアウォール、HostIP/RemotePort 誤り、ホスト未起動 | `Test-NetConnection <HostIP> -Port <RemotePort>` |
| `SerializationException`（型が見つからない／バージョン不一致） | DLL が装置PC のものと別ビルド | 手順 1 のとおり同一 DLL を使用 |
| `ConnectPc` に自PC がない | VB_FERROTEC.exe 未接続、IP 表記差（`IPAddress` にポート付き等） | `--ip` 指定、PoC の `ConnectPc` 一覧出力で実表記を確認 |
| `intIOBit` が `None`／長さ 0 | PLC 未接続、VB 側 `sub_Update_RealTimeData` 未開始 | VB 画面で ONLINE を確認。PullerView では「判定不能（---）」扱い |
| 値が VB 画面と桁違い | `lDivVal` の適用漏れ、index 71/132 系の加算特例（`frmMain` 1338 行付近） | 特例 index は設定時に警告（要件 analysis_vbferrotec_source.md S-15） |
| pythonnet で透過プロキシのプロパティが取れない | pythonnet と Remoting 透過プロキシの相性 | PoC-B で再現確認。再現しなければ pythonnet 側の問題 → 本番は C# ブリッジ（名前付きパイプ／stdout JSON）＋ Python GUI の構成へ切替（ADR 候補） |

## 結果の記録

実行後、`poc_result.csv` と標準出力ログを `docs/poc-result-YYYYMMDD.md` にまとめ、P-1〜P-9 の合否を記入する。
合格なら architecture.md の Remoting 読取り層を pythonnet 方式で確定、P-7/P-8 不合格または透過プロキシ問題があれば C# ブリッジ方式へ。
