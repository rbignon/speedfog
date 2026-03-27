# Standalone .exe Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Package SpeedFog as a standalone Windows .exe that generates ready-to-play seed .zip files, with automatic dependency extraction from user-provided Nexusmods zips.

**Architecture:** PyInstaller one-folder distribution. `speedfog.exe` (Python) orchestrates the pipeline: dependency extraction via `sfextract` (in distribution folder), DAG generation, C# wrapper invocation, and output zip packaging. Path resolution uses `Path(sys.executable).parent` in frozen (.exe) mode and `Path.cwd()` in dev mode.

**Tech Stack:** Python 3.10+, PyInstaller, sfextract (MIT), existing C# wrappers (pre-compiled)

**Spec:** `docs/plans/2026-03-27-standalone-exe-distribution.md`

---

## File Structure

| File | Action | Purpose |
|------|--------|---------|
| `speedfog/paths.py` | Create | `get_base_dir()` with frozen/dev mode detection |
| `speedfog/deps.py` | Create | Dependency detection, extraction, derived data generation |
| `speedfog/packaging.py` | Create | Zip seed output into `output/<seed>.zip` |
| `speedfog/main.py` | Modify | Use `get_base_dir()`, integrate deps check, add zip step |
| `speedfog/fog_mod.py` | Modify | Accept `base_dir` parameter instead of computing `project_root` |
| `speedfog/item_randomizer.py` | Modify | Accept `base_dir` parameter instead of computing `project_root` |
| `tools/generate_clusters.py` | Modify | Add `run()` callable entry point (no argparse) |
| `tools/extract_fog_data.py` | Modify | Add `run()` callable entry point (no argparse) |
| `tests/test_paths.py` | Create | Tests for path resolution |
| `tests/test_deps.py` | Create | Tests for dependency detection |
| `tests/test_packaging.py` | Create | Tests for zip packaging |
| `speedfog.spec` | Create | PyInstaller spec file |
| `pyproject.toml` | Modify | Add PyInstaller to dev dependencies |

---

### Task 1: Path Resolution Module

**Files:**
- Create: `speedfog/paths.py`
- Create: `tests/test_paths.py`

- [ ] **Step 1: Write the test**

```python
# tests/test_paths.py
"""Tests for path resolution."""

import sys
from pathlib import Path

from speedfog.paths import get_base_dir


def test_get_base_dir_dev_mode_returns_cwd(monkeypatch, tmp_path):
    """In dev mode (not frozen), get_base_dir() returns cwd."""
    monkeypatch.chdir(tmp_path)
    monkeypatch.delattr(sys, "frozen", raising=False)
    assert get_base_dir() == tmp_path


def test_get_base_dir_frozen_mode_returns_exe_parent(monkeypatch, tmp_path):
    """In frozen mode (.exe), get_base_dir() returns the exe's parent dir."""
    fake_exe = tmp_path / "dist" / "speedfog.exe"
    fake_exe.parent.mkdir(parents=True)
    fake_exe.touch()
    monkeypatch.setattr(sys, "frozen", True)
    monkeypatch.setattr(sys, "executable", str(fake_exe))
    assert get_base_dir() == fake_exe.parent


def test_get_base_dir_returns_path_object():
    """get_base_dir() should return a Path instance."""
    result = get_base_dir()
    assert isinstance(result, Path)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_paths.py -v`
Expected: FAIL with "ModuleNotFoundError: No module named 'speedfog.paths'"

- [ ] **Step 3: Write the implementation**

```python
# speedfog/paths.py
"""Path resolution for SpeedFog.

In dev mode: base_dir = cwd (project root, where uv run speedfog is called).
In .exe mode: base_dir = directory containing the exe (distribution folder).

This distinction matters because a player might launch the exe from a
different working directory (e.g., via shortcut with custom "Start in").
"""

from __future__ import annotations

import sys
from pathlib import Path


def get_base_dir() -> Path:
    """Return the base directory for all path resolution.

    Frozen (.exe): returns the directory containing the executable.
    Dev mode: returns the current working directory.
    """
    if getattr(sys, "frozen", False):
        return Path(sys.executable).parent
    return Path.cwd()
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest tests/test_paths.py -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add speedfog/paths.py tests/test_paths.py
git commit -m "feat: add path resolution module for standalone exe support"
```

---

### Task 2: Refactor Path Resolution in Existing Modules

**Files:**
- Modify: `speedfog/main.py` (line 148)
- Modify: `speedfog/fog_mod.py` (line 30)
- Modify: `speedfog/item_randomizer.py` (line 93)

The three modules that use `Path(__file__).parent.parent` need to be updated. For `fog_mod.py` and `item_randomizer.py`, add a `base_dir` parameter. For `main.py`, use `get_base_dir()`.

- [ ] **Step 1: Update fog_mod.py to accept base_dir parameter**

In `speedfog/fog_mod.py`, change the function signature and replace the hardcoded project_root:

```python
# Add to imports
from speedfog.paths import get_base_dir

# Change function signature (add base_dir parameter with default)
def run_fogmodwrapper(
    seed_dir: Path,
    game_dir: Path,
    platform: str | None,
    verbose: bool,
    merge_dir: Path | None = None,
    base_dir: Path | None = None,
) -> bool:
```

