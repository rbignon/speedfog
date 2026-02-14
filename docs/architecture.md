# SpeedFog Architecture

SpeedFog generates short randomized Elden Ring runs (~1 hour) with a controlled DAG structure.

## Overview

```
User Config                Python                      C# Writers                     Output
───────────                ──────                      ──────────                     ──────
config.toml ──────► speedfog ──────► graph.json ──────► FogModWrapper ─────────┐
                        │                                     │                 ├───► mod/
                  clusters.json                         FogMod.dll              │
                  (pre-generated)                  (reuses FogRando writer)     │
                                                                                │
item_config.json ─────────────────────────────────► ItemRandomizerWrapper ─────┘
                                                          │              (merge)
                                                  RandomizerCommon.dll
                                                (reuses Item Randomizer)
```

**Key insight**: SpeedFog reuses 100% of FogRando's game writer (`FogMod.dll`) and optionally 100% of Item Randomizer's writer (`RandomizerCommon.dll`). We only generate the graph connections and item config differently.

## Components

### Python Package (`speedfog/`)

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

### C# Fog Writer (`writer/FogModWrapper/`)

Thin wrapper around FogMod.dll that injects our connections and post-processes game files.

| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, configure FogMod options, orchestrate pipeline |
| `GraphLoader.cs` | Parse graph.json v4 format |
| `ConnectionInjector.cs` | Inject connections into FogMod's Graph, extract boss defeat flag |
| `StartingItemInjector.cs` | Give starting items + care package via EMEVD |
| `StartingResourcesInjector.cs` | Give runes, golden seeds, sacred tears via EMEVD |
| `RoundtableUnlockInjector.cs` | Unlock Roundtable Hold at game start |
| `SmithingStoneShopInjector.cs` | Add smithing stones to Twin Maiden Husks shop |
| `ZoneTrackingInjector.cs` | Inject zone tracking flags before fog gate warps |
| `RunCompleteInjector.cs` | Display victory banner on final boss defeat |
| `ChapelGraceInjector.cs` | Add Site of Grace at Chapel of Anticipation |
| `RebirthInjector.cs` | Rebirth (stat reallocation) at Sites of Grace |
| `VanillaWarpRemover.cs` | Remove vanilla warp assets that FogMod couldn't delete |
| `Packaging/` | ModEngine download, config generation, launchers |

### C# Item Writer (`writer/ItemRandomizerWrapper/`)

Thin wrapper around RandomizerCommon.dll for item randomization.

| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, parse item_config.json, call Randomizer |

The wrapper configures `RandomizerOptions` and calls `Randomizer.Randomize()` with:
- `item: true` - enable item randomization
- `enemy: false` - disable enemy randomization (fog gates handle difficulty)
- `seed` - from config
- `difficulty` - placement difficulty (0-100)

### Tools (`tools/`)

Standalone scripts for setup and data generation.

| Script | Purpose |
|--------|---------|
| `setup_dependencies.py` | Extract dependencies, generate derived data, build C# writers |
| `generate_clusters.py` | Parse fog.txt → clusters.json |
| `extract_fog_data.py` | Extract fog gate metadata |

**setup_dependencies.py** extracts:
- From FogRando ZIP: FogMod.dll, SoulsFormats.dll, eldendata/, data files
- From Item Randomizer ZIP: RandomizerCommon.dll, diste/, crash fix DLLs

## Data Flow

### 1. Cluster Generation (one-time)

```
fog.txt (FogRando) ──► generate_clusters.py ──► clusters.json
```

Clusters group connected zones. Once a player enters a cluster via an entry fog, they have access to all zones and can exit via any exit fog.

### 2. DAG Generation (per run)

```
config.toml + clusters.json ──► speedfog ──► graph.json + spoiler.txt
```

The DAG algorithm:
1. Start at Chapel of Anticipation
2. Build layers with uniform cluster types (fairness)
3. Select clusters avoiding zone reuse
4. Track available exits for splits/merges
5. Converge all paths to Radagon

### 3. Item Randomization (optional)

```
item_config.json ──► ItemRandomizerWrapper ──► RandomizerCommon.dll ──► temp/item-randomizer/
```

ItemRandomizerWrapper:
1. Loads item_config.json (seed, difficulty, options)
2. Configures RandomizerOptions (item=true, enemy=false)
3. Calls `Randomizer.Randomize()` to generate randomized items
4. Outputs modified params/EMEVD to temp directory

### 4. Fog Gate Generation

```
graph.json ──► FogModWrapper ──► FogMod.dll ──► mod files
                    ↑
              (--merge-dir temp/item-randomizer/)
```

