# SpeedFog backup store: naming, listing, creation and purge of save backups.
# Dot-sourced by backup_daemon.ps1 and recovery.ps1. Must stay compatible with
# Windows PowerShell 5.1 and ASCII-only (5.1 reads BOM-less scripts as ANSI).
#
# Backup names: <seq>_<kind>_<yyyy-MM-dd_HH.mm.ss>.zip
#   0001_launch1-start_2026-10-13_14.30.00.zip    save when launch 1 began
#   0002_launch1_2026-10-13_14.31.00.zip           periodic backup of launch 1
#   0012_before-restore_2026-10-13_14.41.03.zip    save replaced by a restore
#   0013_launch2-warp_2026-10-13_14.43.00.zip      mid-warp save (see Test-SaveMidWarp)
# <seq> is permanent: a purge never shifts it, so "#8 is the backup just
# before #9" stays true across launches and restores. The -warp suffix can
# follow any kind (launch2-start-warp, before-restore-warp).

$script:BackupNamePattern = '^(\d+)_(?:launch(\d+)(-start)?|(before-restore))(-warp)?_(\d{4}-\d{2}-\d{2}_\d{2}\.\d{2}\.\d{2})\.zip$'
$script:BackupTimeFormat = 'yyyy-MM-dd_HH.mm.ss'
$script:Invariant = [System.Globalization.CultureInfo]::InvariantCulture

function ConvertTo-BackupTime {
    param([string]$Text)
    [datetime]::ParseExact($Text, $script:BackupTimeFormat, $script:Invariant)
}

function Format-BackupClock {
    param([datetime]$Time)
    $Time.ToString('HH:mm', $script:Invariant)
}

# Parse one backup file name. Returns $null for names outside the scheme.
function ConvertFrom-BackupName {
    param([string]$Name)
    $m = [regex]::Match($Name, $script:BackupNamePattern)
    if (-not $m.Success) { return $null }
    $kind = 'periodic'
    $launch = 0
    if ($m.Groups[4].Success) {
        $kind = 'before-restore'
    } else {
        $launch = [int]$m.Groups[2].Value
        if ($m.Groups[3].Success) { $kind = 'start' }
    }
    [pscustomobject]@{
        Seq    = [int]$m.Groups[1].Value
        Kind   = $kind
        Launch = $launch
        Warp   = $m.Groups[5].Success
        Time   = ConvertTo-BackupTime $m.Groups[6].Value
        Name   = $Name
    }
}

# All backups of the directory, oldest first (by permanent number).
function Get-SaveBackups {
    param([string]$BackupsDir)
    $backups = foreach ($file in @(Get-ChildItem -LiteralPath $BackupsDir -Filter '*.zip' -ErrorAction SilentlyContinue)) {
        $backup = ConvertFrom-BackupName $file.Name
        if ($backup) {
            $backup | Add-Member -NotePropertyName Path -NotePropertyValue $file.FullName
            $backup
        }
    }
    @($backups | Sort-Object Seq)
}

function Get-NextLaunchNumber {
    param([string]$BackupsDir)
    $max = 0
    foreach ($b in @(Get-SaveBackups $BackupsDir)) {
        if ($b.Launch -gt $max) { $max = $b.Launch }
    }
    $max + 1
}

# --- Mid-warp saves ---
#
# Elden Ring writes its save at the start of every fog-gate warp, with the
# character slot's map set to the transition placeholder m55_89_00_00 (not a
# map of the game) and the warp still pending, then again about half a
# second after the player lands. The game cannot load the in-between state:
# it stays on the loading screen forever. Such a save sits on disk for the
# whole loading screen, and forever when the game hangs there (the case the
# backups exist for), so the daemon waits for it to settle and the recovery
# menu never picks one by default (see docs/save-backup.md).
#
# ER0000.sl2 is a BND4 with the 10 character slots at 0x300 + k * 0x280010;
# in a slot, the version (uint32, 0 = empty) is at +0x10 and the map at +0x14
# as [index, region, block, area] bytes. Checked offline on 23 backups of the
# 2026-10-19 case: every clean save stores the map the player stands in.
$script:SaveSlotBase = 0x300
$script:SaveSlotStride = 0x280010
$script:SaveSlotCount = 10
$script:MidWarpMap = [byte[]](0x00, 0x00, 0x59, 0x37)

