# FogRando Graph Generation Logic

Reference documentation for understanding how FogRando builds its connection graph from `fog.txt` data and user options.

**Source files analyzed:**
- `reference/fogrando-data/fog.txt` - Zone and entrance definitions
- `reference/fogrando-src/Graph.cs` - Graph construction
- `reference/fogrando-src/GraphConnector.cs` - Connection algorithm

---

## 1. fog.txt Structure

The `fog.txt` file has two main sections:

### 1.1 Areas Section (Lines 1-6543)

Defines zones/areas in the game world.

```yaml
- Name: limgrave_stormfootcatacombs
  Text: Limgrave - Stormfoot Catacombs
  Maps: m30_02_00_00
  MainMaps: m30_02_00_00        # Optional - marks as "core" area
  DefeatFlag: 30020800          # Boss defeat flag (for boss areas)
  BossTrigger: 30022805         # Boss trigger region
  To:                           # World connections (not fog gates)
  - Area: another_area
    Text: description
    Cond: some_condition
    Tags: some_tags
  Tags: minidungeon
```

**Key Area Fields:**

| Field | Description |
|-------|-------------|
| `Name` | Unique identifier for the zone |
| `Text` | Display name |
| `Maps` | List of map IDs belonging to this zone |
| `MainMaps` | Primary map ID - marks zone as **core** |
| `DefeatFlag` | Event flag set when boss is defeated |
| `BossTrigger` | Region ID that triggers boss fight |
| `TrapFlag` | Flag for boss trap mechanics |
| `OpenArea` | Parent overworld area (for field bosses) |
| `To` | World connections to other areas (not randomized fog gates) |
| `Tags` | Zone type tags (see Section 3) |

### 1.2 Entrances Section (Lines 6544+)

Defines warp points and fog gates between zones.

```yaml
Entrances:
- Name: AEG099_001_9000
  ID: 30021800
  Area: m30_02_00_00
  Text: Erdtree Burial Watchdog front
  Silo: fromminor
  ASide:
    Area: limgrave_stormfootcatacombs
    Text: before Erdtree Burial Watchdog's arena
    Cond: limgrave_stormfootcatacombs
    Tags: some_tags
  BSide:
    Area: limgrave_stormfootcatacombs_boss
    Text: at the front of Erdtree Burial Watchdog's arena
    BossDefeatName: area
    BossTriggerName: area
    DestinationMap: m30_02_00_00
    Tags: main
  Tags: dungeon catacomb
```

**Key Entrance Fields:**

| Field | Description |
|-------|-------------|
| `Name` | Asset name (e.g., AEG099_001_9000) |
| `ID` | Numeric identifier |
| `Area` | Map ID where the entrance exists |
| `ASide` | One end of the connection |
| `BSide` | Other end of the connection |
| `Silo` | Grouping for affinity matching (see Section 5) |
| `PairWith` | Links two warps for bidirectional teleportation |
| `Location` | Asset ID for the physical object |
| `Tags` | Connection type tags |

**Side Fields (ASide/BSide):**

| Field | Description |
|-------|-------------|
| `Area` | Zone name this side connects to |
| `Text` | Description of this connection point |
| `Cond` | Condition expression required to use |
| `DestinationMap` | Target map ID for cross-map warps |
| `BossDefeatName` | Boss defeat flag reference |
| `BossTriggerName` | Boss trigger reference |
| `Tags` | Side-specific tags |

---

## 2. Connection Types

### 2.1 Bidirectional Fog Gates (Default)

Standard fog gates that allow travel in both directions.

**Characteristics:**
- No `unique` tag
- Both ASide and BSide defined
- Each side gets both EXIT and ENTRANCE edges

**Graph representation:**
```
Zone A                    Zone B
├── Exit → Zone B        ├── Exit → Zone A
└── Entrance ← Zone B    └── Entrance ← Zone A
```

**Code reference:** `Graph.cs:1502-1515`
```csharp
AddEdge(side7, entrance3, isExit: true);   // EXIT from side
AddEdge(side7, entrance3, isExit: false);  // ENTRANCE to side
```

### 2.2 Unidirectional Warps (`unique` tag)

One-way connections like sending gates, coffins, and portals.

**Characteristics:**
- Has `unique` tag
- Only creates EXIT from ASide, ENTRANCE to BSide
- Cannot be traversed backwards

**Examples:**
- Belfries teleporters (`Tags: unique belfries`)
- Tower of Return chest (`Tags: unique legacy opensplit`)
- Coffin rides (`Tags: unique underground`)

