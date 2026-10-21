# PullerView 要件定義書

- 文書ID: REQ-PullerView
- 版数: 0.21（**v1.0 候補**。仕様変更の決裁者＝開発責任者 中村氏（汉虹への通知不要）。単体起動（VB_FERROTEC.exe 未検出でも表示）を FR-UI-13 として追加。ロゴ案1（インゴット＋トレンド）採用、工程名白箱を x=100 へ。透過方式を「背景のみ透過（線・文字は不透明）」に確定、右側ラベルは固定位置。式样 r3 反映：色指定を RRRGGGBBB(10進)へ変更、開発言語 C# 確定(D-23)、既定位置は r3 図(D-24)、コメント行常時表示(D-25)。1 対 1 構成の ADR-0011 を追加。B/C 群 D-07〜D-19 を中村氏回答で確定。未決事項なし）
- 最終更新: 2026-10-20
- 承認者: 中村勝（株式会社フェローテック）※未承認
- 管理方式: MD駆動設計（本ファイルが唯一の正。実装・テストは本書に従う）
- 入力資料: 式样_PullerView_20261018r3.xlsx（2026-10-20 受領。r2 からの差分: `warningcolorRGB`/`alarmcolorRGB` 10進9桁、セクション範囲コメント [NECK1]〜[TAIL4]、MANUL→MANUAL）、r2（RevZ、上海汉虹精密机械有限公司）、VB_FERROTEC（クライアント）／VB_FERROTEC_SERVER（ホスト）／VB_FERROTEC_Common_2 ソース一式（2026-10-20 受領）、VB_FERROTEC_Common.dll v2.2.0.0
- 関連文書: docs/remoting-analysis.md、docs/vb-ferrotec-common-api.md、docs/pcinfo-mapping.md、docs/analysis_host_frmServer.md、docs/analysis_vbferrotec_source.md、poc/README.md（Remoting 読取り PoC）

> 凡例: **[確定]** = Excel r2 に明記、または中村氏回答で確定 / **[解析]** = VBソース・DLL解析で判明 / **[提案]** = AI提案（要承認） / **[D-xx]** = 第11章の未決事項

---


> **2026-10-21 実機確認による注記（D-16 関連）**: 装置PCの実機は **Windows 7 SP1 x64（OS 6.1.7601、中国語ロケール 2052）** であった（PullerViewMock.exe の APPCRASH レポートより）。D-16 の「Windows 10 x64」は見直しが必要。.NET Framework 4.x は導入済み（RemotingProbe.exe が動作）。製品版は Windows 7 SP1 でも動作する .NET Framework 4.x（4.5〜4.8 の実機バージョンを要確認）をターゲットにする。

## 1. 目的・背景

単結晶引き上げ炉操作アプリケーション `VB_FERROTEC.exe`（VB.NET）が Remoting 経由で公開しているデータを利用し、顧客からの追加機能要望を実現するデータ分析アプリケーション「PullerView」を開発する。操作アプリ本体は改造せず、別プロセスとして**工程ごとのトレンド監視・上下限判定・異常表示**を提供する。

## 2. スコープ

| 区分 | 内容 |
| --- | --- |
| 対象 | 炉操作PC（Windows）上で `VB_FERROTEC.exe` と同一PCで動作する単独デスクトップアプリ（Python） |
| 対象ユーザー | 炉の操作オペレーター、プロセス管理者 |
| 含む | DataSetting.ini 読込／Remoting読取り／1秒周期の上下限判定（4種別）／工程別トレンドグラフ（最大4）／異常時のグラフ背景色変更とコメント表示／frmAutoStatus 連動表示 |
| 含まない | **VB_FERROTEC.exe・PLCへの書込み（異常フラグ引渡し含む）**（ADR-0006、ただし D-02 参照）、リモート接続、Web版、ログ可視化・過去ログ閲覧・プロファイルテーブル比較（次フェーズ）、VB_FERROTEC.exe 本体の改造 |

## 3. 確定した設計判断（ADR）

