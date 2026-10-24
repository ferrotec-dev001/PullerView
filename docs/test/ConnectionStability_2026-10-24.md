# RemotingService 接続安定性試験 報告（2026-10-24）

対象: PullerView.exe（待受側: SocketPort 10031 / RemotePort 10030）× FakeFerrotec.exe（VB_FERROTEC.exe 模擬クライアント）
環境: サンドボックス Linux / Mono 6（.NET 4.0 API のみ使用）。**実機（Windows 7 32bit / .NET 4.0）では未実施**。
手順: `tools/conn_test/run_scenarios.py`（自動）。PullerView は新設の `--headless` モード（画面なし・毎秒 1 行の状態出力）で実行。

## 1. 結果一覧

| # | シナリオ | 修正前 | 修正後 | 備考 |
|---|---|---|---|---|
| S1 | 待受 → 4 秒後にクライアント接続（正常） | OK | OK | 1 Hz で欠落なし、write p99 3〜5 ms |
| S2 | クライアント先行起動、待受は 6 秒後 | OK | OK | クライアントは Connection refused を 2 秒ごと再試行し接続 |
| S3 | 正常切断 → 4 秒後に再接続 | △ | OK | 接続直後の空データで「工程 OTHER へ切替」が出ていた（下記 B）→ 解消 |
| S4 | クライアント強制終了（kill）→ 別クライアント接続 | OK | OK | OS が FIN を送るため即時に切断検知 |
| S5 | **半開ソケット**（書込み・TextID=00 とも停止、ソケットは開いたまま） | **NG** | OK | 修正前: 18 秒以上「接続・通信 OK」のまま古い値を表示し続けた（下記 A/C） |
| S6 | データ更新停止 8 秒（ソケット・TextID=00 は維持） | **NG** | OK | 修正前: 古い値を毎秒グラフ・CSV へ流し続けた（下記 A） |
| S7 | 接続/切断の繰り返し ×8 | OK | OK | 毎回新 SessionID、例外なし |
| S8 | **二重接続**（後発が切れたら） | **NG** | OK | 修正前: 後発切断で未接続表示になり、生きている先発接続を無視（下記 D） |
| S9 | 不正電文（ゴミ／BCC 不正／ETX 無し 5KB／1 バイトずつ） | **NG** | OK | 修正前: BCC 不正フレームを受理、ETX 無し長文の後の正常電文を TextID=XX として破棄（下記 E） |
| S11 | 高頻度書込み（100 ms） | OK | OK | 読取りは 1 回/秒を維持、fails 0 |
| S12 | PullerView 再起動（クライアント接続中） | OK | OK | クライアント側が書込み失敗を検知して再接続 → 復帰 |
| S13 | 接続/切断 ×25（リーク確認） | — | △ | mem 4→9 MB。**スレッド数が 1 接続あたり +1 で残る**（下記 F、実機で要確認） |
| S14 | 連続 150 秒 1 Hz | OK | OK | 148 書込み/149 読取り、[遅延] 0 件、mem 4→7 MB、CSV 1 行/秒 |

タイムライン表記（毎秒 3 桁）: 接続(conn) / 通信 OK(comm) / 鮮度(fresh)。例: `111`=正常、`100`=接続はあるがデータ更新なし、`000`=未接続。

## 2. 見つかった問題と対策（HostLink.cs）

**A. データ鮮度を見ていなかった（S5/S6）**
- 読取りスレッドは毎秒 clsPcObj を読むだけだったため、VB_FERROTEC が書込みを止めても同じ値を「新しいサンプル」として流し続け、通信 OK のまま古い値を表示・CSV 記録していた。
- 対策: VB_FERROTEC は書込みごとに新しい `TP_RealData` を送るので `intIOBit` の**配列参照が前回と同じ＝更新なし**と判定（`TP_PcInfo` は構造体なので内容で比較）。`stale_sec`（既定 5 秒）更新が無ければ通信断扱い（表示 `---`、CSV へは書かない）。更新が再開すれば自動復帰。

