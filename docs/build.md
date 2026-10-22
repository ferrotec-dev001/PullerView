# PullerView ビルド・配置手順（Visual Studio 2022）

対象環境: 実機 **Windows 7 32bit / .NET Framework 4.0**（開発 PC は Windows 10/11 + VS2022）

## 1. 開発 PC の準備
1. Visual Studio 2022（Community 以上）をインストール。ワークロード「.NET デスクトップ開発」を選択。
2. **.NET Framework 4.0 Targeting Pack** を用意する。VS2022 のインストーラーには 4.0 が含まれないため、いずれかの方法で用意する。
   - NuGet パッケージ `Microsoft.NETFramework.ReferenceAssemblies.net40` を参照する（オフラインの場合はパッケージを持ち込む）、または
   - 旧 Targeting Pack（`Microsoft .NET Framework 4 Multi-Targeting Pack`）をインストールする。
   - どうしても用意できない場合は `PullerView.csproj` / `FakeFerrotec.csproj` の `<TargetFrameworkVersion>v4.0</TargetFrameworkVersion>` を `v4.5.2` 等に変更すればビルドできるが、**実機（.NET 4.0）では動作しない**。実機配布用は必ず v4.0 でビルドすること。

## 2. ソリューション構成
```
src/
  PullerView.sln                … VS2022 で開くソリューション（構成: Debug|x86 / Release|x86）
  PullerView/                   … 本体（WinForms, .NET Framework 4.0, x86）
    Program.cs                  … 起動・引数（--sim / --render / --no-layered）・クラッシュログ
    HostLink.cs                 … VB_FERROTEC.exe からの接続待受（SocketPort → SessionID 返信、RemotePort で clsPcObj 公開）、毎秒読取り
    HostWatch.cs                … VB_FERROTEC.exe プロセス／frmAutoStatus ウィンドウ検出（Win32）、表示位置追従
    Model.cs                    … 設定（PullerView.ini / DataSetting.ini）、13 工程モデル、判定、実データ Feed／模擬 Tick
    McMeta.cs                   … stcMCValue（項目名・単位・除数）のキャッシュ McMeta.ini
    PullerViewForm.cs           … 描画（タイトル・コメント行・最大 4 グラフ）、背景のみ透過（UpdateLayeredWindow）
    Forms.cs                    … 設定画面、--sim 用の操作パネル／frmAutoStatus ダミー
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
5. `DataSetting.ini` で工程ごとの表示項目（stcMCValue index）と USL/UCL/LCL/LSL を設定する。歯車アイコン → 設定画面から再読込できる。
6. ログ: `PullerView.log`（動作）、`PullerView_crash.log`（例外）。いずれも EXE と同じフォルダ。

### Microsoft.VisualBasic 8.0.0.0 について
`VB_FERROTEC_Common.dll` は `Microsoft.VisualBasic, Version=8.0.0.0` を参照しているが、.NET Framework 4.0 ではフレームワーク標準アセンブリの統合により GAC の 10.0.0.0 へバインドされる（同じ DLL を使う PoC の RemotingProbe.exe が実機 .NET 4.0 で動作済み）。追加の DLL や bindingRedirect は不要。

## 5. 設計上の要点
- PullerView は **読取り専用**。clsPcObj のプロパティを読むだけで、VB_FERROTEC.exe／PLC へは書き込まない。
- 待受方式: VB_FERROTEC_SERVER を使わず PullerView が SocketPort/RemotePort の待受側になる（1 対 1 接続）。
- X 軸 = 結晶長さ（`strINGOT_LENGTH`）。最大値／目盛は `PullerView.ini [Graph]`。
- 工程は 13 状態（STATUS_CHECK … SHUTDOWN + OTHER）。`strPROCESS` が空／未知のときは OTHER。
- 設定は EXE と同じフォルダの `.ini` に集約する（レジストリ・AppData は使わない）。

## 6. 未検証事項（実機で確認が必要）
- Win32 `FindWindow`/`EnumWindows` による frmAutoStatus 検出と位置追従、`UpdateLayeredWindow` による背景透過は Linux サンドボックスでは確認できていない（サンドボックスでは Socket/Remoting 疎通・描画・判定のみ FakeFerrotec で確認済み）。
