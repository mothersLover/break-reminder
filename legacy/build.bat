@echo off
rem Builds BreakReminder.exe with the C# compiler bundled with Windows (.NET Framework 4). No installs needed.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:BreakReminder.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo OK: BreakReminder.exe
