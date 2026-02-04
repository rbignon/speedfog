# Item Randomizer Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate Item Randomizer into SpeedFog's mod generation workflow.

**Architecture:** Add `[item_randomizer]` config section, generate `item_config.json` and copy enemy preset, call ItemRandomizerWrapper before FogModWrapper with `--merge-dir` option.

**Tech Stack:** Python 3.10+, TOML config, JSON output, C# wrapper (already exists)

---

## Task 1: Add ItemRandomizerConfig dataclass

**Files:**
- Modify: `speedfog/config.py:86-95` (after PathsConfig)
- Test: `tests/test_config.py`

**Step 1: Write the failing test**

Add to `tests/test_config.py`:

```python
def test_item_randomizer_defaults():
    """ItemRandomizerConfig has correct defaults."""
    config = Config.from_dict({})
    assert config.item_randomizer.enabled is True
    assert config.item_randomizer.difficulty == 50
    assert config.item_randomizer.remove_requirements is True
    assert config.item_randomizer.auto_upgrade_weapons is True


def test_item_randomizer_from_toml(tmp_path):
    """ItemRandomizerConfig parses from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[item_randomizer]
enabled = false
difficulty = 75
remove_requirements = false
auto_upgrade_weapons = false
""")
    config = Config.from_toml(config_file)
    assert config.item_randomizer.enabled is False
    assert config.item_randomizer.difficulty == 75
    assert config.item_randomizer.remove_requirements is False
    assert config.item_randomizer.auto_upgrade_weapons is False


def test_item_randomizer_validation_difficulty():
    """difficulty must be 0-100."""
    import pytest

    with pytest.raises(ValueError, match="difficulty must be 0-100"):
        Config.from_dict({"item_randomizer": {"difficulty": 101}})
    with pytest.raises(ValueError, match="difficulty must be 0-100"):
        Config.from_dict({"item_randomizer": {"difficulty": -1}})
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_config.py::test_item_randomizer_defaults -v`
Expected: FAIL with AttributeError (no item_randomizer attribute)

**Step 3: Write minimal implementation**

Add to `speedfog/config.py` after `StartingItemsConfig` class (around line 172):

```python
@dataclass
class ItemRandomizerConfig:
    """Item Randomizer configuration."""

    enabled: bool = True
    difficulty: int = 50
    remove_requirements: bool = True
    auto_upgrade_weapons: bool = True

    def __post_init__(self) -> None:
        """Validate configuration."""
        if self.difficulty < 0 or self.difficulty > 100:
            raise ValueError(f"difficulty must be 0-100, got {self.difficulty}")
```

Update `Config` dataclass (around line 175):

```python
@dataclass
class Config:
    """Main configuration container."""

    seed: int = 0
    budget: BudgetConfig = field(default_factory=BudgetConfig)
    requirements: RequirementsConfig = field(default_factory=RequirementsConfig)
    structure: StructureConfig = field(default_factory=StructureConfig)
    paths: PathsConfig = field(default_factory=PathsConfig)
    starting_items: StartingItemsConfig = field(default_factory=StartingItemsConfig)
    item_randomizer: ItemRandomizerConfig = field(default_factory=ItemRandomizerConfig)
```

Update `Config.from_dict()` method to parse the new section:

```python
    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Config:
        """Create Config from a dictionary (e.g., parsed TOML)."""
        # ... existing code ...
        item_randomizer_section = data.get("item_randomizer", {})

        return cls(
            # ... existing fields ...
            item_randomizer=ItemRandomizerConfig(
                enabled=item_randomizer_section.get("enabled", True),
                difficulty=item_randomizer_section.get("difficulty", 50),
                remove_requirements=item_randomizer_section.get("remove_requirements", True),
                auto_upgrade_weapons=item_randomizer_section.get("auto_upgrade_weapons", True),
            ),
        )
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_config.py::test_item_randomizer_defaults tests/test_config.py::test_item_randomizer_from_toml tests/test_config.py::test_item_randomizer_validation_difficulty -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/config.py tests/test_config.py
git commit -m "feat(config): add ItemRandomizerConfig dataclass"
```