Replace line 30:
```python
    # Old:
    project_root = Path(__file__).parent.parent
    # New:
    project_root = base_dir if base_dir is not None else get_base_dir()
```

- [ ] **Step 2: Update item_randomizer.py to accept base_dir parameter**

In `speedfog/item_randomizer.py`, same pattern:

```python
# Add to imports
from speedfog.paths import get_base_dir

# Change function signature
def run_item_randomizer(
    seed_dir: Path,
    game_dir: Path,
    output_dir: Path,
    platform: str | None,
    verbose: bool,
    base_dir: Path | None = None,
) -> bool:
```

Replace line 93:
```python
    # Old:
    project_root = Path(__file__).parent.parent
    # New:
    project_root = base_dir if base_dir is not None else get_base_dir()
```

- [ ] **Step 3: Update main.py to use get_base_dir()**

In `speedfog/main.py`:

```python
# Add to imports
from speedfog.paths import get_base_dir
```

Replace line 148:
```python
    # Old:
    project_root = Path(__file__).parent.parent
    # New:
    project_root = get_base_dir()
```

Pass `base_dir` to wrapper calls. In the `run_fogmodwrapper` call (~line 411):
```python
        if not run_fogmodwrapper(
            seed_dir, game_dir, config.paths.platform, args.verbose, merge_dir,
            base_dir=project_root,
        ):
```

In the `run_item_randomizer` call (~line 353):
```python
        if run_item_randomizer(
            seed_dir=seed_dir,
            game_dir=game_dir,
            output_dir=item_rando_dir,
            platform=config.paths.platform,
            verbose=args.verbose,
            base_dir=project_root,
        ):
```

- [ ] **Step 4: Run existing tests to verify no regressions**

Run: `pytest tests/ -v`
Expected: All existing tests pass (tests use `Path(__file__)` directly, unaffected)

- [ ] **Step 5: Commit**

```bash
git add speedfog/main.py speedfog/fog_mod.py speedfog/item_randomizer.py
git commit -m "refactor: use get_base_dir() for path resolution across modules"
```

---

### Task 3: Add Callable Entry Points to Tools

**Files:**
- Modify: `tools/generate_clusters.py`
- Modify: `tools/extract_fog_data.py`

Add `run()` functions that encapsulate the `main()` logic without argparse, so `speedfog/deps.py` can call them directly (needed for .exe mode where subprocess with Python is not available).

- [ ] **Step 1: Examine the end of generate_clusters.py main() to understand what it does after parsing args**

Read `tools/generate_clusters.py` from line 2023 to the end to see all the steps main() performs after argument parsing.

- [ ] **Step 2: Add run() function to generate_clusters.py**

Add a `run()` function before `main()` that takes file paths as parameters and performs all the logic. Then refactor `main()` to call `run()`:

```python
def run(
    fog_txt: Path,
    output: Path,
    metadata: Path | None = None,
    exclude_dlc: bool = False,
    exclude_overworld: bool = True,
    verbose: bool = False,
) -> None:
    """Generate clusters.json from fog.txt.

    Callable entry point (no argparse). Raises on failure.
    """
    # ... move core logic from main() here
    # (parsing fog.txt, generating clusters, writing output)


def main() -> int:
    """CLI entry point."""
    # ... argparse ...
    try:
        run(
            fog_txt=args.fog_txt,
            output=args.output,
            metadata=args.metadata,
            exclude_dlc=exclude_dlc,
            exclude_overworld=exclude_overworld,
            verbose=args.verbose,
        )
        return 0
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1
```

- [ ] **Step 3: Run existing tool tests to verify no regressions**

Run: `cd tools && pytest test_generate_clusters.py -v`
Expected: PASS

- [ ] **Step 4: Add run() function to extract_fog_data.py**

Same pattern: add a `run()` function before `main()`:

```python
def run(
    fog_txt: Path,
    output: Path,
    validate_clusters: Path | None = None,
    verbose: bool = False,
) -> None:
    """Extract fog gate metadata from fog.txt.

    Callable entry point (no argparse). Raises on failure.
    """
    # ... move core logic from main() here


def main() -> int:
    """CLI entry point."""
    # ... argparse ...
    try:
        run(
            fog_txt=args.fog_txt,
            output=args.output,
            validate_clusters=args.validate_clusters,
            verbose=args.verbose,
        )
        return 0
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1
```

- [ ] **Step 5: Commit**

```bash
git add tools/generate_clusters.py tools/extract_fog_data.py
git commit -m "refactor: add callable run() entry points to tools for deps module"
```

---

### Task 4: Dependency Detection and Extraction Module

**Files:**
- Create: `speedfog/deps.py`
- Create: `tests/test_deps.py`

This is the core new module. It detects whether dependencies have been extracted, finds zips in `deps/`, runs `sfextract` to extract DLLs, copies files to the right locations, and generates derived data.

- [ ] **Step 1: Write tests for dependency detection**

