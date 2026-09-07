"""Final package assembly for generated SpeedFog seeds."""

from __future__ import annotations

import shutil
from pathlib import Path


class PackagingError(RuntimeError):
    """Raised when bootstrap-managed packaging assets are missing."""


STATIC_MOD_SCRIPT_SUFFIX = "-luabnd-dcx"


def stale_static_mod_scripts(project_root: Path) -> list[str]:
    """Built static mod scripts that are missing or older than their source.

    tools/bootstrap.py repacks every WitchyBND-unpacked directory
    data/mods-src/speedfog/script/<name>-luabnd-dcx/ (the one with a
    _witchy-bnd4.xml manifest) into data/mods/speedfog/script/<name>.luabnd.dcx,
    and a seed ships the built file: a script edited after the last
    bootstrap would run stale in game with nothing to show for it. One
    message per such script, relative to project_root. Nothing to compare
    when data/mods/speedfog/ does not exist (bootstrap never run:
    package_seed notes that case and builds the seed without it) or when
    there is no source to repack.
    """
    static_mod = project_root / "data" / "mods" / "speedfog"
    script_src = project_root / "data" / "mods-src" / "speedfog" / "script"
    if not static_mod.is_dir() or not script_src.is_dir():
        return []
    problems: list[str] = []
    for source in sorted(script_src.iterdir()):
        if (
            not source.is_dir()
            or not source.name.endswith(STATIC_MOD_SCRIPT_SUFFIX)
            or not (source / "_witchy-bnd4.xml").is_file()
        ):
            continue
        name = source.name[: -len(STATIC_MOD_SCRIPT_SUFFIX)] + ".luabnd.dcx"
        built = static_mod / "script" / name
        source_mtime = max(
            path.stat().st_mtime for path in source.rglob("*") if path.is_file()
        )
        rel_built = built.relative_to(project_root).as_posix()
        rel_source = source.relative_to(project_root).as_posix()
        if not built.is_file():
            problems.append(f"{rel_built} not built from {rel_source}/")
        elif built.stat().st_mtime < source_mtime:
            problems.append(f"{rel_built} is older than its source {rel_source}/")
    return problems


def write_modengine_config(
    output_dir: Path,
    *,
    item_randomizer_enabled: bool = False,
    include_crash_fix: bool = False,
    static_mod_enabled: bool = False,
    halloween_mod_enabled: bool = False,
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

    # ModEngine 2 loads mods in declaration order; first wins. The static
    # speedfog mod goes first so its files beat per-seed output, and fogmod
    # stays ahead of itemrando so fog gate edits override the randomizer.
    mods_lines: list[str] = []
    if static_mod_enabled:
        mods_lines.append(
            '    { enabled = true, name = "speedfog", path = "../mods/speedfog" }'
        )
    if halloween_mod_enabled:
        mods_lines.append(
            '    { enabled = true, name = "speedfog-halloween",'
            ' path = "../mods/speedfog-halloween" }'
        )
    mods_lines.append(
        '    { enabled = true, name = "fogmod", path = "../mods/fogmod" }'
    )
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


def _copy_optional_mod(
    project_root: Path, seed_dir: Path, name: str, label: str
) -> bool:
    """Copy data/mods/<name>/ into the seed if it exists and is non-empty.

    Shared by the speedfog static mod and speedfog-halloween overlay blocks
    in package_seed: both must skip registering an empty/missing mod
    directory with ModEngine 2. Returns whether the mod was found and
    copied.
    """
    mod_dir = project_root / "data" / "mods" / name
    enabled = mod_dir.is_dir() and any(f.is_file() for f in mod_dir.rglob("*"))
    if enabled:
        shutil.copytree(mod_dir, seed_dir / "mods" / name, dirs_exist_ok=True)
        print(f"Copied {label} from data/mods/{name}/")
    return enabled


def package_seed(
    project_root: Path,
    seed_dir: Path,
    *,
    item_randomizer_enabled: bool = False,
    item_randomizer_dir: Path | None = None,
    halloween_enabled: bool = False,
) -> None:
    """Assemble the static mod, ModEngine 2, launcher, DLLs, and config."""
    print()
    print("=== Packaging SpeedFog Mod ===")

    copy_packaging_assets(
        project_root,
        seed_dir,
        item_randomizer_enabled=item_randomizer_enabled,
    )
    print("Copied packaging assets from data/packaging/")

    static_mod_enabled = _copy_optional_mod(
        project_root, seed_dir, "speedfog", "static mod"
    )
    if not static_mod_enabled:
        print(
            "Note: data/mods/speedfog/ not found (bootstrap not run or skipped),"
            " building seed without static patches"
        )

    # The halloween overlay is only shipped when the plugin is enabled AND
    # the overlay was actually built at bootstrap (mirrors static_mod_enabled
    # above: a disabled plugin, or a bootstrap run without it, must not
    # register an empty/missing mod directory with ModEngine 2).
    halloween_mod_enabled = halloween_enabled and _copy_optional_mod(
        project_root, seed_dir, "speedfog-halloween", "halloween overlay"
    )

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
        static_mod_enabled=static_mod_enabled,
        halloween_mod_enabled=halloween_mod_enabled,
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
        packaging_dir / "backups" / "resolve_game_path.ps1",
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
