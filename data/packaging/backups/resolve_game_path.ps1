# SpeedFog Game Path Resolver
# Resolves an optional eldenring.exe override for ModEngine 2 (-p/--game-path),
# so a player can run SpeedFog on a frozen copy of the game while the Steam
# install updates freely. Called by launch_speedfog.bat, which captures stdout.
#
# Resolution order (first configured value wins):
#   1. SPEEDFOG_GAME_PATH environment variable
#   2. game_path=... in %APPDATA%\SpeedFog\config.ini (per machine)
#   3. game_path=... in <seed>\config.ini, next to launch_speedfog.bat (per seed;
#      a seed distributor such as SpeedFog Racing can write it from a player's
#      account settings, so the per-machine file above stays authoritative)
# In both ini files the key is case-insensitive, the last matching line wins
# and "# game_path=" comment lines are ignored.
# Either value may be the eldenring.exe file or the Game directory containing it.
# Surrounding whitespace and quotes are stripped; an empty value means
# "not configured".
#
# Output (single line on stdout):
#   <path to eldenring.exe>  when an override is configured and valid
#   (nothing)                when no override is configured
#   INVALID                  when an override is configured but does not exist,
#                            or when the resolver itself fails (fail closed)

$ErrorActionPreference = 'Stop'

function Show-Error([string]$message) {
    # The popup is best-effort: the abort must not fail open if UI is unavailable.
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [void][System.Windows.Forms.MessageBox]::Show(
            $message,
            "SpeedFog",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error)
    } catch {
        [Console]::Error.WriteLine("ERROR: $message")
    }
}

function Read-GamePathFromIni([string]$configPath) {
    # Returns the last game_path= value of the file, stripped of surrounding
    # whitespace and quotes, or $null when the file is missing, has no such
    # key, or the value is empty (so an empty value does not shadow the next
    # source in the resolution order).
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        return $null
    }
    $value = $null
    foreach ($line in Get-Content -LiteralPath $configPath) {
        if ($line -match '^\s*game_path\s*=\s*(.+?)\s*$') {
            $value = $Matches[1].Trim().Trim('"', "'").Trim()
        }
    }
    if ($value) { return $value }
    return $null
}

try {
    $raw = $env:SPEEDFOG_GAME_PATH
    $source = "SPEEDFOG_GAME_PATH"

    if (-not $raw) {
        $source = Join-Path $env:APPDATA "SpeedFog\config.ini"
        $raw = Read-GamePathFromIni $source
    }

    if (-not $raw) {
        $source = Join-Path (Split-Path -Parent $PSScriptRoot) "config.ini"
        $raw = Read-GamePathFromIni $source
    }

    if ($raw) {
        $raw = $raw.Trim().Trim('"', "'").Trim()
    }
    if (-not $raw) {
        exit 0
    }

    $exe = $raw
    if (Test-Path -LiteralPath $raw -PathType Container) {
        $exe = Join-Path $raw "eldenring.exe"
    }

    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        Show-Error "Game path from $source does not point to eldenring.exe:`n$raw"
        Write-Output "INVALID"
        exit 0
    }

    Write-Output $exe
    exit 0
} catch {
    Show-Error "Could not resolve the game path override: $_"
    Write-Output "INVALID"
    exit 0
}