**Code reference:** `Graph.cs:1371-1383`

### 2.3 Selfwarps

Special warps where ASide and BSide are in the **same zone**.

**Characteristics:**
- Has `selfwarp` tag
- Both edges belong to the same area
- Edges are paired but independently randomizable

**Created from:**
1. **Backportals** when `opt["req_backportal"]` is enabled
2. **Artificial evergaols** for crawl mode (`crawlonly selfwarp`)

**Code reference:** `Graph.cs:1086-1094`
```csharp
if (opt["req_backportal"]) {
    warp2.BSide = new Side { Area = warp2.ASide.Area, ... };
    warp2.AddTag("selfwarp");
}
```

### 2.4 Backportals

Return warps after defeating a boss.

**Original definition:**
```yaml
- Name: 30022840
  ASide:
    Area: limgrave_stormfootcatacombs_boss
    Text: return to entrance after Erdtree Burial Watchdog
  BSide:
    Area: limgrave_stormfootcatacombs
    Text: arriving at Stormfoot Catacombs after returning
  Tags: backportal dungeon catacomb
```

**Behavior based on options:**

| Option | Result |
|--------|--------|
| `req_backportal = false` | Marked `unused`, excluded from graph |
| `req_backportal = true` | Converted to selfwarp in boss room |

When converted to selfwarp, the boss room gains a second bidirectional connection point.

### 2.5 World Connections (To: section)

Direct area-to-area links defined in the Areas section, not in Entrances.

**Characteristics:**
- Defined via `To:` field in area definition
- Marked as `IsWorld = true`
- Often have conditions (`Cond:`)
- Not physical fog gates

**Example:**
```yaml
- Name: stormveil_start
  To:
  - Area: stormveil
    Text: with Rusty Key or talking to Gostoc
    Cond: OR scalepass rustykey
```

---

## 3. Tag System

### 3.1 Area Tags

| Tag | Description | Effect on Graph |
|-----|-------------|-----------------|
| `overworld` | Open world area | Excluded from core in crawl mode |
| `minidungeon` | Mini-dungeon (cave, catacomb, etc.) | Low scaling priority |
| `trivial` | Transition area | Usually fixed, not randomized |
| `escape` | Boss arena with escape route | Special handling |
| `start` | Starting area | Graph origin |
| `minor` | Minor boss | Lower tier scaling |
| `optional` | Optional content | Can be excluded |
| `final` | Final boss area | Special routing |
| `dlc` | DLC content | Excluded if DLC disabled |
| `underground` | Underground area | Special categorization |

### 3.2 Entrance Tags

| Tag | Description | Effect |
|-----|-------------|--------|
| `dungeon` | Inside a dungeon | Used for dungeon-specific filtering |
| `cave` | Cave connection | Filtered by `req_cave` |
| `catacomb` | Catacomb connection | Filtered by `req_catacomb` |
| `tunnel` | Tunnel connection | Filtered by `req_tunnel` |
| `gaol` | Gaol connection | Filtered by `req_gaol` |
| `legacy` | Legacy dungeon | Higher priority |
| `major` | Major connection | Higher priority |
| `unique` | One-way warp | Unidirectional only |
| `backportal` | Boss return warp | See Section 2.4 |
| `door` | Internal door | Always fixed |
| `norandom` | Never randomize | Always fixed |
| `crawlonly` | Crawl mode only | Excluded if not crawl |
| `selfwarp` | Same-area warp | Special pairing |
| `unused` | Disabled | Excluded from graph |

### 3.3 Conditional Tags (*only)

Tags ending in `only` are used for conditional inclusion:

| Tag | Active When |
|-----|-------------|
| `caveonly` | `opt["req_cave"]` is true |
| `catacombonly` | `opt["req_catacomb"]` is true |
| `gaolonly` | `opt["req_gaol"]` is true |
| `crawlonly` | `opt["crawl"]` is true |
| `fortressonly` | `opt[Feature.SegmentFortresses]` is true |

**Code reference:** `Graph.cs:1069-1076`
```csharp
foreach (string item8 in new List<string> { "cave", "catacomb", "forge", "gaol" })
{
    if (warp2.HasTag(item8 + "only") && !opt["req_" + item8])
    {
        warp2.AddTag("unused");
    }
}
```

---

## 4. Options and Their Effects

### 4.1 Mode Options

| Option | Description | Effect |
|--------|-------------|--------|
| `crawl` | Dungeon Crawler mode | Excludes overworld from core, enables dungeon-specific logic |
| `shuffle` | World Shuffle mode | Standard full randomization |
| `bossrush` | Boss Rush mode | Special segmented routing |
| `endless` | Endless mode | Segmented with no end |