# True when any non-empty character slot of the save at $Path carries the
# mid-warp placeholder map. A file that is not a BND4 save, or too short,
# is reported as clean.
function Test-SaveMidWarp {
    param([string]$Path)
    $fs = $null
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $magic = New-Object byte[] 4
        if ($fs.Read($magic, 0, 4) -ne 4) { return $false }
        if ([System.Text.Encoding]::ASCII.GetString($magic) -ne 'BND4') { return $false }
        $buf = New-Object byte[] 8
        for ($k = 0; $k -lt $script:SaveSlotCount; $k++) {
            $pos = $script:SaveSlotBase + $k * $script:SaveSlotStride + 0x10
            if ($pos + 8 -gt $fs.Length) { return $false }
            [void]$fs.Seek($pos, [System.IO.SeekOrigin]::Begin)
            if ($fs.Read($buf, 0, 8) -ne 8) { return $false }
            if ([System.BitConverter]::ToUInt32($buf, 0) -eq 0) { continue }
            $warp = $true
            for ($i = 0; $i -lt 4; $i++) {
                if ($buf[4 + $i] -ne $script:MidWarpMap[$i]) { $warp = $false; break }
            }
            if ($warp) { return $true }
        }
        return $false
    } catch {
        return $false
    } finally {
        if ($fs) { $fs.Dispose() }
    }
}

