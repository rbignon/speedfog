"""Tests for the FogModWrapper subprocess wrapper."""

from __future__ import annotations

from pathlib import Path

import pytest

from speedfog import fog_mod
from speedfog.fog_mod import run_fogmodwrapper


class FakeProcess:
    """Stand-in for subprocess.Popen capturing the invocation."""

    def __init__(self, returncode: int = 0, output: list[str] | None = None):
        self.returncode = returncode
        self.stdout = iter(output or ["FogModWrapper output line\n"])

    def wait(self) -> int:
        return self.returncode


@pytest.fixture
def fake_popen(monkeypatch):
    """Replace subprocess.Popen in fog_mod, recording cmd and kwargs."""
    calls: list[dict] = []

    def popen(cmd, **kwargs):
        calls.append({"cmd": cmd, **kwargs})
        return FakeProcess(returncode=popen.returncode)  # type: ignore[attr-defined]

    popen.returncode = 0  # type: ignore[attr-defined]
    monkeypatch.setattr(fog_mod.subprocess, "Popen", popen)
    return popen, calls


@pytest.fixture
def wrapper_exists(monkeypatch):
    """Pretend FogModWrapper.exe exists, leaving other paths untouched."""
    real_exists = Path.exists
    monkeypatch.setattr(
        Path,
        "exists",
        lambda self: self.name == "FogModWrapper.exe" or real_exists(self),
    )


@pytest.fixture
def wine_available(monkeypatch):
    monkeypatch.setattr(fog_mod.shutil, "which", lambda name: f"/usr/bin/{name}")


def test_missing_wrapper_exe_returns_false(tmp_path, monkeypatch, capsys):
    real_exists = Path.exists
    monkeypatch.setattr(
        Path,
        "exists",
        lambda self: self.name != "FogModWrapper.exe" and real_exists(self),
    )

    ok = run_fogmodwrapper(tmp_path, tmp_path, platform="windows", verbose=False)

    assert ok is False
    err = capsys.readouterr().err
    assert "FogModWrapper not found" in err
    assert "bootstrap.py" in err


def test_missing_wine_on_linux_returns_false(
    tmp_path, monkeypatch, wrapper_exists, capsys
):
    monkeypatch.setattr(fog_mod.shutil, "which", lambda name: None)

    ok = run_fogmodwrapper(tmp_path, tmp_path, platform="linux", verbose=False)

    assert ok is False
    assert "Wine not found" in capsys.readouterr().err


def test_windows_platform_runs_exe_natively(tmp_path, wrapper_exists, fake_popen):
    _, calls = fake_popen
    seed_dir = tmp_path / "seed"
    game_dir = tmp_path / "game"

    ok = run_fogmodwrapper(seed_dir, game_dir, platform="windows", verbose=False)

    assert ok is True
    assert len(calls) == 1
    cmd = calls[0]["cmd"]
    assert cmd[0].endswith("FogModWrapper.exe")
    assert "wine" not in cmd


def test_linux_platform_prepends_wine(
    tmp_path, wrapper_exists, wine_available, fake_popen
):
    _, calls = fake_popen

    ok = run_fogmodwrapper(tmp_path, tmp_path, platform="linux", verbose=False)

    assert ok is True
    cmd = calls[0]["cmd"]
    assert cmd[0] == "wine"
    assert cmd[1].endswith("FogModWrapper.exe")


@pytest.mark.parametrize("platform", [None, "auto"])
def test_auto_platform_detects_from_sys_platform(
    tmp_path, monkeypatch, wrapper_exists, wine_available, fake_popen, platform
):
    _, calls = fake_popen
    monkeypatch.setattr(fog_mod.sys, "platform", "linux")

    run_fogmodwrapper(tmp_path, tmp_path, platform=platform, verbose=False)
    assert calls[-1]["cmd"][0] == "wine"

    monkeypatch.setattr(fog_mod.sys, "platform", "win32")
    run_fogmodwrapper(tmp_path, tmp_path, platform=platform, verbose=False)
    assert calls[-1]["cmd"][0] != "wine"


def test_command_arguments_and_cwd(tmp_path, wrapper_exists, fake_popen):
    _, calls = fake_popen
    seed_dir = tmp_path / "seeds" / "123"
    game_dir = tmp_path / "Game"

    run_fogmodwrapper(seed_dir, game_dir, platform="windows", verbose=False)

    cmd = calls[0]["cmd"]
    # seed_dir is both the positional input and the -o output, resolved absolute
    assert cmd[1] == str(seed_dir.resolve())
    assert cmd[cmd.index("-o") + 1] == str(seed_dir.resolve())
    assert cmd[cmd.index("--game-dir") + 1] == str(game_dir.resolve())
    data_dir = cmd[cmd.index("--data-dir") + 1]
    assert data_dir.endswith("/data")
    assert "--merge-dir" not in cmd
    # Runs from the wrapper directory so the exe finds eldendata/
    assert str(calls[0]["cwd"]).endswith("writer/FogModWrapper")


def test_merge_dir_appended_when_given(tmp_path, wrapper_exists, fake_popen):
    _, calls = fake_popen
    merge_dir = tmp_path / "mods" / "itemrando"

    run_fogmodwrapper(
        tmp_path, tmp_path, platform="windows", verbose=False, merge_dir=merge_dir
    )

    cmd = calls[0]["cmd"]
    assert cmd[cmd.index("--merge-dir") + 1] == str(merge_dir.resolve())


def test_nonzero_exit_code_returns_false(tmp_path, wrapper_exists, fake_popen):
    popen, _ = fake_popen
    popen.returncode = 1

    ok = run_fogmodwrapper(tmp_path, tmp_path, platform="windows", verbose=False)

    assert ok is False
