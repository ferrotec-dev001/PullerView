# 8インチ／12インチ 装置 PoC 結果比較（2026-10-21）

## 1. 結論

| 観点 | 8インチ | 12インチ | 判定 |
|---|---|---|---|
| Socket/Remoting 接続・毎秒取得 | ○（77行、中央値 23ms） | ○（64行、中央値 24ms） | 同じ |
| intIOBit 長 | 2300 | 2300 | 同じ |
| stcMCValue 項目数 | 150 | 150 | 同じ |
| PullerView 対象項目（idx65/92/66/115-119）の名称・単位・除数 | INGOT DIA ÷10 等 | 同一 | **同じ** |
| strPROCESS の値 | '', AUTO, MANUAL | '', MANUAL | 同じ挙動（起動直後は空→モード文字列） |
| 150項目のうち名称/単位/除数が異なる項目 | — | — | **38項目が相違（うち除数相違 13項目）** |
| 読取り遅延（>500ms） | なし | 1回（11,576ms @17:31:10） | 12インチのみ発生（原因未特定） |

PullerView が現時点で使う 8 項目（直径 idx65、目標直径 idx92、インゴット長 idx66、工程別長さ idx115〜119）は両装置で完全に一致しており、**同じ DataSetting.ini / 同じ実装で両装置に対応できる**。
ただし 150 項目全体では除数が異なる項目が 13 あるため、PullerView は除数を固定値にせず、**各装置の stcMCValue（mcmeta.csv）から取得する**設計を必須とする。

## 2. 取得値の確認

| 項目 | 8インチ | 12インチ | 備考 |
|---|---|---|---|
| idx65 INGOT DIA [mm] | 229.3 | 0 | 12インチは直径計測値 0（待機中／未計測と推定） |
| idx92 ADC1 TARGET [mm] | 215 | 312 | 目標直径。装置サイズに整合 |
| ingot_len（strINGOT_LENGTH）[mm] | 2429.3 | 2338.0 | |
| idx115〜119 の合計 [mm] | 2429.2（ingot_len と一致） | 2383.3（ingot_len と 45.3 差） | 12インチは工程別長さの合計 ≠ インゴット長。要確認（MeltBack 等の扱い？） |
| plc | True | False→True（接続直後の1行のみ False） | |

## 3. 除数が異なる 13 項目（PullerView で将来グラフ化する場合は装置ごとの除数が必要）

| idx | 項目 | 8インチ div | 12インチ div |
|---|---|---|---|
| 23 | HEATER POWER SET [kW] / HEATER POWER SET [kW] | 10 | 100 |
| 24 | HEATER POWER ACTUAL [kW] / HEATER POWER ACTUAL [kW] | 10 | 100 |
| 32 | - [-] / - [mm] | 1 | 10 |
| 39 | S/R SET [rpm] / S/R SET [rpm] | 10 | 100 |
| 40 | S/R ACTUAL [rpm] / S/R ACTUAL [rpm] | 10 | 100 |
| 46 | C/R SET [rpm] / C/R SET [rpm] | 10 | 1000 |
| 47 | C/R ACTUAL [rpm] / C/R ACTUAL [rpm] | 10 | 1000 |
| 94 | MG CURRENT SET [A] / MG POWER SET [GAUSS] | 10 | 1 |
| 95 | MG CURRENT ACTUAL [A] / MG POWER ACTUAL [GAUSS] | 10 | 1 |
| 107 | HEATER POWER OFFSET TOTAL [kW] / HEATER POWER OFFSET TOTAL [kW] | 10 | 100 |
| 125 | SEED ROTATION OFFSET TOTAL [rpm] / SEED ROTATION OFFSET TOTAL [rpm] | 10 | 100 |
| 132 | CRUCIBLE ROTATION OFF SET TOTAL [rpm] / CRUCIBLE ROTATION OFF SET TOTAL [rpm] | 10 | 1000 |
| 144 | M/L OFFSET [mm] / M/L OFFSET [GAUSS] | 10 | 1 |

## 4. 名称・単位のみ異なる項目（除数は同じ）

| idx | 8インチ | 12インチ |
|---|---|---|
| 2 | COOLING WATER PRESSURE [deg] | SHIELD WTR TEMP [deg] |
| 3 | DRY AIR PRESSURE [L/min] | SHIELD WTR FLOW [L/min] |
| 4 | Ar PRIMARY PRESSURE [deg] | TUBE WTR TEMP [deg] |
| 5 | Ne PRIMARY PRESSURE [L/min] | TUBE WTR FLOW [L/min] |
| 6 | PRIMARY COOLING WATER FLOW [L/min] | FLM1WaterFLOW [L/min] |
| 7 | GROW TOP WATER FLOW [L/min] | FLM2WaterFLOW [L/min] |
| 8 | - [-] | FLM3WaterFLOW [L/min] |
| 9 | BASE TOP WATER FLOW [L/min] | FLM4WaterFLOW [L/min] |
| 10 | BASE BOTTOM WATER FLOW [L/min] | FLM5WaterFLOW [L/min] |
| 11 | HEATER TERMINAL WATER FLOW1 [L/min] | FLM6WaterFLOW [L/min] |
| 12 | HEATER TERMINAL WATER FLOW2 [L/min] | FLM7WaterFLOW [L/min] |
| 13 | HEATER TERMINAL WATER FLOW3 [L/min] | FLM8WaterFLOW [L/min] |
| 14 | CRUCIBLE SHAFT WATER FLOW [L/min] | FLM9WaterFLOW [L/min] |
| 35 | S/L HIGH SET [mm/min] | SL_LIFT_SET [mm/min] |
| 36 | S/L HIGH ACTUAL [mm/min] | SL_LIFT_ACTUAL [mm/min] |
| 42 | C/L HIGH SET [mm/min] | CL_LIFT_SET [mm/min] |
| 43 | C/L HIGH ACTUAL [mm/min] | CL_LIFT_ACTUAL [mm/min] |
| 49 | CRUCIBLE POSITION Actual [mm] | UNUSED [mm] |
| 50 | SHIELD LIFT1 SET [mm/min] | - [mm/min] |
| 61 | SHIELD LIFT ACTUAL [mm/min] | HL_LIFT_SET [mm/min] |
| 62 | SHIELD POSITION [mm] | HL_LOCATION(UHP) SET [mm] |
| 64 | INGOT TARGET [mm/min] | HL_LNK_LIFT_ACTUAL [mm/min] |
| 70 | CRUCIBLE TRAVEL POSITION [mm] | LS POSITION Set [mm] |
| 75 | H/L LEFT ACTUAL [mm/min] | H/L LEFT LIFT ACTUAL [mm/min] |
| 91 | HEATER TEMP RATE SET [deg/min] | HEATER TEMP RATE SET [deg/Hour] |

## 5. 未解明事項

- 12インチで 1 回、読取りに 11.6 秒かかった（8インチでも 16:26 のテストで 2.4 秒／11.5 秒が各1回）。発生タイミングは接続後 30〜60 秒で、VB_FERROTEC 側の初期化（Recipe/FileManage 送信）と重なる可能性があるが未確認。改善版 Probe は遅延時の内訳（どの項目で待ったか、GC 回数、メモリ）を CSV に `#` 行で記録する。
- 12インチの工程別長さ合計とインゴット長の差（45.3mm）。PullerView の X 軸（長さ）に使う値をどちらにするか要決定。