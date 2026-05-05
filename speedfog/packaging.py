"""Final package assembly for generated SpeedFog seeds."""

from __future__ import annotations

import os
import shutil
from pathlib import Path


class PackagingError(RuntimeError):
    """Raised when bootstrap-managed packaging assets are missing."""


def write_modengine_config(
    output_dir: Path,
    *,
    item_randomizer_enabled: bool = False,
    include_crash_fix: bool = False,
) -> Path:
    """Write the ModEngine 2 TOML config consumed by the launcher.

    The config lives next to the ModEngine 2 binaries to avoid polluting the
    seed root, so paths are expressed relative to the modengine2/ directory.
    """
    config_dir = output_dir / "modengine2"
    config_dir.mkdir(parents=True, exist_ok=True)
    config_path = config_dir / "config_speedfog.toml"

    # Backslashes are doubled so that the TOML parser yields "..\lib\X.dll"
    # before the value reaches ModEngine 2. The resulting path is resolved
    # against the config file's parent directory by the loader, so the .dll
    # next to the seed at <seed>/lib/ is reachable from <seed>/modengine2/.
    external_dlls: list[str] = []
    if include_crash_fix:
        external_dlls.append(r"..\\lib\\RandomizerCrashFix.dll")
    if item_randomizer_enabled:
        external_dlls.append(r"..\\lib\\RandomizerHelper.dll")

    if external_dlls:
        dlls_inner = ",\n    ".join(f'"{path}"' for path in external_dlls)
        dlls_block = f"external_dlls = [\n    {dlls_inner},\n]"
    else:
        dlls_block = "external_dlls = []"

    # ModEngine 2 loads mods in declaration order; first wins. Keep fogmod
    # ahead of itemrando so fog gate edits override the randomizer.
    mods_lines: list[str] = [
        '    { enabled = true, name = "fogmod", path = "../mods/fogmod" }'
    ]
    if item_randomizer_enabled:
        mods_lines.append(
            '    { enabled = true, name = "itemrando", path = "../mods/itemrando" }'
        )
    mods_block = ",\n".join(mods_lines)

    config_path.write_text(
        f"""# SpeedFog ModEngine 2 Configuration
# Auto-generated, do not edit manually

[modengine]
debug = false
{dlls_block}

[extension.mod_loader]
enabled = true
loose_params = false
mods = [
{mods_block}
]
""",
        encoding="utf-8",
    )
    return config_path


def copy_packaging_assets(
    project_root: Path,
    output_dir: Path,
    *,
    item_randomizer_enabled: bool = False,
) -> None:
    """Copy the bootstrap-managed packaging tree into a seed directory."""
    packaging_dir = project_root / "data" / "packaging"
    _validate_packaging_assets(packaging_dir, item_randomizer_enabled)

    for src in packaging_dir.iterdir():
        dest = output_dir / src.name
        if src.is_dir():
            shutil.copytree(src, dest, dirs_exist_ok=True)
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)

    _make_linux_scripts_executable(output_dir / "linux")


def package_seed(
    project_root: Path,
    seed_dir: Path,
    *,
    item_randomizer_enabled: bool = False,
    item_randomizer_dir: Path | None = None,
) -> None:
    """Assemble ModEngine 2, launcher, native DLLs, and config for a seed."""
    print()
    print("=== Packaging SpeedFog Mod ===")

    copy_packaging_assets(
        project_root,
        seed_dir,
        item_randomizer_enabled=item_randomizer_enabled,
    )
    print("Copied packaging assets from data/packaging/")

    if item_randomizer_enabled and item_randomizer_dir is not None:
        helper_config = item_randomizer_dir / "RandomizerHelper_config.ini"
        if helper_config.exists():
            lib_dir = seed_dir / "lib"
            lib_dir.mkdir(parents=True, exist_ok=True)
            shutil.copy2(helper_config, lib_dir / "RandomizerHelper_config.ini")
            print("Copied RandomizerHelper_config.ini to lib/")

    write_modengine_config(
        seed_dir,
        item_randomizer_enabled=item_randomizer_enabled,
        include_crash_fix=(
            item_randomizer_enabled
            and (seed_dir / "lib" / "RandomizerCrashFix.dll").exists()
        ),
    )
    print("Generated modengine2/config_speedfog.toml")

    print()
    print("=== SpeedFog mod ready! ===")
    print(f"To play: double-click {seed_dir / 'launch_speedfog.bat'}")


def _validate_packaging_assets(
    packaging_dir: Path,
    item_randomizer_enabled: bool,
) -> None:
    required = [
        packaging_dir / "launch_speedfog.bat",
        packaging_dir / "recovery.bat",
        packaging_dir / "backups" / "config.ini",
        packaging_dir / "backups" / "launch_helper.ps1",
        packaging_dir / "backups" / "backup_daemon.ps1",
        packaging_dir / "backups" / "recovery.ps1",
        packaging_dir / "modengine2" / "modengine2_launcher.exe",
    ]
    if item_randomizer_enabled:
        required.append(packaging_dir / "lib" / "RandomizerCrashFix.dll")
        required.append(packaging_dir / "lib" / "RandomizerHelper.dll")

    missing = [path for path in required if not path.exists()]
    if missing:
        rel = "\n".join(
            f"  - {path.relative_to(packaging_dir.parent)}" for path in missing
        )
        raise PackagingError(
            "Packaging assets are missing. Run tools/bootstrap.py before speedfog.\n"
            f"{rel}"
        )


def _make_linux_scripts_executable(linux_dir: Path) -> None:
    if os.name == "nt" or not linux_dir.is_dir():
        return
    exec_mode = 0o755
    for script in linux_dir.glob("*.sh"):
        script.chmod(exec_mode)