| ID | 決定 | 状態 |
| --- | --- | --- |
| ADR-0001 | **言語は C#（.NET Framework 4.8、WinForms）**。式样 r3 の「Python」記述は本書で置換（理由: 1 対 1 構成で PullerView 自身が .NET Remoting の待機側になるため .NET Framework 専用 API が必須、`VB_FERROTEC_Common.dll` 直接参照、ランタイム追加不要、配布サイズ小） | 確定（2026-10-20 中村氏決定。旧: Python） |
| ADR-0002 | GUI は WinForms + GDI+ 自前描画（mock/ で実証） | 確定（ADR-0001 に伴い変更。旧: PySide6 + pyqtgraph） |
| ADR-0003 | `VB_FERROTEC_Common.dll` を C# から直接参照し、`clsPcObj` を読取り専用で扱う（pythonnet 不要。RISK-01 解消） | 確定（ADR-0001 に伴い変更） |
| ADR-0004 | 配布は単体 exe（.NET Framework 4.8 は Windows 10 標準搭載。追加ランタイム不要） | 確定（ADR-0001 に伴い変更） |
| ADR-0005 | frmAutoStatus 上の表示は独立した最前面ウィンドウを重ねて位置追従（VBフォームへの埋込はしない） | 確定（2026-10-18） |
| ADR-0006 | **PullerViewは読取り専用（表示のみ）**。Remoting経由の書込み・権限取得（`fnc_GetRight`）は行わない | 確定（2026-10-18、Q-18）※r1の記述と矛盾 → D-02 |
| ADR-0008 | **Remoting Service の実体は VB_FERROTEC_SERVER（ホスト）**。`clsPcObj` はホストが `tcp://{HostIP}:{RemotePort}/{SessionID}ePcObj` で公開し、VB_FERROTEC.exe はそのプロキシに毎秒書き込む。PullerView は同じ URL（または `eServer` の `ConnectPc`）から読む | 確定（2026-10-20 中村氏回答＋ソース解析） |
| ADR-0009 | **異常フラグの Remoting 引渡しは行わない**。上下限超過は PullerView 内の表示（背景色・コメント）に閉じる。r2 で当該行は削除済み | 確定（2026-10-20 中村氏回答） |
| ADR-0010 | **PV/SV のライブ値は `stcRealData.intIOBit` から算出**する。`stcMCValue(n).lValue` はホスト側では起動時スナップショットのため使用しない（計算式は FR-DT-04） | 確定（ソース解析） |
| ADR-0011 | **ホスト（VB_FERROTEC_SERVER）なしの 1 対 1 構成では、PullerView が待機側（ホスト代行）になる**。VB_FERROTEC.exe は常に「接続しに行く側」（TCP SocketPort で TextID `00` を送り SessionID を受領 → Remoting RemotePort の `{SessionID}ePcObj` へ毎秒書込み）。PullerView は SocketPort と RemotePort を待ち受け、`clsPcObj` を自プロセス内に公開して書込まれた値をメモリ上で読む。SessionID の返信以外は一切送らない（読取り専用方針を維持）。ホストと同一ポートでの同時起動は不可 | 確定（2026-10-20 中村氏回答）。FR-DT-01 の接続先は「ホスト」→「構成により ホスト or PullerView 自身」に読み替え。PoC: poc/bridge/RemotingProbe.cs |
| ADR-0007 | 設定の責務分担: 表示有効・上下限・グラフ範囲は PullerView 独自設定（DataSetting.ini）。名称・単位は VB 側定義（`stcMCValue`）を流用可 | 確定（2026-10-18、Q-20） |

## 4. 技術スタック（案）

| 項目 | 内容 |
| --- | --- |
| OS | **Windows 10 x64**（装置PC。D-16 確定） |
| 言語 | **C# 7.3 / .NET Framework 4.8**（社内 C# コーディング規約準拠） |
| GUI | WinForms（FormBorderStyle.None・TopMost・Opacity・Region 角丸）+ GDI+ 自前描画 |
| Remoting | System.Runtime.Remoting（TcpChannel / TcpServerChannel、Binary フォーマッタ、TypeFilterLevel.Full）+ `VB_FERROTEC_Common.dll` 直接参照（.NET Framework 4.8 ターゲット、AnyCPU） |
| ウィンドウ検出 | Win32 API（P/Invoke: FindWindow / GetWindowRect / IsWindowVisible）で `VB_FERROTEC.exe` と `frmAutoStatus` を検出・位置追従 |
| 設定 | DataSetting.ini（読込専用・UTF-8）、アプリ設定 JSON（位置・透明度等） |
| テスト | MSTest/NUnit（Remoting 層はインターフェース抽象化しモック） |
| ログ | 日付ローテーションのテキストログ |

## 5. 機能要件

記法: **[必須]** / **[推奨]**

### 5.1 設定読込（FR-CF）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-CF-01 | `DataSetting.ini` を読み込む。書式は第6章 | 必須 | [確定] r1 |
| FR-CF-02 | セクションは `[<工程略称><n>]`（n=1〜4）。工程ごとに最大4グラフ | 必須 | [確定] r2 + D-06/D-21 |
| FR-CF-03 | ファイル欠損・値不正時は当該グラフを無効化してログ出力し、起動は継続する | 必須 | [提案] |
| FR-CF-04 | `pv_pcinfodata_index` / `sv_pcinfodata_index` は `stcMCValue` の配列位置 n（0〜134。PLC データ領域 R110200〜R110479 = 135 項目。配列長は `CST_PLCINF_MAXINDEX_MCVAL`=150 だが 135 以降は PLC 実体なし）。**全 index のライブ値を `stcRealData.intIOBit` から算出できる**（FR-DT-04）。範囲外・`lDivVal`=0 の index は設定エラー | 必須 | [解析]（D-03 解決） |
| FR-CF-07 | `caption=1` のとき `caption_text=<文字列>`、`unit=1` のとき `unit_text=<文字列>` を同セクションから読む。`_text` 欠損時は `stcMCValue` 定義へフォールバックし警告ログ | 必須 | [確定] 2026-10-20 回答（D-07） |
| FR-CF-05 | `DataSetting.ini` は**起動時に自動読込**し、実行中は**設定メニューから手動再読込**できる。再読込時は全グラフを再構成（データはクリア） | 必須 | [確定] 2026-10-20 回答（D-19） |
| FR-CF-06 | `warningcolorRGB`（管理上下限超過時の背景色）・`alarmcolorRGB`（規格上下限超過時の背景色）をセクションごとに読み込む。**10進 9桁 RRRGGGBBB**（例 `000000102`=(0,0,102)、`128000000`=(128,0,0)）。r2 の `warningcolor`/`alarmcolor`（6桁16進）も互換で受理。省略・不正時はアプリ既定色 (0,0,102)/(128,0,0) を使いログ出力 | 必須 | [確定] r3（D-20 の RRGGBB は r3 で置換） |

