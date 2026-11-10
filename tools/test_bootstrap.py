from __future__ import annotations

from pathlib import Path
from unittest.mock import patch

import bootstrap
import pytest
import refresh_vanilla_snapshot
from bootstrap import (
    _static_mod_script_sources,
    build_static_mod_scripts,
    install_modengine_runtime,
    is_itemrando_installed,
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


def test_static_mod_script_sources_skips_when_no_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(
        bootstrap, "STATIC_MOD_SRC_DEST", tmp_path / "mods-src" / "speedfog"
    )
    assert _static_mod_script_sources() == []


def test_static_mod_script_sources_filters_by_suffix_and_manifest(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    static_mod_src = tmp_path / "mods-src" / "speedfog"
    script_dir = static_mod_src / "script"
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

    monkeypatch.setattr(bootstrap, "STATIC_MOD_SRC_DEST", static_mod_src)
    sources = _static_mod_script_sources()
    assert sources == [valid]


def test_build_static_mod_scripts_silent_skip_when_no_sources(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    monkeypatch.setattr(
        bootstrap, "STATIC_MOD_SRC_DEST", tmp_path / "mods-src" / "speedfog"
    )
    monkeypatch.setattr(bootstrap, "STATIC_MOD_DEST", tmp_path / "mods" / "speedfog")

    # ensure_witchybnd must not be called when there are no sources
    with patch.object(bootstrap, "ensure_witchybnd") as mock_ensure:
        assert build_static_mod_scripts() is True
        mock_ensure.assert_not_called()

    captured = capsys.readouterr()
    assert "WitchyBND" not in captured.out
    assert not (tmp_path / "mods").exists()


def _make_itemrando_install(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> dict[str, Path]:
    """Create a complete Item Randomizer install layout under tmp_path and
    point bootstrap's destination constants at it. Returns the named paths so
    a test can delete one and assert the freshness check notices."""
    writer_lib = tmp_path / "writer" / "lib"
    itemrando = writer_lib / "itemrando"
    diste = tmp_path / "diste"
    data = tmp_path / "data"
    packaging_lib = data / "packaging" / "lib"
    for d in (writer_lib, itemrando, diste, data, packaging_lib):
        d.mkdir(parents=True, exist_ok=True)

    monkeypatch.setattr(bootstrap, "WRITER_LIB", writer_lib)
    monkeypatch.setattr(bootstrap, "WRITER_LIB_ITEMRANDO", itemrando)
    monkeypatch.setattr(bootstrap, "DISTE_DEST", diste)
    monkeypatch.setattr(bootstrap, "DATA_DEST", data)
    monkeypatch.setattr(bootstrap, "PACKAGING_LIB_DEST", packaging_lib)

    artifacts: dict[str, Path] = {}
    for dll in bootstrap.ITEMRANDO_REQUIRED_DLLS:
        p = writer_lib / dll
        p.write_bytes(b"dll")
        artifacts[dll] = p
    for dll in bootstrap.ITEMRANDO_PRIVATE_DLLS:
        p = itemrando / dll
        p.write_bytes(b"dll")
        artifacts[f"itemrando/{dll}"] = p
    for filename in bootstrap.ITEMRANDO_DATA_FILES:
        p = data / filename
        p.write_text("data")
        artifacts[filename] = p
    for dll in bootstrap.ITEMRANDO_EXTRA_DLLS:
        p = packaging_lib / dll
        p.write_bytes(b"dll")
        artifacts[f"packaging/{dll}"] = p
    return artifacts


def test_is_itemrando_installed_true_when_complete(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    _make_itemrando_install(tmp_path, monkeypatch)
    assert is_itemrando_installed() is True


@pytest.mark.parametrize(
    "missing",
    [
        "itemrando/SoulsIds.dll",
        "itemrando/SoulsFormats.dll",
        "LightInject.dll",
        "LightInject.Annotation.dll",
        "RandomizerCommon.dll",
    ],
)
def test_is_itemrando_installed_false_when_artifact_missing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, missing: str
) -> None:
    # A non-forced re-run must self-heal after a version bump adds a dependency,
    # so the freshness check has to fail when any required artifact is absent.
    artifacts = _make_itemrando_install(tmp_path, monkeypatch)
    artifacts[missing].unlink()
    assert is_itemrando_installed() is False


def test_refresh_fogmod_snapshot_skips_when_snapshot_missing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # A bootstrap without --fogrando (or before any FogRando install) has no
    # snapshot to refresh; that is a quiet no-op, not an error.
    monkeypatch.setitem(
        refresh_vanilla_snapshot.SNAPSHOTS, "fogmod", tmp_path / "missing"
    )
    calls: list[list[str]] = []
    monkeypatch.setattr(
        refresh_vanilla_snapshot, "main", lambda argv: calls.append(argv) or 0
    )
    assert bootstrap.refresh_fogmod_snapshot(tmp_path / "Game") is True
    assert calls == []


def test_refresh_fogmod_snapshot_runs_all_refresh(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    snapshot = tmp_path / "eldendata" / "Vanilla"
    snapshot.mkdir(parents=True)
    monkeypatch.setitem(refresh_vanilla_snapshot.SNAPSHOTS, "fogmod", snapshot)
    calls: list[list[str]] = []
    monkeypatch.setattr(
        refresh_vanilla_snapshot, "main", lambda argv: calls.append(argv) or 0
    )
    game = tmp_path / "Game"
    assert bootstrap.refresh_fogmod_snapshot(game) is True
    assert calls == [[str(game), "--all"]]


def test_refresh_fogmod_snapshot_propagates_refresh_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    # A refresh that cannot run (packed install, missing game files) must fail
    # the bootstrap: continuing would silently generate seeds from the zip's
    # pre-1.17 snapshot.
    snapshot = tmp_path / "eldendata" / "Vanilla"
    snapshot.mkdir(parents=True)
    monkeypatch.setitem(refresh_vanilla_snapshot.SNAPSHOTS, "fogmod", snapshot)
    monkeypatch.setattr(refresh_vanilla_snapshot, "main", lambda argv: 1)
    assert bootstrap.refresh_fogmod_snapshot(tmp_path / "Game") is False


def _setup_static_mod_builder(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    stderr: str = "",
    winter_files: tuple[str, ...] = (),
) -> list[list[str]]:
    """Point bootstrap at a fake tree with a fake StaticModBuilder exe and
    record the commands handed to subprocess.run. The fake run exits 0 with
    the given stderr and leaves winter_files (relative to the winter folder)
    behind, as a winter build would."""
    import subprocess

    monkeypatch.setattr(bootstrap, "PROJECT_ROOT", tmp_path)
    monkeypatch.setattr(bootstrap, "DATA_DEST", tmp_path / "data")
    monkeypatch.setattr(bootstrap, "STATIC_MOD_DEST", tmp_path / "data/mods/speedfog")
    monkeypatch.setattr(
        bootstrap, "HALLOWEEN_MOD_DEST", tmp_path / "data/mods/speedfog-halloween"
    )
    monkeypatch.setattr(
        bootstrap, "WINTER_MOD_DEST", tmp_path / "data/mods/speedfog-winter"
    )
    exe = tmp_path / "writer/StaticModBuilder/publish/win-x64/StaticModBuilder.exe"
    exe.parent.mkdir(parents=True)
    exe.write_bytes(b"x")
    monkeypatch.setattr(bootstrap.shutil, "which", lambda name: "/usr/bin/wine")
    cmds: list[list[str]] = []

    def fake_run(cmd, **kwargs):
        cmds.append(list(cmd))
        for name in winter_files:
            path = bootstrap.WINTER_MOD_DEST / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"x")
        return subprocess.CompletedProcess(cmd, 0, stdout="", stderr=stderr)

    monkeypatch.setattr(bootstrap.subprocess, "run", fake_run)
    return cmds


def test_run_static_mod_builder_passes_winter_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    cmds = _setup_static_mod_builder(
        tmp_path, monkeypatch, winter_files=("material/allmaterial.matbinbnd.dcx",)
    )

    assert bootstrap.run_static_mod_builder(tmp_path / "Game", winter=True) == (
        True,
        False,
    )
    cmd = cmds[-1]
    i = cmd.index("--winter-dir")
    assert cmd[i + 1] == str(bootstrap.WINTER_MOD_DEST.resolve())
    assert bootstrap.WINTER_MOD_DEST.is_dir()

    assert bootstrap.run_static_mod_builder(tmp_path / "Game", winter=False) == (
        True,
        False,
    )
    assert "--winter-dir" not in cmds[-1]


def test_run_static_mod_builder_reports_unbuilt_winter_but_succeeds(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _setup_static_mod_builder(tmp_path, monkeypatch)

    assert bootstrap.run_static_mod_builder(tmp_path / "Game", winter=True) == (
        True,
        True,
    )
    assert bootstrap.WINTER_NOT_BUILT in capsys.readouterr().out


def test_run_static_mod_builder_does_not_report_a_built_winter_mod(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _setup_static_mod_builder(
        tmp_path, monkeypatch, winter_files=("asset/aet/aet007/aet007_045.tpf.dcx",)
    )

    assert bootstrap.run_static_mod_builder(tmp_path / "Game", winter=True) == (
        True,
        False,
    )
    assert "Winter mod not built" not in capsys.readouterr().out


def test_run_static_mod_builder_echoes_only_winter_failures_from_stderr(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _setup_static_mod_builder(
        tmp_path,
        monkeypatch,
        stderr="0024:fixme:ntdll:NtQuerySystemInformation chatter\n"
        "WINTER FAILED: missing material binder x\n"
        "002c:err:ole:CoGetClassObject chatter\n",
    )

    bootstrap.run_static_mod_builder(tmp_path / "Game", winter=True)

    out = capsys.readouterr().out
    assert "WINTER FAILED: missing material binder x" in out
    assert "chatter" not in out


def test_run_static_mod_builder_announces_the_winter_scan_only_when_on(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _setup_static_mod_builder(tmp_path, monkeypatch)

    bootstrap.run_static_mod_builder(tmp_path / "Game", winter=False)
    assert "FLVER scan" not in capsys.readouterr().out

    bootstrap.run_static_mod_builder(tmp_path / "Game", winter=True)
    assert "FLVER scan" in capsys.readouterr().out


@pytest.mark.parametrize("scripts_ok", [True, False])
def test_main_ends_with_the_winter_line_when_the_winter_mod_is_not_built(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    scripts_ok: bool,
) -> None:
    monkeypatch.setattr(
        "sys.argv", ["bootstrap.py", "--game-dir", "Game", "--no-refresh"]
    )
    monkeypatch.setattr(bootstrap, "copy_oodle_dll", lambda game_dir, force: True)
    monkeypatch.setattr(bootstrap, "ensure_modengine", lambda force: True)
    monkeypatch.setattr(
        bootstrap, "run_static_mod_builder", lambda game_dir, winter: (True, True)
    )
    monkeypatch.setattr(bootstrap, "build_static_mod_scripts", lambda force: scripts_ok)
    monkeypatch.setattr(bootstrap, "warm_itemrando_cache", lambda game_dir: True)

    assert bootstrap.main() == (0 if scripts_ok else 1)

    lines = [line for line in capsys.readouterr().out.splitlines() if line.strip()]
    assert bootstrap.WINTER_NOT_BUILT in lines[-1]
    assert ("Setup complete!" in "\n".join(lines)) is scripts_ok