# Zip the save file under the next permanent number. Kind: start, periodic or
# before-restore (Launch is ignored for before-restore). Returns the zip
# FileInfo with a SaveHash property (SHA256 of the save) and a Warp property
# (mid-warp save, name carrying the -warp suffix), or $null without creating
# anything when the save still hashes to UnlessHash (a game stuck on a
# loading screen does not write its save: no point in piling up copies).
# With -UnlessMidWarp, a mid-warp save is not zipped either: the result is a
# marker object with MidWarp = $true, so the caller can wait for the save to
# settle (New-SettledBackup). Throws when the copy, the hash or the
# compression fails.
function New-SaveBackup {
    param(
        [string]$BackupsDir,
        [string]$SavePath,
        [string]$Kind,
        [int]$Launch,
        [string]$UnlessHash = '',
        [switch]$UnlessMidWarp
    )
    $seq = 1
    foreach ($b in @(Get-SaveBackups $BackupsDir)) {
        if ($b.Seq -ge $seq) { $seq = $b.Seq + 1 }
    }
    switch ($Kind) {
        'start'          { $label = "launch$Launch-start" }
        'periodic'       { $label = "launch$Launch" }
        'before-restore' { $label = 'before-restore' }
        default          { throw "Unknown backup kind: $Kind" }
    }

    # Copy first so no read handle is held on the live save during the slower
    # compression. The archive entry is always ER0000.sl2 (restore extracts it
    # next to the save). -ErrorAction Stop is explicit on each cmdlet: a
    # function-level $ErrorActionPreference does not reach script-module
    # cmdlets such as Compress-Archive.
    $tempCopy = Join-Path $BackupsDir 'ER0000.sl2'
    try {
        Copy-Item -LiteralPath $SavePath -Destination $tempCopy -Force -ErrorAction Stop
        $hash = (Get-FileHash -LiteralPath $tempCopy -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($UnlessHash -and $hash -eq $UnlessHash) { return $null }
        $warp = Test-SaveMidWarp -Path $tempCopy
        if ($warp -and $UnlessMidWarp) {
            return [pscustomobject]@{ MidWarp = $true; SaveHash = $hash }
        }
        if ($warp) { $label += '-warp' }
        $ts = (Get-Date).ToString($script:BackupTimeFormat, $script:Invariant)
        $zipPath = Join-Path $BackupsDir ('{0:D4}_{1}_{2}.zip' -f $seq, $label, $ts)
        Compress-Archive -LiteralPath $tempCopy -DestinationPath $zipPath -Force -ErrorAction Stop
    } finally {
        Remove-Item -LiteralPath $tempCopy -Force -ErrorAction SilentlyContinue
    }
    $zip = Get-Item -LiteralPath $zipPath -ErrorAction Stop
    $zip | Add-Member -NotePropertyName SaveHash -NotePropertyValue $hash
    $zip | Add-Member -NotePropertyName Warp -NotePropertyValue $warp
    $zip
}

# New-SaveBackup that waits for a mid-warp save to settle: while the save is
# mid-warp, re-read it every PollSeconds for up to WaitSeconds (a loading
# screen lasts a few seconds to a minute), then zip whatever is there, tagged
# -warp when the game is still stuck. A poll copies the save again only once
# the game has written it (size or last write time changed), so a stuck game
# costs one copy, not one per poll. Returns the zip FileInfo with an extra
# WaitedSeconds property, or $null when the save is unchanged (UnlessHash) or
# ShouldStop returned true during the wait (the game exited: the next launch's
# start backup picks the file up).
function New-SettledBackup {
    param(
        [string]$BackupsDir,
        [string]$SavePath,
        [string]$Kind,
        [int]$Launch,
        [string]$UnlessHash = '',
        [int]$WaitSeconds = 45,
        [int]$PollSeconds = 5,
        [scriptblock]$ShouldStop = { $false }
    )
    $started = Get-Date
    $seenWrite = $null
    $seenLength = -1
    while ($true) {
        $elapsed = ((Get-Date) - $started).TotalSeconds
        $final = $elapsed -ge $WaitSeconds
        $save = Get-Item -LiteralPath $SavePath -ErrorAction Stop
        $written = ($null -eq $seenWrite) -or ($save.LastWriteTimeUtc -ne $seenWrite) -or ($save.Length -ne $seenLength)
        if ($written -or $final) {
            $seenWrite = $save.LastWriteTimeUtc
            $seenLength = $save.Length
            $zip = New-SaveBackup -BackupsDir $BackupsDir -SavePath $SavePath -Kind $Kind -Launch $Launch `
                -UnlessHash $UnlessHash -UnlessMidWarp:(-not $final)
            if ($null -eq $zip) { return $null }
            if ($zip -is [System.IO.FileInfo]) {
                $zip | Add-Member -NotePropertyName WaitedSeconds -NotePropertyValue ([int][math]::Round($elapsed))
                return $zip
            }
        }
        if (& $ShouldStop) { return $null }
        Start-Sleep -Seconds $PollSeconds
    }
}

# Keep the last $MaxBackups periodic backups of the current launch. Other
# launches are never touched: after a restore (or a crash loop on reload) the
# history of the launch the player is digging into must survive.
function Remove-OldBackups {
    param(
        [string]$BackupsDir,
        [int]$Launch,
        [int]$MaxBackups
    )
    $current = @(Get-SaveBackups $BackupsDir | Where-Object { $_.Kind -eq 'periodic' -and $_.Launch -eq $Launch })
    foreach ($b in @($current | Select-Object -SkipLast $MaxBackups)) {
        Remove-Item -LiteralPath $b.Path -Force -ErrorAction SilentlyContinue
    }
}

# --- backup.log: the daemon's and recovery's runs as one chronological story ---

# Append "<local time> <UTC offset> [SpeedFog <Source>] <Message>" to
# backup.log and return the line. The offset lines the file up with the racing
# mod log, which is in UTC. A log write failure never breaks a backup or a
# restore.
function Write-BackupLog {
    param(
        [string]$BackupsDir,
        [string]$Source,
        [string]$Message
    )
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz', $script:Invariant)
    $line = "$ts [SpeedFog $Source] $Message"
    Add-Content -LiteralPath (Join-Path $BackupsDir 'backup.log') -Value $line -ErrorAction SilentlyContinue
    $line
}

# --- Restore log: one line per restore, "<time>`t<restored zip>`t<undo zip>" ---

function Read-RestoreLog {
    param([string]$BackupsDir)
    $logPath = Join-Path $BackupsDir 'restores.log'
    if (-not (Test-Path -LiteralPath $logPath)) { return @() }
    $restores = foreach ($line in @(Get-Content -LiteralPath $logPath)) {
        $parts = $line -split "`t"
        if ($parts.Count -lt 2) { continue }
        try { $time = ConvertTo-BackupTime $parts[0] } catch { continue }
        $undo = ''
        if ($parts.Count -ge 3) { $undo = $parts[2] }
        [pscustomobject]@{ Time = $time; Restored = $parts[1]; Undo = $undo }
    }
    @($restores | Sort-Object Time)
}

function Add-RestoreLog {
    param(
        [string]$BackupsDir,
        [string]$Restored,
        [string]$Undo
    )
    $ts = (Get-Date).ToString($script:BackupTimeFormat, $script:Invariant)
    Add-Content -LiteralPath (Join-Path $BackupsDir 'restores.log') -Value "$ts`t$Restored`t$Undo"
}

# --- Recovery menu ---

function Get-BackupSeqText {
    param([string]$Name)
    $m = [regex]::Match($Name, '^(\d+)_')
    if ($m.Success) { return "#$([int]$m.Groups[1].Value)" }
    $Name
}

# Default selection: the most recent backup taken while playing (a
# before-restore backup is only an undo, and a mid-warp backup cannot be
# loaded: neither is the natural pick).
function Get-DefaultBackup {
    param([object[]]$Backups)
    @($Backups | Where-Object { $_.Kind -ne 'before-restore' -and -not $_.Warp }) | Select-Object -Last 1
}

# Menu lines, grouped by launch, oldest first. Each entry shows its permanent
# number, its clock time and how long before the last backup of its launch it
# was taken (not the game's exit: an unchanged save adds no backup); mid-warp
# backups, restored backups and before-restore backups are annotated.
function Format-BackupList {
    param(
        [object[]]$Backups,
        [object[]]$Restores
    )
    $width = ('#' + (($Backups | Measure-Object -Property Seq -Maximum).Maximum)).Length

    # First and last backup time of each launch.
    $bounds = @{}
    foreach ($b in $Backups) {
        if ($b.Kind -eq 'before-restore') { continue }
        if (-not $bounds.ContainsKey($b.Launch)) {
            $bounds[$b.Launch] = [pscustomobject]@{ First = $b.Time; Last = $b.Time; LastSeq = $b.Seq }
        } elseif ($b.Seq -gt $bounds[$b.Launch].LastSeq) {
            $bounds[$b.Launch].Last = $b.Time
            $bounds[$b.Launch].LastSeq = $b.Seq
        }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $group = $null
    $previousLaunchEnd = [datetime]::MinValue
    foreach ($b in $Backups) {
        if ($b.Kind -eq 'before-restore') {
            $restore = $Restores | Where-Object { $_.Undo -eq $b.Name } | Select-Object -Last 1
            $key = "restore-$($b.Seq)"
            if ($group -ne $key) {
                $lines.Add("Restore at $(Format-BackupClock $b.Time)")
                $group = $key
            }
            if ($restore) {
                $note = "your save before restoring $(Get-BackupSeqText $restore.Restored)"
            } else {
                $note = 'your save before a restore'
            }
        } else {
            $span = $bounds[$b.Launch]
            $key = "launch-$($b.Launch)"
            if ($group -ne $key) {
                $range = Format-BackupClock $span.First
                if ($span.LastSeq -ne $b.Seq) { $range += " to $(Format-BackupClock $span.Last)" }
                $header = 'Launch {0} ({1}, {2})' -f $b.Launch, $span.First.ToString('yyyy-MM-dd', $script:Invariant), $range
                $origin = $Restores | Where-Object { $_.Time -gt $previousLaunchEnd -and $_.Time -le $span.First } | Select-Object -Last 1
                if ($origin) { $header += ", started from $(Get-BackupSeqText $origin.Restored)" }
                $lines.Add($header)
                $group = $key
                $previousLaunchEnd = $span.Last
            }
            if ($b.Kind -eq 'start') {
                $note = 'start of launch'
            } elseif ($b.Seq -eq $span.LastSeq) {
                $note = 'last backup'
            } else {
                $minutes = [int][math]::Round(($span.Last - $b.Time).TotalMinutes)
                if ($minutes -lt 1) { $note = 'less than 1 min before last backup' }
                else { $note = "$minutes min before last backup" }
            }
        }
        if ($b.Warp) { $note += '   (warp in progress: the game cannot load it)' }
        $restoredAt = $Restores | Where-Object { $_.Restored -eq $b.Name } | Select-Object -Last 1
        if ($restoredAt) { $note += "   <- restored at $(Format-BackupClock $restoredAt.Time)" }
        $lines.Add(('  {0}  {1}  {2}' -f ("#$($b.Seq)").PadRight($width), (Format-BackupClock $b.Time), $note))
    }
    $lines
}
