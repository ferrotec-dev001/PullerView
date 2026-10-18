# VB_FERROTEC Remoting 解析メモ（mdlLocalService）

- 文書ID: ANL-Remoting-01
- 版数: 0.4 / 最終更新: 2026-10-18
- 対象: VB_FERROTEC.exe の `Module mdlLocalService`（VB.NET, 2008/08/20 初版, Ver1.0〜）
- 取扱い: Agent.MD の機密保持条項に従う（社外送信禁止）

## 1. 判明した事実（コードから確定）

| # | 事実 | PullerViewへの影響 |
| --- | --- | --- |
| F-1 | チャネルは **TcpChannel + Binary フォーマッタ**。サーバ側シンクの `TypeFilterLevel = Full`、`RegisterChannel(..., ensureSecurity:=False)` | Q-03 回答: **TCP + Binary**。クライアントも Binary + TypeFilterLevel.Full が必要 |
| F-2 | チャネル生成時 `port = 0`（OS任せ）、`name = stcLocal.strHostName` | 本チャネルは固定ポートで待受けしない。**このモジュールだけでは接続先ポートを特定できない** |
| F-3 | リモートオブジェクトURL: `tcp://{stcLocal.strHostIP}:{stcLocal.iRemotePort}/{g_strSessionID}{Enum_FormID名}` | URIに**セッションID**が含まれる（動的値） |
| F-4 | `Activator.GetObject(GetType(clsPcObj), url)` で取得するオブジェクトは **clsPcObj のみ**（`Enum_FormID.ePcObj`） | 読込対象の入口は `clsPcObj` と推定 |
| F-5 | `g_strSessionID` は、別系統の **TCPソケット通信**（`clsTcpClient`, `strHostIP:iSocketPort`）で送った自ホスト名への応答電文（TextID="00"）で設定される。設定直後に `mdlPcObjRWPLC.ThreadStart()` が起動 | セッションID取得の仕組みがPullerViewにも必要になる可能性 |
| F-6 | `Imports VB_FERROTEC_Common` を使用。`Enum_FormID`, `TP_SOCKETINFO` 等はこの共通アセンブリ由来と推定 | Q-02: 共有インターフェースDLLの有力候補は **VB_FERROTEC_Common.dll**（clsPcObj が含まれるかは要確認） |
| F-7 | 上記ソケット通信は別機能（`CST_PULLER_START_REQUEST` 等、上位との引上げ制御電文、BCC検査、再送ini）。Remoting データ連携とは別系統 | PullerViewのスコープ外。混同しない |

## 2. 重要な疑義（このコードだけでは判断できない）

**このモジュールにはRemotingオブジェクトを「公開」するコード（`RemotingServices.Marshal` / `RegisterWellKnownServiceType`）がなく、`Activator.GetObject` による「取得（クライアント側）」のみである。**

| 仮説 | 内容 | 影響 |
| --- | --- | --- |
| H-A | `clsPcObj` は**上位ホスト側**が公開（セッション毎URI）。VB_FERROTEC は `mdlPcObjRWPLC` でPLC値を読み書きして、そのオブジェクトと遣り取りするクライアント | Excel仕様「VB_FERROTEC.exe の Remoting Service で読む」と前提が異なる。PullerViewは**ホスト側オブジェクトへの接続**（同一PCのみ＝Q-01と矛盾の可能性）か、別の取得経路が必要 |
| H-B | 別モジュール（mdlPcObjRWPLC 等）で VB_FERROTEC自身が clsPcObj を公開している | 公開ポート/URIの確認が必要（port=0 のため動的ポートの場合、PullerViewから発見不能） |

## 3. 追加で必要な資料（優先順）

1. `clsPcObj` の定義（public メンバ、データ構造体、異常フラグの有無）
2. `mdlPcObjRWPLC`（ThreadStart 内で clsPcObj をどう使うか）
3. `stcLocal`（strHostIP / iRemotePort / iSocketPort の値と設定元）、`Enum_FormID`
4. `VB_FERROTEC_Common` のDLL（またはソース）
5. 実機の構成図（VB_FERROTEC.exe、PLC、上位ホストの関係）と、実機での `netstat` 結果（待受ポート確認）

## 4. 技術上の留意点