---

## Task 2: Create enemy preset file

**Files:**
- Create: `data/enemy_preset.yaml`

**Step 1: Create the preset file**

Create `data/enemy_preset.yaml`:

```yaml
# SpeedFog enemy preset
# Randomize only Basic and Wildlife enemies
# Bosses stay in their original arenas (DAG already randomizes which boss you face)

Classes:
  Boss:
    NoRandom: true
  Miniboss:
    NoRandom: true
  MinorBoss:
    NoRandom: true
  NightMiniboss:
    NoRandom: true
  DragonMiniboss:
    NoRandom: true
  Evergaol:
    NoRandom: true
  HostileNPC:
    NoRandom: true
  CaravanTroll:
    NoRandom: true
  # Basic and Wildlife: default behavior (randomized between themselves)
```

**Step 2: Verify file is valid YAML**

Run: `python -c "import yaml; yaml.safe_load(open('data/enemy_preset.yaml'))"`
Expected: No error

**Step 3: Commit**

```bash
git add data/enemy_preset.yaml
git commit -m "feat: add enemy preset for Item Randomizer integration"
```

---

## Task 3: Add item_config.json generation function

**Files:**
- Create: `speedfog/item_randomizer.py`
- Test: `tests/test_item_randomizer.py`

**Step 1: Write the failing test**

Create `tests/test_item_randomizer.py`:

```python
"""Tests for Item Randomizer integration."""

import json
from pathlib import Path

from speedfog.config import Config
from speedfog.item_randomizer import generate_item_config


def test_generate_item_config_basic():
    """generate_item_config creates correct JSON structure."""
    config = Config.from_dict({})
    seed = 12345

    result = generate_item_config(config, seed)

    assert result["seed"] == 12345
    assert result["difficulty"] == 50
    assert result["options"]["item"] is True
    assert result["options"]["enemy"] is True
    assert result["options"]["fog"] is True
    assert result["options"]["crawl"] is True
    assert result["options"]["weaponreqs"] is True
    assert result["preset"] == "enemy_preset.yaml"
    assert result["helper_options"]["autoUpgradeWeapons"] is True


def test_generate_item_config_custom_settings():
    """generate_item_config respects custom config."""
    config = Config.from_dict({
        "item_randomizer": {
            "difficulty": 75,
            "remove_requirements": False,
            "auto_upgrade_weapons": False,
        }
    })
    seed = 99999

    result = generate_item_config(config, seed)

    assert result["seed"] == 99999
    assert result["difficulty"] == 75
    assert result["options"]["weaponreqs"] is False
    assert result["helper_options"]["autoUpgradeWeapons"] is False


def test_generate_item_config_json_serializable():
    """generate_item_config output is JSON serializable."""
    config = Config.from_dict({})
    result = generate_item_config(config, 42)

    # Should not raise
    json_str = json.dumps(result)
    assert isinstance(json_str, str)
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_item_randomizer.py -v`
Expected: FAIL with ModuleNotFoundError

**Step 3: Write minimal implementation**

Create `speedfog/item_randomizer.py`:

```python
"""Item Randomizer integration for SpeedFog."""

from __future__ import annotations

from typing import Any

from speedfog.config import Config


def generate_item_config(config: Config, seed: int) -> dict[str, Any]:
    """Generate item_config.json content for ItemRandomizerWrapper.

    Args:
        config: SpeedFog configuration.
        seed: Random seed for the run.

    Returns:
        Dictionary ready to be serialized to JSON.
    """
    return {
        "seed": seed,
        "difficulty": config.item_randomizer.difficulty,
        "options": {
            "item": True,
            "enemy": True,
            "fog": True,
            "crawl": True,
            "weaponreqs": config.item_randomizer.remove_requirements,
        },
        "preset": "enemy_preset.yaml",
        "helper_options": {
            "autoUpgradeWeapons": config.item_randomizer.auto_upgrade_weapons,
        },
    }
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_item_randomizer.py -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/item_randomizer.py tests/test_item_randomizer.py
git commit -m "feat: add item_config.json generation"
```