### 5.2 データ取得（FR-DT）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-DT-01 | ホスト（VB_FERROTEC_SERVER）が公開する `clsPcObj`（`tcp://{HostIP}:{RemotePort}/{SessionID}ePcObj`）に接続し、毎秒 `stcRealData` と `stcPcInfo` を、起動時に1回 `stcPCINF_DATA`（名称・単位・除数）を読み込む。`HostIP`/`RemotePort` は VB_FERROTEC.exe の `Application.ini` `[Server]` と同値を使う（サンプル: 172.16.87.27 / 10030） | 必須 | [確定] ADR-0008 |
| FR-DT-02 | 取得周期は1秒（VB 側公開周期と一致） | 必須 | [確定]+[解析] |
| FR-DT-03 | 接続断・タイムアウト時は自動再接続（バックオフ）。断中は「通信断」を表示し判定不能とする | 必須 | [提案] |
| FR-DT-04 | PV/SV の数値は `stcRealData.intIOBit`（ZR110000〜112099 の生ワード配列）から算出する: `raw = Int32( (intIOBit[200+2n] & 0xFFFF) << 16 \| (intIOBit[200+2n+1] & 0xFFFF) )`（VB `fnc_CalcDoubleVal(lower=intIOBit[200+2n], upper=intIOBit[200+2n+1])` と同じ。符号付き32bit）、`value = raw / stcMCValue(n).lDivVal`。`TP_PcInfo` の文字列値（`strPROCESS`・長さ等）は工程判定・X軸に用い、数値変換失敗は「判定不能」 | 必須 | [解析] ADR-0010（無効時表記は D-14） |
| FR-DT-05 | 読取りは低負荷・非同期・タイムアウト付きとし、VB_FERROTEC.exe の動作に影響しない | 必須 | [提案] |
| FR-DT-06 | 毎秒取得は `stcRealData`（int[] 6本・約6,300要素≒25KB）と `stcPcInfo` に限定。`stcPCINF_DATA`（名称・単位・除数）は起動時1回（再接続時に再取得）。大型プロパティ（`objAlarmCSV`・`strStatusData` 等）は取得しない | 必須 | [提案]（メモリ最小化） |
| FR-DT-07 | Getter のみ使用。Setter・メソッド呼出し・権限取得はコード上も持たない | 必須 | 確定（ADR-0006） |
| FR-DT-08 | `bStatePlc=False` または `strLineStates="OFFLINE"` の間は判定不能。データ凍結の検出は VB_FERROTEC.exe プロセス有無と併用 | 必須 | [提案] |

### 5.3 上下限判定（FR-AL）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-AL-01 | 1秒ごとに、グラフごとに **管理上限(ucl)超過／管理下限(lcl)超過／規格上限(usl)超過／規格下限(lsl)超過** の4種別を独立に判定する | 必須 | [確定] r1（r0「製造」→r1「規格」に名称変更） |
| FR-AL-02 | 判定対象値は **PV の絶対値**（`pv_pcinfodata_index` の値そのもの）。usl/ucl/lcl/lsl は PV と同じ単位・スケールで指定する。SV は判定に使わず、グラフ描画（参考線）にのみ使う | 必須 | [確定] 2026-10-20 回答（D-04）。r2 サンプル値（±1.0/±0.5）はダミー |
| FR-AL-03 | 判定結果は「正常／各異常／判定不能」を区別。判定不能を正常扱いしない | 必須 | [提案] |
| FR-AL-04 | 境界値ちょうど（PV = 閾値）は正常。超過は**連続 2 秒**（アプリ設定で変更可）継続で確定し、復帰は 1 回の範囲内で即時 | 必須 | [確定] 2026-10-20 回答（D-12） |
| FR-AL-05 | `usl ≥ ucl > lcl ≥ lsl` でない設定は警告ログを出し当該グラフを無効化 | 推奨 | [提案] |
| FR-AL-06 | 判定根拠（工程・グラフ・PV・SV・閾値・種別・時刻）をログに残す | 必須 | [提案] |
| FR-AL-07 | ~~異常フラグの Remoting 経由引渡し~~ 行わない | — | D-02 解決、ADR-0009（FR-AL-90） |
| FR-AL-90 | 上下限超過の結果は **Remoting・ソケット・PLC へ一切送出しない**（表示とローカルログのみ） | 必須 | [確定] r2 で引渡し行削除、ADR-0009 |

