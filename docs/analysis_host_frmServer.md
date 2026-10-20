# 既存ホスト（VB_FERROTEC Server / frmServer.vb）インターフェース分析

- 対象: ホスト側 `frmServer.vb`（作成 2008/08/16、Socket Ver 1.0 改修 2009/06/23、作成者コメントはGB2312の文字化け）
- 目的: PullerView 要件 D-02「異常フラグを Remoting Service へ引き渡す」の実現可否と接続方式の判断材料
- 版: 2026-10-20 r0（中村勝 提供コードに基づく。`VB_FERROTEC_Common`／`clsTcpServer`／`clsPcObj` 等は未入手のため推定を含む）

## 1. ホストの通信経路（コードから確認できたもの）

| # | 経路 | 実装 | 相手 | 役割 |
|---|------|------|------|------|
| R | .NET Remoting | `sub_RegisterService(strHostName, iRemotePort)`、`g_ServerObj.ConnectPc`（`clsPcObj` のリスト）、`mEventRef.UpdateUserEvent` | 装置PC（VB_FERROTEC.exe） | 装置PCが登録し、`stcPcInfo`（稼働状態）をホストが参照 |
| S1 | TCP Server `iSocketPort_PC` | `mTcpListenerPc`（`clsTcpServer`） | 装置PC | MES電文の中継（装置→MES）、日時応答、送受信テスト |
| S2 | TCP Client | `mTcpClientMES`（`clsTcpClient`） | MES | MES電文の送受信。ETX定期送信・再送・再接続スレッドあり |
| S3 | TCP Listener `iSocketPort_LAN` | `mTcpListenerNET` → `clsTcpServer.ReceiveNetThread` | 遠隔PC（LAN） | 遠隔監視接続。装置ごとの接続数を `frmRemoteInfo` で表示 |
| W | TCP Client | `m_TcpClient.LinkWebServer` | Webサーバ | 詳細不明（`clsTcpServer` 側実装） |
| DB | SQL Server | `SocketLog`、`ResultReport` テーブル（`getdate()`） | — | 電文ログ、結果報告の保存・再送 |

装置の識別は IP アドレス（`fnc_IPToDeviceID` / `fnc_DeviceIDToIP`）で行い、`iSocketPort_PC` 側の送信先選択は `readWriteObject.strIP.Contains(...)` の**先頭一致で1件のみ**（`Exit Sub`）。

## 2. Remoting で公開されている装置情報（`clsPcObj.stcPcInfo`）

コード内で参照されているフィールドのみ列挙。

| フィールド | 用途（MES 稼働状況応答への変換） |
|-----------|----------------------------------|
| `bStatePlc` | Online(02)/Offline(01) |
| `int_WorkInf` | 1:Waiting 2:Working 3:Interruption 4:Maintenance |
| `str_RodID` | Rod No. |
| `str_RecipeNO` | Recipe No.（1〜99） |
| `int_PauseCode` | 中断理由コード（WorkInf が 3/4 のとき使用） |
| `str_Diameter` | 140/170/210 → 01/02/03 |
| `int_PullerTimes` | 同一 Rod No 内の引上げ回数 |
| `long_WorkTime` `long_AutoTime` `long_WaitTime` | 稼働実績時間 |
| `long_StopTime1`〜`long_StopTime11` | 停止時間（区分1〜11） |

**異常フラグ／アラームに相当するフィールドや、第三者プロセスがホストへ値を書き込むメソッドは、このファイルの範囲には存在しない。** 異常に近い既存概念は `int_WorkInf=3(Interruption)` と `int_PauseCode` のみで、いずれも装置PC側（VB_FERROTEC.exe）が Remoting 経由で設定するものと推定される。

## 3. D-02 への含意

r1「異常フラグを Remoting Service へ引き渡す」を実現する経路は次の4案に整理できる。いずれも **PullerView 単独では完結せず、既存資産の改修または仕様確認が必要**。

| 案 | 経路 | 必要な改修 | Q-18（読取り専用）との関係 | 評価 |
|----|------|-----------|----------------------------|------|
| A | PullerView → VB_FERROTEC.exe（装置PC内IPC）→ `stcPcInfo` 新フィールド → Remoting → ホスト | VB_FERROTEC.exe、`VB_FERROTEC_Common`（構造体）、ホスト | VB_FERROTEC.exe への書込みが発生（PLCには書かない） | 既存の流れに沿うが改修範囲が最大 |
| B | PullerView が Remoting クライアントとしてホストの新メソッドを呼ぶ | ホスト、`VB_FERROTEC_Common` 参照、PullerView | VB_FERROTEC.exe／PLC には書かない（Q-18 と両立） | .NET Framework Remoting への依存が PullerView に生じる |
| C | PullerView が `iSocketPort_PC` へ新 TextID 電文を送る（`Case Else` で MES へ無改修転送） | MES 側の電文定義 | 同上 | **非推奨**: 同一IPからの2接続目となり、`Contains` 先頭一致のため MES→装置電文が PullerView に誤配送されるリスク |
| D | 異常フラグは PullerView 画面表示・ローカルログのみとし、ホスト連携は対象外 | なし | Q-18 を維持 | r1 の記述を取り下げる必要あり |

### 判断に必要な追加情報
1. r1 の「Remoting Service」が本ホスト（frmServer）を指すのか、装置PC内の別サービスを指すのか
2. 異常フラグの最終的な利用者（MES へ送るのか、ホスト画面表示のみか、ログのみか）
3. 未入手ソース: `VB_FERROTEC_Common`（`clsTcpServer`, `clsTcpClient`, `clsPcObj`, `TP_PcInfor`, `TP_SOCKETINFO`, TextID 定数）、装置PC側 VB_FERROTEC.exe の Remoting 登録部

## 4. PullerView 設計上の留意点（本ホストとの共存）

- **`iSocketPort_PC` へは接続しない**（§3 案C の誤配送リスク）。
- 遠隔監視口 `iSocketPort_LAN`（S3）は既存の遠隔PC向けであり、プロトコルは `clsTcpServer.ReceiveNetThread` に依存。PullerView で流用する場合は同クラスの仕様確認が前提。
- ホストが参照する装置情報は Remoting の `stcPcInfo` に限られるため、PullerView の PV/SV 値（PLC index 由来）をホストへ渡す要件は現状存在しない。
- ホストは .NET Framework（Remoting、`Thread.Abort`、`Microsoft.VisualBasic`）前提。PullerView を .NET 6+ で実装する場合、Remoting 直結（案B）は不可で、案A または別IPC（名前付きパイプ・ファイル・TCP）が必要。

## 5. 参考: ホストコードで確認した注意点（PullerView の責務外）

- `sub_OperationConditionAnswer` 等で `m_clsPcObj` が一致しない場合 `Nothing` 参照となる（Remoting 未登録の装置PCがソケット接続のみしている状態）。
- SQL は文字列連結で組み立て（`SocketLog` の `Data` 列に電文をそのまま埋め込み）。
- スレッド停止は `Thread.Abort`。
