@echo off
REM SpeedFog Launcher for Elden Ring

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Detect save file and start backup daemon
powershell -ExecutionPolicy Bypass -NoProfile -File "%SCRIPT_DIR%backups\launch_helper.ps1"

REM Launch ModEngine 2 with our config
"%SCRIPT_DIR%modengine2\modengine2_launcher.exe" -t er -c "%SCRIPT_DIR%config_speedfog.toml"

endlocal
