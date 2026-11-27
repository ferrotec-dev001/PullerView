# PullerView ビルド・配置手順（Visual Studio 2022）

対象環境: 実機 **Windows 7 32bit / .NET Framework 4.0**（開発 PC は Windows 10/11 + VS2022）

## 1. 開発 PC の準備
1. Visual Studio 2022（Community 以上）をインストール。ワークロード「.NET デスクトップ開発」を選択。
2. **.NET Framework 4.0 Targeting Pack** を導入する。VS2022 のインストーラーには 4.0 が含まれず、そのまま `PullerView.sln` を開くと
   「対象フレームワークがサポートされていません（.NET Framework 4.8 に更新する／Targeting Pack をダウンロードする／読み込まない）」のダイアログが出る。
   ここで **「4.8 に更新する」は選ばない**（実機は .NET 4.0 のため、4.8 でビルドした EXE は実機で起動時に「.NET Framework 4.8 が必要です」となり動かない）。
   次の手順で 4.0 の参照アセンブリを入れると、ダイアログは出なくなり v4.0 のままビルドできる。

   **手順（管理者 PowerShell、1 回だけ）**
   ```powershell
   cd <リポジトリ>\tools
   powershell -ExecutionPolicy Bypass -File .\Install-Net40TargetingPack.ps1
   ```
   - Microsoft 公式 NuGet パッケージ `Microsoft.NETFramework.ReferenceAssemblies.net40`（1.0.3、約 30MB）を取得し、
     VS が Targeting Pack を探すフォルダ `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0`
     に `mscorlib.dll` 等と `RedistList\FrameworkList.xml` をコピーする。
   - インターネットに出られない PC では、別 PC で
     `https://www.nuget.org/api/v2/package/Microsoft.NETFramework.ReferenceAssemblies.net40/1.0.3`
     をダウンロードして持ち込み、`-NupkgPath <nupkg のパス>` を付けて実行する。
   - 手動で行う場合: nupkg を `.zip` にリネームして展開し、`build\.NETFramework\v4.0` フォルダの中身を上記フォルダへコピーする。
   - 実行後に Visual Studio を再起動する。ダイアログが出ずに開き、プロジェクトのプロパティで対象フレームワークが「.NET Framework 4」と表示されれば OK。

   **ダイアログがすでに出てしまった場合**: 「このプロジェクトを読み込まない」を選んで VS を閉じ、上の手順を実行してから開き直す。
   誤って「4.8 に更新する」を選んだ場合は `src/PullerView/PullerView.csproj` と `src/tools/FakeFerrotec/FakeFerrotec.csproj` の
   `<TargetFrameworkVersion>` を `v4.0` に戻す（git で差分を確認すれば分かる）。

   **参考（代替案）**: 実機側を .NET Framework 4.8 に更新できる場合（Windows 7 SP1 は 4.8 をサポート）は、プロジェクトを 4.8 に上げる選択肢もある。
   ただし現行の決定（実機 .NET 4.0 固定）では採らない。4.8 でビルドすると 4.5 以降の API（例: `String.Split(char, StringSplitOptions)`）を
   誤って使っても気付けず、実機で `MissingMethodException` になる点にも注意。

