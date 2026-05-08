from __future__ import annotations

from pathlib import Path

import pytest

from speedfog.packaging import (
    PackagingError,
    copy_packaging_assets,
    package_seed,
    write_modengine_config,
)


def _make_packaging_tree(root: Path) -> None:
    packaging = root / "data" / "packaging"
    for path in [
        "launch_speedfog.bat",
        "recovery.bat",
        "backups/config.ini",
        "backups/launch_helper.ps1",
        "backups/backup_daemon.ps1",
        "backups/recovery.ps1",
        "lib/RandomizerCrashFix.dll",
        "lib/RandomizerHelper.dll",
        "modengine2/modengine2_launcher.exe",
        "modengine2/modengine2/bin/modengine2.dll",
    ]:
        file = packaging / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(path, encoding="utf-8")


def test_write_modengine_config_without_item_randomizer(tmp_path: Path) -> None:
    write_modengine_config(tmp_path)

    content = (tmp_path / "modengine2" / "config_speedfog.toml").read_text(
        encoding="utf-8"
    )
    assert "[modengine]" in content
    assert "debug = false" in content
    assert "[extension.mod_loader]" in content
    assert "loose_params = false" in content
    assert 'name = "fogmod"' in content
    assert 'path = "../mods/fogmod"' in content
    assert "RandomizerCrashFix.dll" not in content
    assert "RandomizerHelper.dll" not in content
    assert "itemrando" not in content


def test_write_modengine_config_with_item_randomizer_loads_fogmod_first(
    tmp_path: Path,
) -> None:
    write_modengine_config(
        tmp_path,
        item_randomizer_enabled=True,
        include_crash_fix=True,
    )

    content = (tmp_path / "modengine2" / "config_speedfog.toml").read_text(
        encoding="utf-8"
    )
    assert 'name = "itemrando"' in content
    assert 'path = "../mods/itemrando"' in content
    assert r"..\\lib\\RandomizerCrashFix.dll" in content
    assert r"..\\lib\\RandomizerHelper.dll" in content
    # ModEngine 2: first mod wins, fogmod must be listed before itemrando.
    assert content.index('name = "fogmod"') < content.index('name = "itemrando"')


def test_copy_packaging_assets_copies_tree_shape(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    _make_packaging_tree(project_root)

    copy_packaging_assets(project_root, seed_dir, item_randomizer_enabled=True)

    assert (seed_dir / "launch_speedfog.bat").exists()
    assert (seed_dir / "recovery.bat").exists()
    assert (seed_dir / "backups" / "config.ini").exists()
    assert (seed_dir / "lib" / "RandomizerCrashFix.dll").exists()
    assert (seed_dir / "lib" / "RandomizerHelper.dll").exists()
    assert (seed_dir / "modengine2" / "modengine2_launcher.exe").exists()
    assert (seed_dir / "modengine2" / "modengine2" / "bin" / "modengine2.dll").exists()


def test_copy_packaging_assets_reports_missing_bootstrap_assets(
    tmp_path: Path,
) -> None:
    with pytest.raises(PackagingError, match="Run tools/bootstrap.py"):
        copy_packaging_assets(tmp_path / "project", tmp_path / "seed")


def test_package_seed_copies_randomizer_helper_config(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    itemrando_dir = seed_dir / "mods" / "itemrando"
    itemrando_dir.mkdir(parents=True)
    (itemrando_dir / "RandomizerHelper_config.ini").write_text(
        "autoUpgrade=true\n",
        encoding="utf-8",
    )
    _make_packaging_tree(project_root)

    package_seed(
        project_root,
        seed_dir,
        item_randomizer_enabled=True,
        item_randomizer_dir=itemrando_dir,
    )

    assert (seed_dir / "lib" / "RandomizerHelper_config.ini").read_text(
        encoding="utf-8"
    ) == "autoUpgrade=true\n"
    assert (seed_dir / "modengine2" / "config_speedfog.toml").exists()
