# SpeedFog Architecture

SpeedFog generates short randomized Elden Ring runs (~1 hour) with a controlled DAG structure.

## Overview

```
User Config                Python Core                 C# Writer                    Output
───────────                ───────────                 ─────────                    ──────
config.toml ──────► speedfog_core ──────► graph.json ──────► FogModWrapper ──────► mod/
                          │                                       │
                    clusters.json                           FogMod.dll
                    (pre-generated)                    (reuses FogRando writer)
```

**Key insight**: SpeedFog reuses 100% of FogRando's game writer (`FogMod.dll`). We only generate the graph connections differently.

## Components

### Python Core (`core/speedfog_core/`)

Generates a balanced DAG of zone connections.

| Module | Purpose |
|--------|---------|
| `config.py` | Parse user config (TOML) |
| `clusters.py` | Load pre-computed zone clusters |
| `dag.py` | DAG data structures (Branch, DagNode, DagEdge) |
| `generator.py` | Main generation algorithm |
| `planner.py` | Layer planning and tier computation |
| `balance.py` | Path weight analysis |
| `validator.py` | DAG constraint validation |
| `output.py` | Export graph.json and spoiler.txt |
| `main.py` | CLI entry point |

### C# Writer (`writer/FogModWrapper/`)

Thin wrapper around FogMod.dll that injects our connections.

| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, configure FogMod options |
| `GraphLoader.cs` | Parse graph.json v2 format |
| `ConnectionInjector.cs` | Inject connections into FogMod's Graph |
| `Packaging/` | ModEngine download, config generation, launchers |

### Tools (`tools/`)

Standalone data generation scripts (run once when FogRando updates).

| Script | Purpose |
|--------|---------|
| `generate_clusters.py` | Parse fog.txt → clusters.json |
| `extract_fog_data.py` | Extract fog gate metadata |

## Data Flow

### 1. Cluster Generation (one-time)

```
fog.txt (FogRando) ──► generate_clusters.py ──► clusters.json
```

Clusters group connected zones. Once a player enters a cluster via an entry fog, they have access to all zones and can exit via any exit fog.

### 2. DAG Generation (per run)

```
config.toml + clusters.json ──► speedfog_core ──► graph.json + spoiler.txt
```

The DAG algorithm:
1. Start at Chapel of Anticipation
2. Build layers with uniform cluster types (fairness)
3. Select clusters avoiding zone reuse
4. Track available exits for splits/merges
5. Converge all paths to Radagon

### 3. Mod Generation

```
graph.json ──► FogModWrapper ──► FogMod.dll ──► mod files
```

FogModWrapper:
1. Loads graph.json
2. Configures FogMod options (crawl mode, scaling, etc.)
3. Builds FogMod's Graph structure (unconnected)
4. Injects our connections
5. Calls `GameDataWriterE.Write()` to generate all mod files

## Data Formats

### config.toml

User configuration for DAG generation.

```toml
[run]
seed = 0                    # 0 = random, N = force seed

[budget]
total_weight = 30           # Target weight per path
tolerance = 5               # Allowed variance

[requirements]
legacy_dungeons = 1         # Minimum per run
bosses = 5
mini_dungeons = 5

[paths]
game_dir = "/path/to/ELDEN RING/Game"
```

### clusters.json

Pre-computed zone clusters with entry/exit fogs.

```json
{
  "version": "1.1",
  "zone_maps": {"stormveil": "m10_00_00_00", ...},
  "clusters": [
    {
      "id": "stormveil_c1d3",
      "zones": ["stormveil_start", "stormveil"],
      "type": "legacy_dungeon",
      "weight": 15,
      "entry_fogs": [{"fog_id": "AEG099_002_9000", "zone": "stormveil_start"}],
      "exit_fogs": [{"fog_id": "AEG099_002_9000", "zone": "stormveil"}, ...]
    }
  ]
}
```

### graph.json v2

DAG serialized for C# consumption.

```json
{
  "version": "2.0",
  "seed": 212559448,
  "options": {"scale": true, "crawl": true},
  "connections": [
    {
      "exit_area": "chapel_start",
      "exit_gate": "m10_01_00_00_AEG099_001_9000",
      "entrance_area": "stormveil",
      "entrance_gate": "m10_00_00_00_AEG099_002_9000"
    }
  ],
  "area_tiers": {"chapel_start": 1, "stormveil": 5, ...}
}
```

Gate names use FogMod's FullName format: `{map}_{gate_name}`.

## Key Design Decisions

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| Architecture | Python + C# hybrid | Python for algorithm, C# for game file manipulation |
| Writer | Reuse FogMod.dll | Avoid reimplementing 5000+ lines of game writer |
| Layers | Uniform cluster type | Competitive fairness (same challenge per layer) |
| Key items | All given at start | Prevent softlocks |
| DLC | Excluded (v1) | Base game focus |
| One-ways | Excluded (v1) | Complexity reduction |

## FogMod Integration

FogModWrapper configures FogMod for SpeedFog:

| Option | Value | Purpose |
|--------|-------|---------|
| `crawl` | true | Dungeon crawler mode, enables tier progression |
| `unconnected` | true | Allow edges without vanilla connections |
| `req_backportal` | true | Boss rooms have return warps |
| `scale` | true | Enemy scaling per tier |

ConfigVars set all key items to TRUE (given at start).

### Connection Injection

FogMod builds a Graph with unconnected edges. We inject our connections:

```csharp
foreach (var conn in graphData.Connections)
{
    var exitEdge = FindExitEdge(graph, conn.ExitArea, conn.ExitGate);
    var entranceEdge = FindEntranceEdge(graph, conn.EntranceArea, conn.EntranceGate);
    graph.Connect(exitEdge, entranceEdge);
}
```

Each fog gate has paired edges (Exit in `node.To`, Entrance in `node.From`). We find the exit edge on the destination node, then use `.Pair` to get the entrance.

## Enemy Scaling

Zones have tiers (1-28) based on their layer in the DAG. FogMod applies SpEffect modifiers:

| Tier Range | Approximate Difficulty |
|------------|------------------------|
| 1-5 | Early game (Limgrave) |
| 6-12 | Mid game (Liurnia, Caelid) |
| 13-20 | Late game (Mountaintops) |
| 21-28 | Endgame (Farum Azula, Haligtree) |

## Output Structure

```
output/
├── ModEngine/              # ModEngine 2 (auto-downloaded)
├── mods/speedfog/          # Mod files
│   ├── param/gameparam/regulation.bin
│   ├── event/*.emevd.dcx
│   ├── map/mapstudio/*.msb.dcx
│   ├── script/talk/*.fmg
│   └── msg/engus/*.fmg
├── lib/                    # Runtime DLLs
├── config_speedfog.toml    # ModEngine config
├── launch_speedfog.bat     # Windows launcher
├── launch_speedfog.sh      # Linux/Proton launcher
└── spoiler.txt             # Path spoiler log
```

## References

- FogRando: https://www.nexusmods.com/eldenring/mods/3295
- SoulsFormats: https://github.com/soulsmods/SoulsFormatsNEXT
- ModEngine 2: https://github.com/soulsmods/ModEngine2
