# CvRecv40 v0.1 — CvImageSaver 受信・保存部の .NET Framework 4.0 移植（検証用）

CvImageSaver v1.1.0（.NET 8）の受信・保存部を、Windows 7 32bit／.NET Framework 4.0 で動く形に書き直したものです。
PullerView.exe（VisionView）へ内蔵する受信処理の元になります。画面（タスクトレイ・設定画面）は含みません。

## 構成

| ファイル | 内容 |
|---|---|
| `bin\CvRecv40.exe` | 受信・保存の確認用コンソール。`CvRecv40.ini` を読んで接続し、Ctrl+C で終了 |
| `bin\FakeCv40.exe` | 模擬カメラ。フォルダの BMP を画像出力ポートと同じ T1 電文で送る |
| `bin\CvRecv40.ini` | 設定（接続先 IP・ポート・カメラ番号・保存先・最大保持枚数 など） |
| `src\CvCore40.cs` | 電文解析・受信・保存・自動削除（PullerView へ組み込む部分） |
| `src\CvRecv40.cs`、`src\FakeCv40.cs` | 上記 2 つの EXE の本体 |
| `src\build.bat` | .NET Framework 4.0 付属の csc.exe でビルドする（Windows 上では未確認） |

リポジトリには EXE を含めていません（`poc/bridge` と同じ扱い）。`src\build.bat` でビルドするか、配布した `CvRecv40_v0.1.zip` に同梱の EXE を使ってください。

## 元の CvImageSaver からの変更

- `async/await` → 専用スレッド、`System.Text.Json`（settings.json）→ INI、Serilog → 日付ごとのテキストログ、C# 8 の構文 → C# 4 の構文
- 電文の解析手順（T1／BR ヘッダ、チェックサム、テキスト行の読み飛ばし、再同期）は元のまま
- OK／NG のフォルダ分けをやめ、1 つのフォルダへ保存
- `camera_no=0` で全カメラの画像を保存
- 最大保持枚数の既定値を 10 枚に変更
- ファイル名に工程名を付けられる（`YYYYMMDDHHMMSS_工程名.bmp`）。同じ秒に複数枚届いた場合は `YYYYMMDDHHMMSS-1_工程名.bmp` のように枝番を付ける（枝番の形式は仮）

## 模擬カメラでの確認手順（1 台の PC で）

```
FakeCv40.exe --folder <BMP のあるフォルダ> --port 8501 --interval 600
```
`CvRecv40.ini` の `ip=127.0.0.1` にして `CvRecv40.exe` を起動すると、`Image\` に保存されます。

FakeCv40 のオプション: `--camera 1,2`（カメラ番号を順に変える）、`--count N`（N 枚で終了）、`--crlf`、`--chunk N`（細かく分割して送る）、`--badchk K`（K 枚ごとにチェックサムを壊す）、`--garbage`（先頭に不正データ）、`--dropafter N`（N 枚ごとに切断）

## 確認済みの内容（2026-11-20、サンドボックス）

.NET Framework 4.0 の参照アセンブリ（Microsoft.NETFramework.ReferenceAssemblies.net40 1.0.3）に対して C# 4・x86 でコンパイルし、Linux の Mono 上で FakeCv40 と接続して確認しました。画像は 1216×1025・24bit の BMP 6 種です。

| 試験 | 結果 |
|---|---|
| 15 枚を細かく分割して送信、max_files=10 | 15 枚受信・保存、古い 5 枚を削除して 10 枚。全ファイルが送信元とバイト一致 |
| カメラ 1・2 を交互に 12 枚、camera_no=1 | 6 枚保存（カメラ 2 の 6 枚は保存しない） |
| カメラ 1〜3 を 12 枚、camera_no=0 | 12 枚すべて保存 |
| 3 枚ごとにチェックサム不一致、reject_checksum=1、CR+LF | 8 枚保存、4 枚破棄 |
| 先頭に不正データ、3 枚ごとに切断 | 再同期して受信、自動再接続 3 回、9 枚すべて保存 |
| save_every=4 で 12 枚 | 3 枚保存 |

## 未確認

- Windows 7 32bit／.NET Framework 4.0 の実機での動作（同梱の EXE はサンドボックスの Mono コンパイラでビルドしたもの）
- 実際の CV-5701 との接続（現在イーサネット未接続）。XG-X2800 の画像出力形式
- 600ms 間隔での長時間運転、保持日数による削除（1 時間ごとの処理）