- **セキュリティ**: `TypeFilterLevel.Full` + 認証なし(`ensureSecurity=False`) は既知の脆弱な構成。PullerViewは同一PCローカル接続に限定し、公開範囲を広げない（Q-01確定と整合）。
- **互換性**: サーバが .NET 2.0(CLR 2.0)、クライアント(pythonnet)は CLR 4.x となる。Binary Remoting は共有型が同一であれば概ね相互運用可能だが、**PoCで要検証**（RISK-01）。
- **書込み（異常フラグ）**: clsPcObj の書込み可能メンバが不明のため FR-AL-03 は未確定（Q-05b）。
- 本モジュールの空 `Catch` やスレッド `Abort` 等の実装上の癖は、PullerView側では踏襲しない（規約: 異常を握りつぶさない）。


---

# 追補: clsPcObj 解析（v0.2）

対象: `Public Class clsPcObj : Inherits clsBaseObj`（Remotingで公開・取得されるオブジェクト。コメントは GB2312 の文字化けを復元して解釈）

## 5. 構造（コードから確定）

| 区分（コメントの意味） | プロパティ | 更新特性 | PullerViewでの用途 |
| --- | --- | --- | --- |
| 実時間データ（約?00ms周期で更新） | `stcRealData`(TP_RealData), `stcPcInfo`(TP_PcInfo), `stcFileManage`(TP_FileManage) | 高頻度 | **監視対象値の取得元の本命**（FR-DT）。工程ステータスもここに含まれる可能性（Q-13） |
| 参数データ（変更時のみ更新） | `VersionNO`, `intProfile1/2`, `intParam`, `intProfileRange`, `intParamRange`, `intManualOption`, `intMainteRange`, `intRecipe` | 変更時 | プロファイルテーブル系（今回スコープ外） |
| 設定ファイル情報（1回のみ読込） | `stcRecipeInf`, `stcOption`, `objAlarmCSV`, `objAlarmMsgCSV`, `stcPCINF_DATA`, `stcFileData`, `stcIOCOMMENT`, `stcMessage` | 起動時1回 | 既存アラーム定義・IOコメント。必要時のみ参照 |
| リモート側からの書込み（PLCへ） | `stcSendPLC`(TP_SendPLC), `stcAlarmInf`(TP_AlarmInf()), `stcFileMessage`, `strStatusData`(Byte()) | 書込み | **異常フラグの書込み先候補は `stcAlarmInf` / `stcSendPLC`**（Q-05b） |

## 6. 書込み権限制御（fnc_GetRight）

- 権限は `right > 2` かつ `stcPcInfo.strLocalOrNet = "1"`（ネット接続モード）の場合のみ付与。`"0"`(ローカル)では常に False。
- 権限保持者は1セッションのみ。先着順。保持中セッションが `SessionList` から消えると次の要求者へ移る。
- 保持者の最終要求から **60秒**超で、`sub_RightManage`（1秒周期）が権限を解放する。
- 各プロパティのSetterには権限チェックがない。権限チェックは**呼出し側の任意利用**（`fnc_GetRight`）に依存。

## 7. PullerViewへの影響・リスク

| ID | 内容 | 対応案 |
| --- | --- | --- |
| R-A | **書込みの衝突**: Setterは構造体/配列を**丸ごと置換**。PullerViewが `stcAlarmInf`/`stcSendPLC` を書くと、遠隔操作クライアントや本体の値を上書きするおそれ（read-modify-writeも非原子的） | 書込み専用の新メンバ追加（VB側改造）を提案するか、書込み先が既存機能と独立か確認。確認前は書込みを実装しない |
| R-B | 権限制御と競合: PullerViewが権限を取得すると、実運用の遠隔クライアントの権限を奪う／阻害する。取得しない場合はSetterが無検査のため権限制御を迂回する形になる | 方針をVB側担当者と合意（Q-18） |
| R-C | ローカルモード(`strLocalOrNet="0"`)では `fnc_GetRight` が常にFalse | 同一PC運用（Q-01）時のモードを確認（Q-18） |
| R-D | 構造体/配列プロパティは**取得ごとに全体をシリアライズ**。巨大な配列(`objAlarmCSV` 等)の毎秒取得は負荷・メモリ増 | 毎秒取得は `stcRealData` 等に限定、設定系は起動時1回（NFR-02/03、Agent.MD メモリ最小化） |
| R-E | `clsBaseObj` で `InitializeLifetimeService` が未オーバーライドの場合、**無通信5分でリモート参照が切れる**可能性 | 再接続ロジックを必須とする（FR-DT-03）。clsBaseObjの確認（Q-19） |
| R-F | `sub_RightManage` は `SessionList.Remove(_CurrentSessionID)` を、権限者がいない時も(Nothingで)毎秒実行。SessionListの型次第で例外→スレッド終了の可能性 | PullerView側の対応不要。VB側既知課題として報告候補 |
| R-G | クラスは `TP_*` 構造体に依存。pythonnetで読むには、これらを含むアセンブリが必要 | `VB_FERROTEC.exe` 自体、または `VB_FERROTEC_Common.dll` を参照（Q-02） |

