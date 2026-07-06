"""Tests for the CLI entry point (speedfog.main)."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

from speedfog import main as main_module
from speedfog.main import StepTimer, main


def _real_clusters_or_skip() -> Path:
    """Skip when the generated data/clusters.json is absent (e.g. on CI)."""
    clusters_path = Path(__file__).parent.parent / "data" / "clusters.json"
    if not clusters_path.exists():
        pytest.skip("clusters.json not found")
    return clusters_path


def _run_main(monkeypatch, *argv: str) -> int:
    monkeypatch.setattr(sys, "argv", ["speedfog", *argv])
    return main()


# --- StepTimer ---


def test_step_timer_records_and_closes_steps():
    timer = StepTimer()
    timer.step("first")
    timer.step("second")  # closes "first"
    total = timer.stop()  # closes "second"

    assert [name for name, _ in timer.steps] == ["first", "second"]
    assert all(duration >= 0 for _, duration in timer.steps)
    assert total >= sum(duration for _, duration in timer.steps) - 1e-6


def test_step_timer_format_summary_lists_steps():
    timer = StepTimer()
    timer.step("Generate DAG")
    timer.step("Build mod")
    timer.stop()

    summary = timer.format_summary()
    assert "Generate DAG" in summary
    assert "Build mod" in summary
    assert "%" in summary


def test_step_timer_stop_without_steps():
    timer = StepTimer()
    assert timer.stop() >= 0
    assert timer.steps == []
    assert timer.format_summary() == ""


# --- main(): error paths that need no generated data ---


def test_main_missing_config_file_returns_1(tmp_path, monkeypatch, capsys):
    rc = _run_main(monkeypatch, str(tmp_path / "does_not_exist.toml"))

    assert rc == 1
    assert "Config file not found" in capsys.readouterr().err


def test_main_malformed_toml_returns_1(tmp_path, monkeypatch, capsys):
    config_path = tmp_path / "config.toml"
    config_path.write_text("[run\nseed = ")

    rc = _run_main(monkeypatch, str(config_path))

    assert rc == 1
    assert "Invalid TOML" in capsys.readouterr().err


def test_main_unknown_config_key_returns_1(tmp_path, monkeypatch, capsys):
    config_path = tmp_path / "config.toml"
    config_path.write_text("[run]\nsead = 42\n")

    rc = _run_main(monkeypatch, str(config_path))

    assert rc == 1
    err = capsys.readouterr().err
    assert "Invalid config" in err
    assert "unknown key run.sead" in err


# --- main(): full pipeline against the real cluster pool ---


def test_main_no_build_writes_graph_json(tmp_path, monkeypatch, capsys):
    _real_clusters_or_skip()

    rc = _run_main(monkeypatch, "--no-build", "--seed", "0", "-o", str(tmp_path))

    assert rc == 0
    seed_dirs = [d for d in tmp_path.iterdir() if d.is_dir()]
    assert len(seed_dirs) == 1
    graph_path = seed_dirs[0] / "graph.json"
    assert graph_path.exists()
    graph = json.loads(graph_path.read_text())
    assert graph["seed"] == int(seed_dirs[0].name)
    assert graph["connections"]
    assert graph["area_tiers"]


def test_main_logs_writes_spoiler_and_generation_log(tmp_path, monkeypatch):
    _real_clusters_or_skip()

    rc = _run_main(
        monkeypatch, "--no-build", "--logs", "--seed", "0", "-o", str(tmp_path)
    )

    assert rc == 0
    seed_dir = next(d for d in tmp_path.iterdir() if d.is_dir())
    assert (seed_dir / "logs" / "spoiler.txt").exists()
    assert (seed_dir / "logs" / "generation.log").exists()


def test_main_invalid_exclude_zones_returns_1(tmp_path, monkeypatch, capsys):
    _real_clusters_or_skip()
    config_path = tmp_path / "config.toml"
    config_path.write_text(
        '[requirements]\nexclude_zones = ["zone_that_does_not_exist"]\n'
    )

    rc = _run_main(monkeypatch, str(config_path), "--no-build", "-o", str(tmp_path))

    assert rc == 1
    assert "invalid exclude_zones" in capsys.readouterr().err


# --- main(): the build seam (FogModWrapper invocation) ---


@pytest.fixture
def fake_build(monkeypatch):
    """Stub the writer subprocesses and packaging, record their calls.

    run_item_randomizer is stubbed as a safety net: the build tests disable
    it via config, and the stub guarantees no Wine process can ever start.
    """
    calls: dict = {"fogmod": [], "itemrando": [], "package": []}

    def fake_run_fogmodwrapper(seed_dir, game_dir, platform, verbose, merge_dir=None):
        calls["fogmod"].append(
            {
                "seed_dir": seed_dir,
                "game_dir": game_dir,
                "platform": platform,
                "merge_dir": merge_dir,
            }
        )
        return fake_run_fogmodwrapper.result  # type: ignore[attr-defined]

    fake_run_fogmodwrapper.result = True  # type: ignore[attr-defined]

    def fake_run_item_randomizer(**kwargs):
        calls["itemrando"].append(kwargs)
        return True

    def fake_package_seed(project_root, seed_dir, **kwargs):
        calls["package"].append({"seed_dir": seed_dir, **kwargs})

    monkeypatch.setattr(main_module, "run_fogmodwrapper", fake_run_fogmodwrapper)
    monkeypatch.setattr(main_module, "run_item_randomizer", fake_run_item_randomizer)
    monkeypatch.setattr(main_module, "package_seed", fake_package_seed)
    return fake_run_fogmodwrapper, calls


def _write_no_itemrando_config(tmp_path: Path) -> Path:
    """Config disabling the item randomizer (enabled by default)."""
    config_path = tmp_path / "config.toml"
    config_path.write_text("[item_randomizer]\nenabled = false\n")
    return config_path


def test_main_build_invokes_fogmodwrapper(tmp_path, monkeypatch, fake_build):
    _real_clusters_or_skip()
    _, calls = fake_build
    config_path = _write_no_itemrando_config(tmp_path)
    game_dir = tmp_path / "Game"
    game_dir.mkdir()
    out_dir = tmp_path / "out"

    rc = _run_main(
        monkeypatch,
        str(config_path),
        "--seed",
        "0",
        "-o",
        str(out_dir),
        "--game-dir",
        str(game_dir),
    )

    assert rc == 0
    assert len(calls["fogmod"]) == 1
    call = calls["fogmod"][0]
    assert call["game_dir"] == game_dir
    assert call["seed_dir"].parent == out_dir
    # Default paths.platform (None = auto-detect) flows through unchanged
    assert call["platform"] is None
    # Item randomizer disabled: never invoked, nothing to merge
    assert calls["itemrando"] == []
    assert call["merge_dir"] is None
    # Packaging runs after a successful build, on the same seed dir
    assert len(calls["package"]) == 1
    assert calls["package"][0]["seed_dir"] == call["seed_dir"]
    assert calls["package"][0]["item_randomizer_enabled"] is False


def test_main_itemrando_enabled_passes_merge_dir(tmp_path, monkeypatch, fake_build):
    _real_clusters_or_skip()
    _, calls = fake_build
    game_dir = tmp_path / "Game"
    game_dir.mkdir()
    out_dir = tmp_path / "out"

    # Default config: item randomizer enabled (stubbed by fake_build)
    rc = _run_main(
        monkeypatch, "--seed", "0", "-o", str(out_dir), "--game-dir", str(game_dir)
    )

    assert rc == 0
    assert len(calls["itemrando"]) == 1
    itemrando_dir = calls["itemrando"][0]["output_dir"]
    assert itemrando_dir == calls["fogmod"][0]["merge_dir"]
    assert calls["package"][0]["item_randomizer_enabled"] is True


def test_main_build_without_game_dir_returns_1(
    tmp_path, monkeypatch, fake_build, capsys
):
    _real_clusters_or_skip()
    _, calls = fake_build
    config_path = _write_no_itemrando_config(tmp_path)

    rc = _run_main(
        monkeypatch, str(config_path), "--seed", "0", "-o", str(tmp_path / "out")
    )

    assert rc == 1
    assert "--game-dir required" in capsys.readouterr().err
    assert calls["fogmod"] == []


def test_main_build_failure_returns_1(tmp_path, monkeypatch, fake_build, capsys):
    _real_clusters_or_skip()
    fake_wrapper, calls = fake_build
    fake_wrapper.result = False
    config_path = _write_no_itemrando_config(tmp_path)
    game_dir = tmp_path / "Game"
    game_dir.mkdir()

    rc = _run_main(
        monkeypatch,
        str(config_path),
        "--seed",
        "0",
        "-o",
        str(tmp_path / "out"),
        "--game-dir",
        str(game_dir),
    )

    assert rc == 1
    assert "Mod build failed" in capsys.readouterr().err
    # graph.json is preserved for debugging even when the build fails
    seed_dir = calls["fogmod"][0]["seed_dir"]
    assert (seed_dir / "graph.json").exists()
    assert calls["package"] == []