### 5.4 メインウィンドウ（FR-UI）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-UI-01 | PullerView はメインウィンドウを持ち、`frmAutoStatus` 上に最前面で表示する。位置は任意に設定でき記憶・復元する | 必須 | [確定] r1 |
| FR-UI-02 | メインウィンドウは半透明。背景色と透明度を設定できる | 必須 | [確定] r1 |
| FR-UI-03 | メインウィンドウは独立して移動でき、表示／非表示を切り替えられる。コメント領域はメインウィンドウ内の上部に置き、メインウィンドウと一体で移動する | 必須 | [確定] r1 + D-08 |
| FR-UI-04 | メインウィンドウは**最小化／最大化**の2状態を持つ。最小化時はタイトル行（工程略称・歯車アイコン・最小化/最大化ボタン）と**コメント行（常時表示）**のみ、最大化時はさらにトレンドグラフを表示 | 必須 | [確定] r1 + D-08 + D-25 |
| FR-UI-05 | 最大化時、トレンドグラフを**最大4個、縦方向**に並べる。高さは表示グラフ数（enable=1 の数）に応じて自動調節 | 必須 | [確定] r1 |
| FR-UI-06 | 画面外・マルチモニタ・DPI 100〜200% で位置が破綻しない | 推奨 | [提案] |
| FR-UI-07 | ウィンドウのデフォルト位置は式样 r3「ウィンドウデフォルト位置」図のとおり **`frmAutoStatus`（1280×1024）左上から (300, 67) px**（トレンド領域の左側に重ねる）。初回起動時のみ。以後は記憶位置 | 必須 | [確定] r3 図 + 2026-10-20 中村氏決定（D-24。旧「右上」を置換） |
| FR-UI-09 | ウィンドウ寸法は式样 r3 図の比率に従う: **幅 390 px、高さ = タイトル行 30 + コメント行 20 + グラフ枠 154×本数 + 余白**（4本で約 680 px ≒ 1024×0.66）。最小化時はタイトル行＋コメント行（50 px） | 必須 | [確定] r3 図から算出（mock/ で実証） |
| FR-UI-13 | **単体起動**: VB_FERROTEC.exe／frmAutoStatus が検出できなくても PullerView は起動・表示できる（既定）。その間はコメント行に「単体起動中 — VB_FERROTEC.exe 未検出」、グラフは `---`、位置追従なし（前回位置を維持）。検出でき次第、自動で接続・追従に移行する。設定「VB_FERROTEC.exe が無くても表示する」を OFF にすると式样の表示条件（未実行時は非表示）どおりに動く。右クリックメニューから 設定／最小化・最大化／終了 | 必須 | [確定] 2026-10-20 中村氏指示（式样「VB_FERROTEC.exe が実行されていない場合、非表示」は設定で選択制に緩和） |
| FR-UI-11 | **透過は背景のみ**: 背景（ウィンドウ・グラフ枠・プロット・コメント行の塗り）だけに透明度を適用し、線・文字・枠線・アイコンは常に不透明で、背後の frmAutoStatus が透けて見える（Win32 `UpdateLayeredWindow` による per-pixel alpha）。不透明度は設定で 0〜100%（既定 65%）。「全体を半透明」方式も設定で選択可 | 必須 | [確定] 2026-10-20 中村氏要望 |
| FR-UI-12 | グラフ右側の値ラベルは式样図と同じ**固定位置**（上から USL / UCL / PV / SV / LCL / LSL を等間隔）。値の変化で位置を動かさない | 必須 | [確定] 2026-10-20 中村氏指示 |
| FR-UI-10 | 外観は式样図準拠: 背景 (77,77,77) 角丸白枠、タイトル行 = **ロゴ（案1: インゴット＋緑点線トレンド＋「Puller」白太字「View」シアン、GDI+ ベクタ描画、高さ 22px）**・工程略称の白箱（**x=100**。ロゴに被らないよう式样図の 84 から +16px）・歯車・○▲/○▼、グラフ枠 = 管理項目名 [単位]、USL/LSL 赤、UCL/LCL 青(0,176,240)、SV 白、PV 緑点線、右側に値ラベル、X 軸 = 結晶長さ縦書き | 必須 | [確定] r3 図 |
| FR-UI-08 | タイトル行に**歯車アイコン**を置き、クリックで設定ダイアログ（背景色・透明度・コメント書式・判定秒数・言語・DataSetting.ini 再読込）を開く | 必須 | [確定] 2026-10-20 回答（D-08） |

### 5.5 トレンドグラフ（FR-GR）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-GR-01 | 現在工程のセクション（`[<工程>1]`〜`[<工程>4]`）のうち `enable=1` のグラフを表示する | 必須 | [確定] r1 |
| FR-GR-02 | 各グラフは PV 線・SV 線・usl/ucl/lcl/lsl の 4 基準線を描画する | 必須 | [確定] r1 + D-11 |
| FR-GR-03 | X軸は**現在工程の工程別長さ [mm]**。取得元は `stcMCValue` index **NECK=115 / CROWN=116 / SHOULDER=117 / BODY=118 / TAIL=119**（VB `strNECK_LENGTH`〜`strTAIL_LENGTH` と同一。値は FR-DT-04 の式で `intIOBit` から算出）。`length` = X軸の最大値 [mm]（0〜length の固定レンジ）、`length_span` = **X軸の目盛間隔 [mm]** | 必須 | [確定] 2026-10-20 回答（D-05） |
| FR-GR-08 | 長さが `length` を超えた場合は X 軸を `length_span` 単位で右へ延長する（データは破棄しない）。長さが減少（工程切替・リセット）した場合は FR-GR-06 に従う | 推奨 | [提案] |
| FR-GR-04 | 上下限超過時、該当グラフの背景色を管理超過=`warningcolorRGB`、規格超過=`alarmcolorRGB` に変える（**規格優先**）。復帰時に通常背景へ戻す | 必須 | [確定] r2 + D-10 |
| FR-GR-05 | キャプション・単位は `caption`/`unit` キーに従い VB 側 `stcMCValue` 定義（`sCaption_j`/`sUnit` 系。言語は FR-CM-04）または `caption_text`/`unit_text` を使用 | 必須 | [確定] r1 + D-07 |
| FR-GR-06 | 工程切替時はグラフデータを**クリア**する（同一工程へ戻った場合も新規開始） | 必須 | [確定] 2026-10-20 回答（D-13） |
| FR-GR-07 | 縦軸は既定で **lsl〜usl に上下 20% のマージンを加えた固定レンジ**。設定ダイアログで自動スケールに切替可 | 必須 | [確定] 2026-10-20 回答（D-11） |

