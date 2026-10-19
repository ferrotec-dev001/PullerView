# PcInfo／RealData マッピング（mdlPcObjRWPLC 解析より）

- 文書ID: ANL-Mapping-01 / 版数: 0.2 / 最終更新: 2026-10-19
- 取扱い: Agent.MD の機密保持条項に従う

## 1. TP_PcInfo の値の由来

`TP_PcInfo`（`clsPcObj.stcPcInfo`）は VB_FERROTEC が **1秒周期**で生成して書込む。数値項目は VB 画面の表示ラベル文字列 `g_lblVal(stcMCValue(n).iDispNo)` を**そのまま文字列で**格納する。
`stcMCValue(n)` の n は**配列の位置（0始まり）**。`stcPCINF_DATA.stcMCValue`（表示定義：名称・単位・書式）は**起動時に1回だけ**設定される静的定義で、**ライブ値は含まない**。

| PcInfoフィールド | stcMCValue 配列位置 n（名称/単位の参照先） |
| --- | --- |
| `PcInfo.strUPPER_PRESSURE` | 0 |
| `PcInfo.strLOWER_PRESSURE` | 1 |
| `PcInfo.strCOOLING_WATER_FLOW` | 15 |
| `PcInfo.strMELT_TOUCH_VALUE` | 16 |
| `PcInfo.strAPC_PRESSURE_SET` | 17 |
| `PcInfo.strAPC_PRESSURE_ACTUAL` | 18 |
| `PcInfo.strMFC_Ar_FLOW_SET` | 19 |
| `PcInfo.strMFC_Ar_FLOW_ACTUAL` | 20 |
| `PcInfo.strHEATER_TEMP` | 21 |
| `PcInfo.strHEATER_TEMP_SET` | 22 |
| `PcInfo.strHEATER_POWER_SET` | 23 |
| `PcInfo.strHEATER_POWER` | 24 |
| `PcInfo.strHEATER_VOLTAGE` | 25 |
| `PcInfo.strHEATER_CURRENT` | 26 |
| `PcInfo.strLS_TEMP_SET` | 27 |
| `PcInfo.strLS_TEMP` | 28 |
| `PcInfo.strAPC_ANGLE_SET` | 29 |
| `PcInfo.strAPC_ANGLE` | 30 |
| `PcInfo.strHIGH_VACUUM_PRESSURE` | 31 |
| `PcInfo.strSEED_HI_MONITOR_SET` | 35 |
| `PcInfo.strSEED_HI_MONITOR` | 36 |
| `PcInfo.strSEED_SR_SET` | 39 |
| `PcInfo.strSEED_SR_ACTUAL` | 40 |
| `PcInfo.strSEED_POSITION` | 41 |
| `PcInfo.strCRUCIBLE_HI_MONITOR_SET` | 42 |
| `PcInfo.strCRUCIBLE_HI_MONITOR` | 43 |
| `PcInfo.strCRUCIBLE_CR_SET` | 46 |
| `PcInfo.strCRUCIBLE_CR_ACTUAL` | 47 |
| `PcInfo.strCRUCIBLE_POSITION` | 48 |
| `PcInfo.strSHIELD_POSITION` | 62 |
| `PcInfo.strINGO_TARGDIA` | 64 |
| `PcInfo.strINGOT_WIDTH` | 65 |
| `PcInfo.strINGOT_LENGTH` | 66 |
| `PcInfo.strINGOT_WEIGHT` | 67 |
| `PcInfo.strADC1_TARGET` | 92 |
| `PcInfo.strADC2_TARGET` | 93 |
| `PcInfo.strNECK_LENGTH` | 115 |
| `PcInfo.strCROWN_LENGTH` | 116 |
| `PcInfo.strSHOULDER_LENGTH` | 117 |
| `PcInfo.strBODY_LENGTH` | 118 |
| `PcInfo.strTAIL_LENGTH` | 119 |

