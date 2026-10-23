<#
.SYNOPSIS
  Visual Studio 2022 に .NET Framework 4.0 の Targeting Pack（参照アセンブリ）を導入する。
.DESCRIPTION
  VS2022 のインストーラーには .NET Framework 4.0 の Targeting Pack が無く、PullerView.sln を開くと
  「対象フレームワークがサポートされていません」と表示される。
  Microsoft 公式の NuGet パッケージ Microsoft.NETFramework.ReferenceAssemblies.net40 に含まれる参照アセンブリ
  （FrameworkList.xml 付き）を、VS が Targeting Pack を探すフォルダへコピーすることで解決する。
    C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0
.NOTES
  - 管理者権限の PowerShell で実行すること（Program Files (x86) 配下へ書き込むため）。
  - インターネットに出られない PC では、別 PC で nupkg をダウンロードし -NupkgPath で指定する。
      https://www.nuget.org/api/v2/package/Microsoft.NETFramework.ReferenceAssemblies.net40/1.0.3
  - 実行後、Visual Studio を再起動して PullerView.sln を開き直す。
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Install-Net40TargetingPack.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Install-Net40TargetingPack.ps1 -NupkgPath C:\work\microsoft.netframework.referenceassemblies.net40.1.0.3.nupkg
#>
param(
    [string]$NupkgPath = "",
    [string]$Version = "1.0.3"
)
$ErrorActionPreference = "Stop"
$pkg = "Microsoft.NETFramework.ReferenceAssemblies.net40"
$dst = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "管理者権限の PowerShell で実行してください。"; exit 1
}

$tmp = Join-Path $env:TEMP "net40refs"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
if ($NupkgPath -eq "") {
    $NupkgPath = Join-Path $tmp "$pkg.$Version.nupkg"
    if (-not (Test-Path $NupkgPath)) {
        Write-Host "ダウンロード中: $pkg $Version（約 30MB）"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/$pkg/$Version" -OutFile $NupkgPath
    }
}
$zip = Join-Path $tmp "pkg.zip"
Copy-Item $NupkgPath $zip -Force
$x = Join-Path $tmp "x"
if (Test-Path $x) { Remove-Item $x -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $x -Force
$src = Join-Path $x "build\.NETFramework\v4.0"
if (-not (Test-Path (Join-Path $src "RedistList\FrameworkList.xml"))) { Write-Error "パッケージ内容が想定と異なります: $src"; exit 1 }

New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item (Join-Path $src "*") $dst -Recurse -Force
Write-Host ""
Write-Host "完了: $dst"
Write-Host ("  mscorlib.dll: " + (Test-Path (Join-Path $dst "mscorlib.dll")) + "  FrameworkList.xml: " + (Test-Path (Join-Path $dst "RedistList\FrameworkList.xml")))
Write-Host "Visual Studio を再起動して PullerView.sln を開き直してください（ダイアログは出なくなります）。"
