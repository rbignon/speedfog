@echo off
REM SpeedFog Launcher for Elden Ring

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Optional game override: SPEEDFOG_GAME_PATH or game_path= in
REM %APPDATA%\SpeedFog\config.ini (see backups\resolve_game_path.ps1).
REM Lets a player run SpeedFog on a frozen copy of the game. An override that
REM points nowhere aborts here rather than silently launching the Steam install.
set "GAME_EXE="
for /f "usebackq delims=" %%P in (`powershell -ExecutionPolicy Bypass -NoProfile -File "%SCRIPT_DIR%backups\resolve_game_path.ps1"`) do set "GAME_EXE=%%P"
if "%GAME_EXE%"=="INVALID" (
    endlocal
    exit /b 1
)
set "GAME_ARGS="
if defined GAME_EXE set GAME_ARGS=-p "%GAME_EXE%"
if defined GAME_EXE echo Game: "%GAME_EXE%"
if not defined GAME_EXE echo Game: Steam install, auto-detected by ModEngine 2

REM Detect save file and start backup daemon
powershell -ExecutionPolicy Bypass -NoProfile -File "%SCRIPT_DIR%backups\launch_helper.ps1"

REM Exit code 2 = Elden Ring already running: abort without launching
if %ERRORLEVEL% EQU 2 (
    endlocal
    exit /b 1
)

REM Launch ModEngine 2 with our config
"%SCRIPT_DIR%modengine2\modengine2_launcher.exe" -t er -c "%SCRIPT_DIR%modengine2\config_speedfog.toml" %GAME_ARGS%

endlocal