### 5.6 コメントウィンドウ（FR-CM）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-CM-01 | 上下限超過時、異常名称をコメントウィンドウにスクロール表示する。復帰時に自動削除 | 必須 | [確定] r1 |
| FR-CM-02 | 複数異常は連結して表示 | 必須 | [提案] |
| FR-CM-03 | 文字色／フォント／大きさ／スクロール向き／速さを設定ダイアログで設定できる（アプリ設定 JSON に保存） | 必須 | [確定] 2026-10-20 回答（D-09） |
| FR-CM-04 | 異常名称は「{caption} 規格上限超過／規格下限超過／管理上限超過／管理下限超過」の形式。言語は既定**日本語**（`sCaption_j`）、設定で英語（`_e`）・中国語（`_c`）に切替 | 必須 | [確定] 2026-10-20 回答（D-15） |
| FR-CM-05 | コメント領域はメインウィンドウ内のタイトル行直下に **1 行（高さ 20 px）で常時表示**。正常時は「異常なし」（対象外工程は「工程 X：監視対象外」、通信断は「通信断」）、異常時は異常名称を連結してスクロール | 必須 | [確定] 2026-10-20 中村氏決定（D-25） |

### 5.7 表示条件・工程連動（FR-ST）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-ST-01 | `frmAutoStatus` 表示中: メインウィンドウを表示し、工程ステータスに応じたグラフを表示 | 必須 | [確定] r1 |
| FR-ST-02 | `frmAutoStatus` 非表示: メインウィンドウを非表示 | 必須 | [確定] r1（r0 の「アイコン最小表示」は廃止） |
| FR-ST-03 | `VB_FERROTEC.exe` 未実行: 非表示 | 必須 | [確定] r1 |
| FR-ST-04 | 工程ステータスの取得元は `TP_PcInfo.strPROCESS`（= `g_strAutoLogStatus`）。大文字化・trim 後の完全一致 | 必須 | [解析] |
| FR-ST-05 | 工程種類（16）: `VACUUM` `LEAK_CHECK` `MELT` `STABILIZE` `NECK` `CROWN` `SHOULDER` `BODY` `TAIL` `ANEEL` `SHUTDOWN` `AUTO` `STATUS_CHECK` `MANUAL` `WAITMATERIALS` `MAINTENANCE` | 必須 | [確定] r1 + [解析]（r1「MANUL」は VB 側 `MANUAL` の誤記と解釈） |
| FR-ST-06 | `strPROCESS` が空文字・未知の値の場合、およびセクション未定義の工程の場合の表示 | 必須 | D-06 |
| FR-ST-90 | グラフ表示の**対象工程は NECK / CROWN / SHOULDER / BODY / TAIL の5工程**。他の11工程（VACUUM, LEAK_CHECK, MELT, STABILIZE, ANEEL, SHUTDOWN, AUTO, STATUS_CHECK, MANUAL, WAITMATERIALS, MAINTENANCE）は対象外とし、メインウィンドウは表示するがグラフは表示しない | 必須 | [確定] r2「工程種類定義」 |
| FR-ST-91 | 工程判定は `stcPcInfo.strPROCESS`（VB `g_strAutoLogStatus`）の文字列一致（大文字）。VB 実値は `AUTO, STATUS_CHECK, VACUUM, LEAK_CHECK, MELT, STABILIZE, NECK, CROWN, SHOULDER, BODY, TAIL, ANEEL, SHUTDOWN, MANUAL, WAITMATERIALS, MAINTENANCE`。r2 の `MANUL` は VB 実値 `MANUAL` の誤記として扱う | 必須 | [解析] mdlRecipeMgr |
| FR-ST-92 | 工程略称（DataSetting.ini セクション名・画面表示用）: VACUUM=VACM, LEAK_CHECK=LKCK, MELT=MELT, STABILIZE=STAB, NECK=NECK, CROWN=CRWN, SHOULDER=SHLD, BODY=BODY, TAIL=TAIL, ANEEL=ANEL, SHUTDOWN=SHDW, AUTO=AUTO, STATUS_CHECK=STCK, MANUAL=MANU, WAITMATERIALS=WAIT, MAINTENANCE=MENT | 必須 | [確定] r2 + D-21 |

### 5.8 アプリ設定（FR-SE）

| ID | 要件 | 区分 | 根拠 |
| --- | --- | --- | --- |
| FR-SE-01 | 位置・最小化/最大化状態・表示ON/OFF・背景色・透明度・コメント書式・判定秒数・言語・縦軸モードを `%LOCALAPPDATA%\PullerView\settings.json` に保存し起動時に復元 | 必須 | [確定] 2026-10-20 回答（D-17） |
| FR-SE-02 | 設定破損時は既定値で起動し、破損ファイルを退避 | 必須 | [提案] |
| FR-SE-03 | 設定変更は**歯車アイコンのクリック**で開く設定ダイアログから行う（右クリックメニューは設けない） | 必須 | [確定] 2026-10-20 回答（D-08） |

## 6. DataSetting.ini 仕様（r2）

