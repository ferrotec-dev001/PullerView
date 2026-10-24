# 調査: RemotingService 経由で VB_FERROTEC.exe に書込み（PLC 書込み）をさせられるか（2026-10-24）

対象ソース: `VB_FERROTEC.zip`（VB_FERROTEC.exe）、`VB_FERROTEC_Common_2.zip`（clsPcObj / clsBaseObj / clsProxyEventObj）、`VB_FERROTEC_SERVER.zip`

## 結論
**できる。** VB_FERROTEC.exe には「サーバー側 clsPcObj からの指示で PLC に書く」経路が実装済みで、PullerView（待受側＝clsPcObj の実体を持つ側）からその経路を叩ける。サンドボックスで模擬クライアント相手に end-to-end で動作を確認した（実 PLC は未接続）。
ただし現在の要件（D-02: PullerView は読取り専用、Remoting/PLC へ書かない）は変更しない。書込みが必要になった場合の別件改造として本資料を残す。

## VB_FERROTEC.exe 側の仕組み（ソース根拠）
| 箇所 | 内容 |
|---|---|
| `PlcAccess/mdlPcObjRWPLC.vb` `ThreadStart` | `proxy = New clsProxyEventObj` → `g_CurrentPcObj = Activator.GetObject(clsPcObj, url)` → **`AddHandler g_CurrentPcObj.WriteMsgEvent, AddressOf proxy.WriteMessage`**（サーバー側オブジェクトのイベントに、自プロセス内の MarshalByRefObject をハンドラとして登録 = サーバー→クライアントのコールバック経路） |
| 同 `sub_Button_Envent(eEventType, eFormID)` | コールバック受信時 `eFormID = ePLC(3)` なら **`stcPLC = g_CurrentPcObj.stcSendPLC`** を読み、各要素について **`sub_Plc_Set(.strDevice, .strAddress, .intSize, .intDatas)`** で PLC へ書込み、その後 `strReadAddr` のアドレス群を再読込（`sub_UpdateParamData` / `sub_UpdateRemoteParam`）。他の eFormID: `eFileManage`(レシピ読込/保存) `eAlarm`(アラーム一覧を `stcAlarmInf` へ書き戻す) `eLoad` `eAutoStatus` |
| `Model/mdlPLC.vb` `sub_Plc_Set` | `m_objMxCom.fnc_Set_R(address, size, data)`（三菱 MX Component、R デバイス、960 語ずつ分割） |
| `VB_FERROTEC_Common/BaseClass/clsBaseObj.vb` | `Public Event WriteMsgEvent As DlgWriteMessage(eEventType As Enum_ButtonEvent, eFormID As Enum_FormID)`、`WriteMessage()` が登録済みデリゲートを順に Invoke（失敗したハンドラは自動で除去） |
| `mdlLocalService.vb` `sub_Start_Service` | クライアント側も `TcpChannel(port=0, BinaryServerFormatterSinkProvider TypeFilterLevel.Full)` を登録 → コールバックを受けられる |

VB_FERROTEC.exe が**リモートオブジェクトから読む**メンバーは `stcSendPLC`（PLC 書込み指示）と `stcFileMessage`（レシピ操作）の 2 つだけ。それ以外の 22 メンバー（stcRealData / stcPcInfo / stcPCINF_DATA / stcAlarmInf …）は VB_FERROTEC.exe → サーバーへの一方向書込み。
なお `stcSendPLC` を**設定する側**のコード（リモート操作 UI）は入手済み zip には含まれていない（VB_FERROTEC_SERVER にも無い）。

## PullerView 側からの手順（VB_FERROTEC_SERVER 経由のリモート UI と同じ）
1. VB_FERROTEC.exe が接続し `AddHandler` 済みであること（`clsBaseObj.WriteMsgEventEvent` のハンドラ数が 1 以上）
2. `clsPcObj.stcSendPLC = TP_SendPLC { stcPLCData = [ TP_SendPLCData { strDevice, strAddress, intSize, intDatas } …], strReadAddr = [再読込アドレス…] }`
3. `clsPcObj.WriteMessage(Enum_ButtonEvent.eBTN_Execute, Enum_FormID.ePLC)` を呼ぶ → VB_FERROTEC.exe がコールバックを受けて PLC へ書く

## サンドボックス検証（`tools/RemotingWritePoc/WritePocHost.exe` × `FakeFerrotec.exe --event-proxy`）
```
HOST  : WriteMsgEvent のハンドラ数 = 1
HOST  : stcSendPLC = {R 33600 size=3 data=[1,2,3]} を設定 → WriteMessage(eBTN_Execute, ePLC) 呼出し → 完了 10ms
CLIENT: WriteMsgEvent 受信 #1 eEventType=eBTN_Execute eFormID=ePLC
CLIENT:   → sub_Plc_Set(device=R, address=33600, size=3, data=[1,2,3])  ※模擬: 実 PLC へは書かない
CLIENT:   → 再読込アドレス: 33600
```
（ログ: `RemotingWrite_host.log` / `RemotingWrite_client.log`）
- クライアントの `AddHandler` を PullerView 側（TypeFilterLevel.Full）が受理し、コールバックが VB_FERROTEC 側に届き、`stcSendPLC` の内容が正しく読めた。
- Mono 固有の注意（実機 .NET では不要）: 透過プロキシに対する `PropertyInfo.GetValue` が空を返すため `get_xxx` メソッドを直接呼ぶ／コールバック内の入れ子 Remoting 呼出しを避けて書込みループ側で読む。

## 実機で確認が必要な点
1. 実 VB_FERROTEC.exe が PullerView 接続時に `AddHandler` まで到達しているか（`WritePocHost` のハンドラ数表示、または PullerView ログ）。`ThreadStart` は Try/Catch で失敗を握りつぶすため、実機で 0 なら Remoting チャネル設定（クライアント側コールバック用ポートの到達性）を確認する。
2. 書込み先アドレス・値の仕様（どの R デバイスが「異常フラグ」か）は PLC 側仕様書が必要。**実機で WritePocHost を使う場合は本当に PLC へ書かれる**ので、安全なアドレスを指定すること。
3. 実 PLC 書込みの所要時間と、PullerView の読取り周期（1 秒）との干渉。

## 要件への影響
- D-02（読取り専用）は維持。本書は「技術的に可能で、改造範囲は PullerView 側に `stcSendPLC` 設定＋`WriteMessage` 呼出しを追加するだけ（VB_FERROTEC.exe の改造不要）」の記録。
- 書込みを採用する場合の追加要件: 書込み条件（どの判定でどのアドレスに何を書くか）、誤書込み防止（設定で明示的に有効化、ログ記録）、VB_FERROTEC 未接続時の扱い。
