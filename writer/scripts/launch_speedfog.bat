@echo off
REM SpeedFog Launcher for Elden Ring

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Detect save file and start backup daemon
powershell -ExecutionPolicy Bypass -NoProfile -File "%SCRIPT_DIR%backups\launch_helper.ps1"

REM Launch ME3 with our profile
"%SCRIPT_DIR%me3.exe" launch --game eldenring --profile "%SCRIPT_DIR%config_speedfog.me3"

endlocal
