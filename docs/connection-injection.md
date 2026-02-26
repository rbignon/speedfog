# Connection Injection

**Date:** 2026-02-26
**Status:** Active

How SpeedFog's graph connections are injected into FogMod's Graph to produce a custom fog gate layout.

## Overview

FogMod builds a `Graph` object from `fog.txt` with all Areas, Nodes, and Edges but leaves them **unconnected** (option `unconnected=true`). SpeedFog's Python DAG generator decides which exits connect to which entrances, and `ConnectionInjector` wires them into FogMod's Graph before the writer runs.

The pipeline in `Program.cs`:

```
graph.json (Python)
    |
    v
GraphLoader.Load()  ->  List<Connection>
    |
    v
ConnectionInjector.InjectAndExtract(graph, connections, finishEvent, finalNodeFlag)
    |   - Wires exit edges to entrance edges
    |   - Extracts boss DefeatFlag for zone tracking
    |
    v
ConnectionInjector.ApplyAreaTiers(graph, areaTiers)
    |   - Sets per-area scaling tiers
    |
    v
GameDataWriterE.Write()  ->  mod files (EMEVD, params, MSBs)
```

## FogMod Graph Model

FogMod's `Graph` stores Areas (zones) with directional edge lists:

| Property | Type | Description |
|----------|------|-------------|
| `Area.To` | `List<Edge>` | Exit edges leaving the area |
| `Area.From` | `List<Edge>` | Entrance edges entering the area |
| `Edge.Pair` | `Edge?` | The opposite direction of a bidirectional fog gate |
| `Edge.Link` | `Edge?` | Currently connected partner edge |
| `Edge.Name` | `string` | FullName of the fog gate (e.g., `m10_01_00_00_AEG099_001_9000`) |

Bidirectional fogs have paired edges: a To edge on one side and a From edge on the other, linked via `Edge.Pair`. One-way warps (e.g., coffin teleporters) have only a From edge on the destination side, with no Pair.

## Connection Model

Each `Connection` in `graph.json` specifies one fog gate link:

| Field | Description |
|-------|-------------|
| `exit_area` | Source area name |
| `exit_gate` | FullName of the exit fog in source area's `To` list |
| `entrance_area` | Destination area name |
| `entrance_gate` | FullName of the entrance fog on destination side |
| `flag_id` | Event flag set when the player traverses this fog gate |
| `ignore_pair` | Preserve the entrance's bidirectional Pair edge on connect |
| `exit_entity_id` | MSB entity ID of exit fog asset (for zone tracking disambiguation) |
| `has_common_event` | Exit is a WarpBonfire gate with a vanilla warp event in common.emevd |

## Connection Strategies

`ConnectionInjector` resolves the entrance edge using two strategies, tried in order:

### Strategy 1: Bidirectional Fogs (To + Pair)

For standard bidirectional fog gates, `entrance_gate` refers to an **exit edge** on the destination area. The actual entrance edge is its `Pair`:

```
Source Area                         Destination Area
-----------                         ----------------
To: [exitEdge] ---connect---> From: [entranceEdge]  <- destExitEdge.Pair
                                To: [destExitEdge]   <- matched by entrance_gate name
```

1. Find `destExitEdge` in `entranceNode.To` where `Name == entrance_gate`
2. Use `destExitEdge.Pair` as the entrance edge

### Strategy 2: One-Way Warps (From direct)

For one-way warps that have no exit edge on the destination, `entrance_gate` matches directly in the `From` list:

```
Source Area                         Destination Area
-----------                         ----------------
To: [exitEdge] ---connect---> From: [entranceEdge]  <- matched by entrance_gate name
                                To: (no matching edge)
```

1. Strategy 1 finds nothing in `To`
2. Find `entranceEdge` in `entranceNode.From` where `Name == entrance_gate`

## Shared Entrance Handling

When N branches merge into a single entrance fog (same `entrance_area` + `entrance_gate`), FogMod requires each connection to have its own entrance edge instance. The injector uses `graph.DuplicateEntrance()` to create independent copies.

**Detection**: Connections are grouped by the key `{entrance_area}|{entrance_gate}`. Groups with count > 1 are shared entrances.

**Flow**:
1. First connection to a shared entrance uses Strategy 1 or 2 normally (primary connection)
2. The resolved entrance edge is stored in `connectedEntrances`
3. Subsequent connections to the same entrance call `graph.DuplicateEntrance(originalEntrance)` to get a fresh edge copy
4. Each duplicate is an independent edge that can be connected without conflicting with others

## ignore_pair Flag

For **entry-as-exit boss arenas** where the same fog gate serves as both the entrance into the boss room and the forward exit to the next area.

Without `ignore_pair`, `graph.Connect()` removes the bidirectional Pair edge, which would break the forward exit direction. Setting `ignore_pair=true` tells FogMod to preserve the Pair edge during connection.

Passed directly to `graph.Connect(exitEdge, entranceEdge, ignorePair: conn.IgnorePair)`.

## Boss Defeat Flag Extraction

The injector extracts the final boss's `DefeatFlag` from FogMod's Graph for downstream use by `RunCompleteInjector` and `ZoneTrackingInjector`.

**Matching**: When a connection's `flag_id` equals `finalNodeFlag` (from `graph.json`), the injector looks up the destination area's `DefeatFlag` in `graph.Areas`.

**Fallback chain** (in `Program.cs`):
1. `graph.json` field `finish_boss_defeat_flag` (from fog.txt via clusters.json) -- preferred
2. FogMod Graph's `Area.DefeatFlag` extracted during injection
3. If both exist and differ, `finish_boss_defeat_flag` takes priority (handles cases like `leyndell_erdtree` where the boss is in a linked zone not directly in the cluster)

## Area Tier Application

`ApplyAreaTiers()` writes the `area_tiers` dictionary from `graph.json` directly into `graph.AreaTiers`. FogMod's writer uses these tiers to apply `EldenScaling` SpEffects to enemies in each zone.

```csharp
graph.AreaTiers[area] = tier;  // e.g., "stormveil" -> 5
```

## Return Value

`InjectAndExtract()` returns an `InjectionResult`:

| Property | Type | Description |
|----------|------|-------------|
| `BossDefeatFlag` | `int` | FogMod's DefeatFlag for the final boss area (0 if not found) |
| `FinishEvent` | `int` | Pass-through of the `finish_event` flag from graph.json |

These are consumed by `ZoneTrackingInjector` and `RunCompleteInjector` to detect boss death and trigger the "RUN COMPLETE" banner.

## Pre-Connection Cleanup

Before connecting edges, the injector disconnects any pre-existing links:

- **Exit edge**: Always disconnected if already linked (each connection has its own exit)
- **Entrance edge** (primary only): Destination exit edge and entrance edge are disconnected if pre-linked
- **Duplicated entrances**: Skip disconnect since `DuplicateEntrance()` returns fresh unlinked edges

This is necessary because `Graph.Construct()` in crawl mode pre-connects edges tagged as "trivial". `Program.cs` also performs a bulk disconnect of trivial edges before injection (step 4b).