```python
# tests/test_deps.py
"""Tests for dependency detection and extraction."""

from __future__ import annotations

from pathlib import Path

from speedfog.deps import check_fogrando_deps, check_itemrando_deps, find_mod_zips


def test_check_fogrando_deps_missing(tmp_path):
    """Returns False when FogRando files are missing."""
    assert check_fogrando_deps(tmp_path) is False


def test_check_fogrando_deps_present(tmp_path):
    """Returns True when all FogRando files are present."""
    # Create required structure
    lib_dir = tmp_path / "lib"
    lib_dir.mkdir()
    for dll in [
        "FogMod.dll", "SoulsFormats.dll", "SoulsIds.dll",
        "BouncyCastle.Cryptography.dll", "Newtonsoft.Json.dll",
        "YamlDotNet.dll", "ZstdNet.dll", "DrSwizzler.dll",
    ]:
        (lib_dir / dll).touch()

    data_dir = tmp_path / "data"
    data_dir.mkdir()
    for f in ["fog.txt", "fogevents.txt", "foglocations2.txt", "er-common.emedf.json"]:
        (data_dir / f).touch()

    eldendata = tmp_path / "writer" / "FogModWrapper" / "eldendata"
    eldendata.mkdir(parents=True)
    (eldendata / "placeholder").touch()

    assert check_fogrando_deps(tmp_path) is True


def test_check_itemrando_deps_missing(tmp_path):
    """Returns False when Item Randomizer files are missing."""
    assert check_itemrando_deps(tmp_path) is False


def test_check_itemrando_deps_present(tmp_path):
    """Returns True when all Item Randomizer files are present."""
    lib_dir = tmp_path / "lib"
    lib_dir.mkdir()
    (lib_dir / "RandomizerCommon.dll").touch()
    (lib_dir / "Pidgin.dll").touch()

    diste = tmp_path / "writer" / "ItemRandomizerWrapper" / "diste"
    diste.mkdir(parents=True)
    (diste / "placeholder").touch()

    data_dir = tmp_path / "data"
    data_dir.mkdir()
    (data_dir / "enemy.txt").touch()

    assets = tmp_path / "writer" / "assets"
    assets.mkdir(parents=True)
    (assets / "RandomizerCrashFix.dll").touch()
    (assets / "RandomizerHelper.dll").touch()

    assert check_itemrando_deps(tmp_path) is True


def test_find_mod_zips_none(tmp_path):
    """Returns None when no zips in deps/."""
    deps_dir = tmp_path / "deps"
    deps_dir.mkdir()
    result = find_mod_zips(tmp_path)
    assert result == (None, None)


def test_find_mod_zips_fogrando_only(tmp_path):
    """Finds FogRando zip by looking for fog/ directory inside zip."""
    import zipfile
    deps_dir = tmp_path / "deps"
    deps_dir.mkdir()

    # Create a fake FogRando zip with fog/FogMod.exe
    zip_path = deps_dir / "FogRando-v1.2.3.zip"
    with zipfile.ZipFile(zip_path, "w") as zf:
        zf.writestr("fog/FogMod.exe", "fake")
        zf.writestr("fog/eldendata/test.txt", "fake")

    fogrando, itemrando = find_mod_zips(tmp_path)
    assert fogrando == zip_path
    assert itemrando is None
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_deps.py -v`
Expected: FAIL with "ModuleNotFoundError: No module named 'speedfog.deps'"

- [ ] **Step 3: Write the deps module**

