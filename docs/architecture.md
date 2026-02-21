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
| `config.py` | Parse user config (TOML), 7 dataclasses for all settings |
| `clusters.py` | Load pre-computed zone clusters, fog gate compatibility |
| `dag.py` | DAG data structures (Branch, DagNode, DagEdge, Dag) |
| `generator.py` | Main generation algorithm (split/merge/passant topology) |
| `planner.py` | Layer type planning and tier interpolation |
| `balance.py` | Path weight analysis and balance reporting |
| `validator.py` | DAG constraint validation against requirements |
| `output.py` | Export graph.json v4 and spoiler.txt with ASCII graph |
| `care_package.py` | Randomized starting build (weapons, armor, spells, etc.) |
| `fog_mod.py` | Wrapper to call FogModWrapper.exe via Wine/native |
| `item_randomizer.py` | Wrapper to call ItemRandomizerWrapper.exe, generate item_config |
| `main.py` | CLI entry point, orchestrates full pipeline |

### C# Shared Library (`writer/FogModWrapper.Core/`)

Shared models and logic used by both wrappers.

| Class | Purpose |
|-------|---------|
| `GraphLoader.cs` | Parse graph.json v4 format |
| `Models/GraphData.cs` | C# models (GraphData, Connection, CarePackageItem) |
| `ResourceCalculations.cs` | Calculate starting runes/seeds/tears from tier progression |
| `ShopIdAllocator.cs` | Allocate shop line IDs avoiding conflicts |

### C# Fog Writer (`writer/FogModWrapper/`)

Thin wrapper around FogMod.dll that injects our connections and post-processes game files.

| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, configure FogMod options, orchestrate pipeline |
| `ConnectionInjector.cs` | Inject connections into FogMod's Graph, extract boss defeat flag |
| `StartingItemInjector.cs` | Give starting items + care package via EMEVD |
| `StartingResourcesInjector.cs` | Give runes, golden seeds, sacred tears, larval tears |
| `RoundtableUnlockInjector.cs` | Unlock Roundtable Hold at game start |
| `SmithingStoneShopInjector.cs` | Add smithing stones to Twin Maiden Husks shop |
| `ZoneTrackingInjector.cs` | Inject zone tracking flags before fog gate warps |
| `RunCompleteInjector.cs` | Display victory banner on final boss defeat |
| `ChapelGraceInjector.cs` | Add Site of Grace + player spawn at Chapel of Anticipation |
| `RebirthInjector.cs` | Rebirth (stat reallocation) at Sites of Grace via ESD |
| `VanillaWarpRemover.cs` | Remove vanilla warp assets that FogMod couldn't delete |
| `Packaging/` | ModEngine download, config generation, launchers |

### C# Item Writer (`writer/ItemRandomizerWrapper/`)

Thin wrapper around RandomizerCommon.dll for item randomization.

| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, parse item_config.json, call Randomizer |
| `ItemRandomizerWrapper.Core/` | Shared models and arg parser |

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
1. Start at Chapel of Anticipation (with Roundtable merged for extra exits)
2. Plan layer types using configured ratios (legacy_dungeon, mini_dungeon, boss_arena, major_boss)
3. Build layers with dynamic topology: split (1→N), merge (N→1), passant (1→1 per branch)
4. Select clusters avoiding zone reuse, respecting fog gate compatibility
5. Interpolate enemy tiers from 1 to `final_tier` (default 28)
6. Force merge all branches before the final boss (configurable candidates, default Radagon/PCR)
7. Validate against budget/requirements, retry with new seeds if needed

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

FogModWrapper pipeline:
1. Load graph.json via GraphLoader
2. Configure FogMod options (crawl mode, scaling, DLC, dungeon types)
3. Load FogRando data files (fog.txt, fogevents.txt, foglocations2.txt)
4. Build FogMod's Graph structure (unconnected nodes/edges)
5. Disconnect trivial edges pre-connected by Graph.Construct() (SpeedFog needs these)
6. Exclude evergaol zones from stake processing (no StakeAsset in fog.txt)
7. Inject our connections via ConnectionInjector, extract boss defeat flag
8. Apply area tiers for enemy scaling
9. Call `GameDataWriterE.Write()` with MergedMods (game dir + item rando output)