## 2. ソリューション構成
```
src/
  PullerView.sln                … VS2022 で開くソリューション（構成: Debug|x86 / Release|x86）
  PullerView/                   … 本体（WinForms, .NET Framework 4.0, x86）
    Program.cs                  … 起動・引数（--sim / --render / --no-layered）・クラッシュログ
    HostLink.cs                 … VB_FERROTEC.exe からの接続待受（鮮度判定・複数接続・半開検出・BCC 検証。試験報告 docs/test/ConnectionStability_2026-10-24.md）（SocketPort → SessionID 返信、RemotePort で clsPcObj 公開）、毎秒読取り
    HostWatch.cs                … VB_FERROTEC.exe プロセス／frmAutoStatus ウィンドウ検出（Win32）、表示位置追従
    Model.cs                    … 設定（PullerView.ini / DataSetting.ini）、13 工程モデル、判定、実データ Feed／模擬 Tick
    McMeta.cs                   … stcMCValue（項目名・単位・除数）のキャッシュ McMeta.ini
    PullerViewForm.cs           … 描画（タイトル・コメント行・最大 4 グラフ）、背景のみ透過（UpdateLayeredWindow）
    Forms.cs                    … --sim 用の操作パネル／frmAutoStatus ダミー
    GraphSettings.cs            … 設定ウィンドウ（タブ構成）、DarkPanel（カード・セグメント・トグル・スライダー・テンキーの共通部品）、DataSetting.ini 編集画面（DsDoc／3 言語 Lx）
    Trend.cs                    … 表示用トレンド集約 TrendBins（1 px = 1 ビンの min/max/last。包絡線＋last 線描画用。FR-UI-17）
    DataLog.cs                  … 生データ CSV ログ（log\PV_yyyyMMdd.csv、炉ログ相当フォーマット暫定。FR-DT-08）
    WindowSettings.cs           … ウィンドウ／表示 タブ（PullerView.ini: 背景色・透過・不透明度・ロゴ・コメント・判定秒数・言語・縦軸・単体起動・工程追従・X 軸）。DarkPanel ベース、変更は即時反映、保存はフッター「保存」
    Logo.cs / Ini.cs            … ロゴ（GDI+ ベクタ）、INI 読み書き
    Resources/frmAutoStatus.png … --sim 用ダミー背景（埋め込みリソース）
  tools/FakeFerrotec/           … VB_FERROTEC.exe の代わりに接続してテストデータを流すコンソールツール
  lib/VB_FERROTEC_Common.dll    … 装置側の共通 DLL（v2.2.0.2、参照・出力フォルダへコピー）
config/
  DataSetting.ini / PullerView.ini / McMeta.ini … 設定ファイル（ビルド時に出力フォルダへコピー）
```

## 3. ビルド
1. `src/PullerView.sln` を VS2022 で開く。
2. ソリューション構成を **Release / x86** にする。
3. 「ビルド」→「ソリューションのビルド」。出力は `src/PullerView/bin/Release/`。
   `PullerView.exe`, `PullerView.exe.config`, `VB_FERROTEC_Common.dll`, `DataSetting.ini`, `PullerView.ini`, `McMeta.ini` が生成される。
4. F5 でそのまま実行できる。VB_FERROTEC.exe が無い PC では `standalone=1`（既定）のため「VB_FERROTEC.exe（frmAutoStatus）未検出」の表示で待受状態になる。

### 装置なしで動作確認する
1. `PullerView.exe` を起動（待受）。
2. `src/tools/FakeFerrotec/bin/Release/FakeFerrotec.exe` を **PullerView.exe と同じフォルダにコピーして**起動する（`VB_FERROTEC_Common.dll` と `McMeta.ini` を同じフォルダから読む）。`FakeFerrotec.exe --proc-sec 30` で 30 秒ごとに NECK→CROWN→SHOULDER→BODY→TAIL→OTHER と工程が進む。
3. PullerView に PV/SV と工程名が表示され、`PullerView.log` に `[SOCKET] TextID=00 … Remoting 公開` と `stcMCValue 受信 150 項目` が記録されれば疎通 OK。
4. 模擬データだけ見たい場合は `PullerView.exe --sim`（操作パネル付き、通信なし）。

