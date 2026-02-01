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
clusters.json      →    graph.json → SpeedFogWriter →   ├── mod/
DAG generation     →                                    ├── ModEngine/
                                                        ├── launch_speedfog.bat
                                                        └── spoiler.txt
```

- **Python**: Configuration, cluster/zone data, DAG generation algorithm
- **C#**: SoulsFormats I/O, EMEVD events, param modifications, packaging
- **Output**: Self-contained folder with ModEngine 2 (auto-downloaded)

## Directory Structure

```
speedfog/
├── data/                    # Shared data files (source + generated)
│   ├── fog.txt              # FogRando zone definitions (source)
│   ├── foglocations2.txt    # FogRando enemy areas (source)
│   ├── er-common.emedf.json # EMEVD instruction definitions
│   ├── clusters.json        # Generated zone clusters
│   ├── fog_data.json        # Generated fog gate metadata
│   ├── zone_metadata.toml   # Zone weight config
│   └── speedfog-events.yaml # EMEVD event templates
├── core/                    # Python - DAG generation (Phase 1-2)
│   └── speedfog_core/
├── writer/                  # C# - Mod file generation (Phase 3-4)
│   ├── lib/                 # DLLs (SoulsFormats, SoulsIds, etc.)
│   └── SpeedFogWriter/
│       ├── Writers/         # Mod file writers
│       └── Packaging/       # ModEngine downloader, launcher scripts
├── reference/               # FogRando decompiled code (READ-ONLY)
│   ├── fogrando-src/        # C# source files
│   └── fogrando-data/       # Reference data (fogevents.txt, foglocations.txt)
├── docs/plans/              # Implementation specifications
└── output/                  # Generated mod (gitignored, self-contained)
```

## Key Files

| File | Purpose |
|------|---------|
| `docs/plans/2026-01-29-speedfog-design.md` | Main design document |
| `docs/plans/phase-1-foundations.md` | Python setup, zone conversion |
| `docs/plans/phase-2-dag-generation.md` | DAG algorithm, balancing |
| `docs/plans/phase-3-csharp-writer.md` | C# writer with FogRando references |
| `docs/plans/phase-4-packaging.md` | ModEngine download, launcher scripts |
| `reference/fogrando-src/GameDataWriterE.cs` | Main FogRando writer (5639 lines) |
| `reference/fogrando-src/EldenScaling.cs` | Enemy scaling logic |
| `config.example.toml` | Example configuration |

## Development Guidelines

### Reference Code
- Files in `reference/` are **read-only** - extracted from FogRando for study
- When adapting FogRando code, document the source line numbers
- Key classes from SoulsIds: `GameEditor`, `ParamDictionary`

### Python (core/)
- Python 3.10+
- TOML for configuration
- JSON for graph output (Python → C# interface)

### C# (writer/)
- .NET 8.0
- Reference DLLs from `writer/lib/`
- Follow FogRando patterns for EMEVD/param manipulation

### Zone Data
- Zone definitions extracted from FogRando's `fog.txt` into `clusters.json`
- Zone→map mapping stored in `clusters.json` under `zone_maps`
- Zone types: `legacy_dungeon`, `mini_dungeon`, `boss_arena`, `major_boss`, `start`, `final_boss`
- Weight defaults in `data/zone_metadata.toml`, overrides per zone
- Weight = approximate duration in minutes

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
# Python core (after Phase 1-2 implementation)
cd core && pip install -e .
speedfog config.toml --spoiler -o /tmp/speedfog
# Creates /tmp/speedfog/<seed>/graph.json and spoiler.txt

# C# writer (after Phase 3-4 implementation)
cd writer && dotnet build
dotnet run -- /tmp/speedfog/<seed> "/path/to/ELDEN RING/Game" ./output

# Play! (output is self-contained with ModEngine + launcher)
./output/launch_speedfog.bat   # Windows
./output/launch_speedfog.sh    # Linux/Proton
```

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