- 文字コード UTF-8、`;` `#` はコメント。セクション `[<工程名><n>]`、n = 1〜4。対象工程は NECK / CROWN / SHOULDER / BODY / TAIL（FR-ST-90）。r2 のコメントには `[VACUUM1]〜[ANEEL4]` の記述が残るが、対象外工程のセクションは読み飛ばす。
- セクション名は **工程略称 + 追番 1〜4**（`[NECK1]`〜`[NECK4]`, `[CRWN1]`〜`[CRWN4]`, `[SHLD1]`〜, `[BODY1]`〜, `[TAIL1]`〜）。略称は FR-ST-92 の16種（VB 実値との対応は FR-ST-91/92）。正式名（`[CROWN1]` 等）は互換のため別名として受理するが、サンプルと文書は略称で統一する（D-21 確定）。
- キーは小文字。数値キーは半角。

| キー | 意味（r1コメント） | 型・例 | 備考 |
| --- | --- | --- | --- |
| enable | 0=非表示, 1=表示 | `1` | |
| pv_pcinfodata_index | PV の `stcMCValue` index（r2: pv from stcMCValue index） | `65` | 0〜134（FR-CF-04） |
| caption | 0=`stcMCValue` のキャプションを使用, 1=`caption_text` を使用 | `0` | D-07 確定 |
| caption_text | caption=1 のときの表示名 | `直径` | 任意キー（r2 外、FR-CF-07） |
| unit_text | unit=1 のときの単位 | `mm` | 任意キー（r2 外、FR-CF-07） |
| unit | 0=`stcMCValue` の単位を使用, 1=直接使用 | `0` | 同上 |
| sv_pcinfodata_index | SV の `stcMCValue` index | `92` | |
| usl / ucl / lcl / lsl | 規格上限 / 管理上限 / 管理下限 / 規格下限 | `1.0` / `0.5` / `-0.5` / `-1.0` | **PV 絶対値**に対する閾値（D-04 確定）。PV と同じ単位 |
| length | グラフ X 軸の最大値 [mm]（工程別長さ index115〜119） | `2300` | D-05 確定 |
| length_span | グラフ X 軸の**目盛間隔** [mm] | `100` | D-05 確定 |
| warningcolorRGB | 管理上下限（ucl/lcl）超過時のグラフ背景色 | `000000102` | r3。**10進 9桁 RRRGGGBBB**。r2 の `warningcolor=000066`（16進）も互換受理 |
| alarmcolorRGB | 規格上下限（usl/lsl）超過時のグラフ背景色 | `128000000` | r3。同上 |

サンプル: `config/DataSetting.ini`

## 7. 非機能要件

