#!/bin/bash
# SpeedFog Save Recovery

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUPS_DIR="$SCRIPT_DIR/../backups"

# --- Parse config.ini for save_path ---
SAVE_PATH=""
config_path="$BACKUPS_DIR/config.ini"
if [ -f "$config_path" ]; then
    _parsed=$(grep -v '^\s*#' "$config_path" | grep -v '^\s*$')
    _save_path=$(echo "$_parsed" | grep '^save_path=' | cut -d= -f2- | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    if [ -n "$_save_path" ]; then SAVE_PATH="$_save_path"; fi
fi

# --- Auto-detect save file if not configured ---
PROTON_APPDATA="$HOME/.local/share/Steam/steamapps/compatdata/1245620/pfx/drive_c/users/steamuser/AppData/Roaming/EldenRing"

# Try to detect active Steam user from loginusers.vdf
if [ -z "$SAVE_PATH" ]; then
    for _vdf in "$HOME/.steam/debian-installation/config/loginusers.vdf" \
                 "$HOME/.steam/steam/config/loginusers.vdf" \
                 "$HOME/.local/share/Steam/config/loginusers.vdf"; do
        if [ -f "$_vdf" ]; then
            _steam_id=""
            _last_uid=""
            while IFS= read -r _line; do
                if [[ "$_line" =~ ^[[:space:]]*\"([0-9]{10,})\" ]]; then
                    _last_uid="${BASH_REMATCH[1]}"
                fi
                if [[ "$_line" == *MostRecent*\"1\"* ]] && [ -n "$_last_uid" ]; then
                    _steam_id="$_last_uid"
                    break
                fi
            done < "$_vdf"
            if [ -n "$_steam_id" ]; then
                _steam_save="$PROTON_APPDATA/$_steam_id/ER0000.sl2"
                if [ -f "$_steam_save" ]; then
                    SAVE_PATH="$_steam_save"
                fi
            fi
            break
        fi
    done
fi

# Fallback: scan for save files
if [ -z "$SAVE_PATH" ]; then
    mapfile -t candidates < <(ls "$PROTON_APPDATA"/*/ER0000.sl2 2>/dev/null)
    count=${#candidates[@]}
    if [ "$count" -eq 0 ]; then
        echo "ERROR: Could not find ER0000.sl2. Please set save_path in backups/config.ini."
        read -r -p "Press Enter to exit"
        exit 1
    elif [ "$count" -eq 1 ]; then
        SAVE_PATH="${candidates[0]}"
    else
        # Sort by modification time (oldest first)
        mapfile -t candidates < <(ls -1t "${candidates[@]}" | tac)
        count=${#candidates[@]}
        echo "Multiple save files found. Select one:"
        for i in "${!candidates[@]}"; do
            _mod=$(date -r "${candidates[$i]}" "+%Y-%m-%d %H:%M" 2>/dev/null)
            _label=""
            if [ "$i" -eq $((count - 1)) ]; then _label=" (most recent)"; fi
            echo "  [$((i + 1))] ${candidates[$i]}  [$_mod]$_label"
        done
        _default="$count"
        read -r -p "Select save file [$_default]: " sel
        if [ -z "$sel" ]; then sel="$_default"; fi
        idx=$((sel - 1))
        if ! [[ "$sel" =~ ^[0-9]+$ ]] || [ "$idx" -lt 0 ] || [ "$idx" -ge "$count" ]; then
            echo "Invalid selection."
            read -r -p "Press Enter to exit"
            exit 1
        fi
        SAVE_PATH="${candidates[$idx]}"
    fi
fi

# --- Header ---
echo "SpeedFog Save Recovery"
echo "======================"
echo ""
echo "Save file: $SAVE_PATH"
echo ""

# --- Warn if game is running ---
if pgrep -x eldenring.exe > /dev/null 2>&1; then
    read -r -p "Warning: Elden Ring appears to be running. Restoring while the game is running may not work. Continue? (y/n) " confirm_run
    if [ "$confirm_run" != "y" ]; then exit 0; fi
fi

# --- List available backups ---
mapfile -t zips < <(ls -1t "$BACKUPS_DIR"/*.zip 2>/dev/null | tac)
if [ ${#zips[@]} -eq 0 ]; then
    echo "No backups found."
    read -r -p "Press Enter to exit"
    exit 0
fi

echo "Available backups (newest last):"
echo ""
zip_count=${#zips[@]}
for i in "${!zips[@]}"; do
    num=$(( i + 1 ))
    name=$(basename "${zips[$i]}")
    annotation=""
    if [[ "$name" == pre-run_* ]]; then annotation="  (Pre-run backup)"; fi
    if [ "$i" -eq $((zip_count - 1)) ]; then
        if [ -n "$annotation" ]; then annotation="$annotation (most recent)"
        else annotation="  (most recent)"; fi
    fi
    echo "  [$num] $name$annotation"
done
echo ""

# --- Prompt for selection ---
default_sel="$zip_count"
read -r -p "Select backup to restore [$default_sel]: " sel_raw
if [ -z "$sel_raw" ]; then sel_raw="$default_sel"; fi
if ! [[ "$sel_raw" =~ ^[0-9]+$ ]] || [ "$sel_raw" -lt 1 ] || [ "$sel_raw" -gt "$zip_count" ]; then
    echo "Invalid selection."
    read -r -p "Press Enter to exit"
    exit 1
fi
sel_idx=$(( sel_raw - 1 ))
zip_path="${zips[$sel_idx]}"
zip_name=$(basename "$zip_path")

# --- Confirm ---
read -r -p "Restore $zip_name? (y/n) [y]: " confirm_restore
if [ -z "$confirm_restore" ]; then confirm_restore=y; fi
if [ "$confirm_restore" != "y" ]; then
    echo "Cancelled."
    read -r -p "Press Enter to exit"
    exit 0
fi
echo ""

# --- Restore ---
save_dir="$(dirname "$SAVE_PATH")"
if unzip -o -j "$zip_path" -d "$save_dir"; then
    echo "Restored successfully."
    echo "You can relaunch the game with linux/launch_speedfog.sh."
else
    echo "ERROR: Failed to restore backup."
fi

echo ""
read -r -p "Press Enter to exit"