```python
# speedfog/deps.py
"""Dependency detection and extraction for SpeedFog.

Handles checking for extracted FogRando/ItemRandomizer dependencies,
finding mod zips in deps/, and extracting them via sfextract.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

# DLLs required from FogRando (via sfextract on FogMod.exe)
FOGRANDO_REQUIRED_DLLS = [
    "FogMod.dll",
    "SoulsFormats.dll",
    "SoulsIds.dll",
    "BouncyCastle.Cryptography.dll",
    "Newtonsoft.Json.dll",
    "YamlDotNet.dll",
    "ZstdNet.dll",
    "DrSwizzler.dll",
]

# Data files extracted from FogRando's eldendata/Base/
FOGRANDO_DATA_FILES = [
    "fog.txt",
    "fogevents.txt",
    "foglocations2.txt",
    "er-common.emedf.json",
]

# DLLs required from Item Randomizer
ITEMRANDO_REQUIRED_DLLS = [
    "RandomizerCommon.dll",
    "Pidgin.dll",
]

# Extra DLLs from Item Randomizer (not from sfextract)
ITEMRANDO_EXTRA_DLLS = [
    "RandomizerCrashFix.dll",
    "RandomizerHelper.dll",
]

# Data files from Item Randomizer's diste/Base/
ITEMRANDO_DATA_FILES = [
    "enemy.txt",
]


def check_fogrando_deps(base_dir: Path) -> bool:
    """Check if FogRando dependencies are extracted."""
    lib_dir = base_dir / "lib"
    if not lib_dir.exists():
        return False
    for dll in FOGRANDO_REQUIRED_DLLS:
        if not (lib_dir / dll).exists():
            return False

    data_dir = base_dir / "data"
    for f in FOGRANDO_DATA_FILES:
        if not (data_dir / f).exists():
            return False

    eldendata = base_dir / "writer" / "FogModWrapper" / "eldendata"
    if not eldendata.exists():
        return False

    return True


def check_itemrando_deps(base_dir: Path) -> bool:
    """Check if Item Randomizer dependencies are extracted."""
    lib_dir = base_dir / "lib"
    for dll in ITEMRANDO_REQUIRED_DLLS:
        if not (lib_dir / dll).exists():
            return False

    diste = base_dir / "writer" / "ItemRandomizerWrapper" / "diste"
    if not diste.exists():
        return False

    data_dir = base_dir / "data"
    for f in ITEMRANDO_DATA_FILES:
        if not (data_dir / f).exists():
            return False

    assets = base_dir / "writer" / "assets"
    for dll in ITEMRANDO_EXTRA_DLLS:
        if not (assets / dll).exists():
            return False

    return True


def find_mod_zips(base_dir: Path) -> tuple[Path | None, Path | None]:
    """Find FogRando and ItemRandomizer zips in deps/.

    Identifies zips by their internal structure:
    - FogRando: contains fog/FogMod.exe
    - ItemRandomizer: contains randomizer/EldenRingRandomizer.exe

    Returns:
        (fogrando_zip, itemrando_zip) tuple, None for missing.
    """
    deps_dir = base_dir / "deps"
    if not deps_dir.exists():
        return None, None

    fogrando_zip = None
    itemrando_zip = None

    for zip_path in sorted(deps_dir.glob("*.zip")):
        try:
            with zipfile.ZipFile(zip_path, "r") as zf:
                names = zf.namelist()
                if any(n == "fog/FogMod.exe" or n.endswith("/fog/FogMod.exe") for n in names):
                    fogrando_zip = zip_path
                elif any(
                    n == "randomizer/EldenRingRandomizer.exe"
                    or n.endswith("/randomizer/EldenRingRandomizer.exe")
                    for n in names
                ):
                    itemrando_zip = zip_path
        except zipfile.BadZipFile:
            continue

    return fogrando_zip, itemrando_zip


def _find_sfextract(base_dir: Path) -> Path | None:
    """Find the sfextract binary."""
    # Check distribution folder first (standalone .exe mode)
    candidates = [
        base_dir / "tools" / "sfextract.exe",
        base_dir / "tools" / "sfextract",
    ]
    for c in candidates:
        if c.exists():
            return c

    # Fall back to system PATH (dev mode)
    result = shutil.which("sfextract")
    if result:
        return Path(result)

    # Check dotnet tools (dev mode)
    dotnet_path = Path.home() / ".dotnet" / "tools" / "sfextract"
    if dotnet_path.exists():
        return dotnet_path

    return None


def _extract_sfextract(
    sfextract: Path, exe_path: Path, output_dir: Path
) -> bool:
    """Extract DLLs from a .NET single-file exe using sfextract."""
    try:
        subprocess.run(
            [str(sfextract), str(exe_path), "-o", str(output_dir)],
            capture_output=True,
            text=True,
            check=True,
        )
        return True
    except subprocess.CalledProcessError as e:
        print(f"Error: sfextract failed: {e.stderr}", file=sys.stderr)
        return False


def extract_fogrando(base_dir: Path, zip_path: Path, sfextract: Path) -> bool:
    """Extract FogRando dependencies from zip."""
    lib_dir = base_dir / "lib"
    data_dir = base_dir / "data"
    eldendata_dest = base_dir / "writer" / "FogModWrapper" / "eldendata"

    temp_dir = Path(tempfile.mkdtemp(prefix="fogrando_"))
    try:
        # Extract zip
        with zipfile.ZipFile(zip_path, "r") as zf:
            zf.extractall(temp_dir)

        fog_dir = temp_dir / "fog"
        exe_path = fog_dir / "FogMod.exe"
        if not exe_path.exists():
            print("Error: FogMod.exe not found in zip", file=sys.stderr)
            return False

        # Extract DLLs via sfextract
        extracted_dir = temp_dir / "extracted"
        extracted_dir.mkdir()
        if not _extract_sfextract(sfextract, exe_path, extracted_dir):
            return False

        # Copy DLLs to lib/
        lib_dir.mkdir(parents=True, exist_ok=True)
        for dll in extracted_dir.glob("*.dll"):
            shutil.copy2(dll, lib_dir / dll.name)
        x64_dir = extracted_dir / "x64"
        if x64_dir.exists():
            for dll in x64_dir.glob("*.dll"):
                shutil.copy2(dll, lib_dir / dll.name)
        libzstd = fog_dir / "libzstd.dll"
        if libzstd.exists():
            shutil.copy2(libzstd, lib_dir / "libzstd.dll")

        # Copy eldendata/
        src_eldendata = fog_dir / "eldendata"
        if not src_eldendata.exists():
            print("Error: eldendata/ not found in zip", file=sys.stderr)
            return False
        if eldendata_dest.exists():
            shutil.rmtree(eldendata_dest)
        shutil.copytree(src_eldendata, eldendata_dest)

        # Copy data files from eldendata/Base/ to data/
        data_dir.mkdir(parents=True, exist_ok=True)
        base_data = src_eldendata / "Base"
        for filename in FOGRANDO_DATA_FILES:
            src = base_data / filename
            if src.exists():
                shutil.copy2(src, data_dir / filename)
            else:
                print(f"Warning: {filename} not found in zip", file=sys.stderr)

        print("FogRando dependencies extracted successfully")
        return True

    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)


def extract_itemrando(base_dir: Path, zip_path: Path, sfextract: Path) -> bool:
    """Extract Item Randomizer dependencies from zip."""
    lib_dir = base_dir / "lib"
    data_dir = base_dir / "data"
    assets_dir = base_dir / "writer" / "assets"
    diste_dest = base_dir / "writer" / "ItemRandomizerWrapper" / "diste"

    temp_dir = Path(tempfile.mkdtemp(prefix="itemrando_"))
    try:
        with zipfile.ZipFile(zip_path, "r") as zf:
            zf.extractall(temp_dir)

        rando_dir = temp_dir / "randomizer"
        exe_path = rando_dir / "EldenRingRandomizer.exe"
        if not exe_path.exists():
            print(
                "Error: EldenRingRandomizer.exe not found in zip",
                file=sys.stderr,
            )
            return False

        # Extract DLLs via sfextract
        extracted_dir = temp_dir / "extracted"
        extracted_dir.mkdir()
        if not _extract_sfextract(sfextract, exe_path, extracted_dir):
            return False

        # Copy required DLLs to lib/
        lib_dir.mkdir(parents=True, exist_ok=True)
        for dll_name in ITEMRANDO_REQUIRED_DLLS:
            src = extracted_dir / dll_name
            if src.exists():
                shutil.copy2(src, lib_dir / dll_name)
            else:
                print(f"Error: {dll_name} not found", file=sys.stderr)
                return False

        # Copy extra DLLs from randomizer/dll/ to writer/assets/
        assets_dir.mkdir(parents=True, exist_ok=True)
        dll_dir = rando_dir / "dll"
        for dll_name in ITEMRANDO_EXTRA_DLLS:
            src = dll_dir / dll_name
            if src.exists():
                shutil.copy2(src, assets_dir / dll_name)

        # Copy data files
        data_dir.mkdir(parents=True, exist_ok=True)
        base_data = rando_dir / "diste" / "Base"
        for filename in ITEMRANDO_DATA_FILES:
            src = base_data / filename
            if src.exists():
                shutil.copy2(src, data_dir / filename)

        # Copy diste/
        src_diste = rando_dir / "diste"
        if not src_diste.exists():
            print("Error: diste/ not found in zip", file=sys.stderr)
            return False
        if diste_dest.exists():
            shutil.rmtree(diste_dest)
        shutil.copytree(src_diste, diste_dest)

        print("Item Randomizer dependencies extracted successfully")
        return True

    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)


def regenerate_derived_data(base_dir: Path) -> bool:
    """Regenerate clusters.json and fog_data.json from fog.txt.

    Imports tools directly (works in both dev and .exe mode).
    """
    data_dir = base_dir / "data"
    tools_dir = base_dir / "tools"

    fog_txt = data_dir / "fog.txt"
    if not fog_txt.exists():
        print(f"Error: fog.txt not found at {fog_txt}", file=sys.stderr)
        return False

    # Import tools by adding tools/ to sys.path temporarily.
    # This is safe because: (1) PyInstaller does not bundle these modules
    # (they are not imported at build time), so no collision risk.
    # (2) pyyaml and tomli are bundled by PyInstaller as speedfog deps,
    # so the tools' imports resolve correctly in frozen mode.
    sys.path.insert(0, str(tools_dir))
    try:
        import generate_clusters as gc_mod
        import extract_fog_data as efd_mod

        gc_mod.run(
            fog_txt=fog_txt,
            output=data_dir / "clusters.json",
            metadata=data_dir / "zone_metadata.toml",
        )
        print("Generated: clusters.json")

        efd_mod.run(
            fog_txt=fog_txt,
            output=data_dir / "fog_data.json",
        )
        print("Generated: fog_data.json")

    except Exception as e:
        print(f"Error generating derived data: {e}", file=sys.stderr)
        return False
    finally:
        sys.path.pop(0)
        # Clean up imported modules to avoid stale references
        for mod_name in ["generate_clusters", "extract_fog_data"]:
            sys.modules.pop(mod_name, None)

    return True


def ensure_deps(base_dir: Path, item_rando_required: bool = True) -> bool:
    """Check and extract dependencies if needed.

    Args:
        base_dir: Root directory for path resolution.
        item_rando_required: Whether Item Randomizer deps are needed.
            Set to False when item_randomizer.enabled is False in config.

    Returns True if all dependencies are available, False on error.
    """
    fogrando_ok = check_fogrando_deps(base_dir)
    itemrando_ok = not item_rando_required or check_itemrando_deps(base_dir)

    if fogrando_ok and itemrando_ok:
        return True

    # Find sfextract
    sfextract = _find_sfextract(base_dir)
    if sfextract is None:
        print(
            "Error: sfextract not found. Place sfextract.exe in tools/ "
            "or install via: dotnet tool install -g sfextract",
            file=sys.stderr,
        )
        return False

    # Find zips
    fogrando_zip, itemrando_zip = find_mod_zips(base_dir)

    if not fogrando_ok:
        if fogrando_zip is None:
            print(
                "Error: FogRando dependencies missing.\n"
                "Download Fog Gate Randomizer from:\n"
                "  https://www.nexusmods.com/eldenring/mods/3295\n"
                "and place the .zip file in the deps/ folder.",
                file=sys.stderr,
            )
            return False
        print(f"Extracting FogRando from {fogrando_zip.name}...")
        if not extract_fogrando(base_dir, fogrando_zip, sfextract):
            return False

    if item_rando_required and not itemrando_ok:
        if itemrando_zip is None:
            print(
                "Error: Item Randomizer dependencies missing.\n"
                "Download Elden Ring Item and Enemy Randomizer from:\n"
                "  https://www.nexusmods.com/eldenring/mods/428\n"
                "and place the .zip file in the deps/ folder.",
                file=sys.stderr,
            )
            return False
        print(f"Extracting Item Randomizer from {itemrando_zip.name}...")
        if not extract_itemrando(base_dir, itemrando_zip, sfextract):
            return False

    # Generate derived data (clusters.json, fog_data.json)
    clusters_path = base_dir / "data" / "clusters.json"
    fog_data_path = base_dir / "data" / "fog_data.json"
    if not clusters_path.exists() or not fog_data_path.exists():
        print("Generating derived data...")
        if not regenerate_derived_data(base_dir):
            return False

    return True
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_deps.py -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add speedfog/deps.py tests/test_deps.py
git commit -m "feat: add dependency detection and extraction module"
```

