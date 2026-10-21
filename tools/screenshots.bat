@echo off
rem Rebuilds the app, then regenerates docs\screenshots\*.png using demo data
rem (screenshot mode never touches your real settings or traffic history).
cd /d "%~dp0.."
call build.bat || exit /b 1
start "" /wait TrafficTarget.exe /screenshots "%~dp0..\docs\screenshots"