### 4.2 Content Options

| Option | Description | Effect |
|--------|-------------|--------|
| `dlc` | Include DLC | Enables DLC areas and connections |
| `boss` | Boss fog gates | If false, boss fogs are fixed |
| `minor` | Minor connections | If false, minor PvP fogs are fixed |
| `major` | Major connections | If false, major PvP fogs are fixed |

### 4.3 Dungeon Options (Crawl Mode)

| Option | Description | Effect |
|--------|-------------|--------|
| `req_cave` | Include caves | Enables `caveonly` connections |
| `req_catacomb` | Include catacombs | Enables `catacombonly` connections |
| `req_gaol` | Include gaols | Enables `gaolonly` connections |
| `req_backportal` | Include backportals | Converts backportals to selfwarps |
| `req_minorwarp` | Minor warps | Affects `uniqueminor` handling |
| `req_dungeon` | Require dungeons | Validation check |

### 4.4 Coupling Options

| Option | Description | Effect |
|--------|-------------|--------|
| `coupledwarp` | Coupled sending gates | If false, `uniquegate` becomes `unique` |
| `coupledminor` | Coupled minor warps | If false, `uniqueminor` becomes `unique` |
| `affinity` | Affinity matching | Enables silo-based matching |

---

## 5. Silo System

Silos group entrances by type for balanced distribution during randomization.

### 5.1 Silo Types

| Silo | Paired With | Description |
|------|-------------|-------------|
| `toopen` | `fromopen` | Open world connections |
| `tominor` | `fromminor` | Minor dungeon / evergaol connections |
| `tomini` | `frommini` | Mini-dungeon entrances |
| `toroom` | `fromroom` | Small room connections |

### 5.2 Silo Matching

When `opt["affinity"]` is enabled, connections are matched within their silo:

**Code reference:** `GraphConnector.cs:268-276`
```csharp
foreach (string silo in new List<string> { "minor", "mini", "room" })
{
    List<Edge> allTos = list.Where(e => e.Side.Silo == "to" + silo).ToList();
    List<Edge> allFroms = list2.Where(e => e.Side.Silo == "from" + silo).ToList();
    ConnectEdges(allTos, allFroms, silo + " silo");
}
```

This ensures:
- Evergaol exits connect to appropriate evergaol-style destinations
- Mini-dungeon entrances connect to mini-dungeon exits
- Academy gates connect properly

---

## 6. Zone Grouping (Core/Pseudo-Core)

### 6.1 Core Areas

Areas marked as "core" are central to the randomization graph.

**Determined by:**
1. Has `MainMaps` field defined
2. Propagated via world connections from core areas

**Code reference:** `Graph.cs:607-676`

### 6.2 Pseudo-Core Areas

Areas connected to core areas via world connections but not themselves core.

**Example: Stormveil**
```
stormveil_margit    ← Boss arena (escape tag)
       ↓
stormveil_start     ← PSEUDO-CORE (has To: → stormveil)
       ↓ (world connection)
stormveil           ← CORE (has MainMaps: m10_00_00_00)
       ↓
stormveil_godrick   ← Boss arena
       ↓
stormveil_throne    ← trivial
```

**Grouping logic:**
1. `stormveil` has `MainMaps` → marked CORE
2. `stormveil_start` has `To: stormveil` → marked pseudo-core
3. During routing, pseudo-core is merged with its core parent

### 6.3 Crawl Mode Core Exclusions

In crawl mode, these area types are excluded from core:

```csharp
list.Add("open");
list.AddRange(new string[] { "cave", "tunnel", "catacomb", "grave", "cellar", "gaol", "forge" });
```

**Code reference:** `Graph.cs:1120-1124`

---

## 7. Graph Construction Process

### 7.1 Phase 1: Load and Filter

1. Parse Areas section
2. Parse Entrances section
3. Apply tag-based filtering:
   - Remove `unused` entrances
   - Mark `norandom`, `door`, `trivial` as `IsFixed`
   - Handle `crawlonly` vs non-crawl mode
   - Process `*only` conditional tags

### 7.2 Phase 2: Create Edges

For each entrance:

**Bidirectional fog gates:**
```csharp
// For each side
AddEdge(side, entrance, isExit: true);   // Can leave
AddEdge(side, entrance, isExit: false);  // Can enter
```

**Unidirectional warps (`unique`):**
```csharp
AddEdge(ASide, warp, isExit: true);      // Exit only
AddEdge(BSide, warp, isExit: false);     // Entrance only
```