Post-processing (after FogMod writes, step numbers match Program.cs):
- **7b** StartingItemInjector: give goods + care package items via EMEVD
- **7c** StartingResourcesInjector: runes (CharaInitParam), seeds/tears/larval tears (ItemLots)
- **7d** RoundtableUnlockInjector: set flag 1040292051 to bypass finger pickup
- **7e** SmithingStoneShopInjector: add smithing stones to Twin Maiden Husks
- **7f** ZoneTrackingInjector: insert SetEventFlag before each fog gate WarpPlayer
- **7g** RunCompleteInjector: golden banner + jingle on final boss defeat
- **7h** ChapelGraceInjector: Site of Grace + WarpPlayer for initial spawn
- **7i** RebirthInjector: rebirth option at graces via ESD editing (ConsistentID 73)
- **7j** VanillaWarpRemover: delete vanilla warp MSB assets that conflict with fog gates

Packaging: download ModEngine 2, generate config, create launcher scripts.

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
tolerance = 5               # Max allowed spread between paths

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
  "version": "1.5",
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

### graph.json v4.1

DAG serialized for C# consumption, visualization tools, and racing.

```json
{
  "version": "4.1",
  "seed": 212559448,
  "total_layers": 8, "total_nodes": 12, "total_zones": 24, "total_paths": 3,
  "options": {"scale": true, "shuffle": true},
  "nodes": {
    "stormveil_c1d3": {
      "type": "legacy_dungeon",
      "display_name": "Stormveil Castle",
      "zones": ["stormveil_start", "stormveil"],
      "layer": 1, "tier": 5, "weight": 15,
      "exits": [
        {"fog_id": "AEG099_002_9000", "text": "Godrick front", "from": "stormveil", "to": "stormveil_godrick_3c4d"}
      ],
      "entrances": [
        {"text": "before Stormveil main gate", "from": "chapel_start_a1b2", "to": "stormveil_start", "to_text": "Stormveil Castle Start"}
      ]
    }
  },
  "edges": [{"from": "chapel_start_a1b2", "to": "stormveil_c1d3"}],
  "connections": [
    {
      "exit_area": "chapel_start",
      "exit_gate": "m10_01_00_00_AEG099_001_9000",
      "entrance_area": "stormveil",
      "entrance_gate": "m10_00_00_00_AEG099_002_9000",
      "flag_id": 1040292800
    }
  ],
  "area_tiers": {"chapel_start": 1, "stormveil": 5},
  "event_map": {"1040292800": "stormveil_c1d3"},
  "final_node_flag": 1040292801,
  "finish_event": 1040292802,
  "finish_boss_defeat_flag": 9010800,
  "run_complete_message": "RUN COMPLETE",
  "chapel_grace": true,
  "starting_goods": [8126],
  "starting_runes": 50000,
  "starting_golden_seeds": 5,
  "starting_sacred_tears": 3,
  "starting_larval_tears": 10,
  "care_package": [
    {"type": 0, "id": 1130008, "name": "Uchigatana +8"},
    {"type": 4, "id": 10100, "name": "Bloodhound's Step"}
  ],
  "remove_entities": [{"map": "m12_05_00_00", "entity_id": 12051500}]
}
```

Gate names use FogMod's FullName format: `{map}_{gate_name}`.

**Key field groups:**
- `nodes`/`edges`: DAG topology for visualization and spoiler
- `connections`/`area_tiers`: FogModWrapper consumption (fog gate wiring + enemy scaling)
- `event_map`/`final_node_flag`/`finish_event`: racing zone tracking
- `finish_boss_defeat_flag`: boss DefeatFlag from fog.txt (primary source for death detection)
- `starting_*`/`care_package`: player starting loadout
- `care_package[].type`: 0=Weapon, 1=Protector, 2=Accessory, 3=Goods, 4=Gem (Ash of War)
- `remove_entities`: vanilla warp MSB assets to delete

## Care Package System

SpeedFog gives players a randomized starting build so they can be combat-ready from the first fog gate.

**Python side** (`care_package.py`):
- Loads curated item pools from `data/care_package_items.toml`
- Samples weapons, shields, catalysts, armor, talismans, sorceries, incantations, crystal tears, and ashes of war
- Weapon upgrade levels are seed-deterministic: standard weapons get `weapon_upgrade`, somber weapons get `floor(standard / 2.5)`
- Per-category counts configurable in `[care_package]` config section

