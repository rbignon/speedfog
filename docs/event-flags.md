# Event Flags & EMEVD Reference

**Date:** 2026-02-10
**Status:** Active

Quick reference for SpeedFog's event flag allocation and EMEVD event IDs.

## VirtualMemoryFlag: How Elden Ring Stores Flags

Elden Ring stores event flags in a **sparse tree of category pages**, not a flat address space:

```
flag_id  = 1050294400
category = flag_id / 1000 = 1050294
offset   = flag_id % 1000 = 400
```

Each category page stores 1000 flags as a bitfield (125 bytes). **Only pre-allocated categories exist at runtime.** Writing to a flag whose category doesn't exist is a **silent no-op** -- no crash, no error, just nothing happens.

Categories are allocated when EMEVD instructions reference them. SpeedFog's use of category 1040299 confirmed this: flags 1040299000-002 work in practice because FogRando's EMEVD references that category. Similarly, SpeedFog's dedicated categories (1050290 and 1050294) become live as soon as our injected EMEVD events reference them.

### Flag behavior by last four digits

The **fourth-from-last digit** of a flag ID determines its persistence behavior:

| Last four digits | Behavior |
|-----------------|----------|
| 0xxx | Saved flag |
| 1xxx | Invalid (do not use as flag, only as event ID) |
| 2xxx | Temporary flag |
| 3xxx | Invalid (do not use as flag, only as event ID) |
| 4xxx | Saved flag |
| 5xxx | Temporary flag |
| 6xxx | Invalid (do not use as flag, only as event ID) |
| 7xxx | Saved flag |
| 8xxx | Saved flag |
| 9xxx | Saved flag |

**Saved flags** persist across area reloads (save+quit, death, fast travel).
**Temporary flags** reset on area reload.

SpeedFog uses offset 0xxx (saved) for persistent mod state and offset 4xxx (saved) for zone tracking. Zone tracking flags were originally in the 2xxx (temporary) range but moved to 4xxx to survive area reloads, preventing a race condition during fog gate warps.

Source: [Elden Ring Event Flag Sheet](https://docs.google.com/spreadsheets/d/17sE1a1h87BhpiUwKUyJ9ZjKTeehXA4OuLwmQvTfwo_M/edit) (maintained by thefifthmatt).

## SpeedFog Flag Allocation

SpeedFog uses a **dedicated base `1050290000`**, in map coordinates m60_50_29_00 (white/unclaimed in the community flag sheet). This avoids any dependency on FogRando's internal allocation.

### Layout

| Range | Offsets | Type | Purpose | Set by |
|-------|---------|------|---------|--------|
| 1050290000-1050290099 | 0xxx offsets 0-99 | Saved (persistent) | Mod state flags | Various injectors |
| 1050294000-1050294999 | 4xxx offsets 0-999 | Saved (persistent) | Zone tracking, finish event, death markers | ZoneTrackingInjector, RunCompleteInjector, DeathMarkerInjector |

Both ranges use saved flags (persist across area reloads):
- Category 1050290 (offset 0-099): persistent mod state (items given, etc.)
- Category 1050294 (offset 0-999): zone tracking, cleared by racing mod after capture

### Saved Flags (1050290xxx)

| Flag | Offset | Purpose | Set by |
|------|--------|---------|--------|
| 1050290000 | 0 | `items_spawned_flag`: one-shot guard for starting item/resource delivery | StartingItemInjector, StartingResourcesInjector |
| 1050290001 | 1 | `banner_shown_flag`: one-shot guard for run complete banner | RunCompleteInjector |

### Zone Tracking Flags (1050294xxx)

| Range | Purpose | Set by |
|-------|---------|--------|
| 1050294000-1050294998 | Zone tracking: flag set when player traverses each fog gate | ZoneTrackingInjector |
| 1050294999 | Finish event: flag set when final boss is defeated | RunCompleteInjector |

Actual flag assignments within 1050294000-1050294999 are allocated sequentially per run and stored in graph.json. The 1000-flag budget is sufficient for large DAGs (60+ layers, hundreds of connections).

### FogRando-Owned Flags (legacy, unchanged)

These flags are defined by FogRando and remain at their original addresses. SpeedFog reads or sets them but does not own their categories.

| Flag | Category | Purpose | Set by |
|------|----------|---------|--------|
| 1040292051 | 1040292 | Roundtable unlock / start trigger | RoundtableUnlockInjector |
| 1040299000 | 1040299 | Starting resources already given | StartingResourcesInjector |
| 1040299001 | 1040299 | Starting items already given | StartingItemInjector |
| 1040299002 | 1040299 | Chapel warp already performed | ChapelGraceInjector |

Flag 1040292051 is defined by FogRando (used in `common_roundtable` and `common_fingerstart` templates). SpeedFog's RoundtableUnlockInjector just sets it early to bypass the finger pickup.

Flags 1040299000-002 are in category 1040299, which FogRando allocates via its own EMEVD events.

### graph.json Fields

| Field | Description |
|-------|-------------|
| `connections[].flag_id` | Flag set when player traverses this fog gate |
| `event_map` | `{flag_id: cluster_id}` mapping for zone tracking |
| `final_node_flag` | Flag for entering the final boss zone |
| `finish_event` | Flag set when final boss is defeated |
| `items_spawned_flag` | Saved flag (1050290000) used as one-shot guard for item delivery |

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
| 755865000+ | BossTriggerInjector | common.emevd | Force boss activation at fog gate warp regions |

All events are registered in Event 0 via `InitializeEvent` (bank 2000, id 0).

## Risks & Constraints

- **1000 flag budget**: Offsets 0-999 in category 1050294 give 1000 zone tracking flags. Sufficient for large DAGs (60+ layers).
- **Category allocation**: Categories 1050290 and 1050294 are activated by our injected EMEVD events. No prior FogRando allocation needed, but if EMEVD injection fails these flags become silent no-ops.
- **FogRando legacy flags**: Flags 1040292051 and 1040299000-002 depend on FogRando's category allocation. Check on FogRando updates if these stop working.

## References

- Flag allocation in Python: `speedfog/output.py` (`EVENT_FLAG_BASE`)
- Zone tracking injection: `writer/FogModWrapper/ZoneTrackingInjector.cs`
- FogRando flag allocation: `reference/fogrando-src/GameDataWriterE.cs` L124-135
- VirtualMemoryFlag reader (racing mod): `speedfog-racing/mod/src/eldenring/event_flags.rs`
