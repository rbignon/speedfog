@echo off
REM SpeedFog Launcher for Elden Ring

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Detect save file and start backup daemon
powershell -ExecutionPolicy Bypass -NoProfile -File "%SCRIPT_DIR%backups\launch_helper.ps1"

REM Exit code 2 = Elden Ring already running: abort without launching
if %ERRORLEVEL% EQU 2 (
    endlocal
    exit /b 1
)

REM Launch ModEngine 2 with our config
"%SCRIPT_DIR%modengine2\modengine2_launcher.exe" -t er -c "%SCRIPT_DIR%modengine2\config_speedfog.toml"

endlocal
