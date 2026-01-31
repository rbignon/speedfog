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

## 5. Option → Tag Effect Matrix (Elden Ring)

This section provides the complete mapping between options and their effects on tags during graph generation.

### 5.1 Entrance Processing (Fog Gates)

**Code reference:** `Graph.cs:967-993`

#### Always Applied (No Option Required)

| Tag | Effect | Code |
|-----|--------|------|
| `unused` | Skip entirely | `continue` at L872-874 |
| `norandom` | `IsFixed = true` | L973 |
| `door` | `IsFixed = true` | L973 |
| `dlcend` | `IsFixed = true` | L973 |
| `trivial` | `IsFixed = true` (unless Segmented) | L977-979 |

#### Crawl Mode Toggle

| Condition | Tag | Effect | Code |
|-----------|-----|--------|------|
| `opt["crawl"] = true` | `nocrawl` | `unused` | L981 |
| `opt["crawl"] = false` | `crawlonly` | `unused` | L981 |

#### Fortress Mode Toggle

| Condition | Tag | Effect | Code |
|-----------|-----|--------|------|
| `opt[Feature.SegmentFortresses] = true` | `nofortress` | `unused` | L989 |
| `opt[Feature.SegmentFortresses] = false` | `fortressonly` | `unused` | L989 |

### 5.2 Warp Processing (Teleporters, Backportals)

**Code reference:** `Graph.cs:995-1104`

#### Highwall Special Case

| Condition | Effect | Code |
|-----------|--------|------|
| `!opt["pvp"] && !opt["boss"]` | Add `norandom` tag | L997-1001 |
| `opt["pvp"] \|\| opt["boss"]` | Add `unused` tag | L1003-1005 |

#### Always Applied

| Tag | Effect | Code |
|-----|--------|------|
| `unused` | Skip entirely | L1008-1010 |
| `norandom` | `IsFixed = true` | L1012-1014 |

#### DLC Filtering

| Condition | Tag | Effect | Code |
|-----------|-----|--------|------|
| `!opt["dlc1"]` | `dlc1` | `IsFixed = true` | L1028-1031 |
| `!opt["dlc2"]` | `dlc2` | `IsFixed = true` | L1032-1035 |

#### Unique Warp Handling

| Condition | Tag | New Tag Added | Code |
|-----------|-----|---------------|------|
| `!opt["coupledwarp"] && !opt[Feature.Segmented]` | `uniquegate` | `unique` | L1036-1039 |
| `!opt["coupledminor"] && !opt[Feature.Segmented]` | `uniqueminor` | `unique` | L1040-1043 |
| `opt["crawl"] && !opt["req_minorwarp"]` | `uniqueminor` | `unique` | L1044-1047 |

#### Crawl-Only / Fortress-Only

| Condition | Tag | Effect | Code |
|-----------|-----|--------|------|
| `!opt["crawl"]` | `crawlonly` | `unused` | L1048-1051 |
| `!opt[Feature.SegmentFortresses]` | `fortressonly` | `unused` | L1052-1055 |
| `opt[Feature.SegmentFortresses]` | `nofortress` | `unused` | L1052-1055 |
| `!opt[Feature.Segmented]` | `segmentonly` | `unused` | L1056-1059 |
| `opt[Feature.Segmented]` | `nosegment` | `unused` | L1056-1059 |

#### Dungeon Type Filtering (Crawl Mode Only)

**Code reference:** `Graph.cs:1060-1078`

When `opt["crawl"] = true`:

| Condition | Tag | Effect |
|-----------|-----|--------|
| `!opt["req_cave"] \|\| opt["req_backportal"]` | `caveonly` | `unused` |
| `!opt["req_catacomb"] \|\| opt["req_backportal"]` | `catacombonly` | `unused` |
| `!opt["req_forge"] \|\| opt["req_backportal"]` | `forgeonly` | `unused` |
| `!opt["req_gaol"] \|\| opt["req_backportal"]` | `gaolonly` | `unused` |

Special case: `openremove` tag → `unused` + `remove` (L1062-1066)

#### Backportal Handling

**Code reference:** `Graph.cs:1079-1100`

| Condition | Effect | Code |
|-----------|--------|------|
| `opt[Feature.Segmented]` | Check for `unique` tag | L1082-1084 |
| `opt["req_backportal"]` | Convert to selfwarp (BSide.Area = ASide.Area) | L1086-1095 |
| `opt["crawl"] && HasTag("forge")` | Convert to selfwarp | L1081 |
| Otherwise | `unused` | L1096-1099 |

### 5.3 IsCore Determination

**Code reference:** `Graph.cs:1154-1240`

The `IsCore` flag determines if a connection is part of the main randomization graph.

#### Base Calculation

```
isCore = true (default)

if HasTag("minorwarp"):
    isCore = tagIsCore("minorwarp") AND (!hasListTag OR hasListCoreTag)
else if hasListTag:
    isCore = hasListCoreTag
```

Where:
- `list` = `["underground", "colosseum", "divine", "belfries", "graveyard", "evergaol"]`
- In crawl mode, `list` also includes: `["open", "cave", "tunnel", "catacomb", "grave", "cellar", "gaol", "forge"]`
- `tagIsCore(tag)` = `hasTag(tag) AND (opt["req_" + tag] OR opt["req_all"])`

#### Crawl Mode Overrides

| Condition | Tag | IsCore | Code |
|-----------|-----|--------|------|
| `opt["crawl"]` | `open` | `false` | L1169-1171 |
| `opt["crawl"]` | `neveropen` | `true` | L1173-1175 |
| `opt["crawl"] && !opt["req_rauhruins"]` | `rauhruins` | `false` | L1177-1180 |