## 8. 仮説の更新

`clsPcObj` は「PLCデータの保持＋遠隔クライアント向け書込み受付」を担い、`strLocalOrNet` で動作モードを持つ。
→ **VB_FERROTEC.exe（PLC接続側PC）が `clsPcObj` を公開し、遠隔PC上のVB_FERROTECがGetObjectで取得する構成**（H-B寄り）の可能性が高まった。
ただし公開コード（Marshal/Register…）と待受ポート（port=0問題）は未確認のため確定はしない。


---

# 追補2: VB_FERROTEC_Common.dll 解析反映（v0.3）

詳細は `docs/vb-ferrotec-common-api.md`。

## 9. 確定事項

| # | 事実 | 影響 |
| --- | --- | --- |
| C-1 | `clsPcObj`・`clsBaseObj`・全 `TP_*` 構造体は**このDLLに含まれる** | Q-02 解決。PullerViewはこのDLLだけで型参照できる（pythonnetでロード可。AnyCPU・強名前なし） |
| C-2 | `clsBaseObj.InitializeLifetimeService` は Nothing を返す（リース無期限） | R-E 解消。ただし通信断・再起動時の再接続は必要 |
| C-3 | `TP_PcInfo` に監視向けの**名前付き値**が75項目（string型）: `strPROCESS`, `strHEATER_TEMP`/`_POWER`/`_VOLTAGE`/`_CURRENT`, `strMFC_Ar_FLOW_*`, `strSEED_*`, `strCRUCIBLE_*`, `strINGOT_*`, `strCOOLING_WATER_FLOW`, `str_State`, `str_Alarm`(bool) 等 | データ取得元の有力候補。値は**文字列**のため数値変換が必要 |
| C-4 | `TP_PCINF_DATA.stcMCValue`(`TP_MCINFDAT[]`) に、既存の表示定義がある: `iDINDEX`, `lValue`, `sCaption`(+`_e/_j/_c`多言語), `sUnit`, `sFormat`, `lDivVal`(除数), `iColor`, `iDispNo`, `bGraphDisp`, `lRangeUPPER/BOTTOM` | `datasetting.ini` の Address/名称/単位/小数点と**重複・流用可能**。多言語名称がある |
| C-5 | `TP_RealData` は `intMainte/intIOMonitor/intAlarm/intManual/intIOBit/intLift` の6つの **int[]**（PLC生データ） | Address が配列インデックスになる可能性 |
| C-6 | `TP_SendPLC` = `TP_SendPLCData[]`（`strDevice`, `strAddress`, `intSize`, `intDatas`） + `strReadAddr[]` → **PLCのデバイス/アドレスへ直接書込む構造** | 異常フラグ用途で使うのは**極めて危険**。PullerViewは使用しない |
| C-7 | `TP_AlarmInf` = `strAlarmName`(string) + `strAlarm`(string[]) | 異常フラグ／異常名称を渡す入口の有力候補（Q-05b）。ただし書込み時の上書きリスク(R-A)は残る |
| C-8 | `clsServerObj` が `PcObjList`/`AddPcObject`/`GetPcObject` を持つ | 上位サーバが各炉PCの `clsPcObj` を集約する構成。PullerViewは炉PC自身の `clsPcObj` へ接続する前提（Q-01確定と整合）。公開側の確認は引続き必要 |

## 10. 残課題

1. 炉PCの `clsPcObj` の実際の公開先（port=0問題、`RemotingServices.Marshal`/Register箇所）→ `mdlPcObjRWPLC` など VB_FERROTEC.exe 側コードが必要
2. `strPROCESS`/`str_State` の実際の値（11工程との対応）→ 実機のサンプル値
3. `TP_AlarmInf` の `strAlarmName`/`strAlarm[]` の意味と、既存のアラーム処理での使われ方
4. `datasetting.ini` の Address 書式（案: `PcInfo.strHEATER_TEMP` / `MC.<iDINDEX>` / `IOMonitor.<index>`）


---

# 追補3: 方針決定（2026-10-18）

**PullerViewは読取り専用（表示のみ）とする（ADR-0006、Q-18確定）。** これにより本書の R-A（書込み衝突）、R-B（権限競合）、R-C（ローカルモードの権限）、および `TP_AlarmInf`/`TP_SendPLC` に関する書込み検討は**対象外**となる。`fnc_GetRight` も呼ばない。残る論点は、clsPcObj の公開先・ポート（Q-19）と、読取り対象項目の確定のみ。
