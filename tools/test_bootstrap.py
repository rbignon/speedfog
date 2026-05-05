from __future__ import annotations

from pathlib import Path

import pytest
from bootstrap import install_modengine_runtime


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