### 5.4 Complete Decision Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ENTRANCE PROCESSING                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  HasTag("unused")? ──YES──> SKIP                                    │
│         │                                                           │
│         NO                                                          │
│         ↓                                                           │
│  HasTag("norandom" | "door" | "dlcend")? ──YES──> IsFixed = true    │
│         │                                                           │
│         NO                                                          │
│         ↓                                                           │
│  HasTag("trivial") && !Segmented? ──YES──> IsFixed = true           │
│         │                                                           │
│         NO                                                          │
│         ↓                                                           │
│  ┌──────────────────────────────────────┐                           │
│  │ opt["crawl"]?                        │                           │
│  │   YES: HasTag("nocrawl")? → unused   │                           │
│  │   NO:  HasTag("crawlonly")? → unused │                           │
│  └──────────────────────────────────────┘                           │
│         │                                                           │
│         ↓                                                           │
│  ┌──────────────────────────────────────┐                           │
│  │ SegmentFortresses?                   │                           │
│  │   YES: HasTag("nofortress")? → unused│                           │
│  │   NO:  HasTag("fortressonly")? →     │                           │
│  │        unused                        │                           │
│  └──────────────────────────────────────┘                           │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                          WARP PROCESSING                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  HasTag("unused")? ──YES──> SKIP                                    │
│         │                                                           │
│         NO                                                          │
│         ↓                                                           │
│  HasTag("uniquegate") && !coupledwarp && !Segmented?                │
│         │──YES──> AddTag("unique")                                  │
│         │                                                           │
│  HasTag("uniqueminor") && !coupledminor && !Segmented?              │
│         │──YES──> AddTag("unique")                                  │
│         │                                                           │
│  HasTag("uniqueminor") && crawl && !req_minorwarp?                  │
│         │──YES──> AddTag("unique")                                  │
│         │                                                           │
│         ↓                                                           │
│  ┌──────────────────────────────────────┐                           │
│  │ crawl mode dungeon filtering:        │                           │
│  │                                      │                           │
│  │ For each type in [cave, catacomb,    │                           │
│  │                   forge, gaol]:      │                           │
│  │   HasTag(type+"only") &&             │                           │
│  │   (!req_{type} || req_backportal)?   │                           │
│  │     → unused                         │                           │
│  └──────────────────────────────────────┘                           │
│         │                                                           │
│         ↓                                                           │
│  ┌──────────────────────────────────────┐                           │
│  │ HasTag("backportal")?                │                           │
│  │   Segmented? → check unique          │                           │
│  │   req_backportal || (crawl && forge)?│                           │
│  │     → Convert to selfwarp            │                           │
│  │   Otherwise → unused                 │                           │
│  └──────────────────────────────────────┘                           │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       IsCore DETERMINATION                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Default: isCore = true                                             │
│                                                                     │
│  Category tags: [underground, colosseum, divine, belfries,          │
│                  graveyard, evergaol]                               │
│  + In crawl mode: [open, cave, tunnel, catacomb, grave,             │
│                    cellar, gaol, forge]                             │
│                                                                     │
│  For each category tag the entrance has:                            │
│    isCore = opt["req_{tag}"] OR opt["req_all"]                      │
│                                                                     │
│  Crawl mode overrides:                                              │
│    HasTag("open")? → isCore = false                                 │
│    HasTag("neveropen")? → isCore = true                             │
│    HasTag("rauhruins") && !req_rauhruins? → isCore = false          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.5 Summary: Options That Affect Graph Content

| Option | Tags Affected | Inclusion Effect |
|--------|---------------|------------------|
| `crawl` | `crawlonly`, `nocrawl`, `open`, category tags | Mode switch |
| `req_cave` | `caveonly`, `cave` (IsCore) | Include/exclude caves |
| `req_catacomb` | `catacombonly`, `catacomb` (IsCore) | Include/exclude catacombs |
| `req_gaol` | `gaolonly`, `gaol` (IsCore) | Include/exclude gaols |
| `req_backportal` | `backportal` | Enable boss return warps |
| `req_minorwarp` | `uniqueminor` | Minor warp coupling |
| `req_rauhruins` | `rauhruins` (IsCore) | Rauh Ruins in crawl |
| `req_all` | All category tags | Override for IsCore |
| `coupledwarp` | `uniquegate` | Sending gate directionality |
| `coupledminor` | `uniqueminor` | Minor warp directionality |
| `dlc1`, `dlc2` | `dlc1`, `dlc2` | DLC content inclusion |
| `Feature.Segmented` | `segmentonly`, `nosegment`, `trivial` | Segmented mode |
| `Feature.SegmentFortresses` | `fortressonly`, `nofortress` | Fortress segmentation |

---

## 6. Silo System

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

## 7. Zone Grouping (Core/Pseudo-Core)

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

## 8. Graph Construction Process

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

## 9. Mini-Dungeon Connection Model

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

## 10. Key Code References

| Feature | File | Lines | Function |
|---------|------|-------|----------|
| Mode selection | GraphConnector.cs | 42-47 | `Connect()` |
| Entrance tag filtering | Graph.cs | 967-993 | Entrance processing (ER) |
| Warp tag filtering | Graph.cs | 995-1104 | Warp processing loop |
| Unique warp handling | Graph.cs | 1036-1047 | `uniquegate`/`uniqueminor` |
| Dungeon type filtering | Graph.cs | 1060-1078 | `*only` tag handling |
| Backportal handling | Graph.cs | 1079-1099 | Backportal → selfwarp conversion |
| IsCore determination | Graph.cs | 1154-1240 | Core status calculation |
| Core area marking | Graph.cs | 607-676 | `MarkCoreAreas()` |
| Edge creation | Graph.cs | 320-345 | `AddEdge()` |
| Bidirectional pairing | Graph.cs | 1402-1424 | Dictionary-based pairing |
| Selfwarp pairing | Graph.cs | 1391-1395 | Direct edge pairing |
| Silo matching | GraphConnector.cs | 268-276 | Affinity-based connection |
| Tier calculation | GraphConnector.cs | 1616-1701 | `AreaTiers` assignment |
| Low connection priority | GraphConnector.cs | 191-199 | `lowConnection` set |

---

## 11. Implications for SpeedFog

### 11.1 SpeedFog Option Equivalences

SpeedFog needs to decide which FogRando options to "virtually enable" for parsing fog.txt.

**Goal:** Maximize zones with 2-3 bidirectional connection points for DAG generation.

#### Recommended Option Settings

| FogRando Option | SpeedFog Setting | Rationale |
|-----------------|------------------|-----------|
| `crawl` | **false** | We want all connection types, not just dungeon-focused |
| `req_backportal` | **true** | Boss rooms need 2nd connection point |
| `req_cave` | **true** | Include cave connections |
| `req_catacomb` | **true** | Include catacomb connections |
| `req_gaol` | **true** | Include gaol connections |
| `req_tunnel` | **true** | Include tunnel connections |
| `coupledwarp` | **true** | Make sending gates bidirectional where possible |
| `coupledminor` | **true** | Make minor warps bidirectional |
| `dlc` | **false** | Base game only for v1 |
| `Feature.Segmented` | **false** | Standard mode |

#### Resulting Tag Processing

With these settings, SpeedFog should:

| Tag | Action |
|-----|--------|
| `unused` | **Exclude** |
| `crawlonly` | **Exclude** (crawl=false) |
| `dlc`, `dlc1`, `dlc2` | **Exclude** |
| `norandom`, `door` | **Include as fixed** (not randomizable but traversable) |
| `trivial` | **Include** (needed for world connections) |
| `backportal` | **Convert to bidirectional** (like selfwarp) |
| `unique` (inherent) | **Include as unidirectional** |
| `uniquegate` | **Convert to bidirectional** (coupledwarp=true) |
| `uniqueminor` | **Convert to bidirectional** (coupledminor=true) |
| `*only` (caveonly, etc.) | **Include** (req_*=true) |

### 11.2 Inherently Unidirectional Connections

Some connections **cannot be made bidirectional** regardless of options:

#### Event-Triggered Warps

| Connection | From | To | Tag | Notes |
|------------|------|-----|-----|-------|
| Burn Erdtree | `flamepeak_firegiant` | `farumazula_prestart` | `unique major` | Story progression, irreversible |
| After Maliketh | `farumazula_maliketh` | `leyndell2` | `unique major` | Triggers Ashen Capital |

#### Coffin Rides

