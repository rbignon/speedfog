from __future__ import annotations

from pathlib import Path

import pytest

from speedfog import packaging
from speedfog.packaging import (
    PackagingError,
    copy_packaging_assets,
    package_seed,
    write_me3_config,
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
        "linux/launch_speedfog.sh",
        "linux/backup_daemon.sh",
        "linux/recovery.sh",
        "lib/RandomizerCrashFix.dll",
        "lib/RandomizerHelper.dll",
        "me3/bin/me3",
        "me3/bin/win64/me3.exe",
    ]:
        file = packaging / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(path, encoding="utf-8")


def test_write_me3_config_without_item_randomizer(tmp_path: Path) -> None:
    write_me3_config(tmp_path)

    content = (tmp_path / "me3" / "config_speedfog.me3").read_text(encoding="utf-8")
    assert 'profileVersion = "v1"' in content
    assert "[[supports]]" in content
    assert 'game = "eldenring"' in content
    assert 'path = "../mods/fogmod"' in content
    assert "[[natives]]" not in content
    assert "RandomizerCrashFix.dll" not in content
    assert "RandomizerHelper.dll" not in content
    assert "itemrando" not in content
    assert "[modengine]" not in content


def test_write_me3_config_with_item_randomizer_loads_fogmod_last(
    tmp_path: Path,
) -> None:
    write_me3_config(
        tmp_path,
        item_randomizer_enabled=True,
        include_crash_fix=True,
    )

    content = (tmp_path / "me3" / "config_speedfog.me3").read_text(encoding="utf-8")
    assert 'path = "../mods/itemrando"' in content
    assert "../lib/RandomizerCrashFix.dll" in content
    assert "../lib/RandomizerHelper.dll" in content
    assert content.index('path = "../mods/itemrando"') < content.index(
        'path = "../mods/fogmod"'
    )


def test_copy_packaging_assets_copies_tree_shape(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    _make_packaging_tree(project_root)

    copy_packaging_assets(project_root, seed_dir, item_randomizer_enabled=True)

    assert (seed_dir / "launch_speedfog.bat").exists()
    assert (seed_dir / "recovery.bat").exists()
    assert (seed_dir / "backups" / "config.ini").exists()
    assert (seed_dir / "linux" / "launch_speedfog.sh").exists()
    assert (seed_dir / "lib" / "RandomizerCrashFix.dll").exists()
    assert (seed_dir / "lib" / "RandomizerHelper.dll").exists()
    assert (seed_dir / "me3" / "bin" / "me3").exists()
    assert (seed_dir / "me3" / "bin" / "win64" / "me3.exe").exists()


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
    assert (seed_dir / "me3" / "config_speedfog.me3").exists()


def test_copy_phantom_catalog_copies_when_present(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    (project_root / "data").mkdir(parents=True)
    seed_dir.mkdir()
    catalog_src = project_root / "data" / "phantom_skins.toml"
    catalog_src.write_text("# stub catalog\n", encoding="utf-8")

    packaging.copy_phantom_catalog(project_root, seed_dir)

    catalog_dest = seed_dir / "phantom_skins.toml"
    assert catalog_dest.exists()
    assert catalog_dest.read_text(encoding="utf-8") == "# stub catalog\n"


def test_copy_phantom_catalog_skips_when_absent(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    (project_root / "data").mkdir(parents=True)
    seed_dir.mkdir()

    # No catalog source. Should not raise; should not create dest.
    packaging.copy_phantom_catalog(project_root, seed_dir)

    assert not (seed_dir / "phantom_skins.toml").exists()