**Selfwarps:**
```csharp
edge.Pair = edge2;
edge2.Pair = edge;
// Both edges in same area, paired for joint randomization
```

### 7.3 Phase 3: Mark Core Areas

1. Identify areas with `MainMaps`
2. Propagate core status via world connections
3. Mark pseudo-core areas
4. Apply crawl mode exclusions

**Code reference:** `Graph.cs:607-676` (`MarkCoreAreas()`)

### 7.4 Phase 4: Connect

**Crawl mode** (`ConnectRandom()`):
1. Fix peripheral (non-core) connections
2. Map evergaols to minidungeon bosses
3. Apply silo-based affinity matching
4. Connect remaining edges

**Standard mode:**
1. Collect all unconnected edges
2. Sort by priority (low connection areas first)
3. Randomly pair exits with entrances
4. Validate graph connectivity

### 7.5 Phase 5: Calculate Tiers

After connection, assign scaling tiers based on distance from start:

```csharp
g.AreaTiers = new Dictionary<string, int>();
g.AreaTiers["chapel_start"] = 1;
g.AreaTiers["erdtree"] = 17;

// Interpolate tiers along paths between major bosses
```

**Code reference:** `GraphConnector.cs:1616-1701`

---

## 8. Mini-Dungeon Connection Model

### 8.1 Standard Mini-Dungeon Structure

```
[Overworld]
    ↕ entrance fog (bidirectional)
[Main Dungeon Area]
    ↕ boss fog (bidirectional)
[Boss Room]
    ↓ backportal (when enabled: selfwarp)
[Main Dungeon Area]
```

### 8.2 Connection Counts (with backportal enabled)

| Zone | Connection Points | Exits | Entrances |
|------|-------------------|-------|-----------|
| Main Area | 2 | 2 | 2 |
| Boss Room | 2 | 2 | 2 |

**Main Area connections:**
1. Overworld entrance (bidirectional)
2. Boss fog (bidirectional)

**Boss Room connections:**
1. Boss fog (bidirectional)
2. Backportal as selfwarp (bidirectional)

### 8.3 Eligibility for DAG

With backportals enabled, mini-dungeons are **eligible** for DAG structures because:
- Each zone has at least 2 connection points
- Each connection point is bidirectional (can be used as entry OR exit)
- The graph can route through: `Entry → Main → Boss → Exit`

---

## 9. Key Code References

| Feature | File | Lines | Function |
|---------|------|-------|----------|
| Mode selection | GraphConnector.cs | 42-47 | `Connect()` |
| Tag filtering | Graph.cs | 878-1104 | Entrance processing loop |
| Backportal handling | Graph.cs | 1079-1099 | Backportal → selfwarp conversion |
| Core area marking | Graph.cs | 607-676 | `MarkCoreAreas()` |
| Edge creation | Graph.cs | 320-345 | `AddEdge()` |
| Bidirectional pairing | Graph.cs | 1402-1424 | Dictionary-based pairing |
| Selfwarp pairing | Graph.cs | 1391-1395 | Direct edge pairing |
| Silo matching | GraphConnector.cs | 268-276 | Affinity-based connection |
| Tier calculation | GraphConnector.cs | 1616-1701 | `AreaTiers` assignment |
| Low connection priority | GraphConnector.cs | 191-199 | `lowConnection` set |

---

## 10. Implications for SpeedFog

### 10.1 Parsing Requirements

1. **Parse Areas section** for zone definitions and tags
2. **Parse Entrances section** for fog gate definitions
3. **Filter based on desired mode:**
   - Exclude `crawlonly` if not using crawl-like logic
   - Exclude `unused` entries
   - Include `backportal` as bidirectional connections

### 10.2 Connection Counting

For each zone, count:
- Entrances where `ASide.Area == zone` (gives 1 exit + 1 entrance)
- Entrances where `BSide.Area == zone` (gives 1 exit + 1 entrance)
- Backportals where `ASide.Area == zone` (boss rooms gain connections)

### 10.3 Eligible Zones for DAG

A zone is eligible for DAG inclusion if:
- It has **at least 2 connection points**
- It is not tagged `trivial`, `unused`, or similar exclusion tags
- Its connections are not all `unique` (one-way only)

### 10.4 Zone Grouping

Consider merging:
- Zones connected via `To:` world connections
- `*_start` zones with their parent (e.g., `stormveil_start` + `stormveil`)
- Boss arenas with their dungeon (if treating as single logical unit)