| ID | 分類 | 要件 | 検証 |
| --- | --- | --- | --- |
| NFR-01 | 配布 | **exe 化（PyInstaller 等、Python 不要）**、Windows 10 x64。管理者権限なしで実行できる場所（例: `C:\PullerView\`）にフォルダ配置。.NET Framework 4.8 は OS 同梱 | 実機インストール |
| NFR-02 | 性能 | 常駐メモリ **200MB 以下**、CPU **5% 以下**（装置PC上で VB_FERROTEC.exe と同居） | 計測 |
| NFR-03 | 性能 | 1秒周期の取得・判定・描画が 24h×30日連続でも遅延・メモリ増加しない | 長時間試験 |
| NFR-04 | 可用性 | 外部ネットワーク通信なし（Remoting先はローカル／社内のみ） | 通信遮断テスト |
| NFR-05 | 安全 | 読取り専用。PullerView の起動・停止・異常終了が炉の動作に影響しない | レビュー・異常系テスト |
| NFR-06 | 保守性 | docs/ の MD を唯一の設計正とし、コード変更時に同期 | レビュー |
| NFR-07 | 品質 | 判定・ini解析・設定管理は単体テスト（正常・境界・異常）。Remoting はモック可能 | pytest |
| NFR-08 | 機密 | Agent.MD 準拠。ソース・設計書・社内データを社外へ送信しない | レビュー |
| NFR-09 | ログ | 日次ローテーション、**保持 7 日**。判断根拠（FR-AL-06）を記録 | 実機確認 |
| NFR-10 | 互換 | VB_FERROTEC.exe の動作・描画性能に影響しない | 実機計測 |

## 8. アーキテクチャ方針（詳細は docs/architecture.md で別途）

```
src/pullerview/
├── core/            # ドメインモデル（Graph設定, Limits, JudgeResult, Process）
├── data_access/     # IPcObjReader（pythonnet実装 / Fake実装）, DataSetting.ini ローダ, MC index→PcInfo マッピング
├── monitoring/      # 1秒ポーリング, 工程検出, 長さ軸サンプル保持
├── diagnostics/     # 上下限判定（純粋関数）
├── ui/              # メインウィンドウ, トレンドグラフ(×4), コメント表示
├── win_integration/ # VB_FERROTEC.exe / frmAutoStatus 検出・追従
└── settings/        # アプリ設定 JSON
```

## 9. リスク

| ID | リスク | 対応 |
| --- | --- | --- |
| RISK-01 | pythonnet（CLR4）から .NET 2.0 Remoting（Binary）への接続可否 | フェーズ0で PoC。不可なら C# 小型ブリッジ（.NET Framework）+ ローカル IPC |
| RISK-04 | frmAutoStatus への位置追従の安定性 | PoC（WinEvent フック等） |
| RISK-05 | Python製 GUI のメモリ・配布サイズ | PySide6 モジュール絞込み、Nuitka 検討 |
| RISK-07 | 可用性がホスト稼働・ネットワークに依存（VB_FERROTEC はホスト未接続時にデータを公開しない）。SessionID の取得方法未確定 | D-01 の確定、通信断表示・自動再接続 |
| RISK-08 | （解消）全 index のライブ値を `stcRealData.intIOBit` から算出可能。機種による index 割付差は「なし」と確認（D-22） | 解消 |
| （解消） | RISK-02 DLL 入手、RISK-03/06 書込み影響 | 解消済み |

## 10. r0 → r1 の主な変更点と整合性メモ

| 項目 | r0 | r1 | 影響 |
| --- | --- | --- | --- |
| 判定名称 | 管理／製造 | 管理(ucl/lcl)／規格(usl/lsl) | 用語変更 |
| ウィンドウ構成 | メニューアイコン／コメント／デジタルリニアメーター／トレンド（独立4種） | メインウィンドウ（最小化／最大化）+ コメント表示。メーター・メニューアイコン廃止 | UI再設計 |
| グラフ配置 | 水平方向・スケール左端 | 縦方向・最大4・高さ自動 | |
| 異常表示 | グラフの色 | グラフの**背景色** | |
| frmAutoStatus 非表示時 | アイコン最小表示 | メインウィンドウ非表示 | |
| 工程種類 | 11 | 16（AUTO 等を追加） | 既解析と一致 |
| ini 書式 | 項目単位（Name/Address/…） | 工程×グラフ単位（pv/sv index, usl/ucl/lcl/lsl, length） | 第6章で全面置換。旧 `Processes` キー提案は廃止 |
| X軸 | （未定義・時間を想定） | 工程別長さ [mm]（index115〜119）、`length_span`=目盛間隔 | D-05 確定 |
| 異常フラグ引渡し | あり | あり（記載継続） | **ADR-0006（書込みなし）と矛盾** → D-02 |
| Sheet2（コンテナ番号表） | あり | なし | 対象外で確定 |

### 10.1 r1 → r2 の主な変更点

| 項目 | r1 | r2 | 反映 |
| --- | --- | --- | --- |
| 異常フラグの Remoting 引渡し | あり | **削除** | ADR-0009、D-02 解決 |
| 工程種類定義 | 16工程の列挙のみ | 対象／対象外と略称を追加（対象 = NECK, CROWN, SHOULDER, BODY, TAIL） | FR-ST-90〜92、D-06 解決 |
| DataSetting.ini | 12キー | `warningcolor` / `alarmcolor` 追加、キー説明コメント追加 | 第6章、FR-CF-06 |
| セクション名 | `[VACUUM1]` 形式（正式名） | 略称 + 追番（`[NECK1]`, `[CRWN1]` …） | 第6章、D-21 確定 |
| 工程名 `MANUL` | 同左 | 同左（VB 実値は `MANUAL`） | FR-ST-91 で吸収 |

### 10.2 ソース解析で確定した事項（2026-10-20）

- 「Remoting Service」= **VB_FERROTEC_SERVER**（ホスト）。`mdlServerService.sub_RegisterService` が `TcpServerChannel(port=RemotePort)` を登録し、装置PC がソケット接続した時点で `clsPcObj` を `{SessionID}ePcObj` として Marshal する。SessionID は `"PC_ID" & 連番 & ":" & 装置IP`。
- VB_FERROTEC.exe は `Activator.GetObject(clsPcObj, url)` で得た**プロキシ**に、`sub_ReadPLCData` のループで毎秒 `stcRealData`・`stcPcInfo` を代入する。つまり値の実体はホスト側にある。
- `stcPCINF_DATA`（`stcMCValue`: 名称・単位・`lDivVal`）は初期化時に **1回だけ** 代入される。`stcMCValue(n).lValue` はホスト側では更新されない。
- ライブ値は `stcRealData.intIOBit`（ZR110000〜112099、2,100ワード）に含まれる。MC 値領域は R110200〜R110479、`index n = (Address−110200)/2`、下位ワード `intIOBit[200+2n]`・上位ワード `intIOBit[200+2n+1]`。
- SessionID を知らずに接続する方法: `tcp://{HostIP}:{RemotePort}/eServer` の `clsServerObj.ConnectPc`（`clsPcObj` の一覧）から `IPAddress` が自PCと一致するものを選ぶ。
- **PullerView はホストのソケットポート（SocketPort）には接続しない**。同一 IP からの2本目の接続はホスト側の `strIP.Contains` 先頭一致により MES→装置電文の誤配送を招く（docs/analysis_host_frmServer.md）。

## 11. 要件定義で決定すべき事項（未決一覧）

優先度 A = 設計着手に必須、B = 実装前に必要、C = 既定値で先行可

**2026-10-20 時点で D-01〜D-25 はすべて解決。** 以下は決定の記録（取り消し線＝解決済み）。