**B. 接続直後の空データで工程が OTHER に切り替わる（S3/S5/S6/S8/S9）**
- ハンドシェイク直後、クライアントがまだ何も書いていない時点で読んだ空の構造体（strPROCESS=""）を正規サンプルとして処理し、「BODY → OTHER → NECK」とグラフを 2 回クリアしていた。
- 対策: `intIOBit` 未設定かつ `TP_PcInfo` が空のうちは読み飛ばす。

**C. 半開ソケットを検出できない（S5）**
- ケーブル抜け・相手 PC のフリーズなど FIN が来ない切断では、ソケット read がブロックし続け永久に「接続中」だった。
- 対策: 受信タイムアウト 5 秒で巡回し、**TextID=00 再送もデータ更新も `heartbeat_timeout_sec`（既定 60 秒）無ければソケットを閉じてセッション破棄**（VB_FERROTEC 側の再接続を促す）。加えて TCP KeepAlive を有効化。

**D. 二重接続時の扱い（S8）**
- 最後にハンドシェイクした接続のみ保持し、それが切れると残っている接続があっても未接続になっていた。
- 対策: セッション一覧を持ち、採用中が切れたら**残っている最新の接続へ自動で戻す**（ログ `[SESSION] 採用: …`）。セッションが変われば M 側でグラフをクリア。

**E. 電文パーサの堅牢性（S9）**
- BCC を検証していなかった。ETX の無い長文の後ろに正常電文が続くと最初の STX から切り出して TextID 誤判定。受信バッファ無制限。
- 対策: BCC 不一致は破棄してログ、**最後の STX** から再同期、ETX の来ない受信は 64 KB で破棄。

**F. 接続を繰り返すとスレッド数が増える（S13）— 実機確認事項**
- 25 回の接続/切断で OS スレッド数 7 → 32。PullerView 自身のクライアントスレッドは全て終了ログを出しており、残っているのは Remoting TCP サーバーチャネルの接続処理スレッド（Mono 実装はアイドルスレッドを保持する）。.NET Framework では接続クローズで終了する実装のため実機では解消する見込みだが未確認。
- 確認方法: 実機で `PullerView.exe --headless` を起動し、VB_FERROTEC を数回再起動して STAT 行の `thr=` が増え続けないことを見る。増え続ける場合はチャネル設定（`exclusiveAddressUse` / 接続ごとの `Disconnect` 後に `ChannelServices` の接続を閉じる）で対処する。

その他: 終了時の `ThreadAbortException` ログを抑止。`PullerView.ini [Host]` に `stale_sec` / `heartbeat_timeout_sec` を追加。

## 3. 追加したツール
- `PullerView.exe --headless [秒]` — 画面なしで待受・受信のみ。毎秒 `[STAT] conn= comm= reads= fails= fresh= stale= bad= proc= len= sess= mem= thr=` を出力（現地での接続診断にも使用可。Windows ではコンソールを自動で開く）。
- `FakeFerrotec.exe` 試験オプション — `--duration` `--connect-delay` `--disconnect-at/--reconnect-after` `--kill-at` `--silent-at/--silent-for` `--freeze-at` `--flap N` `--garbage` `--interval` `--heartbeat` `--no-meta`。終了時に `STATS sessions= writes= writeErr= lat_p50/p99/max`。
- `tools/conn_test/run_scenarios.py` — 上記シナリオ一括実行（Windows は `python run_scenarios.py --dir <exe フォルダ>`、Mono は `--mono`）。

## 4. 実機で確認すべきこと
1. 実 VB_FERROTEC.exe の TextID=00 再送間隔（ReConnect_Time）。`heartbeat_timeout_sec` はその 3 倍以上にする（既定 60 秒）。
2. 実 VB_FERROTEC.exe が書込みを止めるケースの有無（PLC 通信断時に stcRealData を更新しないなら `stale_sec` で `---` 表示になる＝期待動作）。
3. S13 のスレッド数（上記 F）。
4. PullerView 再起動直後のポート再バインド（Windows の TIME_WAIT。失敗時は `ExclusiveAddressUse=false` / `ReuseAddress` を検討）。
