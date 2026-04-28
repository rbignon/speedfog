"""Final package assembly for generated SpeedFog seeds."""

from __future__ import annotations

import os
import shutil
from pathlib import Path


class PackagingError(RuntimeError):
    """Raised when bootstrap-managed packaging assets are missing."""


def write_me3_config(
    output_dir: Path,
    *,
    mod_path: str = "mods/fogmod",
    item_randomizer_enabled: bool = False,
    include_crash_fix: bool = False,
) -> Path:
    """Write the ME3 profile consumed by the launch scripts."""
    output_dir.mkdir(parents=True, exist_ok=True)
    config_path = output_dir / "config_speedfog.me3"

    natives: list[str] = []
    if include_crash_fix:
        natives.append("lib/RandomizerCrashFix.dll")
    if item_randomizer_enabled:
        natives.append("lib/RandomizerHelper.dll")

    natives_block = "\n\n".join(f'[[natives]]\npath = "{path}"' for path in natives)

    packages: list[str] = []
    if item_randomizer_enabled:
        packages.extend(
            [
                "[[packages]]",
                'id = "itemrando"',
                'path = "mods/itemrando"',
                "",
            ]
        )
    packages.extend(
        [
            "[[packages]]",
            'id = "fogmod"',
            f'path = "{mod_path}"',
        ]
    )
    packages_block = "\n".join(packages)

    config_path.write_text(
        f"""# SpeedFog ME3 Profile
# Auto-generated, do not edit manually
profileVersion = "v1"

[[supports]]
game = "eldenring"

{natives_block}

{packages_block}
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
    """Assemble ME3, launchers, native DLLs, and config for a generated seed."""
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

    write_me3_config(
        seed_dir,
        item_randomizer_enabled=item_randomizer_enabled,
        include_crash_fix=(
            item_randomizer_enabled
            and (seed_dir / "lib" / "RandomizerCrashFix.dll").exists()
        ),
    )
    print("Generated config_speedfog.me3")

    print()
    print("=== SpeedFog mod ready! ===")
    print("To play:")
    print(f"  Windows: double-click {seed_dir / 'launch_speedfog.bat'}")
    print(f"  Linux:   run {seed_dir / 'linux' / 'launch_speedfog.sh'}")


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
        packaging_dir / "linux" / "launch_speedfog.sh",
        packaging_dir / "linux" / "backup_daemon.sh",
        packaging_dir / "linux" / "recovery.sh",
        packaging_dir / "me3" / "bin" / "me3",
        packaging_dir / "me3" / "bin" / "win64" / "me3.exe",
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
