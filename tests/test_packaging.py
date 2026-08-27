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
        "backups/resolve_game_path.ps1",
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
    assert "external_dlls = []" in content
    assert 'name = "speedfog"' not in content


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


def test_write_modengine_config_with_static_mod_lists_speedfog_first(
    tmp_path: Path,
) -> None:
    write_modengine_config(
        tmp_path,
        item_randomizer_enabled=True,
        static_mod_enabled=True,
    )

    content = (tmp_path / "modengine2" / "config_speedfog.toml").read_text(
        encoding="utf-8"
    )
    assert 'path = "../mods/speedfog"' in content
    # ModEngine 2: first mod wins. Static files beat fogmod, fogmod beats
    # itemrando.
    assert content.index('name = "speedfog"') < content.index('name = "fogmod"')
    assert content.index('name = "fogmod"') < content.index('name = "itemrando"')


def test_copy_packaging_assets_copies_tree_shape(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    _make_packaging_tree(project_root)

    copy_packaging_assets(project_root, seed_dir, item_randomizer_enabled=True)

    assert (seed_dir / "launch_speedfog.bat").exists()
    assert (seed_dir / "recovery.bat").exists()
    assert (seed_dir / "backups" / "config.ini").exists()
    assert (seed_dir / "backups" / "resolve_game_path.ps1").exists()
    assert (seed_dir / "lib" / "RandomizerCrashFix.dll").exists()
    assert (seed_dir / "lib" / "RandomizerHelper.dll").exists()
    assert (seed_dir / "modengine2" / "modengine2_launcher.exe").exists()
    assert (seed_dir / "modengine2" / "modengine2" / "bin" / "modengine2.dll").exists()


def test_copy_packaging_assets_reports_missing_bootstrap_assets(
    tmp_path: Path,
) -> None:
    with pytest.raises(PackagingError, match="Run tools/bootstrap.py"):
        copy_packaging_assets(tmp_path / "project", tmp_path / "seed")


def test_copy_packaging_assets_requires_game_path_resolver(
    tmp_path: Path,
) -> None:
    # Without the resolver the launcher silently falls back to the Steam
    # install, so a missing script must fail packaging, not the player.
    project_root = tmp_path / "project"
    _make_packaging_tree(project_root)
    (project_root / "data" / "packaging" / "backups" / "resolve_game_path.ps1").unlink()

    with pytest.raises(PackagingError, match="resolve_game_path.ps1"):
        copy_packaging_assets(project_root, tmp_path / "seed")


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


def test_package_seed_copies_static_mod_and_registers_it(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    _make_packaging_tree(project_root)
    static_file = (
        project_root / "data" / "mods" / "speedfog" / "chr" / "c0000.anibnd.dcx"
    )
    static_file.parent.mkdir(parents=True)
    static_file.write_text("patched", encoding="utf-8")

    package_seed(project_root, seed_dir)

    assert (seed_dir / "mods" / "speedfog" / "chr" / "c0000.anibnd.dcx").read_text(
        encoding="utf-8"
    ) == "patched"
    content = (seed_dir / "modengine2" / "config_speedfog.toml").read_text(
        encoding="utf-8"
    )
    assert content.index('name = "speedfog"') < content.index('name = "fogmod"')


def test_package_seed_without_static_mod_omits_the_entry(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    _make_packaging_tree(project_root)

    package_seed(project_root, seed_dir)

    assert not (seed_dir / "mods" / "speedfog").exists()
    content = (seed_dir / "modengine2" / "config_speedfog.toml").read_text(
        encoding="utf-8"
    )
    assert 'name = "speedfog"' not in content


def test_package_seed_with_empty_static_mod_omits_the_entry(tmp_path: Path) -> None:
    project_root = tmp_path / "project"
    seed_dir = tmp_path / "seed"
    _make_packaging_tree(project_root)
    (project_root / "data" / "mods" / "speedfog").mkdir(parents=True)

    package_seed(project_root, seed_dir)

    assert not (seed_dir / "mods" / "speedfog").exists()
    content = (seed_dir / "modengine2" / "config_speedfog.toml").read_text(
        encoding="utf-8"
    )
    assert 'name = "speedfog"' not in content