**C# side** (`StartingItemInjector.cs`):
- Weapons (type 0), Armor (1), Accessories (2), Goods (3): given via `DirectlyGivePlayerItem` EMEVD instruction
- Ashes of War (type 4): given via `ShopLineupParam` with equipType=4, price=0 in Twin Maiden Husks shop (EMEVD's DirectlyGivePlayerItem doesn't support Gem type)

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
| Gems via shop | ShopLineupParam | EMEVD can't give Gem type; shop with price=0 works |
| Initial spawn | WarpPlayer in EMEVD | Engine controls first spawn, not MSB/SetPlayerRespawnPoint |
| Rebirth | ESD editing at graces | ConsistentID 73, uses larval tears as currency |

## FogMod Integration

FogModWrapper configures FogMod for SpeedFog:

**Hard-coded in Program.cs:**

| Option | Value | Purpose |
|--------|-------|---------|
| `crawl` | true | Dungeon crawler mode, enables tier progression |
| `unconnected` | true | Allow edges without vanilla connections |
| `req_backportal` | true | Boss rooms have return warps |
| `roundtable` | true | Roundtable Hold available from start |
| `newgraces` | true | Additional Sites of Grace |
| `dlc` | true | Include Shadow of the Erdtree areas |
| `coupledminor` | true | Keep transporter chest warps as coupled pairs |
| `req_dungeon/cave/tunnel/catacomb/grave/graveyard/forge/gaol/legacy/major/underground/minorwarp` | true | Include all dungeon types in fog graph |

**From graph.json `options`** (set by Python `output.py`):

| Option | Default | Purpose |
|--------|---------|---------|
| `scale` | true | Enemy scaling per tier |
| `shuffle` | true | Randomize fog gate connections |

Explicitly **not** set: `req_evergaol` (evergaols lack StakeAsset in fog.txt, causing errors).

ConfigVars set all key items and progression flags to TRUE (given at start, prevents softlocks).

### Graph Pre-processing

After FogMod's `Graph.Construct()`, SpeedFog applies two workarounds:

1. **Disconnect trivial edges**: In crawl mode, FogMod marks "trivial" entrances as IsFixed and pre-connects them. SpeedFog needs these edges available for its own graph, so it disconnects them.
2. **Exclude evergaols**: Sets `IsExcluded=true` and `BossTrigger=0` on evergaol areas to prevent FogMod from creating Stakes for zones without StakeAsset data.

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
<seed_dir>/
├── graph.json                # DAG data (always generated)
├── spoiler.txt               # Path spoiler log (--spoiler)
├── ModEngine/                # ModEngine 2 (auto-downloaded)
├── mods/
│   ├── fogmod/               # FogMod output (fog gates, scaling, events)
│   │   ├── param/gameparam/regulation.bin
│   │   ├── event/*.emevd.dcx
│   │   ├── map/mapstudio/*.msb.dcx
│   │   ├── script/talk/*.talkesdbnd.dcx
│   │   └── msg/engus/*.fmg
│   └── itemrando/            # Item Randomizer output (optional)
├── lib/                      # Runtime DLLs (crash fix, helper)
├── config_speedfog.toml      # ModEngine config
├── launch_speedfog.bat       # Windows launcher
└── launch_speedfog.sh        # Linux/Proton launcher
```

## Further Documentation

- [event-flags.md](event-flags.md) — Event flag allocation, EMEVD event IDs, VirtualMemoryFlag constraints
- [dag-generation.md](dag-generation.md) — DAG generation algorithm: topology operations, cluster compatibility, retry system
- [clusters.md](clusters.md) — Cluster generation: zone grouping, fog classification, bidirectional detection
- [item-giving-limitations.md](item-giving-limitations.md) — EMEVD item type constraints and Ash of War workaround

## References

- FogRando: https://www.nexusmods.com/eldenring/mods/3295
- Item Randomizer: https://www.nexusmods.com/eldenring/mods/428
- SoulsFormats: https://github.com/soulsmods/SoulsFormatsNEXT
- ModEngine 2: https://github.com/soulsmods/ModEngine2
