# SpeedFog Standalone .exe Distribution

## Goal

Distribute the SpeedFog seed generator as a standalone Windows .exe so players can
generate ready-to-play seed .zip files without installing Python or .NET. The primary
use case is speedfog-racing: an organizer generates a seed .zip locally, then uploads
it to the racing platform for other players to download.

## Constraints

- **Cannot redistribute** FogMod or ItemRandomizer DLLs/data (Nexusmods licensing)
- Players must provide the mod .zip files themselves
- The .exe must produce a complete, self-contained mod output (same as today's pipeline)
- `game_dir` (ELDEN RING/Game folder) is always required

## Approach

PyInstaller single-file .exe bundling the Python code, pre-compiled C# wrappers, and
`sfextract` (MIT license). At runtime, the .exe extracts DLLs and game data from
user-provided Nexusmods .zip files on first launch.

## Distribution Structure

```
speedfog-v1.0/
├── speedfog.exe              # PyInstaller binary
├── config.toml               # Player config (editable)
├── deps/                     # Player drops Nexusmods zips here
│   ├── (FogRando.zip)
│   └── (ItemRandomizer.zip)
├── data/                     # Generated on first launch
│   ├── clusters.json
│   ├── fog_data.json
│   ├── fog.txt
│   ├── fogevents.txt
│   ├── foglocations2.txt
│   └── ...
├── lib/                      # DLLs extracted on first launch
├── writer/                   # C# wrappers + extracted game data
│   ├── FogModWrapper/
│   └── ItemRandomizerWrapper/
└── output/                   # Generated seed zips
    ├── 212559448.zip
    └── ...
```

Players receive a .zip containing `speedfog.exe`, an example `config.toml`, and an
empty `deps/` folder. They drop the Nexusmods zips into `deps/`, edit `config.toml`,
and run `speedfog.exe`.

## Execution Flow

```
speedfog.exe config.toml
     │
     ├─ 1. Path resolution (base_dir = directory containing the .exe)
     │
     ├─ 2. Dependency check
     │     ├─ lib/ exists and contains DLLs?
     │     │     └─ NO → Check deps/*.zip
     │     │           ├─ Zips found → Extract via bundled sfextract
     │     │           │   ├─ DLLs → lib/
     │     │           │   ├─ eldendata/ → writer/FogModWrapper/
     │     │           │   ├─ diste/ → writer/ItemRandomizerWrapper/
     │     │           │   └─ fog.txt, fogevents.txt, etc. → data/
     │     │           └─ Zips missing → Clear error:
     │     │                 "Place FogRando.zip and ItemRandomizer.zip in deps/"
     │     └─ YES → Continue
     │
     ├─ 3. Derived data generation (if missing)
     │     ├─ clusters.json (from fog.txt)
     │     └─ fog_data.json (from fog.txt)
     │
     ├─ 4. DAG generation (Python, unchanged)
     │     └─ graph.json + logs in temp directory
     │
     ├─ 5. ItemRandomizerWrapper.exe (subprocess, if enabled in config)
     │     └─ Produces randomized items → temp/item-randomizer/
     │
     ├─ 6. FogModWrapper.exe (subprocess)
     │     └─ Produces mod files (EMEVD, params, MSB)
     │     └─ Merges item randomizer output if present
     │
     └─ 7. Packaging
           └─ Zip everything → output/<seed>.zip
```

The output .zip contains the same content as today's pipeline: graph.json, logs,
mod files, ModEngine 2, and the launcher script.

## What Gets Bundled in the .exe (PyInstaller)

**Embedded at build time:**
- Python code (speedfog/ package)
- `sfextract` binary (MIT license)
- `writer/FogModWrapper/publish/win-x64/` (pre-compiled C# wrapper)
- `writer/ItemRandomizerWrapper/publish/win-x64/` (pre-compiled C# wrapper)
- `tools/generate_clusters.py` and `tools/extract_fog_data.py` (derived data generators)
- Tracked data: `care_package_items.toml`, `zone_metadata.toml`, `i18n/`

**Not embedded (extracted at runtime from player-provided zips):**
- DLLs: FogMod.dll, SoulsFormats.dll, RandomizerCommon.dll, etc.
- Game data: `eldendata/`, `diste/`, `fog.txt`, `fogevents.txt`, etc.

**Produced at runtime:**
- `clusters.json`, `fog_data.json` (generated from fog.txt)
- Seed .zip files in `output/`

## Code Changes Required

### Path resolution refactor

Replace `Path(__file__).parent.parent` with a `base_dir` that resolves to the
directory containing the .exe (or the project root in dev mode):

```python
import sys

def get_base_dir() -> Path:
    if getattr(sys, 'frozen', False):
        # PyInstaller: base_dir is the directory containing the .exe
        return Path(sys.executable).parent
    else:
        # Dev mode: base_dir is the project root
        return Path(__file__).parent.parent
```

All path references throughout the codebase use `base_dir` instead of hardcoded
relative paths from `__file__`.

### New setup module

A `speedfog/setup.py` module that handles:
- Detecting presence of extracted dependencies (`lib/`, `data/fog.txt`, etc.)
- Locating .zip files in `deps/`
- Running `sfextract` to extract DLLs from single-file .NET executables
- Placing extracted files in the correct locations
- Calling `generate_clusters.py` and `extract_fog_data.py` for derived data

This reuses the logic from `tools/setup_dependencies.py` but adapted for the
standalone context (no external tool dependencies, paths relative to base_dir).

### Output packaging

Add zip packaging after the mod generation step:
- Collect all output files (mod, ModEngine, launcher, graph.json, logs)
- Create `output/<seed>.zip`

### PyInstaller spec file

A `speedfog.spec` file describing:
- Entry point: `speedfog/main.py`
- Bundled binaries: C# wrappers, sfextract
- Bundled data: tracked data files
- Hidden imports if needed (tomli, yaml)

## Error Handling

- **Missing zips**: explicit message listing which files to place in `deps/` with
  Nexusmods URLs
- **Invalid zips**: if `sfextract` fails, indicate the zip format is not recognized
  (wrong FogRando version?)
- **Invalid game_dir**: verify the directory exists and contains expected files
  (e.g., `regulation.bin`) before launching wrappers
- **Wrapper failures**: capture stdout/stderr and display to the player
- **Invalid config**: existing validation in `config.py` with clear messages

## Build Workflow (Developer)

1. `python tools/setup_dependencies.py --fogrando ... --itemrando ...` (unchanged)
2. `pyinstaller speedfog.spec` (new, bundles pre-compiled artifacts)
3. Distribute the resulting `speedfog-v1.0/` folder as a .zip

PyInstaller runs under Wine with Windows Python installed (same environment as C#
wrapper compilation). Alternatively, a GitHub Actions Windows runner could handle
the build.

## Versioning

The .exe is tied to a specific FogRando version (the one used at build time). In v1,
a simple "tested with FogRando vX.Y" note in distribution README suffices. Future
versions could store an expected hash or version number and warn on mismatch.

## Future: GUI Launcher

A separate `speedfog-gui.exe` (not part of this spec) could provide a graphical
config.toml editor with a "Generate" button that launches `speedfog.exe` as a
subprocess. Architecture: the GUI is a pure frontend, `speedfog.exe` remains the
CLI backend. Recommended framework: customtkinter (modern look, small bundle
overhead).
