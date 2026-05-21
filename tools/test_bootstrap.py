from __future__ import annotations

from pathlib import Path
from unittest.mock import patch

import bootstrap
import pytest
from bootstrap import (
    _overlay_script_sources,
    build_overlay_scripts,
    install_modengine_runtime,
    select_witchybnd_asset,
)


def _make_modengine_release(root: Path) -> Path:
    """Build a fake ModEngine 2 extracted-archive layout under root."""
    staging = root / "ModEngine-9.9.9.0-win64"
    (staging / "modengine2" / "bin").mkdir(parents=True)
    (staging / "modengine2" / "crashpad").mkdir(parents=True)
    (staging / "modengine2" / "tools" / "scyllahide").mkdir(parents=True)
    (staging / "modengine2" / "include" / "spdlog").mkdir(parents=True)
    (staging / "modengine2" / "assets" / "debug_menu").mkdir(parents=True)
    (staging / "modengine2" / "lib").mkdir(parents=True)
    (staging / "modengine2" / "share" / "cmake").mkdir(parents=True)

    (staging / "modengine2_launcher.exe").write_bytes(b"launcher")
    (staging / "launchmod_eldenring.bat").write_text("@echo off\n")
    (staging / "launchmod_armoredcore6.bat").write_text("@echo off\n")
    (staging / "config_eldenring.toml").write_text("[modengine]\n")
    (staging / "README.txt").write_text("hello\n")

    (staging / "modengine2" / "bin" / "modengine2.dll").write_bytes(b"dll")
    (staging / "modengine2" / "bin" / "lua.dll").write_bytes(b"dll")
    (staging / "modengine2" / "crashpad" / "crashpad_handler.exe").write_bytes(b"exe")
    (staging / "modengine2" / "tools" / "scyllahide" / "scylla_hide.ini").write_text(
        "[settings]\n"
    )
    (staging / "modengine2" / "include" / "spdlog" / "spdlog.h").write_text("#pragma\n")
    (staging / "modengine2" / "assets" / "debug_menu" / "data.bin").write_bytes(b"x")
    (staging / "modengine2" / "lib" / "modengine2.lib").write_bytes(b"x")
    (staging / "modengine2" / "share" / "cmake" / "modengine2.cmake").write_text("x\n")

    return staging


def test_install_modengine_runtime_keeps_runtime_essentials(tmp_path: Path) -> None:
    staging = _make_modengine_release(tmp_path)
    dest = tmp_path / "modengine2"

    install_modengine_runtime(staging, dest)

    assert (dest / "modengine2_launcher.exe").exists()
    assert (dest / "modengine2" / "bin" / "modengine2.dll").exists()
    assert (dest / "modengine2" / "bin" / "lua.dll").exists()
    assert (dest / "modengine2" / "crashpad" / "crashpad_handler.exe").exists()
    assert (dest / "modengine2" / "tools" / "scyllahide" / "scylla_hide.ini").exists()


def test_install_modengine_runtime_drops_other_game_files(tmp_path: Path) -> None:
    staging = _make_modengine_release(tmp_path)
    dest = tmp_path / "modengine2"

    install_modengine_runtime(staging, dest)

    assert not (dest / "launchmod_eldenring.bat").exists()
    assert not (dest / "launchmod_armoredcore6.bat").exists()
    assert not (dest / "config_eldenring.toml").exists()
    assert not (dest / "README.txt").exists()


def test_install_modengine_runtime_drops_dev_subdirs(tmp_path: Path) -> None:
    staging = _make_modengine_release(tmp_path)
    dest = tmp_path / "modengine2"

    install_modengine_runtime(staging, dest)

    assert not (dest / "modengine2" / "include").exists()
    assert not (dest / "modengine2" / "assets").exists()
    assert not (dest / "modengine2" / "lib").exists()
    assert not (dest / "modengine2" / "share").exists()