FogModWrapper:
1. Loads graph.json
2. Configures FogMod options (crawl mode, scaling, etc.)
3. Creates MergedMods with game dir + item randomizer output (if present)
4. Builds FogMod's Graph structure (unconnected)
5. Injects our connections via ConnectionInjector
6. Calls `GameDataWriterE.Write()` - reads from merged dirs, writes combined output
7. Post-processes: starting items, resources, shop, zone tracking, victory banner, grace, rebirth, vanilla warp removal

**Merge order matters**: Item Randomizer runs first, FogMod merges on top. This matches the official FogRando documentation.

## Data Formats

### item_config.json

Configuration for item randomization (ItemRandomizerWrapper).

```json
{
  "seed": 12345,
  "difficulty": 50,
  "options": {
    "item": true,
    "enemy": false
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `seed` | int | Randomization seed |
| `difficulty` | int | Placement difficulty 0-100 (higher = harder to find key items) |
| `options` | object | Boolean flags for RandomizerOptions |

Common options:
- `item: true` - Enable item randomization
- `enemy: false` - Disable enemy randomization (fog tiers handle difficulty)
- `scale: true` - Enable enemy scaling (usually handled by FogMod)

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
  "version": "1.4",
  "zone_maps": {"stormveil": "m10_00_00_00", ...},
  "zone_names": {"stormveil": "Stormveil Castle", ...},
  "clusters": [
    {
      "id": "stormveil_c1d3",
      "zones": ["stormveil_start", "stormveil"],
      "type": "legacy_dungeon",
      "weight": 15,
      "entry_fogs": [{"fog_id": "AEG099_002_9000", "zone": "stormveil_start", "text": "Godrick front"}],
      "exit_fogs": [{"fog_id": "AEG099_002_9000", "zone": "stormveil", "text": "Godrick front"}, ...]
    }
  ]
}
```

### graph.json v4

DAG serialized for C# consumption, visualization tools, and racing.

```json
{
  "version": "4.0",
  "seed": 212559448,
  "options": {"scale": true, "crawl": true},
  "nodes": {
    "stormveil_c1d3": {
      "type": "legacy_dungeon",
      "display_name": "Stormveil Castle",
      "zones": ["stormveil_start", "stormveil"],
      "layer": 1,
      "tier": 5,
      "weight": 15,
      "exits": [
        {"fog_id": "AEG099_002_9000", "text": "Godrick front", "from": "stormveil", "to": "stormveil_godrick_3c4d"}
      ]
    }
  },
  "edges": [
    {"from": "chapel_start_a1b2", "to": "stormveil_c1d3"}
  ],
  "connections": [
    {
      "exit_area": "chapel_start",
      "exit_gate": "m10_01_00_00_AEG099_001_9000",
      "entrance_area": "stormveil",
      "entrance_gate": "m10_00_00_00_AEG099_002_9000",
      "flag_id": 1040292800
    }
  ],
  "area_tiers": {"chapel_start": 1, "stormveil": 5, ...},
  "event_map": {"1040292800": "stormveil_c1d3"},
  "final_node_flag": 1040292801,
  "finish_event": 1040292802,
  "remove_entities": [
    {"map": "m12_05_00_00", "entity_id": 12051500}
  ]
}
```

Gate names use FogMod's FullName format: `{map}_{gate_name}`.

v4 additions over v3: `flag_id` per connection, `event_map`, `final_node_flag`, `finish_event` (for zone tracking/racing), `run_complete_message`, `chapel_grace`, `starting_goods`, `care_package`, `exits` per node (fog_id, text, destination), `remove_entities` (vanilla warp MSB assets to delete).

## Key Design Decisions

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| Architecture | Python + C# hybrid | Python for algorithm, C# for game file manipulation |
| Fog Writer | Reuse FogMod.dll | Avoid reimplementing 5000+ lines of game writer |
| Item Writer | Reuse RandomizerCommon.dll | Avoid reimplementing 3000+ lines of item logic |
| Merge Order | Items first, then fog | Matches official FogRando documentation |
| Layers | Uniform cluster type | Competitive fairness (same challenge per layer) |
| Key items | All given at start | Prevent softlocks |
| Enemy scaling | Via fog tiers, not item rando | FogMod handles scaling per zone tier |
| DLC | Included | Shadow of the Erdtree zones, PCR as final boss candidate |
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
├── mods/fogmod/            # Mod files
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

## Event Flags & EMEVD

See [event-flags.md](event-flags.md) for the complete reference on event flag allocation, EMEVD event IDs, and VirtualMemoryFlag constraints.

## References

- FogRando: https://www.nexusmods.com/eldenring/mods/3295
- Item Randomizer: https://www.nexusmods.com/eldenring/mods/428
- SoulsFormats: https://github.com/soulsmods/SoulsFormatsNEXT
- ModEngine 2: https://github.com/soulsmods/ModEngine2