- `str_Length` = n=66、`str_Weight` = n=67（INGOT_LENGTH/WEIGHT と同一元）。
- 上記以外の主な項目: `strPROCESS`（= VB側 `g_strAutoLogStatus`）、`str_State`（= `g_strStatus`）、`str_RodID`、`str_RecipeNO`、`str_RecipeName`、`str_Diameter`、`str_Alarm`（bool: アラームキューに1件以上）、`bStatePlc`（PLC接続）、`strLineStates`（"ONLINE"/"OFFLINE"）、`str_Access`（LOCAL/REMOTE）、`strLocalOrNet`（"0"=Local/"1"=Remote）、`str_DateTime`（**分単位**、`yyyy-MM-dd HH:mm`）、各種時間 long（`long_AutoTime` 等）。

## 2. TP_RealData（PLC生データ、`ZR` デバイスの word 配列）

| フィールド | PLC範囲 | 内容（コメントより） |
| --- | --- | --- |
| `intMainte` | R66000〜R66699 | Maintenance |
| `intIOMonitor` | R100000〜R100799 | PC IF IO Monitor |
| `intAlarm` | R102000〜R103599 | PC IF Alarm |
| `intManual` | R104000〜R104169 | PC Manual |
| `intIOBit` | R110000〜R112099 | PC IO Bit |
| `intLift` | R114000〜R114899 | （Lift関連） |

配列 index = 「PLCアドレス − 先頭アドレス」。値は生の整数（工学値変換は未適用）。

## 3. 更新特性

- `sub_ReadPLCData` が `Thread.Sleep(1000)` のループで `stcRealData` と `stcPcInfo` を `clsPcObj` へ**1秒ごとに丸ごと代入**（読み手は常に整合した1世代を得る）。PLC内部の読込は100ms周期だが、公開は1秒周期。
- PullerView の取得周期 1秒は公開周期と一致する。より速く読んでも新しい値は得られない。
- 値が凍結（VB停止・PLC断・ホスト断）しても `stcPcInfo` に更新時刻（秒単位）はない。検出は ①VB_FERROTEC.exe のプロセス有無 ②`bStatePlc`/`strLineStates` ③（要確認）ホスト接続状態 を併用する。

## 4. strPROCESS の値（mdlRecipeMgr.sub_AutoLogState_string 解析）

`strPROCESS` = VB側 `g_strAutoLogStatus`。`sub_CheckProcess` が `g_stcINFO.stcPLCINF.stcPCINFBIT.iMCSTS(1)` のビット（下位ビットから最初に立っているビット番号 i、無ければ -1）を `iautostatus` として設定する。

**自動運転モード（`iMCMode = eAuto`）の場合**

| iautostatus | strPROCESS | Excel工程名 |
| --- | --- | --- |
| -1（ビットなし） | `AUTO` | （工程外：自動運転・工程未開始） |
| 0 | `STATUS_CHECK` | （Excel定義外） |
| 1 | `VACUUM` | Vacuum |
| 2 | `LEAK_CHECK` | LeakCheck |
| 3 | `MELT` | Melt |
| 4 | `STABILIZE` | Stabilize |
| 5 | `NECK` | Neck |
| 6 | `CROWN` | Crown |
| 7 | `SHOULDER` | Shoulder |
| 8 | `BODY` | Body |
| 9 | `TAIL` | Tail |
| 10 | `ANEEL` | Aneel（**VB側の綴りが `ANEEL`。誤記ではない**） |
| 11 | `SHUTDOWN` | ShutDown |
| 12〜15 | `""`（空文字） | 未定義 |

**自動運転以外のモード**

| iMCMode | strPROCESS |
| --- | --- |
| eManual | `MANUAL` |
| eWaitMaterials | `WAITMATERIALS` |
| eMaintenance | `MAINTENANCE` |
| 上記以外／初期状態 | `""`（空文字） |

注意:
- 複数ビットが同時に立つ場合は**最下位ビットの工程**が採用される。
- 値は大文字・アンダースコア区切り。PullerViewは大文字化・前後空白除去して比較する。
- 空文字・未知の値は「工程不明」として扱い、工程連動表示のフォールバック規則（FR-ST-06）に従う。
- `strPROCESS` の更新契機は `sub_CheckProcess` の呼出し周期（要確認）。公開は1秒周期。
- `str_State`（= `g_strStatus`）の値は未解析（Q-22）。
