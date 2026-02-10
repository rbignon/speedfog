# SpeedFog

Elden Ring mod that generates short randomized runs (~1 hour) with a controlled DAG structure.

## Project Context

SpeedFog creates focused paths from Chapel of Anticipation to a random major boss with:
- Balanced parallel branches (no disadvantaged paths)
- No dead ends (all paths lead to the end)
- Configurable parameters (bosses, dungeons, duration)

Unlike FogRando which randomizes the entire world, SpeedFog generates a smaller, curated experience.

## Architecture

**Hybrid Python + C#:**

```
Python (speedfog/)      C# (writer/)                              Output
─────────────────       ─────────────────                         ─────────────────
config.toml        →                                              output/
clusters.json      →    graph.json → FogModWrapper ──────────┐    ├── mod/
DAG generation     →                      ↑                  ├──► ├── ModEngine/
                        item_config.json → ItemRandomizerWrapper  ├── launch_speedfog.bat
                                          (optional)              └── spoiler.txt
```

- **Python**: Configuration, cluster/zone data, DAG generation algorithm (package at root)
- **C#**:
  - FogModWrapper - thin wrapper calling FogMod.dll with our graph connections
  - ItemRandomizerWrapper - thin wrapper calling RandomizerCommon.dll for item randomization (optional)
- **Output**: Self-contained folder with ModEngine 2 (auto-downloaded)

### Item Randomization Workflow

When item randomization is enabled, the workflow is:
1. **ItemRandomizerWrapper** runs first → outputs to `temp/item-randomizer/`
2. **FogModWrapper** runs with `--merge-dir temp/item-randomizer/` → merges item changes
3. Final output contains both fog gate randomization and item randomization

## Directory Structure

```
speedfog/
├── pyproject.toml           # Python project config (at root)
├── speedfog/                # Python package - DAG generation
│   ├── __init__.py
│   ├── main.py              # CLI entry point
│   ├── config.py            # Configuration loading
│   ├── dag.py               # DAG data structures
│   └── generator.py         # DAG generation algorithm
├── tests/                   # Python tests
├── data/                    # Shared data files
│   ├── fog.txt              # FogRando zone definitions (gitignored)
│   ├── foglocations2.txt    # FogRando enemy areas (gitignored)
│   ├── fogevents.txt        # FogRando event templates (gitignored)
│   ├── er-common.emedf.json # EMEVD instruction definitions (gitignored)
│   ├── clusters.json        # Generated zone clusters (gitignored)
│   ├── fog_data.json        # Generated fog gate metadata (gitignored)
│   └── zone_metadata.toml   # Zone weight config (tracked)
├── writer/                  # C# - Mod file generation
│   ├── lib/                 # DLLs (FogMod, RandomizerCommon, SoulsFormats, etc.)
│   ├── assets/              # Extra DLLs (RandomizerCrashFix, RandomizerHelper)
│   ├── FogModWrapper/       # Fog gate writer - thin wrapper calling FogMod.dll
│   │   ├── Program.cs       # CLI entry point
│   │   ├── GraphLoader.cs   # Load graph.json v4
│   │   ├── ConnectionInjector.cs  # Inject connections into FogMod Graph
│   │   ├── StartingItemInjector.cs  # Inject starting item events into EMEVD
│   │   ├── StartingResourcesInjector.cs  # Inject runes, seeds, tears
│   │   ├── RoundtableUnlockInjector.cs  # Unlock Roundtable Hold at start
│   │   ├── SmithingStoneShopInjector.cs  # Add smithing stones to shop
│   │   ├── ZoneTrackingInjector.cs  # Zone tracking flags for racing
│   │   ├── RunCompleteInjector.cs  # Inject "RUN COMPLETE" message on final boss defeat
│   │   ├── ChapelGraceInjector.cs  # Site of Grace at Chapel of Anticipation
│   │   └── eldendata/       # FogRando game data (gitignored)
│   └── ItemRandomizerWrapper/  # Item randomizer - thin wrapper calling RandomizerCommon.dll
│       ├── Program.cs       # CLI entry point
│       └── diste/           # Item Randomizer game data (gitignored)
├── tools/                   # Standalone scripts
│   ├── setup_dependencies.py    # Extract FogRando and Item Randomizer dependencies
│   ├── generate_clusters.py # Generate clusters.json from fog.txt
│   └── extract_fog_data.py  # Extract fog gate metadata
├── reference/               # FogRando decompiled code (READ-ONLY)
│   ├── fogrando-src/        # C# source files
│   └── fogrando-data/       # Reference data (foglocations.txt)
├── docs/                    # Documentation
│   ├── architecture.md      # System architecture
│   └── event-flags.md       # Event flag allocation and EMEVD reference
└── output/                  # Generated mod (gitignored, self-contained)
```

