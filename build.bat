@echo off
rem Builds TrafficTarget.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Nothing to install. Double-click this file, or run it from a command prompt / CI.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The .NET Framework 4 C# compiler was not found on this PC.> build_log.txt
  type build_log.txt
  if not defined CI pause
  exit /b 1
)
if not defined CI taskkill /im TrafficTarget.exe /f >nul 2>&1
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"TrafficTarget.exe" /win32icon:"assets\app.ico" /win32manifest:"assets\app.manifest" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /recurse:"src\*.cs" > build_log.txt 2>&1
if errorlevel 1 (
  echo BUILD FAILED>> build_log.txt
  type build_log.txt
  if not defined CI pause
  exit /b 1
)
echo BUILD OK>> build_log.txt
type build_log.txt
if not defined CI start "" "%~dp0TrafficTarget.exe"
endlocal