def test_install_modengine_runtime_replaces_existing_dest(tmp_path: Path) -> None:
    staging = _make_modengine_release(tmp_path)
    dest = tmp_path / "modengine2"
    dest.mkdir()
    (dest / "stale.txt").write_text("old\n")

    install_modengine_runtime(staging, dest)

    assert not (dest / "stale.txt").exists()
    assert (dest / "modengine2_launcher.exe").exists()


def test_install_modengine_runtime_errors_when_runtime_missing(tmp_path: Path) -> None:
    staging = tmp_path / "broken"
    staging.mkdir()
    (staging / "modengine2_launcher.exe").write_bytes(b"x")

    with pytest.raises(FileNotFoundError, match="modengine2/ subdirectory"):
        install_modengine_runtime(staging, tmp_path / "out")


def test_select_witchybnd_asset_picks_win_x64() -> None:
    release = {
        "assets": [
            {
                "name": "WitchyBND-v3.0.0.1-linux-x64.zip",
                "browser_download_url": "linux",
            },
            {"name": "WitchyBND-v3.0.0.1-win-x64.zip", "browser_download_url": "win"},
        ]
    }
    asset = select_witchybnd_asset(release)
    assert asset is not None
    assert asset["browser_download_url"] == "win"


def test_select_witchybnd_asset_returns_none_when_missing() -> None:
    release = {
        "assets": [
            {
                "name": "WitchyBND-v3.0.0.1-linux-x64.zip",
                "browser_download_url": "linux",
            },
        ]
    }
    assert select_witchybnd_asset(release) is None


def test_select_witchybnd_asset_picks_first_when_multiple(
    capsys: pytest.CaptureFixture[str],
) -> None:
    release = {
        "assets": [
            {"name": "WitchyBND-v3.0.0.1-win-x64.zip", "browser_download_url": "a"},
            {"name": "WitchyBND-v3.0.0.2-win-x64.zip", "browser_download_url": "b"},
        ]
    }
    asset = select_witchybnd_asset(release)
    assert asset is not None
    assert asset["browser_download_url"] == "a"
    captured = capsys.readouterr()
    assert "Multiple" in captured.out


def test_overlay_script_sources_skips_when_no_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(bootstrap, "OVERLAY_SRC_DEST", tmp_path / "overlay-src")
    assert _overlay_script_sources() == []


def test_overlay_script_sources_filters_by_suffix_and_manifest(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    overlay_src = tmp_path / "overlay-src"
    script_dir = overlay_src / "script"
    script_dir.mkdir(parents=True)

    # Valid: correct suffix + manifest
    valid = script_dir / "471000_battle-luabnd-dcx"
    valid.mkdir()
    (valid / "_witchy-bnd4.xml").write_text("<bnd4/>")
    (valid / "471000_battle.lua").write_text("-- lua")

    # Invalid: wrong suffix
    wrong_suffix = script_dir / "something_else"
    wrong_suffix.mkdir()
    (wrong_suffix / "_witchy-bnd4.xml").write_text("<bnd4/>")

    # Invalid: correct suffix but no manifest
    no_manifest = script_dir / "472000_battle-luabnd-dcx"
    no_manifest.mkdir()
    (no_manifest / "472000_battle.lua").write_text("-- lua")

    # Invalid: file (not directory) with matching name
    (script_dir / "473000_battle-luabnd-dcx").write_text("not a dir")

    monkeypatch.setattr(bootstrap, "OVERLAY_SRC_DEST", overlay_src)
    sources = _overlay_script_sources()
    assert sources == [valid]


def test_build_overlay_scripts_silent_skip_when_no_sources(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    monkeypatch.setattr(bootstrap, "OVERLAY_SRC_DEST", tmp_path / "overlay-src")
    monkeypatch.setattr(bootstrap, "OVERLAY_DEST", tmp_path / "overlay")

    # ensure_witchybnd must not be called when there are no sources
    with patch.object(bootstrap, "ensure_witchybnd") as mock_ensure:
        assert build_overlay_scripts() is True
        mock_ensure.assert_not_called()

    captured = capsys.readouterr()
    assert "WitchyBND" not in captured.out
    assert not (tmp_path / "overlay").exists()