| Connection | From | To | Tag |
|------------|------|-----|-----|
| Deeproot → Ainsel | `deeproot` | `ainsel` | `unique underground` |

#### Belfry Teleporters

| Connection | From | To | Tag |
|------------|------|-----|-----|
| Belfry → Chapel | `liurnia` | `chapel_postboss` | `unique belfries` |
| Belfry → Nokron | `liurnia` | `siofra_limited` | `unique belfries` |
| Belfry → Farum | `liurnia` | `farumazula_belfries` | `unique belfries` |

#### Trap Chests

| Connection | From | To | Tag |
|------------|------|-----|-----|
| Tower of Return | `peninsula` | `leyndell_divinebridge` | `unique legacy opensplit` |
| (Several in Caelid) | Various | Various | `unique` |

#### Sending Gates (Inherently One-Way)

| Connection | From | To | Tag |
|------------|------|-----|-----|
| Deeproot → Leyndell | `deeproot_boss` | `leyndell` | `unique underground` |
| Siofra → Dragonkin | `siofra` | `siofra_dragonkin` | `unique underground minorwarp` |

### 11.3 Parsing Requirements

1. **Parse Areas section** for zone definitions and tags
2. **Parse Entrances section** for fog gate definitions
3. **Apply SpeedFog option logic:**
   - Exclude `crawlonly`, `dlc*`, `unused`
   - Include all `*only` tags (treating req_*=true)
   - Convert `backportal` to bidirectional selfwarp
   - Convert `uniquegate`/`uniqueminor` to bidirectional
   - Keep inherent `unique` as unidirectional

### 11.4 Connection Counting

For each zone, count bidirectional connection points:

**From Entrances (fog gates):**
- Where `ASide.Area == zone` AND not `unique` → +1 bidirectional
- Where `BSide.Area == zone` AND not `unique` → +1 bidirectional

**From Warps:**
- `backportal` where `ASide.Area == zone` → +1 bidirectional (selfwarp)
- `uniquegate`/`uniqueminor` → +1 bidirectional (if coupled)
- Pure `unique` → +1 unidirectional only

**Total for DAG eligibility:**
- Need ≥2 bidirectional connections
- Unidirectional connections can serve as extra exits but not entries

### 11.5 Eligible Zones for DAG

A zone is eligible for DAG inclusion if:
- It has **at least 2 bidirectional connection points**
- OR it has **1 bidirectional + 1 unidirectional exit** (can be end of branch)
- It is not tagged `unused`
- It is not purely `trivial` with no connections

### 11.6 Zone Grouping Strategy

**Merge candidates:**
- `*_start` zones with their parent (e.g., `stormveil_start` + `stormveil`)
- Zones connected only via `To:` world connections
- Boss arenas that have no external connections except through parent dungeon

**Keep separate:**
- Boss rooms with backportals (they have 2 connections)
- Zones with multiple external fog gates

### 11.7 Summary: SpeedFog Simplifications

Compared to FogRando's full complexity, SpeedFog can simplify:

| FogRando Feature | SpeedFog Approach |
|------------------|-------------------|
| Multiple modes (crawl, shuffle, etc.) | Single "DAG mode" |
| Dynamic option processing | Fixed option equivalences |
| Silo-based affinity matching | Not needed (DAG determines connections) |
| IsCore calculation | All included zones are "core" |
| Tier-based scaling | Pre-assigned tiers per zone |
| Runtime graph validation | Build-time DAG validation |

**Key insight:** SpeedFog doesn't need to replicate FogRando's randomization algorithm. It only needs to:
1. Parse fog.txt to understand zone connectivity
2. Filter zones by connection count (≥2 bidirectional)
3. Generate a valid DAG structure
4. Output connection data for the C# writer

---

## 12. Complete Tag Reference

This section provides a comprehensive reference for every tag used in FogRando, organized by category. Each tag's precise effect on graph construction is documented with code references.

### 12.1 Core State Tags

These tags control whether an entrance/warp is included in the graph at all.

#### `unused`

**Meaning:** The entrance/warp should be completely excluded from the graph.

**Processing:**
- Entrances: Skipped immediately at L872-874
- Warps: Skipped immediately at L1008-1010

**Effect:** No edges created. The connection does not exist in the randomization graph.

**When applied:**
- Explicitly in fog.txt
- Dynamically added by various condition checks (crawl mode, DLC filtering, etc.)

---

#### `norandom`

**Meaning:** The connection exists but should never be randomized.

**Processing:** `entrance.IsFixed = true` (L973 for entrances, L1012-1014 for warps)

**Effect:**
- Edges are created
- Connection is fixed to its vanilla destination
- Traversable but not part of randomization pool

**Use cases:**
- Critical progression paths
- Tutorial connections
- Connections that would break if randomized

---

#### `door`

**Meaning:** An internal door connection (not a fog gate).

**Processing:**
- `entrance.IsFixed = true` (L973)
- Creates bidirectional world-style connection (L1449-1475)
- Only one edge pair per area-pair (deduplication via hashSet)

**Effect:**
- Fixed bidirectional passage
- May have conditions (`DoorCond`)
- Treated as world connection for traversal

**Example:** Locked doors requiring keys

---

### 12.2 Mode Toggle Tags

These tags control inclusion based on game mode (crawl, segmented, etc.).

#### `crawlonly`

**Meaning:** Include ONLY in Dungeon Crawler mode.

**Processing:**
- Entrances: `!opt["crawl"]` → `unused` (L981-987)
- Warps: `!opt["crawl"]` → `unused` (L1048-1050)

**Effect:** Excluded from World Shuffle mode, included in Crawl mode.

**Use cases:**
- Artificial dungeon connections for crawler mode
- Tier-gated overworld access points

---

#### `nocrawl`

**Meaning:** EXCLUDE from Dungeon Crawler mode.

**Processing:**
- Entrances: `opt["crawl"]` → `unused` (L981)
- World connections: `opt["crawl"]` → skipped (L1319)

**Effect:** Included in World Shuffle, excluded from Crawl mode.

**Use cases:**
- Open-world connections that don't fit dungeon crawler

---

#### `fortressonly`

**Meaning:** Include ONLY in Fortress Segmented mode.

**Processing:** `!opt[Feature.SegmentFortresses]` → `unused` (L989-991, L1052-1055)

**Effect:** Only exists when fortress segments are enabled.

**Use cases:**
- Special selfwarp exits for fortress segments
- Fortress-specific routing

---

#### `nofortress`

**Meaning:** EXCLUDE from Fortress Segmented mode.

**Processing:** `opt[Feature.SegmentFortresses]` → `unused` (L989-991, L1052-1055)

**Effect:** Excluded when fortress segments are enabled.

**Use cases:**
- World connections incompatible with fortress segmentation
- Dropped connections in fortress mode

---

#### `segmentonly`

**Meaning:** Include ONLY in Segmented modes (Boss Rush, Endless).

**Processing:** `!opt[Feature.Segmented]` → `unused` (L1056-1058)

**Effect:** Only exists in segmented game modes.

**Use cases:**
- Return warps for segment completion
- Segment-specific routing

---

#### `nosegment`

