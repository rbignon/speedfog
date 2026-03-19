#!/bin/bash
# SpeedFog Save Backup Daemon
# Launched by launch_speedfog.sh with the save file path as an argument.

SAVE_PATH="$1"
if [ -z "$SAVE_PATH" ]; then
    echo "Usage: $0 <save_path>"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUPS_DIR="$SCRIPT_DIR/../backups"

# --- Logging ---
log() {
    local line="[SpeedFog Backup] $1"
    echo "$line"
    echo "$line" >> "$BACKUPS_DIR/backup.log"
}

# --- Parse config.ini ---
interval=1
max_backups=10
config_path="$BACKUPS_DIR/config.ini"
if [ -f "$config_path" ]; then
    _parsed=$(grep -v '^\s*#' "$config_path" | grep -v '^\s*$')
    _interval=$(echo "$_parsed" | grep '^interval=' | cut -d= -f2 | tr -d '[:space:]')
    _max_backups=$(echo "$_parsed" | grep '^max_backups=' | cut -d= -f2 | tr -d '[:space:]')
    if [ -n "$_interval" ]; then interval="$_interval"; fi
    if [ -n "$_max_backups" ]; then max_backups="$_max_backups"; fi
fi

log "Save file: $SAVE_PATH"

# --- Wait for Elden Ring to start ---
log "Waiting for Elden Ring to start..."
waited=0
found=0
while [ $waited -lt 60 ]; do
    if pgrep -x eldenring.exe > /dev/null 2>&1; then
        found=1
        break
    fi
    sleep 5
    waited=$((waited + 1))
done
if [ $found -eq 0 ]; then
    log "ERROR: Timed out waiting for Elden Ring to start. Exiting."
    exit 1
fi
log "Game detected."

# --- Pre-run backup ---
if [ -f "$SAVE_PATH" ]; then
    ts=$(date +%Y-%m-%d_%H.%M.%S)
    pre_run_zip="$BACKUPS_DIR/pre-run_$ts.zip"
    if ! zip -j "$pre_run_zip" "$SAVE_PATH"; then
        log "WARNING: Failed to create pre-run backup."
    else
        log "Pre-run backup: pre-run_$ts.zip"
    fi
fi

log "Backup daemon started (interval: $interval min, keep: $max_backups)"

# --- Backup loop ---
backup_count=0
while true; do
    sleep $((interval * 60))

    if ! pgrep -x eldenring.exe > /dev/null 2>&1; then
        log "Game exited. Daemon stopping. ($backup_count backups created)"
        exit 0
    fi

    if [ ! -f "$SAVE_PATH" ]; then
        log "WARNING: Save file not found, skipping backup."
        continue
    fi

    ts=$(date +%Y-%m-%d_%H.%M.%S)
    zip_name="ER0000_$ts.zip"
    zip_path="$BACKUPS_DIR/$zip_name"

    if ! zip -j "$zip_path" "$SAVE_PATH"; then
        log "WARNING: Failed to create backup: $zip_name"
        continue
    fi

    backup_count=$((backup_count + 1))

    zip_size=$(stat -c%s "$zip_path")
    if [ "$zip_size" -lt 102400 ]; then
        zip_size_kb=$((zip_size / 1024))
        log "WARNING: $zip_name is only ${zip_size_kb} KB (expected ~3 MB)"
    else
        zip_size_mb=$(awk "BEGIN {printf \"%.1f\", $zip_size / 1048576}")
        log "Backup: $zip_name ($zip_size_mb MB)"
    fi

    # Purge old backups beyond max_backups
    ls -1 "$BACKUPS_DIR"/ER0000_*.zip 2>/dev/null | sort | head -n -"$max_backups" | xargs -r rm -f
done
