# VB_FERROTEC_Common.dll API 解析（自動抽出）

- 文書ID: ANL-CommonDLL-01 / 版数: 0.1 / 最終更新: 2026-10-18
- 抽出方法: メタデータ解析（ILコードの逆コンパイルはしていない。型・フィールド・列挙値のみ）
- 取扱い: Agent.MD の機密保持条項に従う

## 1. アセンブリ情報

| 項目 | 値 |
| --- | --- |
| アセンブリ名/版 | VB_FERROTEC_Common / 2.2.0.0 |
| ランタイム | .NET Framework 2.0（metadata v2.0.50727） |
| ビット数 | AnyCPU（32bit必須フラグなし）→ 64bit Python から読込可 |
| 強名前署名 | なし（バージョン厳密バインドの制約なし） |
| 参照 | mscorlib, System, Microsoft.VisualBasic |

## 2. Remoting関連クラス

| クラス | 基底 | 役割（推定） |
| --- | --- | --- |
| `clsBaseObj` | MarshalByRefObject | SessionID/IPAddress/PcName/SessionList、`InitializeLifetimeService`（**Nothing返却＝リース無期限**をILで確認） |
| `clsPcObj` | clsBaseObj | 炉PC(VB_FERROTEC)のデータ保持オブジェクト（前回解析） |
| `clsPcObjWeb` | clsPcObj | `clsPcObj` を内包(`m_BaseObj`)し全プロパティを委譲するWeb向けラッパ |
| `clsServerObj` | MarshalByRefObject | 上位サーバ側: ServerIP/RemotePort/SocketPort/ConnString/ActivedPc/SessionList/**PcObjList**、`GetPcObject`/`AddPcObject`/`fnc_GetRight` |
| `clsWebServerObj` | MarshalByRefObject | サーバ一覧 |
| `clsEventObj`/`clsProxyEventObj` | MarshalByRefObject | イベント通知用（UpdateUser, DataView, WriteMessage） |
| `clsTcpClient`/`clsTcpServer` | Object | 別系統の生TCPソケット（上位との電文） |
| `mdlCommon` | - | 電文ID定数(`CST_*`)、再送/再接続時間 |

- `[Serializable]` 付与: TP_* 構造体の主要なもの35型（値渡しで受信可能）。
- `Enum_Protocol` は TCP / HTTP を持つ。

## 3. 主な型定義（抽出結果）

```
===  TP_RealData
   intMainte : int[] 
   intIOMonitor : int[] 
   intAlarm : int[] 
   intManual : int[] 
   intIOBit : int[] 
   intLift : int[] 
===  TP_PcInfo
   strPROCESS : string 
   strUPPER_PRESSURE : string 
   strLOWER_PRESSURE : string 
   strHEATER_TEMP_SET : string 
   strHEATER_TEMP : string 
   strHEATER_POWER_SET : string 
   strHEATER_POWER : string 
   strHEATER_VOLTAGE : string 
   strHEATER_CURRENT : string 
   strMFC_Ar_FLOW_SET : string 
   strMFC_Ar_FLOW_ACTUAL : string 
   strSEED_HI_MONITOR_SET : string 
   strSEED_HI_MONITOR : string 
   strSEED_SR_SET : string 
   strSEED_SR_ACTUAL : string 
   strSEED_POSITION : string 
   strCRUCIBLE_HI_MONITOR_SET : string 
   strCRUCIBLE_HI_MONITOR : string 
   strCRUCIBLE_CR_SET : string 
   strCRUCIBLE_CR_ACTUAL : string 
   strCRUCIBLE_POSITION : string 
   strLS_TEMP_SET : string 
   strLS_TEMP : string 
   strINGOT_WIDTH : string 
   strNECK_LENGTH : string 
   strCROWN_LENGTH : string 
   strSHOULDER_LENGTH : string 
   strBODY_LENGTH : string 
   strTAIL_LENGTH : string 
   strINGO_TARGDIA : string 
   strAPC_ANGLE : string 
   strHIGH_VACUUM_PRESSURE : string 
   strCOOLING_WATER_FLOW : string 
   strMELT_TOUCH_VALUE : string 
   strINGOT_LENGTH : string 
   strINGOT_WEIGHT : string 
   strAPC_ANGLE_SET : string 
   strAPC_PRESSURE_SET : string 
   strAPC_PRESSURE_ACTUAL : string 
   strADC1_TARGET : string 
   strADC2_TARGET : string 
   strSHIELD_POSITION : string 
   str_RecipeName : string 
   str_RodID : string 
   str_Access : string 
   str_State : string 
   str_Alarm : bool 
   str_Time : string 
   str_Length : string 
   str_Charge : string 
   str_Weight : string 
   str_DateTime : string 
   strLineStates : string 
   strLocalOrNet : string 
   bStatePlc : bool 
   str_Diameter : string 
   str_RecipeNO : string 
   int_WorkInf : int 
   int_PauseCode : int 
   int_PullerTimes : int 
   long_WorkTime : long 
   long_AutoTime : long 
   long_WaitTime : long 
   long_AllStopTime : long 
   long_StopTime1 : long 
   long_StopTime2 : long 
   long_StopTime3 : long 
   long_StopTime4 : long 
   long_StopTime5 : long 
   long_StopTime6 : long 
   long_StopTime7 : long 
   long_StopTime8 : long 
   long_StopTime9 : long 
   long_StopTime10 : long 
   long_StopTime11 : long 
===  TP_MCINFDAT
   iDINDEX : int 
   lValue : long 
   sCaption : string 
   sCaption_e : string 
   sCaption_j : string 
   sCaption_c : string 
   sUnit : string 
   sFormat : string 
   lDivVal : long 
   iColor : int 
   iDispNo : int 
   bGraphDisp : bool 
   lRangeUPPER : string 
   lRangeBOTTOM : string 
   bRESIZE : bool 
===  TP_PCINF_DATA
   stcMCValue : TP_MCINFDAT[] 
   stcAUTOValue : TP_AUTOINF[] 
   stcPROFINF : TP_PROFSETPINF[] 
   stcPARMINF : TP_PARMAITEM[] 
===  TP_AUTOINF
   iValue : int[] 
===  TP_AlarmInf
   strAlarmName : string 
   strAlarm : string[] 
===  TP_SendPLC
   stcPLCData : TP_SendPLCData[] 
   strReadAddr : string[] 
===  TP_SendPLCData
   strDevice : string 
   strAddress : string 
   intSize : int 
   intDatas : int[] 
===  TP_FileMessage
   strPath : string 
   iTypeIndex : Enum_FileType 
===  Enum_FormID
   eDefault : Enum_FormID  = 4294967295
   ePcObj : Enum_FormID  = 0
   eServer : Enum_FormID  = 1
   eEvent : Enum_FormID  = 2
   ePLC : Enum_FormID  = 3
   eLoad : Enum_FormID  = 4
   eMin : Enum_FormID  = 10
   eAlarm : Enum_FormID  = 11
   eAutoStatus : Enum_FormID  = 12
   eFileManage : Enum_FormID  = 13
   eMax : Enum_FormID  = 14
===  Enum_Address
   eR30000_R43199 : Enum_Address  = 0
   eR48100_R57699 : Enum_Address  = 1
   eR64000_R65299 : Enum_Address  = 2
   eR66000_R66599 : Enum_Address  = 3
   eR70000_R73439 : Enum_Address  = 4
   eR74000_R74599 : Enum_Address  = 5
   eR78000_R79930 : Enum_Address  = 6
   eR80000_R81199 : Enum_Address  = 7
   eR94000_R97599 : Enum_Address  = 8
   eR100000_R100799 : Enum_Address  = 9
   eR102000_R103599 : Enum_Address  = 10
   eR104000_R104169 : Enum_Address  = 11
   eR110000_R111199 : Enum_Address  = 12
   eR114000_R114899 : Enum_Address  = 13
   eR30000 : Enum_Address  = 33600
   eR48100 : Enum_Address  = 48100
   eR64000 : Enum_Address  = 64000
   eR66000 : Enum_Address  = 66000
   eR70000 : Enum_Address  = 70000
   eR74000 : Enum_Address  = 74000
   eR78000 : Enum_Address  = 78000
   eR80000 : Enum_Address  = 80000
   eR94000 : Enum_Address  = 94000
   eR100000 : Enum_Address  = 100000
   eR102000 : Enum_Address  = 102000
   eR104000 : Enum_Address  = 104000
   eR110000 : Enum_Address  = 110000
   eR114000 : Enum_Address  = 114000
```

> 注: `Enum_Address` の値は PLC の R デバイスの範囲（例 R30000〜R43199）。`TP_RealData` の各 int[] との対応は未確認。