---

### Task 5: Integrate Dependency Check into Main Entry Point

**Files:**
- Modify: `speedfog/main.py`

Add a `--skip-deps-check` flag and call `ensure_deps()` before DAG generation.

- [ ] **Step 1: Add deps check to main.py**

Add the CLI argument after existing arguments:

```python
    parser.add_argument(
        "--skip-deps-check",
        action="store_true",
        help="Skip dependency extraction check (assume deps are in place)",
    )
```

Add the import at the top:

```python
from speedfog.deps import ensure_deps
```

Add the deps check right after config loading (before "Find clusters.json"), around line 147:

```python
    # Check dependencies (extract from zips if needed)
    if not args.skip_deps_check and not args.no_build:
        if not ensure_deps(
            project_root,
            item_rando_required=config.item_randomizer.enabled,
        ):
            return 1
```

- [ ] **Step 2: Run existing tests to verify no regressions**

Run: `pytest tests/ -v`
Expected: All pass (existing tests don't trigger deps check because they don't call main())

- [ ] **Step 3: Commit**

```bash
git add speedfog/main.py
git commit -m "feat: integrate dependency check into main entry point"
```

---

### Task 6: Output Zip Packaging

**Files:**
- Create: `speedfog/packaging.py`
- Create: `tests/test_packaging.py`
- Modify: `speedfog/main.py`