## 4. 実機への配置（Windows 7 32bit / .NET 4.0）
1. 実機の任意フォルダ（例 `C:\PullerView\`）に `bin/Release` の次のファイルを置く。
   `PullerView.exe`, `PullerView.exe.config`, `VB_FERROTEC_Common.dll`, `DataSetting.ini`, `PullerView.ini`, `McMeta.ini`
2. `PullerView.ini` の `[Host]` を VB_FERROTEC.exe の `Application.ini [Server]` に合わせる（`remote_port`=RemotePort、`socket_port`=SocketPort）。`[Ment]` は `Ment.ini [Soket Package Element]` に合わせる。`application_ini=C:\...\Application.ini` と書けば、そこからポートを自動で読む。
3. **VB_FERROTEC_SERVER は起動しない**（PullerView が同じポートで待受するため競合する）。
4. 起動順: `PullerView.exe` → `VB_FERROTEC.exe`（または VB_FERROTEC.exe の再起動）。VB_FERROTEC.exe 起動時の初回接続で `stcMCValue` が届き `McMeta.ini` が実機値に更新される。
5. `DataSetting.ini` の工程ごとの表示項目（stcMCValue index）と USL/UCL/LCL/LSL は、歯車アイコン → 設定ウィンドウ「グラフ設定（DataSetting.ini）」タブで編集・保存できる（テキストエディタでの編集も可。保存時に `DataSetting.ini.bak` を作成）。
6. ログ: `PullerView.log`（動作）、`PullerView_crash.log`（例外）。いずれも EXE と同じフォルダ。

### Microsoft.VisualBasic 8.0.0.0 について
`VB_FERROTEC_Common.dll` は `Microsoft.VisualBasic, Version=8.0.0.0` を参照しているが、.NET Framework 4.0 ではフレームワーク標準アセンブリの統合により GAC の 10.0.0.0 へバインドされる（同じ DLL を使う PoC の RemotingProbe.exe が実機 .NET 4.0 で動作済み）。追加の DLL や bindingRedirect は不要。

## 5. 設計上の要点
- PullerView は **読取り専用**。clsPcObj のプロパティを読むだけで、VB_FERROTEC.exe／PLC へは書き込まない。
- 待受方式: VB_FERROTEC_SERVER を使わず PullerView が SocketPort/RemotePort の待受側になる（1 対 1 接続）。
- X 軸 = 結晶長さ（`strINGOT_LENGTH`）。最大値／目盛は `PullerView.ini [Graph]`。
- 工程は 13 状態（STATUS_CHECK … SHUTDOWN + OTHER）。`strPROCESS` が空／未知のときは OTHER。
- 設定は EXE と同じフォルダの `.ini` に集約する（レジストリ・AppData は使わない）。
- frmAutoStatus 未検出時（`standalone=0`）はロゴアイコンのみの小窓になる。クリックで最小化ウィンドウに復帰、再検出で元の表示に戻る。
- 工程追従は タイトル行のアイコン（歯車の左）で 自動（Remoting 追従）／手動（13 工程から固定）を切替。`[Graph] proc_mode / manual_proc` に保存。

## 6. 未検証事項（実機で確認が必要）
- Win32 `FindWindow`/`EnumWindows` による frmAutoStatus 検出と位置追従、`UpdateLayeredWindow` による背景透過は Linux サンドボックスでは確認できていない（サンドボックスでは Socket/Remoting 疎通・描画・判定のみ FakeFerrotec で確認済み）。

## PullerView V2（ブランチ `PullerView_V2`）

VisionView（晶線解析）を内蔵した合体版です。ビルド手順は同じで、追加の DLL はありません。出力フォルダに `VisionView.ini` もコピーされます。内容と検証結果は `docs/V2_VisionView.md` を参照してください。

### XG-X 画像取り込み（2.0.1.0／タグ `V2-v2`）

- ソリューションにプロジェクト `XgxBridge`（`src/tools/XgxBridge/`、出力 `PullerView.XgxBridge.exe`、.NET Framework 4.0／x86）を追加しました。ソリューションをビルドすると、`PullerView\bin\<構成>\XgxBridge\` へ exe と exe.config がコピーされます。
- ビルドに KEYENCE の DLL は不要です。`Vapi.Net.dll` は実行時に、設定した「VisionTerminal のインストール先フォルダ」から読み込みます。リポジトリ・配布物には含めません。
- 実機へは `PullerView.exe` と同じフォルダに `XgxBridge\` フォルダごと配置します（CV シリーズだけを使う場合は無くても動きます）。
- 画面なしの確認: `PullerView.exe --xgx-test 60`（XG-X）、`PullerView.exe --camera-test 60`（`[Camera] model` の機種）。
- 内容・確認結果・未確認事項は `docs/V2_XGX.md` を参照してください。

### フォルダ選択ボタン、受信フォルダの長さの上限（2.0.2.0／タグ `V2-v3`）

- 設定画面のフォルダ欄（カメラ設定: 受信フォルダ／VisionTerminal のインストール先フォルダ／保存先フォルダ／再生フォルダ、ログ設定: 保存フォルダ）の右に、フォルダを選ぶボタンを追加しました。キーボードでの入力も従来どおりできます。
- 選んだフォルダが PullerView.exe のフォルダの下にあるときは相対パス（例 `Image`）で入ります。VisionTerminal のインストール先フォルダだけは常に絶対パスで入ります。
- XG-X の受信フォルダの上限を「100 文字」から「ANSI で 160 バイト」（全角は 2 バイト）に変えました。理由は `docs/V2_XGX.md` の 3 章を参照してください。

### XG-X ブリッジの原因調査の強化（2.0.3.0／タグ `V2-v4`）

- Vapi.Net.dll を読み込めないとき、読み込めない DLL の名前を表示します。`PullerView.XgxBridge.exe --check --vapi-dir <フォルダ>` で、XG-X へ接続せずに確認だけ行えます。
- ブリッジが異常終了したとき、Windows のイベントログの記録と、読み込まれていた DLL の一覧を PullerView.log に残します。異常終了が 3 回続くと再起動を 60 秒以上空けます。
- 実機ログの分析と手順は `docs/V2_XGX.md` の 8 章を参照してください。

### ZR アドレス指定によるグラフ項目の選択（2.0.4.0／タグ `V2-v5`）

PV / SV に `stcRealData.intIOBit` の ZR アドレス（ZR110000〜112299）を指定できる。定義は EXE と同じフォルダの `ZrMap.ini`（`config/ZrMap.ini` がビルド時にコピーされる）。詳細と除数の注意は `docs/V2_ZrMap.md`。

### ZrMap.ini の更新（2.0.5.0／タグ `V2-v6`）

開発責任者が編集した `config/ZrMap.ini` を反映。プログラムの変更はバージョン番号のみ。内容は `docs/V2_ZrMap.md` 6 章。

### XG-X コールバックの修正（2.0.6.0／タグ `V2-v7`）

Vapi.Net.dll が `BeginInvoke` で呼ぶコールバックを、通常のメソッドに結び付けたデリゲートに変更（実機の ArgumentException 対策）。`docs/V2_XGX.md` 9 章。

### JPEG 画像への対応（2.0.11.0／タグ `V2-v12`）

XG-X 受信フォルダ・画像保存・OK_SNAP／NG_SNAP・ファイル再生が .jpg／.jpeg にも対応。受け取った形式のまま保存する。`docs/V2_Jpeg.md`。

### 探索枠とメニスカスの自動検出（2.0.10.0／タグ `V2-v11`）

工程ごとの探索枠（画像に対する %）の中でメニスカスを自動検出し、その周辺だけを解析する。XG-X の画像（坩堝全体が写る）向け。「晶線解析設定」タブの「探索枠…」で、ドラッグまたは数値で設定。`VisionLocate.cs`、`RoiForm.cs`、`docs/V2_SearchFrame.md`。

### 画像の保存 OK_SNAP／NG_SNAP（2.0.9.0／タグ `V2-v10`）

未検出と数えた画像を自動で `NG_SNAP` へ、解析画像の下の「OKへ保存」「NGへ保存」ボタンで `OK_SNAP`／`NG_SNAP` へ保存する。ファイル再生の既定フォルダは `NG_SNAP`。設定は `VisionView.ini [Snap]`（上限は既定 1000 枚。上限時の動作は「ログ設定」タブで選択）。`docs/V2_Snap.md`（V2-v8 の教示画像の保存を置き換え）。
