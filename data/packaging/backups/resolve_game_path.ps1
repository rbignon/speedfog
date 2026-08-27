# SpeedFog Game Path Resolver
# Resolves an optional eldenring.exe override for ModEngine 2 (-p/--game-path),
# so a player can run SpeedFog on a frozen copy of the game while the Steam
# install updates freely. Called by launch_speedfog.bat, which captures stdout.
#
# Resolution order:
#   1. SPEEDFOG_GAME_PATH environment variable
#   2. game_path=... in %APPDATA%\SpeedFog\config.ini (key is case-insensitive,
#      the last matching line wins, "# game_path=" comment lines are ignored)
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

try {
    $raw = $env:SPEEDFOG_GAME_PATH
    $source = "SPEEDFOG_GAME_PATH"

    if (-not $raw) {
        $configPath = Join-Path $env:APPDATA "SpeedFog\config.ini"
        if (Test-Path -LiteralPath $configPath -PathType Leaf) {
            foreach ($line in Get-Content -LiteralPath $configPath) {
                if ($line -match '^\s*game_path\s*=\s*(.+?)\s*$') {
                    $raw = $Matches[1]
                    $source = $configPath
                }
            }
        }
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