Add the ability to zip the seed output into `output/<seed>.zip`.

- [ ] **Step 1: Write tests**

```python
# tests/test_packaging.py
"""Tests for output zip packaging."""

from __future__ import annotations

import zipfile
from pathlib import Path

from speedfog.packaging import package_seed_zip


def test_package_seed_zip_creates_zip(tmp_path):
    """package_seed_zip creates a zip containing the seed directory contents."""
    # Create a fake seed directory
    seed_dir = tmp_path / "seeds" / "12345"
    seed_dir.mkdir(parents=True)
    (seed_dir / "graph.json").write_text('{"version": "4.2"}')
    logs_dir = seed_dir / "logs"
    logs_dir.mkdir()
    (logs_dir / "spoiler.txt").write_text("spoiler content")

    output_dir = tmp_path / "output"
    output_dir.mkdir()

    result = package_seed_zip(seed_dir, output_dir)

    assert result == output_dir / "12345.zip"
    assert result.exists()

    # Verify zip contents
    with zipfile.ZipFile(result, "r") as zf:
        names = zf.namelist()
        assert "graph.json" in names
        assert "logs/spoiler.txt" in names


def test_package_seed_zip_overwrites_existing(tmp_path):
    """package_seed_zip overwrites an existing zip."""
    seed_dir = tmp_path / "seeds" / "99999"
    seed_dir.mkdir(parents=True)
    (seed_dir / "graph.json").write_text('{"new": true}')

    output_dir = tmp_path / "output"
    output_dir.mkdir()

    # Create an old zip
    old_zip = output_dir / "99999.zip"
    old_zip.write_text("old")

    result = package_seed_zip(seed_dir, output_dir)
    assert result.exists()

    # Verify it's a valid zip (not the old text content)
    with zipfile.ZipFile(result, "r") as zf:
        assert "graph.json" in zf.namelist()


def test_package_seed_zip_creates_output_dir(tmp_path):
    """package_seed_zip creates the output directory if it doesn't exist."""
    seed_dir = tmp_path / "seeds" / "42"
    seed_dir.mkdir(parents=True)
    (seed_dir / "graph.json").write_text("{}")

    output_dir = tmp_path / "output"
    # Don't create output_dir

    result = package_seed_zip(seed_dir, output_dir)
    assert result.exists()
    assert output_dir.exists()
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_packaging.py -v`
Expected: FAIL with "ModuleNotFoundError"

- [ ] **Step 3: Write the implementation**

