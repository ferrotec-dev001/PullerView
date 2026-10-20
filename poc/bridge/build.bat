@echo off
rem .NET Framework 4.8 同梱の C# コンパイラでビルド（Visual Studio 不要）
rem VB_FERROTEC_Common.dll は VB_FERROTEC.exe と同じフォルダのものをこのフォルダへコピーしてから実行
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:exe /platform:anycpu /out:RemotingProbe.exe /r:VB_FERROTEC_Common.dll /r:System.Runtime.Remoting.dll /r:System.Core.dll RemotingProbe.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo OK: RemotingProbe.exe
