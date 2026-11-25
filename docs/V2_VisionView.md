# PullerView V2（PullerView＋VisionView 合体版）

- ブランチ: `PullerView_V2`（単体版は `main` のまま。V2 のタグは `V2-v1` から）
- 2.0.1.0（`V2-v2`）で XG-X の画像取り込みを追加しました。機種の切替・設定・確認結果は `docs/V2_XGX.md` を参照してください。以下は `V2-v1` 時点の記述です
- 対象: Windows 7 32bit／.NET Framework 4.0／x86。Visual Studio 2022 で `src/PullerView.sln` を開いてビルド（手順は `docs/build.md` と同じ）
- 追加した DLL はありません。晶線解析は C# だけで実装しています（OpenCV は使っていません）

## 追加・変更したファイル（`src/PullerView/`）

| ファイル | 内容 |
|---|---|
| `VisionAnalyzer.cs` | 晶線解析。輪郭方式（edge／ridge。CROWN 中後期・BODY）と輝度方式（NECK） |
| `VisionView.cs` | VisionView.ini の読み書き、画像入力（カメラ TCP／ファイル再生／模擬カメラ）、シールド遮蔽時の判定除外、真偽度、OK／NG、イベント |
| `VisionPane.cs` | 統合画面 案 B-2 の右欄、タイトル行のカメラのアイコンと判定バッジ |
| `VisionSettings.cs`・`CameraSettings.cs` | 設定タブ「晶線解析設定」「カメラ設定」 |
| `CvCore40.cs` | 画像の受信・保存（`poc/CvRecv40` と同じ内容） |
| `PullerViewForm.cs`・`GraphSettings.cs`・`Model.cs`・`Program.cs` | 上記の組込み（変更箇所に `V2:` のコメント） |
| `config/VisionView.ini` | 設定ファイル（EXE と同じフォルダへコピーされる） |

## 動作

- `VisionView.ini` の `[Vision] enable=1` で有効。`enable=0` またはファイルが無いときは単体版と同じ動作
- 画像入力元 `source`: `camera`（画像出力ポートへ TCP 接続）／`file`（`[Playback] folder` の BMP をファイル名順に再生）／`sim`（再生フォルダの BMP を本機内で TCP 送信し、受信・保存・解析まで本番と同じ経路を通す）
- 工程 `process_source`: `auto`（VB_FERROTEC の工程。未接続・模擬データ中は `manual_process`）／`manual`／`filename`（`…_NECK.bmp`）
- VB_FERROTEC.exe・カメラのどちらにも接続していなくても、`source=file` で解析・判定・表示・イベントログまで動く
- 画面: 最大化時に右欄（判定・真偽度／解析画像／座標展開グラフ・真偽度の推移・判定履歴）。タイトル行のカメラのアイコンで右欄を開閉（状態は `pane` に保存）。畳んでいる間と最小化中は、判定をタイトル行のバッジで表示
- 解析画像: 識別線＝黄（BODY はリング長半径×0.55、CROWN は頂点まで）、凹凸マーク ×＝緑の細線、シールドの陰の晶線（BODY）＝黄の破線
- 判定: 真偽度＝期待位置で検出できた回数÷直近 N 回×100。N 回に満たない間は判定しない。閾値と同じ値は OK。工程切替でリセットし、`judge_delay_sec` の間は待機
- 受信した画像は OK／NG を判断せず 1 つのフォルダ（`[Image] folder`）へ `YYYYMMDDHHMMSS_工程名.bmp` で保存。最大保持枚数の既定は 10 枚
- 設定画面のタブ順: グラフ設定／晶線解析設定／カメラ設定／ログ設定／表示設定。VisionView.ini は「保存」と「閉じる」で保存され、保存時に画像入力をやり直す

## 検証（2026-11-20、サンドボックス。Linux の Mono 上）

`PullerView.exe --vision-test <画像フォルダ> <工程>` の結果を、Python の参照実装（OpenCV）と 1 枚ずつ比較しました。画像はすべて正常画像で、NG 画像では確認していません。

| 工程・画像 | 参照実装 | V2（C#） | 晶線の角度の一致 |
|---|---|---|---|
| BODY 52 枚（11/19） | 検出 24 | 検出 24・判定対象外 28・未検出 0 | 52 枚すべて一致 |
| CROWN 中後期 232 枚（11/18、リング半径 100px 以上） | 検出 231 | 検出 231 | 231 枚一致。1 枚は閾値付近の候補が 1 本多い（判定は同じ） |
| NECK 35 枚（10/29 の 15 枚・11/18 の 20 枚） | 晶線 25 本・32 本 | 晶線 25 本・32 本 | 35 枚すべて一致 |

- 解析時間: 輪郭方式 約 220 ms／枚、輝度方式 約 25 ms／枚（サンドボックスの 2 コア・Mono での値。装置 PC の値ではありません）
- 模擬カメラ（`source=sim`）で、受信 → 保存（10 枚を超えた分は古い順に削除）→ 解析 → 判定イベントまで動くことを確認
- .NET Framework 4.0 の参照アセンブリに対して C# 5・x86 でコンパイルできることを確認

## 未確認・未実装

- Windows 7 32bit 実機と Visual Studio 2022 でのビルド・実行（サンドボックスには Windows が無いため）
- 実際のカメラ（CV-5701）との接続。XG-X は `docs/V2_XGX.md` の「確認できていないこと」を参照
- SHOULDER・TAIL・CROWN 初期（リングが小さい時期）は画像での確認なし。SHOULDER は CROWN と同じ仮の設定、TAIL は既定で対象外（`[TAIL] enable=0`）
- 全サイズ（2432×2050）の画像は 2×2 画素を平均して 1/2 にしてから解析する（参照実装との比較は未実施）
- シールドの陰の晶線（破線）は回転からの推算位置のみで、画像による位置合わせはしていない
- 右欄を畳んだ状態で工程追従が「手動」のときは、タイトル行に判定バッジを出す場所が無いため表示しない
- 工程ごとの `enable`、`pair_tol_deg`、`ring_aspect`、`display_gain`・`display_gamma` は設定画面に無く、VisionView.ini で直接変更する