**Meaning:** EXCLUDE from Segmented modes.

**Processing:** `opt[Feature.Segmented]` → `unused` (L1056-1058)

**Effect:** Excluded from Boss Rush/Endless modes.

**Use cases:**
- Standard warps that don't work with segmentation

---

### 12.3 Dungeon Type Tags

These tags categorize connections by dungeon type and control inclusion via `req_*` options.

#### `dungeon`

**Meaning:** Connection is inside a dungeon (any type).

**Processing:**
- Used for `IsCore` calculation (L1127)
- Special handling for specific dungeons (L1101-1104)

**Effect:** General dungeon categorization. Affects core status in non-crawl mode.

---

#### `cave`

**Meaning:** Connection related to a cave-type dungeon.

**Processing:** Affects `IsCore` calculation when `opt["crawl"]` (L1123)

**Effect:** In crawl mode, `IsCore = opt["req_cave"] || opt["req_all"]`

---

#### `caveonly`

**Meaning:** Include only when caves are enabled in crawl mode.

**Processing:** `!opt["req_cave"] || opt["req_backportal"]` → `unused` (L1069-1076)

**Effect:** Excluded if caves disabled or backportals enabled (since backportals change dungeon topology).

---

#### `catacomb`

**Meaning:** Connection related to a catacomb-type dungeon.

**Processing:** Same as `cave` but for catacombs.

**Effect:** In crawl mode, `IsCore = opt["req_catacomb"] || opt["req_all"]`

---

#### `catacombonly`

**Meaning:** Include only when catacombs are enabled in crawl mode.

**Processing:** Same as `caveonly` but checks `req_catacomb`.

---

#### `tunnel`

**Meaning:** Connection related to a tunnel/mine dungeon.

**Processing:** Affects `IsCore` calculation in crawl mode (L1123).

**Effect:** In crawl mode, `IsCore = opt["req_tunnel"] || opt["req_all"]`

**Note:** Unlike other dungeon types, there is no `tunnelonly` tag - tunnels are not conditionally excluded via the `*only` loop.

---

#### `gaol`

**Meaning:** Connection related to a gaol (evergaol prison).

**Processing:** Affects `IsCore` calculation in crawl mode.

**Effect:** In crawl mode, `IsCore = opt["req_gaol"] || opt["req_all"]`

---

#### `gaolonly`

**Meaning:** Include only when gaols are enabled in crawl mode.

**Processing:** Same pattern as `caveonly`.

---

#### `forge`

**Meaning:** Connection related to a forge dungeon.

**Processing:**
- Affects `IsCore` calculation
- Special backportal handling: `opt["crawl"] && warp.HasTag("forge")` → convert to selfwarp (L1081)

**Effect:** Forges get selfwarps in crawl mode even without `req_backportal`.

---

#### `forgeonly`

**Meaning:** Include only when forges are enabled.

**Processing:** Same pattern as `caveonly` but checks `req_forge`.

---

#### `grave`

