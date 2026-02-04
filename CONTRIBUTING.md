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
git clone https://github.com/user/speedfog.git
cd speedfog

# Install Python dependencies (from project root)
uv pip install -e ".[dev]"

# Install sfextract
dotnet tool install -g sfextract

# Download FogRando from Nexusmods (requires account):
# https://www.nexusmods.com/eldenring/mods/3295

# Extract FogRando dependencies
python tools/setup_dependencies.py /path/to/FogRando.zip

# Build C# writer
cd writer/FogModWrapper && dotnet build
```

### Updating FogRando Dependencies

When a new version of FogRando is released:

```bash
python tools/setup_dependencies.py /path/to/NewFogRando.zip --force
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
│   └── FogModWrapper/           # Main writer
│       ├── Program.cs           # CLI entry point
│       ├── GraphLoader.cs       # Load graph.json from Python
│       ├── ConnectionInjector.cs # Inject connections into FogMod
│       └── eldendata/           # Game data (gitignored, from FogRando)
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
│   ├── setup_dependencies.py        # Extract FogRando dependencies
│   ├── generate_clusters.py     # Generate clusters.json
│   └── extract_fog_data.py      # Generate fog_data.json
│
├── reference/                   # FogRando source (READ-ONLY)
│   └── fogrando-src/            # Decompiled C# for reference
│
├── docs/                        # Documentation
│   ├── architecture.md          # System architecture
│   └── plans/                   # Design documents
│
├── seeds/                       # Generated seeds (gitignored)
└── output/                      # Generated mod (gitignored)
```

## Architecture

SpeedFog uses a hybrid Python + C# architecture:

```
Python (speedfog/)          C# (writer/)                 Output
──────────────────          ──────────────────           ──────────────────
config.toml            →                                 seeds/<seed>/
clusters.json          →    graph.json → FogModWrapper → ├── graph.json
DAG generation         →                                 └── spoiler.txt

                            FogModWrapper + game files → output/
                                                         ├── mod/
                                                         ├── ModEngine/
                                                         └── launch_speedfog.bat
```

- **Python**: Configuration, zone clustering, DAG generation (package at root)
- **C#**: Thin wrapper around FogMod.dll for mod file generation
- **Interface**: `graph.json` passes DAG from Python to C#

See [docs/architecture.md](docs/architecture.md) for details.

## Data Flow

1. `fog.txt` → `generate_clusters.py` → `clusters.json`
2. `fog.txt` → `extract_fog_data.py` → `fog_data.json`
3. `config.toml` + `clusters.json` → `speedfog` CLI → `graph.json`
4. `graph.json` + game files → `FogModWrapper` → mod files

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

### Reference Code

Files in `reference/` are **read-only** - extracted from FogRando for study.

When implementing features:
1. First check how FogRando does it in `reference/fogrando-src/`
2. Document source line numbers when adapting code
3. Aim for behavioral parity with FogRando

### Debugging In-Game Issues

For any in-game problem (fog gates, warps, scaling):
1. Find the equivalent FogRando implementation in `reference/`
2. Compare our output with FogRando's expected behavior
3. Check event templates in `data/fogevents.txt`

### FogRando Reference Points

Key sections in `reference/fogrando-src/GameDataWriterE.cs`:

| Feature | Lines | Notes |
|---------|-------|-------|
| Load game data | L37-70 | MSBs, EMEVDs, params |
| Fog models | L190-194 | AEG099_230/231/232 |
| Create fog gate | L262+ | `addFakeGate` helper |
| EMEVD events | L1781-1852 | Event creation |
| Scaling | L1964-1966 | EldenScaling integration |
| Write output | L4977-5030 | Params, EMEVDs |

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
