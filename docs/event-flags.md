# Event Flags & EMEVD Reference

Quick reference for SpeedFog's event flag allocation and EMEVD event IDs.

## VirtualMemoryFlag: How Elden Ring Stores Flags

Elden Ring stores event flags in a **sparse tree of category pages**, not a flat address space:

```
flag_id  = 1040292800
category = flag_id / 1000 = 1040292
offset   = flag_id % 1000 = 800
```

Each category page stores 1000 flags as a bitfield (125 bytes). **Only pre-allocated categories exist at runtime.** Writing to a flag whose category doesn't exist is a **silent no-op** — no crash, no error, just nothing happens.

This means you cannot pick arbitrary flag ranges. You must use a category that the game (or a mod) has already allocated.

## SpeedFog Flag Allocation

SpeedFog piggybacks on **category 1040292**, which FogRando pre-allocates for its own use.

| Range | Offsets | Purpose | Set by |
|-------|---------|---------|--------|
| 1040292100-299 | 100-299 | FogRando internal flags | FogMod.dll |
| 1040292800-999 | 800-999 | Zone tracking (fog gate traversal) | ZoneTrackingInjector |

The 500-offset gap (300-799) avoids collision with FogRando's allocation.

### Other Flags

| Flag | Category | Purpose | Set by |
|------|----------|---------|--------|
| 1040292051 | 1040292 | Roundtable unlock / start trigger | RoundtableUnlockInjector |
| 1040299000 | 1040299 | Starting resources already given | StartingResourcesInjector |
| 1040299001 | 1040299 | Starting items already given | StartingItemInjector |
| 1040299002 | 1040299 | Chapel warp already performed | ChapelGraceInjector |

Flag 1040292051 is defined by FogRando (used in `common_roundtable` and `common_fingerstart` templates). SpeedFog's RoundtableUnlockInjector just sets it early to bypass the finger pickup.

Flags 1040299000-001 are in category 1040299, which is also pre-allocated by FogRando.

### graph.json Fields

| Field | Description |
|-------|-------------|
| `connections[].flag_id` | Flag set when player traverses this fog gate |
| `event_map` | `{flag_id: cluster_id}` mapping for zone tracking |
| `final_node_flag` | Flag for entering the final boss zone |
| `finish_event` | Flag set when final boss is defeated |

## EMEVD Event IDs

SpeedFog injects custom events into EMEVD files:

| Event ID | Injector | EMEVD File | Purpose |
|----------|----------|------------|---------|
| 755860000 | StartingItemInjector | common.emevd | Give starting goods + care package |
| 755860100 | RoundtableUnlockInjector | common.emevd | Set start flag to unlock Roundtable |
| 755861000 | StartingResourcesInjector | common.emevd | Give runes, golden seeds, sacred tears |
| 755862000 | ZoneTrackingInjector | common.emevd | Monitor final boss defeat flag |
| 755863000 | RunCompleteInjector | common.emevd | Display victory banner + jingle |
| 755864000 | ChapelGraceInjector | m10_01_00_00.emevd | One-shot warp to chapel grace (initial spawn) |

All events are registered in Event 0 via `InitializeEvent` (bank 2000, id 0).

## Risks & Constraints

- **FogRando dependency**: Category 1040292 only exists because FogRando allocates it. If FogRando changes its allocation layout, SpeedFog flags could collide or stop working. Check on FogRando updates.
- **200 flag limit**: Offsets 800-999 give 200 zone tracking flags. More than enough for any DAG (typical runs use 10-20).
- **No arbitrary ranges**: Previous attempt with range 9000000 failed silently because category 9000 doesn't exist. Never use untested flag ranges.

## References

- Flag allocation in Python: `speedfog/output.py` (`EVENT_FLAG_BASE`)
- Zone tracking injection: `writer/FogModWrapper/ZoneTrackingInjector.cs`
- FogRando flag allocation: `reference/fogrando-src/GameDataWriterE.cs` L124-135
- VirtualMemoryFlag reader (racing mod): `speedfog-racing/mod/src/eldenring/event_flags.rs`