```python
# speedfog/packaging.py
"""Output zip packaging for SpeedFog seed distribution."""

from __future__ import annotations

import zipfile
from pathlib import Path


def package_seed_zip(seed_dir: Path, output_dir: Path) -> Path:
    """Package a seed directory into a zip file.

    Creates output_dir/<seed_name>.zip containing all files from seed_dir.

    Args:
        seed_dir: Directory containing the generated seed (graph.json, mods/, etc.)
        output_dir: Directory where the zip file will be created.

    Returns:
        Path to the created zip file.
    """
    output_dir.mkdir(parents=True, exist_ok=True)
    seed_name = seed_dir.name
    zip_path = output_dir / f"{seed_name}.zip"

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for file_path in sorted(seed_dir.rglob("*")):
            if file_path.is_file():
                arcname = file_path.relative_to(seed_dir)
                zf.write(file_path, arcname)

    return zip_path
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_packaging.py -v`
Expected: PASS

- [ ] **Step 5: Add --zip flag and integrate into main.py**

Add CLI arguments:

```python
    parser.add_argument(
        "--zip",
        action="store_true",
        default=None,
        help="Package output as a zip file in output/ (default in .exe mode)",
    )
    parser.add_argument(
        "--no-zip",
        action="store_true",
        help="Disable zip packaging even in .exe mode",
    )
```

Add imports:

```python
import sys
from speedfog.packaging import package_seed_zip
```

Add zip packaging at the end of main(), just before the timer summary (before `total = timer.stop()`):

```python
    # Package as zip: explicit --zip, or auto in frozen (.exe) mode
    should_zip = args.zip or (getattr(sys, "frozen", False) and not args.no_zip)
    if should_zip and not args.no_build:
        timer.step("Package zip")
        zip_output_dir = project_root / "output"
        zip_path = package_seed_zip(seed_dir, zip_output_dir)
        print(f"Packaged: {zip_path}")
```

- [ ] **Step 6: Run all tests**

Run: `pytest tests/ -v`
Expected: All pass

- [ ] **Step 7: Commit**

```bash
git add speedfog/packaging.py tests/test_packaging.py speedfog/main.py
git commit -m "feat: add zip packaging for seed output"
```

---

### Task 7: PyInstaller Configuration

**Files:**
- Create: `speedfog.spec`
- Modify: `pyproject.toml`

Create the PyInstaller spec file for building the standalone distribution.

- [ ] **Step 1: Add pyinstaller to dev dependencies in pyproject.toml**

Add `"pyinstaller>=6.0"` to the `dev` optional dependencies list:

```toml
[project.optional-dependencies]
dev = [
    "pytest>=7.0",
    "pytest-cov>=4.0",
    "ruff>=0.4.0",
    "mypy>=1.10",
    "pre-commit>=3.0",
    "pyinstaller>=6.0",
]
```

- [ ] **Step 2: Create the PyInstaller spec file**

```python
# speedfog.spec
# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller spec for SpeedFog standalone distribution.

Build with: pyinstaller speedfog.spec

The output is a folder (not a single file) containing:
- speedfog.exe (Python runtime + speedfog code)
- Internal PyInstaller support files

The distribution folder also needs (placed manually or by build script):
- config.toml (example config)
- deps/ (empty, for user to drop mod zips)
- tools/sfextract.exe (for dependency extraction)
- tools/generate_clusters.py (for derived data generation)
- tools/extract_fog_data.py (for derived data generation)
- writer/FogModWrapper/publish/win-x64/ (pre-compiled wrapper)
- writer/ItemRandomizerWrapper/publish/win-x64/ (pre-compiled wrapper)
- data/care_package_items.toml (tracked data)
- data/zone_metadata.toml (tracked data)
- data/i18n/ (translations)
"""

import os

block_cipher = None

a = Analysis(
    ['speedfog/main.py'],
    pathex=[],
    binaries=[],
    datas=[
        # Data files are NOT bundled here; they are placed by
        # tools/build_distribution.sh into the distribution folder.
        # This keeps the PyInstaller build simple and avoids duplication.
    ],
    hiddenimports=[
        'tomli',
        'yaml',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='speedfog',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='speedfog',
)
```

- [ ] **Step 3: Create a build script for assembling the distribution**

```bash
# tools/build_distribution.sh
#!/usr/bin/env bash
# Build the SpeedFog standalone distribution.
#
# Prerequisites:
#   - Python dependencies installed (uv pip install -e ".[dev]")
#   - setup_dependencies.py already run (C# wrappers compiled)
#   - sfextract binary available
#
# Output: dist/speedfog-v<version>/

set -euo pipefail

VERSION="0.1.0"
DIST_DIR="dist/speedfog-v${VERSION}"

echo "Building SpeedFog v${VERSION} distribution..."

# 1. Run PyInstaller
echo "Step 1: PyInstaller build..."
pyinstaller speedfog.spec --noconfirm

# 2. Create distribution structure
echo "Step 2: Assembling distribution..."
rm -rf "${DIST_DIR}"
mkdir -p "${DIST_DIR}"

# Copy PyInstaller output
cp -r dist/speedfog/* "${DIST_DIR}/"

# Copy example config
cp config.example.toml "${DIST_DIR}/config.toml"

# Create deps directory (empty, for user)
mkdir -p "${DIST_DIR}/deps"

# Copy tools needed at runtime
mkdir -p "${DIST_DIR}/tools"
cp tools/generate_clusters.py "${DIST_DIR}/tools/"
cp tools/extract_fog_data.py "${DIST_DIR}/tools/"
# sfextract.exe must be provided separately (download from GitHub releases)
# cp /path/to/sfextract.exe "${DIST_DIR}/tools/"

# Copy pre-compiled C# wrappers
mkdir -p "${DIST_DIR}/writer/FogModWrapper/publish"
cp -r writer/FogModWrapper/publish/win-x64 "${DIST_DIR}/writer/FogModWrapper/publish/"

mkdir -p "${DIST_DIR}/writer/ItemRandomizerWrapper/publish"
cp -r writer/ItemRandomizerWrapper/publish/win-x64 "${DIST_DIR}/writer/ItemRandomizerWrapper/publish/"

# Copy tracked data
mkdir -p "${DIST_DIR}/data/i18n"
cp data/care_package_items.toml "${DIST_DIR}/data/"
cp data/zone_metadata.toml "${DIST_DIR}/data/"
cp data/item_preset.yaml "${DIST_DIR}/data/" 2>/dev/null || true
cp -r data/i18n/* "${DIST_DIR}/data/i18n/" 2>/dev/null || true

# Create output directory
mkdir -p "${DIST_DIR}/output"

# 3. Create distribution zip
echo "Step 3: Creating zip..."
cd dist
zip -r "speedfog-v${VERSION}.zip" "speedfog-v${VERSION}/"
cd ..

echo ""
echo "Distribution built: dist/speedfog-v${VERSION}.zip"
echo ""
echo "Before distributing, place sfextract.exe in:"
echo "  ${DIST_DIR}/tools/sfextract.exe"
```