| ID | 優先 | 決定事項 | 推奨案 / 補足 |
| --- | --- | --- | --- |
| D-01 | A | ~~接続先の取得方法~~ **解決（2026-10-20）**: ホスト = VB_FERROTEC_SERVER（別PC。`Application.ini` `[Server]` `HostIP`/`RemotePort`）。SessionID は `eServer` の `ConnectPc` から自IPで特定（10.2 節） | 残: 実機の HostIP/RemotePort 値と、ホストが装置PCと別筐体であることの確認 |
| D-02 | A | ~~異常フラグの引渡し~~ **解決（2026-10-20）**: 引渡しは行わない。r2 で当該行削除 | ADR-0009 |
| D-03 | A | ~~index の制約~~ **解決（2026-10-20）**: 全 index（0〜134）のライブ値を `stcRealData.intIOBit` から算出可能（FR-DT-04） | ADR-0010 |
| D-04 | A | ~~判定対象~~ **解決（2026-10-20）**: **PV 絶対値** | FR-AL-02 |
| D-05 | A | ~~X軸（長さ）~~ **解決（2026-10-20）**: 取得元は**工程別長さ index115〜119**（NECK/CROWN/SHOULDER/BODY/TAIL）、`length_span` は**目盛間隔** | FR-GR-03。残: 工程開始直後に長さが 0 のままの区間の扱い（0 にプロット） |
| D-06 | A | ~~セクション範囲~~ **解決（r2）**: 対象工程は NECK / CROWN / SHOULDER / BODY / TAIL。対象外工程ではグラフなし（FR-ST-90） | 残: 対象外工程時のメインウィンドウ表示内容（工程名のみ表示を推奨） |
| D-07 | B | ~~caption / unit の直接指定~~ **解決**: `caption=1`+`caption_text`、`unit=1`+`unit_text` | FR-CF-07（2026-10-20） |
| D-08 | B | ~~ウィンドウ構成の詳細~~ **解決**: コメント領域はメイン内上部。最小化＝タイトル行＋コメント領域。設定は**歯車アイコンのクリック**で開く | FR-UI-04/08、FR-CM-05、FR-SE-03（2026-10-20） |
| D-09 | C | ~~コメントの書式設定~~ **解決**: 残す（設定ダイアログ＋JSON） | FR-CM-03（2026-10-20） |
| D-10 | C | ~~背景色~~ **解決**: 規格優先、既定色 000066 / 800000 | FR-GR-04（2026-10-20） |
| D-11 | C | ~~グラフ描画~~ **解決**: PV・SV・4基準線、縦軸 lsl〜usl ±20% 固定（自動に切替可） | FR-GR-02/07（2026-10-20） |
| D-12 | C | ~~判定の細則~~ **解決**: 境界値は正常、連続 2 秒で確定（設定可） | FR-AL-04（2026-10-20） |
| D-13 | B | ~~工程切替時~~ **解決**: クリア | FR-GR-06（2026-10-20） |
| D-14 | B | ~~データ無効時~~ **解決**: `---` 表示・単位なし・判定不能 | FR-DT-04/05（2026-10-20） |
| D-15 | C | ~~言語~~ **解決**: 既定日本語、設定で英/中。文言「{caption} 規格上限超過」 | FR-CM-04（2026-10-20） |
| D-16 | B | ~~実行環境~~ **解決**: Windows 10 x64、exe 化（Python 不要） | NFR-01（2026-10-20） |
| D-17 | C | ~~アプリ設定の保存~~ **解決**: `%LOCALAPPDATA%\PullerView\settings.json`、既定位置 frmAutoStatus 右上 | FR-SE-01、FR-UI-07（2026-10-20） |
| D-18 | C | ~~NFR 数値~~ **解決**: 200MB / 5% / 7日 | NFR-02/09（2026-10-20） |
| D-19 | C | ~~設定の再読込~~ **解決**: 起動時自動読込＋設定メニューから手動再読込 | FR-CF-05（2026-10-20） |
| D-20 | B | ~~色コードの並び~~ **解決（2026-10-20）→ r3 で改定**: `warningcolorRGB`/`alarmcolorRGB` = **10進 9桁 RRRGGGBBB**（RRGGBB 16進は互換受理） | FR-CF-06 |
| D-21 | B | ~~セクション名の工程表記~~ **解決（2026-10-20）**: **略称 + 追番 1〜4**（`[NECK1]`, `[CRWN1]`, `[SHLD1]`, `[BODY1]`, `[TAIL1]` …）。正式名は互換の別名 | 第6章、FR-ST-92 |
| D-23 | A | ~~開発言語~~ **解決（2026-10-20）**: **C#（.NET Framework 4.8, WinForms）を採用**。理由: 1 対 1 構成で PullerView 自身が Remoting 待機側になる（.NET Framework 専用）、DLL 直接参照、ランタイム追加不要 | ADR-0001〜0004 改訂。仕様変更の決裁は開発責任者（中村氏）。汉虹への通知は不要（2026-10-20 中村氏） |
| D-24 | B | ~~ウィンドウ既定位置~~ **解決（2026-10-20）**: 式样 r3 図のとおり frmAutoStatus 左上から (300, 67)。「右上」は撤回 | FR-UI-07 |
| D-25 | B | ~~コメント領域の表示方法~~ **解決（2026-10-20）**: タイトル行直下に 1 行、**常時表示** | FR-CM-05、FR-UI-04 |
| D-22 | B | ~~機種差~~ **解決（2026-10-20）**: index 割付に機種差は**ない** | RISK-08 解消 |

## 12. 用語

| 用語 | 定義 |
| --- | --- |
| PV / SV | 現在値（Process Value）／設定値（Set Value） |
| usl / lsl | 規格上限／下限（user standard level） |
| ucl / lcl | 管理上限／下限（user control level） |
| frmAutoStatus | VB_FERROTEC.exe の自動運転ステータス画面 |
| stcMCValue | VB 側の表示定義配列（名称・単位等。静的） |
| TP_PcInfo | ホスト公開 `clsPcObj` 上の実時間値（文字列、1秒更新） |
