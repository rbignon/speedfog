# Contributing to SpeedFog

## Development Setup

### Prerequisites

- Python 3.10+ with [uv](https://github.com/astral-sh/uv)
- .NET 8.0 SDK
- Wine (Linux only, for running the C# writer)
- [sfextract](https://github.com/Droppers/SingleFileExtractor) dotnet tool

### Initial Setup

```bash
# Clone the repository
git clone https://github.com/rbignon/speedfog.git
cd speedfog

# Install Python dependencies (from project root)
uv pip install -e ".[dev]"

# Install sfextract
dotnet tool install -g sfextract

# Download from Nexusmods (requires account):
# - FogRando: https://www.nexusmods.com/eldenring/mods/3295
# - Item Randomizer (optional): https://www.nexusmods.com/eldenring/mods/428

# Extract dependencies, generate derived data, and build C# writers
python tools/bootstrap.py \
  --fogrando /path/to/FogRando.zip \
  --itemrando /path/to/ItemRandomizer.zip

# Or FogRando only (no item randomization)
python tools/bootstrap.py --fogrando /path/to/FogRando.zip
```

### Updating FogRando Dependencies

When a new version of FogRando is released:

```bash
python tools/bootstrap.py --fogrando /path/to/NewFogRando.zip --force

# With Item Randomizer
python tools/bootstrap.py \
  --fogrando /path/to/NewFogRando.zip \
  --itemrando /path/to/NewItemRandomizer.zip \
  --force
```

## Project Structure

```
speedfog/
├── pyproject.toml               # Python project config (at root)
├── speedfog/                    # Python package - DAG generation
│   ├── __init__.py
│   ├── main.py                  # CLI entry point
│   ├── config.py                # Configuration loading
│   ├── dag.py                   # DAG data structures
│   ├── generator.py             # DAG generation algorithm
│   └── ...
│
├── tests/                       # Python tests
│
├── writer/                      # C# - Mod file generation
│   ├── lib/                     # DLLs (gitignored, from FogRando)
│   ├── assets/                  # Extra DLLs (RandomizerCrashFix, etc.)
│   ├── scripts/                 # Launchers and backup scripts
│   ├── FogModWrapper.Core/      # Shared library (GraphLoader, models)
│   ├── FogModWrapper/           # Fog gate writer (calls FogMod.dll)
│   │   ├── Program.cs           # CLI entry point
│   │   ├── ConnectionInjector.cs # Inject connections into FogMod
│   │   └── eldendata/           # Game data (gitignored, from FogRando)
│   ├── FogModWrapper.Tests/     # xUnit tests
│   ├── ItemRandomizerWrapper/   # Item randomizer (calls RandomizerCommon.dll)
│   │   ├── Program.cs           # CLI entry point
│   │   └── diste/               # Item Randomizer data (gitignored)
│   └── ItemRandomizerWrapper.Tests/  # xUnit tests
│
├── data/                        # Shared data files
│   ├── fog.txt                  # FogRando zones (gitignored)
│   ├── fogevents.txt            # EMEVD templates (gitignored)
│   ├── foglocations2.txt        # Enemy areas (gitignored)
│   ├── er-common.emedf.json     # EMEVD definitions (gitignored)
│   ├── clusters.json            # Generated clusters (gitignored)
│   ├── fog_data.json            # Generated fog metadata (gitignored)
│   └── zone_metadata.toml       # Zone weights (tracked)
│
├── tools/                       # Standalone scripts
│   ├── bootstrap.py             # Project bootstrap (extract deps, build, generate data)
│   ├── generate_clusters.py     # Generate clusters.json
│   └── extract_fog_data.py      # Generate fog_data.json
│
├── docs/                        # Documentation
│   ├── architecture.md          # System architecture
│   ├── dag-generation.md        # DAG generation algorithm
│   ├── item-randomizer.md       # ItemRandomizerWrapper integration
│   ├── care-package.md          # Randomized starting build system
│   └── ...                      # See docs/ for full list
│
└── seeds/                       # Generated runs (gitignored)
```

## Architecture

SpeedFog uses a hybrid Python + C# architecture:

```
Python (speedfog/)      C# (writer/)                              Output
─────────────────       ─────────────────                         ─────────────────
config.toml        →                                              seeds/<seed>/
clusters.json      →    graph.json → FogModWrapper ──────────┐    ├── mods/
DAG generation     →                      ↑                  ├──► ├── ModEngine/
                        item_config.json → ItemRandomizerWrapper  ├── launch_speedfog.bat
                                          (optional)              └── spoiler.txt
```

- **Python**: Configuration, zone clustering, DAG generation (package at root)
- **C#**: Thin wrappers around FogMod.dll and RandomizerCommon.dll
- **Interface**: `graph.json` passes DAG from Python to C#

See [docs/architecture.md](docs/architecture.md) for details.

## Data Flow

1. `fog.txt` → `generate_clusters.py` → `clusters.json`
2. `fog.txt` → `extract_fog_data.py` → `fog_data.json`
3. `config.toml` + `clusters.json` → `speedfog` CLI → `graph.json`
4. `item_config.json` + game files → `ItemRandomizerWrapper` → randomized items (optional)
5. `graph.json` + game files → `FogModWrapper` (merges item randomizer output) → mod files

## Running Tests

```bash
# Python - all tests (from project root)
pytest -v

# Python - with coverage
pytest --cov=speedfog

# C# - integration test
cd writer/test && ./run_integration.sh
```

## Code Style

### Python

- Formatter: `ruff format`
- Linter: `ruff check`
- Type checker: `mypy`

Pre-commit hooks run automatically on commit.

### C#

- Formatter: `dotnet format`
- Follow FogRando patterns for EMEVD/param manipulation

## Key Guidelines

### FogRando Parity

SpeedFog aims for behavioral parity with FogRando. When implementing features:
1. Aim to match FogRando's in-game behavior
2. Event templates come directly from `data/fogevents.txt` (copied from FogRando)

### Debugging In-Game Issues

For any in-game problem (fog gates, warps, scaling):
1. Compare our output with FogRando's expected behavior
2. Check event templates in `data/fogevents.txt`
3. Use `tools/dump_emevd_warps/` to inspect compiled EMEVD files

## Commits

- Create commits frequently
- Use conventional commit messages:
  - `feat:` new features
  - `fix:` bug fixes
  - `docs:` documentation
  - `refactor:` code restructuring
  - `test:` test changes

## Pull Requests

1. Create a feature branch
2. Make changes with tests
3. Ensure all tests pass
4. Update documentation if needed
5. Submit PR with clear description

## Questions?

Open an issue for questions or discussion.
