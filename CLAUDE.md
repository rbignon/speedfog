# SpeedFog

Elden Ring mod that generates short randomized runs (~1 hour) with a controlled DAG structure.

## Project Context

SpeedFog creates focused paths from Chapel of Anticipation to Radagon/Elden Beast with:
- Balanced parallel branches (no disadvantaged paths)
- No dead ends (all paths lead to the end)
- Configurable parameters (bosses, dungeons, duration)

Unlike FogRando which randomizes the entire world, SpeedFog generates a smaller, curated experience.

## Architecture

**Hybrid Python + C#:**

```
Python (core/)          C# (writer/)                    Output
─────────────────       ─────────────────               ─────────────────
config.toml        →                                    output/
clusters.json      →    graph.json → FogModWrapper  →   ├── mod/
DAG generation     →                                    ├── ModEngine/
                                                        ├── launch_speedfog.bat
                                                        └── spoiler.txt
```

- **Python**: Configuration, cluster/zone data, DAG generation algorithm
- **C#**: FogModWrapper - thin wrapper calling FogMod.dll with our graph connections
- **Output**: Self-contained folder with ModEngine 2 (auto-downloaded)

## Directory Structure

```
speedfog/
├── data/                    # Shared data files (source + generated)
│   ├── fog.txt              # FogRando zone definitions (source)
│   ├── foglocations2.txt    # FogRando enemy areas (source)
│   ├── fogevents.txt        # FogRando event templates (source)
│   ├── er-common.emedf.json # EMEVD instruction definitions
│   ├── clusters.json        # Generated zone clusters
│   ├── fog_data.json        # Generated fog gate metadata
│   └── zone_metadata.toml   # Zone weight config
├── core/                    # Python - DAG generation (Phase 1-2)
│   └── speedfog_core/
├── writer/                  # C# - Mod file generation (Phase 3-4)
│   ├── lib/                 # DLLs (FogMod, SoulsFormats, SoulsIds, etc.)
│   └── FogModWrapper/       # Main writer - thin wrapper calling FogMod.dll
│       ├── Program.cs       # CLI entry point
│       ├── GraphLoader.cs   # Load graph.json v2
│       ├── ConnectionInjector.cs  # Inject connections into FogMod Graph
│       └── eldendata/       # Game data (gitignored, symlinked)
├── tools/                   # Standalone data generation scripts
│   ├── generate_clusters.py # Generate clusters.json from fog.txt
│   ├── extract_fog_data.py  # Extract fog gate metadata
│   └── test_generate_clusters.py  # Tests for generate_clusters.py
├── reference/               # FogRando decompiled code (READ-ONLY)
│   ├── fogrando-src/        # C# source files
│   └── fogrando-data/       # Reference data (foglocations.txt)
├── docs/plans/              # Implementation specifications
└── output/                  # Generated mod (gitignored, self-contained)
```

## Key Files

| File | Purpose |
|------|---------|
| `docs/plans/2026-01-29-speedfog-design.md` | Main design document |
| `docs/plans/phase-1-foundations.md` | Python setup, zone conversion |
| `docs/plans/phase-2-dag-generation.md` | DAG algorithm, balancing |
| `docs/plans/2026-02-01-fogmod-wrapper-design.md` | FogModWrapper architecture |
| `docs/plans/phase-4-packaging.md` | ModEngine download, launcher scripts |
| `reference/fogrando-src/GameDataWriterE.cs` | Main FogRando writer (5639 lines) |
| `reference/fogrando-src/EldenScaling.cs` | Enemy scaling logic |
| `docs/reference/fogrando-graph-logic.md` | FogRando graph generation analysis |
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

### Python (core/)
- Python 3.10+
- TOML for configuration
- JSON for graph output (Python → C# interface)

### C# (writer/)
- .NET 8.0
- Reference DLLs from `writer/lib/`
- Follow FogRando patterns for EMEVD/param manipulation

**FogModWrapper** (uses FogMod.dll directly):
| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, loads options, calls FogMod's GameDataWriterE |
| `GraphLoader` | Parses graph.json v2 format from Python |
| `ConnectionInjector` | Injects our connections into FogMod's Graph object |

**Key FogMod classes** (from FogMod.dll):
| Class | Purpose |
|-------|---------|
| `GameDataWriterE` | Main writer - handles all EMEVD/params/MSB |
| `Graph` | Nodes/edges representing fog connections |
| `RandomizerOptions` | Configuration options (crawl, scale, etc.) |
| `AnnotationData` | Parsed fog.txt data |

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

## Commands

```bash
# Python core
cd core && uv pip install -e ".[dev]"
speedfog config.toml --spoiler -o /tmp/speedfog
# Creates /tmp/speedfog/<seed>/graph.json and spoiler.txt

# Python tests
cd core && pytest -v

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

# Play! (output is self-contained with ModEngine + launcher)
./output/launch_speedfog.bat   # Windows
./output/launch_speedfog.sh    # Linux/Proton
```

## Testing

```bash
# Python - all tests (core + tools)
pytest -v

# Python - core package only
cd core && pytest -v

# Python - tools only
pytest tools/ -v

# Python - with coverage
pytest --cov=speedfog_core

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

- DLC content excluded in v1 (base game only)
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

### graph.json v2 (Python → C# interface)

```json
{
  "version": "2.0",
  "seed": 212559448,
  "options": {"scale": true, "crawl": true},
  "connections": [
    {"exit_area": "zone1", "exit_gate": "m10_...", "entrance_area": "zone2", "entrance_gate": "m31_..."}
  ],
  "area_tiers": {"zone1": 1, "zone2": 5}
}
```

Connections use FogMod's edge FullName format: `{map}_{gate_name}` (e.g., `m10_01_00_00_AEG099_001_9000`)

### fogevents.txt

FogRando EMEVD event templates with parameter notation `X{offset}_{size}`:
- `X0_4` = 4-byte int at offset 0
- `X12_1` = 1-byte value at offset 12

Key templates: `scale`, `showsfx`, `fogwarp`, `common_fingerstart`, `common_roundtable`

### fog_data.json

Fog gate metadata: `{fog_id: {type, zones[], map, entity_id, model, position[], rotation[]}}`

Duplicate fog IDs handled by prefixing with map ID (e.g., `m10_00_00_00_AEG099_002_9000`)