**Meaning:** Connection related to a grave-type dungeon (Hero's Graves).

**Processing:** Affects `IsCore` calculation in crawl mode (L1123).

**Effect:** In crawl mode, `IsCore = opt["req_grave"] || opt["req_all"]`

---

#### `cellar`

**Meaning:** Connection related to a cellar-type dungeon.

**Processing:** Affects `IsCore` calculation in crawl mode (L1123).

**Effect:** In crawl mode, `IsCore = opt["req_cellar"] || opt["req_all"]`

---

### 12.4 Warp Directionality Tags

These tags control whether warps are one-way or bidirectional.

#### `unique`

**Meaning:** Inherently one-way warp (cannot be made bidirectional).

**Processing:**
- Creates EXIT from ASide and ENTRANCE to BSide (L1371-1375), same as all warps
- No Pair relationship established (unlike bidirectional warps)
- **Key difference:** If one side is ignored (in `Ignore` set), unique warps are silently skipped (L1379-1383), while non-unique warps throw an exception

**Effect:**
- Strictly unidirectional
- Cannot be used as entry point from BSide
- Allows partial inclusion when one side's area is excluded

**Examples:**
- Coffin rides
- Story-progression teleports (Burning Erdtree)
- Belfry portals
- Trap chests

---

#### `uniquegate`

**Meaning:** One-way sending gate that CAN be made bidirectional.

**Processing:**
- If `!opt["coupledwarp"] && !opt[Feature.Segmented]` → add `unique` tag (L1036-1038)
- Otherwise: treated as bidirectional

**Effect:**
- With `coupledwarp=false`: unidirectional
- With `coupledwarp=true`: bidirectional (paired with return)

**Examples:** Sending gates between legacy dungeons

---

#### `uniqueminor`

**Meaning:** Minor one-way warp that CAN be made bidirectional.

**Processing:**
- If `!opt["coupledminor"] && !opt[Feature.Segmented]` → add `unique` (L1040-1042)
- If `opt["crawl"] && !opt["req_minorwarp"]` → add `unique` (L1044-1046)

**Effect:**
- With `coupledminor=false`: unidirectional
- With `coupledminor=true`: bidirectional
- In crawl without `req_minorwarp`: unidirectional

**Examples:** Minor warps between underground areas

---

#### `selfwarp`

**Meaning:** Both ends of the warp are in the SAME zone.

**Processing:**
- Edges are paired: `edge.Pair = edge2; edge2.Pair = edge` (L1391-1394)
- Both edges belong to same area

**Effect:** Creates a bidirectional connection point within a single zone. Essential for boss rooms to have 2 connection points.

**Created by:**
- `backportal` conversion when `req_backportal=true`
- Explicit `selfwarp` tag in fog.txt
- Artificial evergaol creation in crawl mode

---

### 12.5 Backportal System

#### `backportal`

**Meaning:** Return warp after defeating a boss.

**Processing (L1079-1099):**
```
if opt[Feature.Segmented]:
    warp.HasTag("unique")  // Note: appears to be a no-op (return value unused)
else if opt["req_backportal"] || (opt["crawl"] && HasTag("forge")):
    BSide.Area = ASide.Area  // Convert to selfwarp
    AddTag("selfwarp")
else:
    AddTag("unused")
```

**Effect:**
| Mode | Result |
|------|--------|
| Segmented | No action (L1084 appears to be a no-op in source) |
| Crawl + forge | Convert to selfwarp |
| `req_backportal=true` | Convert to selfwarp |
| Otherwise | Marked unused |

**Importance for SpeedFog:** Setting `req_backportal=true` gives boss rooms a second bidirectional connection point, making them eligible for DAG inclusion.

---

### 12.6 Core Status Tags

These tags affect whether a connection is part of the main randomization "core".

#### `open`

**Meaning:** Open-world (overworld) connection.

**Processing:**
- In crawl mode: `IsCore = false` (L1169-1171)
- Adds to category list for IsCore calculation (L1122)

**Effect:** Overworld connections are not core in crawl mode.

---

#### `neveropen`

**Meaning:** Force core status even in crawl mode.

**Processing:** In crawl mode: `IsCore = true` (L1173-1175)

**Effect:** Overrides `open` exclusion. Connection is always core.

**Use cases:** Critical dungeon entrances that happen to be in overworld

---

#### `openonly`

**Meaning:** Connection only exists in overworld context.

**Processing:** World connections with `openonly` excluded from core propagation (L670)

**Effect:** Not propagated during core marking.

**Use cases:** Tier-gated overworld access in crawl mode

---

#### `opensplit`

**Meaning:** Unique warp that can split core/non-core.

**Processing (L1251-1277):**
- In crawl with `unique`: if one side is core and one isn't
- If `opensplit`: keep core side, mark non-core as `unused` + `remove`
- Otherwise: mark entire warp as `unused` + `remove`

**Effect:** Allows one-way warps to work partially in crawl mode.

**Examples:** Tower of Return chest (overworld → Leyndell)

---

#### `openremove`

**Meaning:** Remove entirely in crawl mode.

**Processing:** `opt["crawl"]` → `unused` + `remove` (L1062-1066)

**Effect:** Completely removed from crawl mode, including physical asset.

**Use cases:** Sending gates that would break crawl progression

---

### 12.7 Location Category Tags

These tags categorize connections for `IsCore` calculation and silo matching.

#### `underground`

**Meaning:** Underground area connection.

**Processing:** Category tag for `IsCore` calculation (L1119)

**Effect:** `IsCore = opt["req_underground"] || opt["req_all"]`

---

#### `belfries`

**Meaning:** Four Belfries teleporter connection.

**Processing:** Category tag for `IsCore` calculation (L1119)

**Effect:** `IsCore = opt["req_belfries"] || opt["req_all"]`

---

#### `colosseum`

**Meaning:** Colosseum arena connection.

**Processing:** Category tag for `IsCore` calculation (L1119)

**Effect:** `IsCore = opt["req_colosseum"] || opt["req_all"]`

---

#### `divine`

**Meaning:** Divine Tower connection.

**Processing:** Category tag for `IsCore` calculation (L1119)

**Effect:** `IsCore = opt["req_divine"] || opt["req_all"]`

---

#### `graveyard`

**Meaning:** Stranded Graveyard connection.

**Processing:** Category tag for `IsCore` calculation (L1119)

**Effect:** `IsCore = opt["req_graveyard"] || opt["req_all"]`

---

#### `evergaol`

**Meaning:** Evergaol connection.

**Processing:** Category tag for `IsCore` calculation (L1119)

**Effect:** `IsCore = opt["req_evergaol"] || opt["req_all"]`

---

#### `minorwarp`

**Meaning:** Minor inter-area warp.

**Processing (L1159-1162):**
```
if hasTag("minorwarp"):
    isCore = tagIsCore("minorwarp") AND (!hasListTag OR hasListCoreTag)
```

**Effect:** Requires both `req_minorwarp` AND any applicable category tag.

---

#### `rauhruins`

**Meaning:** Rauh Ruins connection (DLC).

**Processing:**
- In crawl: `!opt["req_rauhruins"]` → `IsCore = false` (L1177-1180)
- Dungeon items with `rauhruins` excluded if `req_rauhruins` (L1290-1293)

**Effect:** Rauh Ruins optionally included in crawl mode.

---

### 12.8 Area Classification Tags

#### `overworld`

**Meaning:** Area is an overworld/open-world zone.

**Processing:**
- Adds `overworld_adjacent` to connected non-overworld areas (L650-660)
- In crawl mode: `openonly` items become `optional` (L782-784)

**Effect:** Classification for world structure and crawl mode filtering.

---

#### `minidungeon`

**Meaning:** Area is a mini-dungeon (cave, catacomb, etc.).

**Processing:**
- Area cost = 1 (L1525-1527)
- Excluded from major scaling bosses (L680-684)

**Effect:** Lower traversal cost, not counted as major boss for scaling.

---

#### `trivial`

**Meaning:** Transition/trivial area with no significant content.

**Processing:**
- Entrances: `IsFixed = true` unless Segmented (L977-979)
- Area cost = 0 (L1519-1520)
- `avoidstart` calculation considers trivial chains (L725)

**Effect:** Fixed connections, zero cost, not a starting point.

---

#### `start`

**Meaning:** Starting area (Chapel of Anticipation).

**Processing:** Sets `area.Mode = AreaMode.Both` (L786)

**Effect:** Available in both base game and DLC modes.

---

#### `final`

**Meaning:** Final boss area.

**Processing:** Excluded from major scaling bosses (L680)

**Effect:** Not used for tier interpolation.

---

#### `optional`

**Meaning:** Optional content.

**Processing:**
- Applied when area is excluded by DLC mode (L790-791)
- Applied in crawl mode for `openonly` areas (L782-784)

**Effect:** Area can be skipped without blocking progression.

---

#### `escape`

**Meaning:** Boss arena with escape possibility.

**Processing:** Used in `TagOpenStart()` for valid starting points.

**Effect:** Marks boss rooms that have a way out.

---

#### `minor`

**Meaning:** Minor boss area.

**Processing:**
- Area cost = 1 (L1525-1527)
- Excluded from major scaling bosses (L682)

**Effect:** Lower scaling tier, smaller area.

---

### 12.9 DLC Tags

#### `dlc`

**Meaning:** DLC content.

**Processing:**
- Sets `area.Mode = AreaMode.DLC` (L786)
- `area.IsExcluded = true` when `ExcludeMode == DLC` (L787)

**Effect:** Excluded when DLC is disabled.

---

#### `dlc1` / `dlc2`

**Meaning:** Specific DLC content (DS3/ER DLC packs).

**Processing:** `!opt["dlc1"]` or `!opt["dlc2"]` → `IsFixed = true` (L1028-1034)

**Effect:** Fixed (not randomized) when specific DLC disabled.

---

#### `dlconly`

**Meaning:** Only include when NOT in DLC-only mode.

**Processing:** `ExcludeMode == AreaMode.Base` → skip (L1319)

**Effect:** Excluded in DLC-only playthrough.

---

#### `dlcend`

**Meaning:** DLC ending connection.

**Processing:** `IsFixed = true` (L973)

**Effect:** Never randomized, preserves DLC ending.

---

#### `dlchack`

**Meaning:** DLC compatibility hack.

**Processing:** Special handling for DLC-base game transitions.

**Effect:** Technical workaround for DLC integration.

---

### 12.10 Pairing and Routing Tags

#### `return` / `returnpair`

**Meaning:** Return warp pair for evergaols and divine towers.

**Processing:** Used for `PairWith` matching in bidirectional warp creation (L1402-1424)

**Effect:** Links two warps as bidirectional pair.

**Pattern:** `return` ASide pairs with `returnpair` BSide.

---

#### `main`

**Meaning:** Main/primary connection point.

**Processing:** Used for side identification and labeling.

**Effect:** Marks the "main" side of a multi-sided entrance.

---

#### `major`

**Meaning:** Major connection/boss.

**Processing:**
- Filtering: `!opt["major"]` → fixed (DS1/DS3)
- Scaling: Included in major boss list for tier calculation

**Effect:** Higher priority, affects scaling.

---

#### `legacy`

**Meaning:** Legacy dungeon connection.

**Processing:** Often combined with `opensplit`, `uniquegate`.

**Effect:** Categorization for legacy dungeon handling.

---

#### `critical`

**Meaning:** Critical progression connection.

**Processing:** Combined with other tags for importance.

**Effect:** Cannot be removed without breaking progression.

---

### 12.11 Special Behavior Tags

#### `avoidstart`

**Meaning:** Area should not be a random starting point.

**Processing:**
- Calculated in `TagOpenStart()` (L709-765)
- Propagated to sides of entrances (L732-745)

**Effect:** Area/entrance not eligible for random start placement.

---

#### `afterstart`

**Meaning:** Connection only valid after start.

**Processing:** `ExcludeMode == AreaMode.Base && HasTag("afterstart")` → Ignore (L1193-1195)

**Effect:** Side ignored in base-only mode.

---

#### `newgate`

**Meaning:** Newly created fog gate (not vanilla).

**Processing:** Required for `crawlonly` entrances (L983-986)

**Effect:** Artificial gate added by randomizer.

---

#### `temp`

**Meaning:** Temporary/placeholder connection.

**Processing:** Skipped in construction (L1319, L1363)

**Effect:** Not included in final graph.

---

#### `hard`

**Meaning:** Requires hard/difficult skip.

**Processing:**
- Skipped unless `opt["hard"]` (L1319)
- Text defaults to "hard skip" (L323)

**Effect:** Only included with hard mode enabled.

---

#### `drop`

**Meaning:** One-way drop (fall/slide).

**Processing:** Used in world connections for area-to-area drops.

**Effect:** Unidirectional world connection.

---

#### `shortcut`

**Meaning:** Bidirectional shortcut.

**Processing (L1340-1350):**
- Creates paired edges
- Adds condition requiring source area access

**Effect:** Bidirectional passage with origin condition.

---

#### `remove`

**Meaning:** Physically remove the asset.

**Processing:** Added alongside `unused` for removal.

**Effect:** Asset deleted from game, not just disabled.

---

#### `randomonly`

**Meaning:** Only exists when randomization is active.

**Processing:** Standard inclusion, meant for documentation.

**Effect:** Connection wouldn't exist in vanilla game.

---

#### `baseonly`

**Meaning:** Only include in base game (not DLC-only).

**Processing:** `ExcludeMode == AreaMode.Base` → `unused` + `remove` (L1279-1283)

**Effect:** Removed in DLC-only playthrough.

---

#### `highwall`

**Meaning:** High Wall teleporter (DS3-style).

**Processing (L997-1006):**
```
if !opt["pvp"] && !opt["boss"]:
    AddTag("norandom")
else:
    AddTag("unused")
```

**Effect:** Fixed when no PvP/boss, unused otherwise.

---

#### `ownstart`

**Meaning:** Area can be its own starting point.

**Processing:** Affects starting position selection.

**Effect:** Eligible as custom start location.

---

#### `remembrance`

**Meaning:** Remembrance boss area.

**Processing:** Classification for boss categorization.

**Effect:** Major boss with remembrance drop.

---

#### `altlogic`

**Meaning:** Alternative logic for segmented mode.

**Processing (L1219-1222):**
```csharp
if (side.HasTag("altlogic") && opt[Feature.Segmented])
{
    return false;  // Do not ignore the alternate side
}
```

**Effect:** In segmented mode, prevents alternate sides from being ignored. Changes how `AlternateOf` sides are handled.

**Use case:** Allows alternate routing paths in segmented boss rush modes.

---

### 12.12 Condition-Related Tags

#### `dnofts`

**Meaning:** "Do not offer the shortcut" - requires reaching from other side.

**Processing (L1462-1471):**
```
if side.HasTag("dnofts"):
    side.Expr = combine(doorCond, Named(otherSide.Area))
```

**Effect:** Door requires reaching the other area first.

---

#### `noscalecond`

**Meaning:** No scaling condition required.

**Processing:** Affects enemy scaling logic.

**Effect:** Enemies don't use tier-based scaling.

---

#### `treeskip` / `instawarp`

**Meaning:** Special skip conditions.

**Processing:**
- `treeskip`: `!opt["treeskip"]` → skip (L1319)
- `instawarp`: `!opt["instawarp"]` → skip (L1319)

**Effect:** Conditional inclusion based on skip settings.

---

### 12.13 Silo Tags

Silos control affinity-based matching during randomization.

#### Silo Values

| Silo | Partner | Description |
|------|---------|-------------|
| `toopen` | `fromopen` | Open-world connections |
| `tominor` | `fromminor` | Minor dungeon/evergaol |
| `tomini` | `frommini` | Mini-dungeon entrance |
| `toroom` | `fromroom` | Small room connection |

**Processing (L831-856):**
- `Silo` field sets `side.Silo` and `side.LinkedSilo`
- `to*` silos get `from*` partner and vice versa
- Both sides get appropriate silo assignments

**Effect:** Affinity matching connects compatible entrance types.

---

### 12.14 Tag Processing Summary

#### Order of Processing

1. **Area tags**: Applied to areas during loading
2. **Entrance tags**: Applied to entrances, mark `IsFixed`
3. **Warp tags**: Applied to warps, handle coupling/backportal
4. **Side tags**: Applied during side processing, affect `IsCore`
5. **Dynamic tags**: Added during construction (`unused`, `selfwarp`, etc.)

#### Tag Inheritance

- Entrance-level tags apply to all sides
- Side-level tags are side-specific
- `hasTag()` checks both entrance AND side: `e.HasTag(tag) || side.HasTag(tag)`

#### Tag Combinations

Common combinations and their meanings:

| Combination | Meaning |
|-------------|---------|
| `unique belfries` | One-way Belfry portal |
| `unique legacy opensplit` | One-way legacy dungeon warp, can split in crawl |
| `uniquegate legacy opensplit` | Coupled sending gate between legacy dungeons |
| `backportal dungeon catacomb` | Boss return in catacomb |
| `crawlonly selfwarp` | Artificial evergaol for crawl mode |
| `fortressonly selfwarp legacy` | Fortress segment exit from legacy dungeon |
| `evergaol return` | Evergaol exit portal |
| `evergaol returnpair` | Evergaol entry portal (paired with return) |

#### Game-Specific Tags Not Documented

The following tags exist in Graph.cs but are specific to Dark Souls 1 or Dark Souls 3 and not relevant to Elden Ring:

| Tag | Game | Description |
|-----|------|-------------|
| `kiln` | DS1/DS3 | Kiln of the First Flame connections |
| `lordvessel` | DS1 | Lordvessel-gated connections |
| `world` | DS1 | World warp option |
| `boss` | DS1/DS3 | Boss fog gates (different handling in ER) |
| `pvp` | DS3 | PvP area connections |
| `small` | DS1/DS3 | Small area cost calculation |

---

### 12.15 SpeedFog Tag Filtering Summary

For SpeedFog's DAG generation with recommended options:

| Tag | Action | Reason |
|-----|--------|--------|
| `unused` | **Exclude** | No connection exists |
| `norandom` | **Include as fixed** | Traversable, not randomizable |
| `door` | **Include as fixed** | Internal passage |
| `trivial` | **Include** | Needed for world connections |
| `crawlonly` | **Exclude** | `crawl=false` |
| `nocrawl` | **Include** | `crawl=false` |
| `dlc`, `dlc1`, `dlc2` | **Exclude** | DLC disabled |
| `backportal` | **Convert to selfwarp** | `req_backportal=true` |
| `unique` (inherent) | **Include as unidirectional** | Cannot be bidirectional |
| `uniquegate` | **Convert to bidirectional** | `coupledwarp=true` |
| `uniqueminor` | **Convert to bidirectional** | `coupledminor=true` |
| `selfwarp` | **Include as bidirectional** | Two edges in same zone |
| `caveonly`, `catacombonly`, etc. | **Include** | `req_*=true` |
| `fortressonly`, `segmentonly` | **Exclude** | Non-segmented mode |

---

## 13. Runtime Attributes Reference

This section documents the runtime attributes (properties) that are computed during graph construction. Unlike tags which are parsed from fog.txt, these attributes are set programmatically based on options and tag processing.

### 13.1 Edge Attributes (Graph.Edge)

Edges represent directional connections in the graph. Each fog gate/warp creates multiple edges.

#### `Type` (EdgeType)

**Values:** `Exit`, `Entrance`, `Unknown`

**Meaning:** Direction of travel this edge represents.

**Set by:** `AddEdge()` method based on `isExit` parameter (L333-342)

**Effect:**
- `Exit`: Edge goes FROM the side's area (can leave via this connection)
- `Entrance`: Edge goes TO the side's area (can enter via this connection)

**Example:** A bidirectional fog gate creates 4 edges:
- Side A: Exit + Entrance
- Side B: Exit + Entrance

---

#### `IsFixed`

**Type:** `bool`

**Meaning:** Connection cannot be randomized; remains at vanilla destination.

**Set by:** Inherited from `Entrance.IsFixed` at edge creation (L324)

**Triggers:**
- Tags: `norandom`, `door`, `dlcend`, `trivial` (non-segmented)
- DLC filtering: `dlc1`, `dlc2` when disabled
- Game-specific: `boss`, `pvp`, `kiln`, etc.

**Effect:**
- Fixed edges are connected immediately during construction (L1497-1499)
- Not included in randomization pool
- Still traversable for routing

**Code reference:** `Graph.cs:324`
```csharp
bool isFixed = e?.IsFixed ?? true;
```

---

#### `IsWorld`

**Type:** `bool` (computed from `Side.IsWorld`)

**Meaning:** This is a world connection (area-to-area), not a physical fog gate.

**Set by:** World connections created from `Area.To` section (L1338-1339)

**Effect:**
- No physical fog gate asset
- Used for area connectivity (shortcuts, drops)
- Affects core propagation logic

---

#### `Pair`

**Type:** `Edge` (reference)

**Meaning:** The opposite-direction edge at the same connection point.

**Set by:**
- Bidirectional fog gates: automatic pairing (L1420-1423)
- Selfwarps: `edge.Pair = edge2` (L1393-1394)

**Effect:**
- When one edge is connected, its Pair is also updated (L437-446)
- Enables bidirectional traversal
- `null` for unique (one-way) warps

**Constraint:** Exit pairs with Entrance, never same type (L131-133)

---

#### `Link`

**Type:** `Edge` (reference)

**Meaning:** The edge this one connects TO after randomization.

**Set by:** `Connect()` method during graph linking (L423-424)

**Effect:**
- Exit.Link → Entrance it connects to
- Entrance.Link → Exit that connects to it
- Forms the actual traversal path

**Code reference:** `Graph.cs:423-424`
```csharp
entrance.Link = exit;
exit.Link = entrance;
```

---

#### `FixedLink`

**Type:** `Edge` (reference)

**Meaning:** The original/vanilla connection target (before randomization).

**Set by:** During edge creation for non-unique warps (L1385-1386, L1493-1496)

**Effect:**
- Preserved even after randomization
- Used for validation and debugging
- Allows restoration to vanilla state

---

#### `LinkedExpr`

**Type:** `Expr`

**Meaning:** Combined condition expression after linking two edges.

**Set by:** `Connect()` method (L425-426)

**Calculation:**
```csharp
LinkedExpr = combineExprs(entrance.Expr, exit.Expr)
// If both non-null and different: AND them together
// Otherwise: return the non-null one
```

**Effect:** Represents full requirement to traverse this path.

---

#### `From` / `To`

**Type:** `string` (area name)

**Meaning:**
- `From`: Source area (set for Exit edges)
- `To`: Destination area (set for Entrance edges)

**Set by:**
- Initially: from `Side.Area` at creation
- After linking: filled in by `Connect()` (L421-422)

**Effect:** Defines the actual connection in the graph.

---

### 13.2 Side Attributes (AnnotationData.Side)

Sides represent one end of an entrance/warp definition.

#### `IsCore`

**Type:** `bool`

**Meaning:** This side is part of the main randomization "core".

**Set by:** Complex calculation at L1154-1182

**Calculation:**
```
Default: isCore = true

If has category tag (underground, belfries, etc.):
    isCore = opt["req_" + tag] || opt["req_all"]

Crawl mode overrides:
    open → isCore = false
    neveropen → isCore = true
    rauhruins (without req_rauhruins) → isCore = false
```

**Effect:**
- Core sides are prioritized in connection algorithm
- Non-core sides may be fixed to periphery
- Affects `Area.IsCore` propagation

**Code reference:** `Graph.cs:1156-1182`

---

#### `IsPseudoCore`

**Type:** `bool`

**Meaning:** Connected to core area via world connection but not itself core.

**Set by:** `MarkCoreAreas()` method (L643)

**Effect:**
- Treated similarly to core for routing
- Merged with parent core area in logic

**Example:** `stormveil_start` is pseudo-core because it has `To: stormveil` (which is core).

---

#### `IsWorld`

**Type:** `bool`

**Meaning:** This side represents a world connection, not a fog gate.

**Set by:** Explicitly for `Area.To` connections (L1338-1339)

**Effect:**
- No physical asset manipulation
- Affects edge creation pattern
- Different handling in connector

---

#### `IsExcluded`

**Type:** `bool`

**Meaning:** This side's area is excluded by DLC mode.

**Set by:** Inherited from `Area.IsExcluded` (L1188)

**Effect:**
- Side may be ignored or have special handling
- Affects `AlternateOf` processing
- May prevent edge creation

---

#### `Expr`

**Type:** `Expr`

**Meaning:** Condition required to use this connection.

**Set by:** Parsed from `Cond` field (L1153)

**Format:** Boolean expression with area/item names
- `AND area1 area2` - requires both
- `OR area1 area2` - requires either
- `OR3 a b c d e` - requires 3 of 5

**Effect:** Edges inherit this; combined in `LinkedExpr` after linking.

---

#### `Silo` / `LinkedSilo`

**Type:** `string`

**Meaning:** Grouping for affinity-based matching.

**Set by:** From entrance `Silo` field, split to both sides (L831-855)

**Values:** `toopen`/`fromopen`, `tominor`/`fromminor`, `tomini`/`frommini`, `toroom`/`fromroom`

**Effect:** When affinity enabled, connections match within silo groups.

---

#### `SegmentIndex`

**Type:** `int` (default: -1)

**Meaning:** Index of segment this side belongs to (segmented modes only).

**Set by:** Segment processing in connector

**Effect:** Used for Boss Rush / Endless mode routing.

---

### 13.3 Entrance Attributes (AnnotationData.Entrance)

Entrances represent fog gates and warps as defined in fog.txt.

#### `IsFixed`

**Type:** `bool`

**Meaning:** This entrance should not be randomized.

**Set by:** Tag processing in `Construct()` (L973-979, L1012-1014)

**Triggers:**
- `norandom`, `door`, `dlcend` tags
- `trivial` tag (non-segmented mode)
- DLC content when DLC disabled
- Game-specific options (boss, pvp, etc.)

**Effect:**
- Edges created with `IsFixed = true`
- Connected immediately to vanilla destination
- Not part of randomization pool

---

#### `FullName`

**Type:** `string`

**Meaning:** Unique identifier for this entrance.

**Set by:** Constructed during parsing (L808-824)

**Format by game:**
- DS1: `Name` or `ID.ToString()`
- DS3: `Area_ID`
- ER: `Area_Name`

**Effect:** Used as dictionary key and for debugging.

---

### 13.4 Area Attributes (AnnotationData.Area)

Areas represent zones/locations in the game world.

#### `IsCore`

**Type:** `bool`

**Meaning:** Area is part of the main randomization graph.

**Set by:** `MarkCoreAreas()` method (L631)

**Determination:**
1. Has any edge with `Side.IsCore = true`
2. Connected to core area via world connection (propagated)

**Effect:**
- Core areas are central to randomization
- Non-core areas are peripheral (evergaols, etc.)

---

#### `IsExcluded`

**Type:** `bool`

**Meaning:** Area is excluded by DLC mode setting.

**Set by:** Mode comparison (L787-791)

**Calculation:**
```csharp
area.IsExcluded = area.Mode == ExcludeMode;
// If ExcludeMode is DLC, areas with Mode=DLC are excluded
// If ExcludeMode is Base, areas with Mode=Base are excluded
```

**Effect:**
- Excluded areas get `optional` tag
- Sides in excluded areas get `IsExcluded = true`
- May affect alternate handling

---

#### `Mode` (AreaMode)

**Type:** `enum` - `None`, `Base`, `DLC`, `Both`

**Meaning:** Which game content this area belongs to.

**Set by:** Tag-based calculation (L786)

**Calculation:**
```csharp
Mode = HasTag("dlc") ? DLC : (HasTag("start") ? Both : Base)
```

**Effect:** Compared against `ExcludeMode` to determine `IsExcluded`.

---

#### `IsBoss`

**Type:** `bool` (computed property)

**Meaning:** Area contains a boss fight.

**Calculation:** `DefeatFlag > 0 || HasTag("boss")` (L138-148)

**Effect:**
- Affects scaling calculations
- Used in segment type determination
- Influences area cost

---

### 13.5 Graph-Level Attributes

#### `ExcludeMode` (AreaMode)

**Type:** `enum`

**Meaning:** Which content category to exclude from randomization.

**Set by:** Option processing (L769-776)

**Values:**
- `None`: Include all content
- `Base`: Exclude base game (DLC-only run)
- `DLC`: Exclude DLC (base game only)

**Effect:** Areas matching this mode are excluded.

---

#### `Ignore` (HashSet<(string, string)>)

**Type:** `HashSet<(entranceFullName, areaName)>`

**Meaning:** Specific entrance-side combinations to skip.

**Populated by:** Various conditions (L1189-1204)

**Triggers:**
- `ExcludeIfRandomized` when that entrance is randomized
- `afterstart` in base-only mode
- `AlternateOf` handling
- Explicit `unused` on side

**Effect:** Sides in Ignore set don't create edges.

---

#### `AreaTiers` (Dictionary<string, int>)

**Type:** `Dictionary<areaName, tierNumber>`

**Meaning:** Scaling tier assigned to each area.

**Set by:** `GraphConnector` after connection (L1616-1701 in GraphConnector.cs)

**Range:** 1-34 (vanilla), subset for SpeedFog

**Effect:** Determines enemy scaling level for each area.

---

### 13.6 Attribute Relationships

```
┌─────────────────────────────────────────────────────────────────┐
│                        PARSING PHASE                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  fog.txt                                                        │
│     │                                                           │
│     ├─→ Area ──→ Mode, IsBoss, IsExcluded                       │
│     │              └─→ propagates to Side.IsExcluded            │
│     │                                                           │
│     ├─→ Entrance ──→ IsFixed (from tags)                        │
│     │      │           └─→ propagates to Edge.IsFixed           │
│     │      │                                                    │
│     │      └─→ Side ──→ IsCore, Expr, Silo                      │
│     │                    └─→ propagates to Edge properties      │
│     │                                                           │
│     └─→ Warp (same as Entrance)                                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     CONSTRUCTION PHASE                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  For each Entrance/Warp:                                        │
│     │                                                           │
│     ├─→ Create Edges (Exit + Entrance per side)                 │
│     │     └─→ Edge.Type, Edge.From/To (partial)                 │
│     │                                                           │
│     ├─→ Set Edge.Pair (for bidirectional)                       │
│     │                                                           │
│     ├─→ Set Edge.FixedLink (original connection)                │
│     │                                                           │
│     └─→ If IsFixed: Connect immediately                         │
│           └─→ Edge.Link, Edge.From/To (complete)                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     CONNECTION PHASE                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  MarkCoreAreas():                                               │
│     └─→ Area.IsCore, Side.IsPseudoCore                          │
│                                                                 │
│  Connect (randomization):                                       │
│     └─→ Edge.Link (randomized connections)                      │
│         └─→ Edge.LinkedExpr (combined conditions)               │
│                                                                 │
│  CalculateTiers():                                              │
│     └─→ Graph.AreaTiers                                         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

### 13.7 SpeedFog Relevant Attributes

For SpeedFog's DAG generation, the most important attributes are:

| Attribute | Class | Importance |
|-----------|-------|------------|
| `IsFixed` | Edge/Entrance | Determines if connection can be rerouted |
| `IsCore` | Side/Area | Identifies main graph components |
| `Pair` | Edge | Enables bidirectional connection counting |
| `Type` | Edge | Distinguishes exits from entrances |
| `From`/`To` | Edge | Defines connection endpoints |
| `IsWorld` | Edge/Side | Identifies non-fog-gate connections |
| `Expr` | Side | Conditions for traversal |

**Key insight for SpeedFog:**
- Count edges where `Pair != null` for bidirectional connections
- `IsFixed` edges are traversable but not reassignable
- `IsCore` identifies the "interesting" parts of the graph
- `IsWorld` edges don't have physical fog gates
