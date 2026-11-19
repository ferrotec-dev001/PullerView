@echo off
rem .NET Framework 4.0 付属のコンパイラでビルドする（Visual Studio 不要）。出力は ..\bin
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /platform:x86 /target:exe /out:..\bin\CvRecv40.exe CvCore40.cs CvRecv40.cs
"%CSC%" /nologo /platform:x86 /target:exe /out:..\bin\FakeCv40.exe FakeCv40.cs
pause