## Key Files

| File | Purpose |
|------|---------|
| `docs/architecture.md` | System architecture and data formats |
| `docs/event-flags.md` | Event flag allocation and EMEVD reference |
| `reference/fogrando-src/GameDataWriterE.cs` | Main FogRando writer (5639 lines) |
| `reference/fogrando-src/EldenScaling.cs` | Enemy scaling logic |
| `config.example.toml` | Example configuration |

## Development Guidelines

### Reference Code
- Files in `reference/` are **read-only** - extracted from FogRando for study
- When adapting FogRando code, document the source line numbers
- Key classes from SoulsIds: `GameEditor`, `ParamDictionary`
- **For in-game bugs**: Always consult FogRando sources first - we aim to match its behavior exactly

### Event Templates
- Event templates are loaded directly from FogRando's `data/fogevents.txt`
- SpeedFog aims to match FogRando behavior exactly - no custom templates
- Only add C# logic when FogRando templates cannot express the behavior

### Python (speedfog/)
- Python 3.10+
- TOML for configuration
- JSON for graph output (Python → C# interface)
- Package at root, use `uv run speedfog` from project root

### C# (writer/)
- .NET 8.0
- Reference DLLs from `writer/lib/`
- Follow FogRando patterns for EMEVD/param manipulation

**FogModWrapper** (uses FogMod.dll directly):
| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, loads options, calls FogMod's GameDataWriterE |
| `GraphLoader` | Parses graph.json v4 format from Python |
| `ConnectionInjector` | Injects connections into FogMod's Graph, extracts warp data |
| `StartingItemInjector` | Injects starting item events into common.emevd |
| `StartingResourcesInjector` | Injects runes (CharaInitParam), seeds/tears (ItemLots) |
| `RoundtableUnlockInjector` | Unlocks Roundtable Hold at game start |
| `SmithingStoneShopInjector` | Adds smithing stones to Twin Maiden Husks shop |
| `ZoneTrackingInjector` | Injects SetEventFlag before fog gate warps for racing |
| `RunCompleteInjector` | Injects "RUN COMPLETE" golden banner on final boss defeat |
| `ChapelGraceInjector` | Adds Site of Grace at Chapel of Anticipation |

**ItemRandomizerWrapper** (uses RandomizerCommon.dll directly):
| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, loads item_config.json, calls Randomizer.Randomize() |

**Key FogMod classes** (from FogMod.dll):
| Class | Purpose |
|-------|---------|
| `GameDataWriterE` | Main writer - handles all EMEVD/params/MSB |
| `Graph` | Nodes/edges representing fog connections |
| `RandomizerOptions` | Configuration options (crawl, scale, etc.) |
| `AnnotationData` | Parsed fog.txt data |

**Key RandomizerCommon classes** (from RandomizerCommon.dll):
| Class | Purpose |
|-------|---------|
| `Randomizer` | Main entry point - orchestrates randomization |
| `RandomizerOptions` | Configuration (item, enemy, seed, difficulty) |
| `Permutation` | Item placement logic |
| `PermutationWriter` | Writes randomized items to params/EMEVD |
| `GameData` | Loads game files from diste/ |

### Zone Data
- Zone definitions extracted from FogRando's `fog.txt` into `clusters.json`
- Zone→map mapping stored in `clusters.json` under `zone_maps`
- Zone types: `legacy_dungeon`, `mini_dungeon`, `boss_arena`, `major_boss`, `start`, `final_boss`
- Weight defaults in `data/zone_metadata.toml`, overrides per zone
- Weight = approximate duration in minutes

### FogMod Options
Key options set by FogModWrapper for SpeedFog:

| Option | Value | Purpose |
|--------|-------|---------|
| `crawl` | true | Dungeon crawler mode - enables tier progression |
| `unconnected` | true | Allow edges without vanilla connections |
| `req_backportal` | true | Enable return warps from boss rooms |
| `scale` | true | Apply enemy scaling per tier |

ConfigVars in `Program.cs` set all key items to TRUE (given at start).

## FogRando Reference Points

When implementing features, refer to these sections in `GameDataWriterE.cs`:

| Feature | Lines | Notes |
|---------|-------|-------|
| Load game data | L37-70 | MSBs, EMEVDs, params |
| Fog models | L190-194 | AEG099_230/231/232 |
| Create fog gate | L262+ | `addFakeGate` helper |
| EMEVD events | L1781-1852 | Event creation |
| Scaling | L1964-1966 | EldenScaling integration |
| Write output | L4977-5030 | Params, EMEVDs |

## Setup

```bash
# 1. Install Python dependencies (from project root)
uv pip install -e ".[dev]"

# 2. Install sfextract (for extracting DLLs from .NET single-file executables)
dotnet tool install -g sfextract

# 3. Download mods from Nexusmods (requires account):
#    - FogRando: https://www.nexusmods.com/eldenring/mods/3295
#    - Item Randomizer (optional): https://www.nexusmods.com/eldenring/mods/428

# 4. Extract dependencies (both mods recommended)
python tools/setup_dependencies.py \
  --fogrando /path/to/FogRando.zip \
  --itemrando /path/to/ItemRandomizer.zip

# Or extract only FogRando (legacy mode)
python tools/setup_dependencies.py /path/to/FogRando.zip

# 5. Build C# writers (done automatically by setup, or manually)
cd writer/FogModWrapper && dotnet build
cd writer/ItemRandomizerWrapper && dotnet build
```

## Commands

```bash
# Generate a run (from project root)
uv run speedfog config.toml --spoiler
# Creates seeds/<seed>/graph.json and spoiler.txt

# Python tests
pytest -v

# C# FogModWrapper - build and publish
cd writer/FogModWrapper
dotnet build
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64

# FogModWrapper - run (Linux with Wine)
wine publish/win-x64/FogModWrapper.exe \
  <seed_dir> \
  --game-dir <game_dir> \
  --data-dir ../../data \
  -o output

# Example paths:
#   seed_dir: seeds/212559448 (contains graph.json and spoiler.txt)
#   game_dir: /data/thewall/Game (ELDEN RING/Game folder)

# ItemRandomizerWrapper - build and publish
cd writer/ItemRandomizerWrapper
dotnet build
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64

# ItemRandomizerWrapper - run (generates randomized items)
wine publish/win-x64/ItemRandomizerWrapper.exe \
  item_config.json \
  --game-dir <game_dir> \
  -o temp/item-randomizer

# item_config.json format:
# {"seed": 12345, "difficulty": 50, "options": {"item": true, "enemy": false}}

# Play! (output is self-contained with ModEngine + launcher)
./output/launch_speedfog.bat   # Windows
./output/launch_speedfog.sh    # Linux/Proton
```

## Testing

```bash
# Python - all tests
pytest -v

# Python - with coverage
pytest --cov=speedfog

# Tools tests (generate_clusters.py)
cd tools && pytest test_generate_clusters.py -v

# C# - integration test
cd writer/test && ./run_integration.sh
```

## Debugging

### Philosophy

1. **Match FogRando behavior**: SpeedFog aims to closely replicate FogRando's in-game behavior. When investigating issues, always consult `reference/fogrando-src/` first.

2. **Prefer FogRando parity**: Fix issues by consulting FogRando sources first. Event templates come directly from `data/fogevents.txt` (copied from FogRando).

3. **Reference-driven debugging**: For any in-game problem (fog gates not working, warps failing, scaling issues), first find the equivalent FogRando implementation in `reference/`.

### Common Issues

**EMEVD events not triggering:**
- Check event templates in `data/fogevents.txt` against FogRando source in `reference/`
- Verify common events are initialized by FogMod's GameDataWriterE
- Confirm entity IDs exist in the target MSB

**Fog gates not visible:**
- Compare `fog_data.json` entries with FogRando's gate creation in `GameDataWriterE.cs:L262+`
- Check model names (AEG099_230/231/232)

**Warp destinations wrong:**
- Verify warp positions in `fog_data.json` against `fog.txt` Entrances section
- Check map coordinate encoding (m, area, block, sub bytes)

**Enemy scaling incorrect:**
- Compare with `EldenScaling.cs` tier definitions
- Verify SpEffect IDs match game params

## Important Notes

- One-way paths (coffins, drops) excluded in v1
- Key items given at start to prevent softlocks
- Enemy scaling uses tiers 1-28 (subset of vanilla's 1-34)

## Data Sources

| Data | Source | Format |
|------|--------|--------|
| Key item IDs | `data/fog.txt` L3258-3358 | `ID: 3:XXXX` |
| Zone definitions | `data/fog.txt` Areas section | YAML |
| Warp positions | `data/fog.txt` Entrances section | YAML |
| Enemy scaling tiers | `data/foglocations2.txt` EnemyAreas section | YAML |
| EMEVD instructions | `data/er-common.emedf.json` | JSON |
| Scaling logic | `reference/fogrando-src/EldenScaling.cs` | C# |

## Data Formats

### graph.json v4 (Python → C# + visualization + racing)

```json
{
  "version": "4.0",
  "seed": 212559448,
  "options": {"scale": true, "crawl": true},
  "nodes": {"cluster_id": {"type": "legacy_dungeon", "display_name": "Stormveil Castle", "zones": [...], "layer": 1, "tier": 5, "weight": 15}},
  "edges": [{"from": "cluster_id_1", "to": "cluster_id_2"}],
  "connections": [
    {"exit_area": "zone1", "exit_gate": "m10_...", "entrance_area": "zone2", "entrance_gate": "m31_...", "flag_id": 1040292800}
  ],
  "area_tiers": {"zone1": 1, "zone2": 5},
  "event_map": {"1040292800": "cluster_id"},
  "finish_event": 1040292802
}
```

- `nodes`/`edges`: DAG topology for visualization tools
- `connections`/`area_tiers`: FogModWrapper consumption (unchanged from v2)
- `event_map`: flag_id (str) → cluster_id mapping for racing zone tracking
- `finish_event`: flag_id set on final boss defeat
- `flag_id` per connection: event flag set when fog gate is traversed
- Event flags allocated sequentially from base 1040292800 (range 1040292800–1040292999)
- Connections use FogMod's edge FullName format: `{map}_{gate_name}` (e.g., `m10_01_00_00_AEG099_001_9000`)

### fogevents.txt

FogRando EMEVD event templates with parameter notation `X{offset}_{size}`:
- `X0_4` = 4-byte int at offset 0
- `X12_1` = 1-byte value at offset 12

Key templates: `scale`, `showsfx`, `fogwarp`, `common_fingerstart`, `common_roundtable`

### fog_data.json

Fog gate metadata: `{fog_id: {type, zones[], map, entity_id, model, position[], rotation[]}}`

Duplicate fog IDs handled by prefixing with map ID (e.g., `m10_00_00_00_AEG099_002_9000`)
