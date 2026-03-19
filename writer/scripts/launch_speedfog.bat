@echo off
REM SpeedFog Launcher for Elden Ring

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Detect save file and start backup daemon
powershell -ExecutionPolicy Bypass -NoProfile -File "%SCRIPT_DIR%backups\launch_helper.ps1"

REM Launch ModEngine with our config
"%SCRIPT_DIR%ModEngine\modengine2_launcher.exe" -t er -c "%SCRIPT_DIR%config_speedfog.toml"

endlocal