---

## Task 4: Add run_item_randomizer function

**Files:**
- Modify: `speedfog/item_randomizer.py`
- Test: `tests/test_item_randomizer.py`

**Step 1: Write the failing test**

Add to `tests/test_item_randomizer.py`:

```python
import shutil
from unittest.mock import patch, MagicMock


def test_run_item_randomizer_missing_wrapper(tmp_path):
    """run_item_randomizer returns False if wrapper not found."""
    from speedfog.item_randomizer import run_item_randomizer

    seed_dir = tmp_path / "seed"
    seed_dir.mkdir()
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    output_dir = tmp_path / "output"

    result = run_item_randomizer(
        seed_dir=seed_dir,
        game_dir=game_dir,
        output_dir=output_dir,
        platform=None,
        verbose=False,
    )

    assert result is False


def test_run_item_randomizer_builds_correct_command(tmp_path, monkeypatch):
    """run_item_randomizer builds correct command line."""
    from speedfog.item_randomizer import run_item_randomizer

    seed_dir = tmp_path / "seed"
    seed_dir.mkdir()
    (seed_dir / "item_config.json").write_text("{}")
    game_dir = tmp_path / "game"
    game_dir.mkdir()
    output_dir = tmp_path / "output"

    # Mock the wrapper executable existence
    project_root = Path(__file__).parent.parent
    wrapper_exe = project_root / "writer" / "ItemRandomizerWrapper" / "publish" / "win-x64" / "ItemRandomizerWrapper.exe"

    captured_cmd = []

    def mock_popen(cmd, **kwargs):
        captured_cmd.extend(cmd)
        mock_process = MagicMock()
        mock_process.stdout = iter([])
        mock_process.wait.return_value = None
        mock_process.returncode = 0
        return mock_process

    # Only run if wrapper exists (skip in CI)
    if not wrapper_exe.exists():
        import pytest
        pytest.skip("ItemRandomizerWrapper not built")

    monkeypatch.setattr("subprocess.Popen", mock_popen)

    result = run_item_randomizer(
        seed_dir=seed_dir,
        game_dir=game_dir,
        output_dir=output_dir,
        platform="windows",
        verbose=False,
    )

    assert result is True
    assert str(seed_dir / "item_config.json") in captured_cmd
    assert "--game-dir" in captured_cmd
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_item_randomizer.py::test_run_item_randomizer_missing_wrapper -v`
Expected: FAIL with ImportError (function doesn't exist)

**Step 3: Write minimal implementation**

Add to `speedfog/item_randomizer.py`:

```python
import shutil
import subprocess
import sys
from pathlib import Path


def run_item_randomizer(
    seed_dir: Path,
    game_dir: Path,
    output_dir: Path,
    platform: str | None,
    verbose: bool,
) -> bool:
    """Run ItemRandomizerWrapper to generate randomized items/enemies.

    Args:
        seed_dir: Directory containing item_config.json and enemy_preset.yaml
        game_dir: Path to Elden Ring Game directory
        output_dir: Output directory for randomized files
        platform: "windows", "linux", or None for auto-detect
        verbose: Print command and output

    Returns:
        True on success, False on failure.
    """
    project_root = Path(__file__).parent.parent
    wrapper_dir = project_root / "writer" / "ItemRandomizerWrapper"
    wrapper_exe = wrapper_dir / "publish" / "win-x64" / "ItemRandomizerWrapper.exe"

    if not wrapper_exe.exists():
        print(f"Error: ItemRandomizerWrapper not found at {wrapper_exe}", file=sys.stderr)
        print(
            "Run: python tools/setup_dependencies.py --fogrando <path> --itemrando <path>",
            file=sys.stderr,
        )
        return False

    # Detect platform
    if platform is None or platform == "auto":
        platform = "windows" if sys.platform == "win32" else "linux"

    # Check Wine availability on non-Windows
    if platform == "linux" and shutil.which("wine") is None:
        print(
            "Error: Wine not found. Install wine to run Item Randomizer on Linux.",
            file=sys.stderr,
        )
        return False

    # Build command with absolute paths
    seed_dir = seed_dir.resolve()
    game_dir = game_dir.resolve()
    output_dir = output_dir.resolve()
    config_path = seed_dir / "item_config.json"

    if platform == "linux":
        cmd = ["wine", str(wrapper_exe.resolve())]
    else:
        cmd = [str(wrapper_exe.resolve())]

    cmd.extend([
        str(config_path),
        "--game-dir",
        str(game_dir),
        "--data-dir",
        str(wrapper_dir / "diste"),
        "-o",
        str(output_dir),
    ])

    if verbose:
        print(f"Running: {' '.join(cmd)}")
        print(f"Working directory: {wrapper_dir}")

    # Run from wrapper_dir so it finds diste/
    process = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        cwd=wrapper_dir,
        bufsize=1,
    )

    assert process.stdout is not None
    for line in process.stdout:
        print(line, end="")

    process.wait()
    return process.returncode == 0
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_item_randomizer.py::test_run_item_randomizer_missing_wrapper -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/item_randomizer.py tests/test_item_randomizer.py
git commit -m "feat: add run_item_randomizer function"
```

---

## Task 5: Integrate Item Randomizer into main.py

**Files:**
- Modify: `speedfog/main.py`

**Step 1: Add imports**

Add to imports in `speedfog/main.py`:

```python
import json

from speedfog.item_randomizer import generate_item_config, run_item_randomizer
```

**Step 2: Add Item Randomizer orchestration**

Modify the `main()` function. After the graph.json export (around line 272) and before the mod building section (line 288), add:

```python
    # Run Item Randomizer if enabled
    if config.item_randomizer.enabled and not args.no_build:
        print("Running Item Randomizer...")

        # Generate item_config.json
        item_config = generate_item_config(config, actual_seed)
        item_config_path = seed_dir / "item_config.json"
        with item_config_path.open("w") as f:
            json.dump(item_config, f, indent=2)
        if args.verbose:
            print(f"Written: {item_config_path}")

        # Copy enemy preset
        project_root = Path(__file__).parent.parent
        preset_src = project_root / "data" / "enemy_preset.yaml"
        preset_dst = seed_dir / "enemy_preset.yaml"
        if preset_src.exists():
            shutil.copy(preset_src, preset_dst)
            if args.verbose:
                print(f"Copied: {preset_dst}")
        else:
            print(f"Warning: Enemy preset not found at {preset_src}", file=sys.stderr)

        # Run ItemRandomizerWrapper
        item_rando_output = seed_dir / "temp" / "item-randomizer"
        item_rando_output.mkdir(parents=True, exist_ok=True)

        if not run_item_randomizer(
            seed_dir=seed_dir,
            game_dir=game_dir,
            output_dir=item_rando_output,
            platform=config.paths.platform,
            verbose=args.verbose,
        ):
            print(
                "Error: Item Randomizer failed (continuing without it)",
                file=sys.stderr,
            )
            item_rando_output = None
```

**Step 3: Update FogModWrapper call to use --merge-dir**

Modify `run_fogmodwrapper` function signature to accept `merge_dir`:

```python
def run_fogmodwrapper(
    seed_dir: Path,
    game_dir: Path,
    platform: str | None,
    verbose: bool,
    merge_dir: Path | None = None,
) -> bool:
```

Add `--merge-dir` to command if provided (after line 80):

```python
    if merge_dir is not None:
        cmd.extend(["--merge-dir", str(merge_dir.resolve())])
```

Update the call to `run_fogmodwrapper` in `main()`:

```python
        # Determine merge_dir based on Item Randomizer
        merge_dir = None
        if config.item_randomizer.enabled and item_rando_output and item_rando_output.exists():
            merge_dir = item_rando_output

        if not run_fogmodwrapper(
            seed_dir, game_dir, config.paths.platform, args.verbose, merge_dir
        ):
```

**Step 4: Run full test suite**

Run: `pytest -v`
Expected: All tests pass

**Step 5: Commit**

```bash
git add speedfog/main.py
git commit -m "feat: integrate Item Randomizer into main workflow"
```

---

## Task 6: Update config.example.toml

**Files:**
- Modify: `config.example.toml`

**Step 1: Add item_randomizer section**

Add at the end of `config.example.toml`:

```toml
[item_randomizer]
# Enable Item/Enemy Randomizer integration (default: true)
# Requires: python tools/setup_dependencies.py --itemrando <path>
enabled = true

# Item placement difficulty 0-100 (default: 50)
# Higher = powerful items more dispersed, key items harder to find
difficulty = 50

# Remove stat requirements on weapons and spells (default: true)
# Allows using any weapon regardless of STR/DEX/INT/FTH/ARC
remove_requirements = true

# Auto-upgrade weapons to player's highest upgrade level (default: true)
# New weapons match your current max, no manual upgrading needed
auto_upgrade_weapons = true
```

**Step 2: Commit**

```bash
git add config.example.toml
git commit -m "docs: add item_randomizer section to config.example.toml"
```

---

## Task 7: Rename setup_fogrando.py to setup_dependencies.py

**Files:**
- Rename: `tools/setup_fogrando.py` → `tools/setup_dependencies.py`
- Modify: `CLAUDE.md`
- Modify: `speedfog/main.py` (error message)
- Modify: `speedfog/item_randomizer.py` (error message)

**Step 1: Rename the file**

```bash
git mv tools/setup_fogrando.py tools/setup_dependencies.py
```

**Step 2: Update error messages**

In `speedfog/main.py` line 46, change:
```python
        print("Run: python tools/setup_fogrando.py <fogrando.zip>", file=sys.stderr)
```
to:
```python
        print("Run: python tools/setup_dependencies.py --fogrando <path> --itemrando <path>", file=sys.stderr)
```

In `speedfog/item_randomizer.py`, the error message is already correct.

**Step 3: Update CLAUDE.md**

Search and replace `setup_fogrando.py` with `setup_dependencies.py` in CLAUDE.md.

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: rename setup_fogrando.py to setup_dependencies.py"
```

---

## Task 8: Update ItemRandomizerWrapper to support preset and helper_options

**Files:**
- Modify: `writer/ItemRandomizerWrapper/Program.cs`

**Step 1: Update RandomizerConfig class**

In `Program.cs`, update the `RandomizerConfig` class (around line 210):

```csharp
class RandomizerConfig
{
    public int Seed { get; set; }
    public int Difficulty { get; set; } = 50;
    public Dictionary<string, bool>? Options { get; set; }
    public string? Preset { get; set; }
    public Dictionary<string, bool>? HelperOptions { get; set; }
}
```

**Step 2: Load and apply preset**

In `RunRandomizer` method, after building RandomizerOptions, add preset loading:

```csharp
// Load enemy preset if specified
Preset? preset = null;
if (!string.IsNullOrEmpty(randoConfig.Preset))
{
    var presetPath = Path.Combine(Path.GetDirectoryName(config.ConfigPath) ?? ".", randoConfig.Preset);
    if (File.Exists(presetPath))
    {
        Console.WriteLine($"Loading preset: {presetPath}");
        preset = Preset.LoadPreset(presetPath);
    }
    else
    {
        Console.WriteLine($"Warning: Preset not found: {presetPath}");
    }
}
```

Update the `Randomize` call to pass the preset:

```csharp
randomizer.Randomize(
    opt,
    GameSpec.FromGame.ER,
    notify: status => Console.WriteLine($"  {status}"),
    outPath: config.OutputDir,
    preset: preset,
    itemPreset: null,
    messages: new Messages("diste"),
    gameExe: Path.Combine(config.GameDir, "eldenring.exe")
);
```

**Step 3: Handle helper_options**

Add helper options handling after randomization (writes to INI file for RandomizerHelper.dll):

```csharp
// Write helper options if specified
if (randoConfig.HelperOptions != null && randoConfig.HelperOptions.Count > 0)
{
    var helperIniPath = Path.Combine(config.OutputDir, "RandomizerHelper_config.ini");
    using var writer = new StreamWriter(helperIniPath);
    writer.WriteLine("[settings]");
    foreach (var kv in randoConfig.HelperOptions)
    {
        writer.WriteLine($"{kv.Key} = {kv.Value.ToString().ToLowerInvariant()}");
    }
    Console.WriteLine($"Written: {helperIniPath}");
}
```

**Step 4: Build and verify**

```bash
cd writer/ItemRandomizerWrapper
dotnet build
```
Expected: Build succeeds

**Step 5: Commit**

```bash
git add writer/ItemRandomizerWrapper/Program.cs
git commit -m "feat(ItemRandomizerWrapper): support preset and helper_options"
```

---

## Task 9: Update FogModWrapper to support --merge-dir

**Files:**
- Modify: `writer/FogModWrapper/Program.cs`

**Step 1: Check if --merge-dir already exists**

The FogModWrapper may already support `--merge-dir` since it's mentioned in the architecture. Verify by reading Program.cs and checking argument parsing.

If not present, add argument parsing:

```csharp
case "--merge-dir":
    if (i + 1 >= args.Length) return null;
    config.MergeDir = args[++i];
    break;
```

And add to Config class:

```csharp
public string? MergeDir { get; set; }
```

Then pass to FogMod as MergedMods:

```csharp
MergedMods? mergedMods = null;
if (!string.IsNullOrEmpty(config.MergeDir))
{
    mergedMods = new MergedMods(config.MergeDir);
}
```

**Step 2: Build and verify**

```bash
cd writer/FogModWrapper
dotnet build
```
Expected: Build succeeds

**Step 3: Commit**

```bash
git add writer/FogModWrapper/Program.cs
git commit -m "feat(FogModWrapper): add --merge-dir support for Item Randomizer"
```

---

## Task 10: Integration test

**Files:**
- Test manually

**Step 1: Create test config**

Create a test config file with Item Randomizer enabled:

```toml
[run]
seed = 42

[paths]
game_dir = "/path/to/elden/ring/Game"

[item_randomizer]
enabled = true
```

**Step 2: Run SpeedFog**

```bash
uv run speedfog test_config.toml --verbose
```

**Step 3: Verify output**

Check that:
1. `seeds/42/item_config.json` was created
2. `seeds/42/enemy_preset.yaml` was copied
3. `seeds/42/temp/item-randomizer/` contains randomized files (if ItemRandomizerWrapper ran)
4. Final mod in `seeds/42/` includes merged content

**Step 4: Test with Item Randomizer disabled**

```toml
[item_randomizer]
enabled = false
```

Verify that ItemRandomizerWrapper is skipped.

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | ItemRandomizerConfig dataclass | config.py, test_config.py |
| 2 | Enemy preset YAML | data/enemy_preset.yaml |
| 3 | item_config.json generation | item_randomizer.py, test_item_randomizer.py |
| 4 | run_item_randomizer function | item_randomizer.py |
| 5 | Main.py integration | main.py |
| 6 | Config example update | config.example.toml |
| 7 | Rename setup script | tools/, CLAUDE.md |
| 8 | ItemRandomizerWrapper preset support | Program.cs |
| 9 | FogModWrapper --merge-dir | Program.cs |
| 10 | Integration test | manual |
