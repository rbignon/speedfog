#!/bin/bash
# SpeedFog Launcher for Elden Ring (Linux/Proton)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/.."

# --- Parse backups/config.ini ---
enabled=true
SAVE_PATH=""
config_path="$OUTPUT_DIR/backups/config.ini"
if [ -f "$config_path" ]; then
    _parsed=$(grep -v '^\s*#' "$config_path" | grep -v '^\s*$')
    _enabled=$(echo "$_parsed" | grep '^enabled=' | cut -d= -f2 | tr -d '[:space:]')
    _save_path=$(echo "$_parsed" | grep '^save_path=' | cut -d= -f2- | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    if [ -n "$_enabled" ]; then enabled="$_enabled"; fi
    if [ -n "$_save_path" ]; then SAVE_PATH="$_save_path"; fi
fi

# --- Save detection and backup daemon ---
if [ "$enabled" != "false" ]; then
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
        mapfile -t _candidates < <(ls "$PROTON_APPDATA"/*/ER0000.sl2 2>/dev/null)
        _count=${#_candidates[@]}
        if [ "$_count" -eq 0 ]; then
            echo "WARNING: Could not auto-detect Elden Ring save file."
            echo "To enable backups, set save_path in backups/config.ini"
        elif [ "$_count" -eq 1 ]; then
            SAVE_PATH="${_candidates[0]}"
        else
            # Sort by modification time (oldest first)
            mapfile -t _candidates < <(ls -1t "${_candidates[@]}" | tac)
            _count=${#_candidates[@]}
            echo "Multiple Elden Ring save files found:"
            for _i in "${!_candidates[@]}"; do
                _mod=$(date -r "${_candidates[$_i]}" "+%Y-%m-%d %H:%M" 2>/dev/null)
                _label=""
                if [ "$_i" -eq $((_count - 1)) ]; then _label=" (most recent)"; fi
                echo "  [$((_i + 1))] ${_candidates[$_i]}  [$_mod]$_label"
            done
            _default="$_count"
            read -r -p "Select save file [$_default]: " _sel
            if [ -z "$_sel" ]; then _sel="$_default"; fi
            _idx=$((_sel - 1))
            if ! [[ "$_sel" =~ ^[0-9]+$ ]] || [ "$_idx" -lt 0 ] || [ "$_idx" -ge "$_count" ]; then
                echo "Invalid selection. Skipping backups."
            else
                SAVE_PATH="${_candidates[$_idx]}"
            fi
        fi
    fi

    if [ -n "$SAVE_PATH" ]; then
        bash "$SCRIPT_DIR/backup_daemon.sh" "$SAVE_PATH" &
    fi
fi

# --- Launch ModEngine ---
wine "$OUTPUT_DIR/ModEngine/modengine2_launcher.exe" -t er -c "$OUTPUT_DIR/config_speedfog.toml"