- [ ] **Step 4: Make build script executable**

Run: `chmod +x tools/build_distribution.sh`

- [ ] **Step 5: Commit**

```bash
git add speedfog.spec tools/build_distribution.sh pyproject.toml
git commit -m "feat: add PyInstaller spec and build script for standalone distribution"
```

---

### Task 8: Validation and Game Dir Check

**Files:**
- Modify: `speedfog/deps.py`
- Modify: `tests/test_deps.py`

Add game_dir validation to the deps module (check that the directory contains expected game files).

- [ ] **Step 1: Write test**

Add to `tests/test_deps.py`:

```python
from speedfog.deps import validate_game_dir


def test_validate_game_dir_valid(tmp_path):
    """Returns True when game dir contains regulation.bin."""
    game_dir = tmp_path / "Game"
    game_dir.mkdir()
    (game_dir / "regulation.bin").touch()
    assert validate_game_dir(game_dir) is True


def test_validate_game_dir_missing_regulation(tmp_path):
    """Returns False when regulation.bin is missing."""
    game_dir = tmp_path / "Game"
    game_dir.mkdir()
    assert validate_game_dir(game_dir) is False


def test_validate_game_dir_not_exists(tmp_path):
    """Returns False when directory doesn't exist."""
    assert validate_game_dir(tmp_path / "nonexistent") is False
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_deps.py::test_validate_game_dir_valid -v`
Expected: FAIL

- [ ] **Step 3: Add validate_game_dir to deps.py**

```python
def validate_game_dir(game_dir: Path) -> bool:
    """Validate that a directory looks like ELDEN RING/Game.

    Checks for regulation.bin which is always present in the game directory.
    """
    if not game_dir.exists():
        return False
    return (game_dir / "regulation.bin").exists()
```

- [ ] **Step 4: Run tests**

Run: `pytest tests/test_deps.py -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add speedfog/deps.py tests/test_deps.py
git commit -m "feat: add game directory validation"
```

---

### Task 9: Smoke Test Checklist

Manual verification that the distribution works end-to-end. This cannot be fully automated (requires game files and Nexusmods zips).

- [ ] **Step 1: Verify dev mode still works**

Run from project root (all existing tests + a manual generation):

```bash
pytest tests/ -v
uv run speedfog config.toml --logs --no-build
```

Expected: all tests pass, graph.json generated.

- [ ] **Step 2: Verify build script produces distribution**

```bash
bash tools/build_distribution.sh
ls -la dist/speedfog-v0.1.0/
```

Expected: distribution folder contains speedfog.exe, config.toml, deps/, data/, writer/, tools/, output/.

- [ ] **Step 3: Verify .exe launches and shows help**

```bash
cd dist/speedfog-v0.1.0
wine speedfog.exe --help
```

Expected: help text displayed, no import errors.

- [ ] **Step 4: Verify deps extraction (requires Nexusmods zips)**

Place FogRando.zip and ItemRandomizer.zip in `dist/speedfog-v0.1.0/deps/`, then:

```bash
cd dist/speedfog-v0.1.0
wine speedfog.exe config.toml --no-build
```

Expected: deps extracted automatically, graph.json generated.

- [ ] **Step 5: Verify full pipeline (requires game files)**

```bash
cd dist/speedfog-v0.1.0
wine speedfog.exe config.toml --game-dir /path/to/Game
```

Expected: seed .zip created in output/.

- [ ] **Step 6: Document results and commit**

Note any issues found during smoke testing. Fix blockers before proceeding.

---

### Task 10: Run Code Review

Run the code review agent to verify all changes against the spec before finalizing.

- [ ] **Step 1: Run code review agent**

Use `superpowers:requesting-code-review` to review all changes against the spec at `docs/plans/2026-03-27-standalone-exe-distribution.md`.

- [ ] **Step 2: Address any feedback**

Fix issues identified by the review.

- [ ] **Step 3: Final commit if needed**
