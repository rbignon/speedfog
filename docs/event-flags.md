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
| 1050290100-1050290299 | 0xxx offsets 100-299 | Saved (persistent) | Race item use and removal flags | RaceItemsPlugin |
| 1050290300-1050290499 | 0xxx offsets 300-499 | Saved (persistent) | Race item talisman removal flags | RaceItemsPlugin |
| 1050290500-1050290999 | 0xxx offsets 500-999 | Saved (persistent) | Relay lock, Chapel warp and connection warp flags | RelayPlugin (raised by the racing mod) |
| 1050294000-1050294999 | 4xxx offsets 0-999 | Saved (persistent) | Zone tracking, finish event, death markers | ZoneTrackingInjector, RunCompleteInjector, DeathMarkerInjector |

Both ranges use saved flags (persist across area reloads):
- Category 1050290 (offset 0-999): persistent mod state (items given, etc.), the race item goods' use/removal flags, the talisman removal flags and the relay flags
- Category 1050294 (offset 0-999): zone tracking, cleared by racing mod after capture
  (so generator EMEVD must never wait on these: features needing "player entered
  cluster X" set their own 1050290xxx mirror flags, see the Halloween jumpscares)

### Saved Flags (1050290xxx)

| Flag | Offset | Purpose | Set by |
|------|--------|---------|--------|
| 1050290000 | 0 | `items_spawned_flag`: one-shot guard for starting item/resource delivery | StartingItemInjector, StartingResourcesInjector |
| 1050290001 | 1 | `banner_shown_flag`: one-shot guard for run complete banner | RunCompleteInjector |
| 1050290010-1050290019 | 10-19 | Halloween jumpscare layer played (one per chosen layer, ascending) | HalloweenJumpscareInjector |
| 1050290020-1050290029 | 20-29 | Halloween jumpscare "left layer" mirror flags, set before warps into the next layer | ZoneTrackingInjector (via RegionToFlags) |
| 1050290030-1050290099 | 30-99 | Halloween jumpscare "entered cluster" mirror flags, set before warps into an armed cluster | ZoneTrackingInjector (via RegionToFlags) |
| 1050290100-1050290199 | 100-199 | Race item use flags (`1050290100 + n`, n = goods id - 1450800; only the Race Item goods, 1050290101, has one): set by the plugin's use event when the player uses the goods, read and cleared by the racing mod | RaceItemsPlugin |
| 1050290200-1050290299 | 200-299 | Race item removal flags (`1050290200 + n`, one per goods: 1050290200 the Shield, 1050290201 the Race Item): set by the racing mod to take the goods back | RaceItemsPlugin (waits on them) |
| 1050290300-1050290499 | 300-499 | Talisman removal flags (`1050290300 + n`, n = the talisman's index in `data/plugins/race_items_talismans.toml`): set by the racing mod to take that talisman from the player (Talisman Thief) | RaceItemsPlugin (waits on them) |

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

Event ID ranges and auxiliary flags are declared centrally in
`writer/FogModWrapper.Core/SpeedFogIds.cs` (injectors reference it;
`SpeedFogIdsTests` asserts the ranges never overlap). Current allocation:

| Event ID base | Injector | EMEVD File | Purpose |
|---------------|----------|------------|---------|
| 755860000 | StartingItemInjector | common.emevd | Give starting goods + care package |
| 755860100 | RoundtableUnlockInjector | common.emevd | Set start flag to unlock Roundtable |
| 755861000 | StartingResourcesInjector | common.emevd | Give runes, golden seeds, sacred tears |
| 755862000 | ZoneTrackingInjector | common.emevd | Monitor final boss defeat flag |
| 755862100 | DeathMarkerInjector | map emevds | Bloodstain SFX events (sequential, capacity 900) |
| 755863000 | RunCompleteInjector | common.emevd | Display victory banner + jingle |
| 755864000 | ChapelGraceInjector | m10_01_00_00.emevd | One-shot warp to chapel grace (initial spawn) |
| 755865000 | WeatherInjector | common.emevd | Force fixed weather + pin clock hour (opt-in) |
| 755865100 | GateDecorInjector | map emevds | Halloween decoration SFX (sequential, capacity 400) |
| 755865500 | HalloweenDarknessInjector | common.emevd | Re-apply the evergaol gparam override (opt-in) |
| 755865600 | HalloweenJumpscareInjector | map emevds | Halloween jumpscare actors (one event per actor, capacity 100) |
| 755865700 | RaceItemsPlugin | common.emevd | Race items (opt-in): use events at `base + n` (usable goods only), removal events at `base + 50 + n`, n = goods id - 1450800 (capacity 100) |
| 755865800 | RaceItemsPlugin | common.emevd | Race items (opt-in): talisman removal events at `base + n`, n = the talisman's index in `data/plugins/race_items_talismans.toml` (capacity 200) |

All events are registered in Event 0 via `InitializeEvent` (bank 2000, id 0),
built by `EmevdHelper.InitializeEvent`.

## Reserved Param ID Ranges (non-flag)

Allocations in regulation.bin params (not event flags). Listed here so future contributors do not collide.

| Range | Param(s) | Owner | Reference | Notes |
|-------|----------|-------|-----------|-------|
| 1450700-1450799 | PhantomParam, SpEffectVfxParam, SpEffectParam | Phantom skins catalog | `docs/phantom-skins.md` | |
| 1450800-1450899 | EquipParamGoods, SpEffectParam | RaceItemsPlugin | `SpeedFogIds.RaceItemParamIdMin/Max`, `RaceItemGoodsIdMax`, `RaceItemEffectIdMin/Max`, `data/plugins/race_items.toml` | Race items: goods id = marker SpEffect id, in 1450800-1450819 (1450800 Shield, 1450801 Race Item); item effect SpEffects in 1450820-1450849; vanilla has neither (checked 2026-11-12) |
| 1450850-1450899 | NpcParam, NpcThinkParam | RaceItemsPlugin | `SpeedFogIds.RaceItemNpcParamIdMin/Max`, `RaceItemHunterTierCount`, `data/plugins/race_items.toml` `[hunter]`, `speedfog/race_items.py` `NPC_PARAM_RANGE` | The Hunter: one NpcParam per tier at 1450850 + (tier - 1), tiers 1 to 34 (1450850-1450883), and NpcThinkParam 1450850; the rest of the range is spare. Separate tables from the goods and SpEffect rows above, which share the numbers |
| 755890000 | NpcParam | UntouchableBossInjector | `SpeedFogIds.UntouchableBossNpcRow` | Boss clone; outside 5280-band to avoid nerflantern patching |
| 755890000 | SpEffectParam | UntouchableBossInjector | `SpeedFogIds.UntouchableBossSpEffectRow` | Partial damage-cut state for boss |
| 755890000 | NpcThinkParam | AmbientSpawnInjector | `SpeedFogIds.HostileGreeterThinkRow` | Greeter think row (species basis: untouchable 52800000 or wormface 45700100, perception capped at `GREETER_AGGRO_DIST`) |
| 755890001 | NpcParam | AmbientSpawnInjector | `SpeedFogIds.DecorativeAmbusherNpcRow` | Decorative ambusher clone (species basis: Sage's Cave skeleton 35000030 or Ainsel basilisk 41500060, token HP, no runes) |
| 755890001 | SpEffectParam | AmbientSpawnInjector | `SpeedFogIds.DecorativeAmbusherSpEffectRow` | Near-zero attack rates for ambushers (tier-1 scaling row basis) |
| 755890001 | NpcThinkParam | UntouchableBossInjector | `SpeedFogIds.UntouchableBossThinkRow` | Boss think row (52800000 basis, `battleGoalID` 755890) |
| 755890002 | NpcParam | AmbientSpawnInjector | `SpeedFogIds.HostileGreeterNpcRow` | Hostile greeter clone (species basis: Aging Untouchable 52800086 or Altus wormface 45700030, flat `GREETER_TIER` scaling, no runes, no drop) |
| 755890002 | SpEffectParam | UntouchableBossInjector | `SpeedFogIds.UntouchableBrokenSpEffectRow` | Boss's broken state after the first parry (x2 damage taken) |
| 755890003 | NpcParam | HalloweenJumpscareInjector | `SpeedFogIds.HalloweenJumpscareNpcRow` | Jumpscare actor clone (greeter recipe: flat `GREETER_TIER` scaling, no runes, no drop) |
| 755890003 | SpEffectParam | UntouchableBossInjector | `SpeedFogIds.UntouchableNearWarpMarkerSpEffect` | Near-warp marker, copy of vanilla 20011452, written in every seed (the static anibnd applies it on every untouchable's burst 3001) |

## Reserved Icon IDs

Menu icon ids (a third namespace, not entity ids nor param rows). Vanilla icon ids top out around 8490. Each id names two textures, `MENU_ItemIcon_<id>` (the small icon) and `MENU_Knowledge_<id>` (the inventory detail panel's large icon).

| Range | Owner | Reference | Notes |
|-------|-------|-----------|-------|
| 60383-60384 | Halloween plugin | `SpeedFogIds.HalloweenGoldenSeedIcon`, `HalloweenSacredTearIcon` | Golden Seed pumpkin, corrupted Sacred Tear |
| 60385-60414 | RaceItemsPlugin | `SpeedFogIds.RaceItemIconIdMin/Max`, `[icons]` in `data/plugins/race_items.toml` | One icon per item kind (60385-60395) and the Race Item goods' item box (60396); 60397-60403 the Star, Vanish, Unseen Foes, Slow-mo, (60401 free, the Mini's until it went), Giant and Camera, 60404-60406 the Talisman Thief, Junk Weapon and Hunter, 60407-60414 spare |

## Reserved FMG Entry IDs

Text entries added to the game's message files (another namespace: FMG entry
ids, per FMG file).

| Range | FMG | Owner | Reference | Notes |
|-------|-----|-------|-----------|-------|
| 1450800-1450899 | GoodsName, GoodsInfo, GoodsCaption (engus + frafr) | RaceItemsPlugin | `data/plugins/race_items.toml` | Entry id = goods id (the Shield and the Race Item; items have no texts of their own) |
| 755890000-755890099 | NpcName (item_dlc02.msgbnd.dcx, engus + frafr) | BossNameInjector | `SpeedFogIds.BossNameFmgIds` | Healthbar names of relocated enemies with no vanilla NpcName entry of that text, one id per distinct name (`docs/boss-healthbar-names.md`). Vanilla NpcName ids sit in the 9xxxxxxxx band; the Item Randomizer's `editnames` allocator (off) starts at 907770000 |

## Risks & Constraints

- **1000 flag budget**: Offsets 0-999 in category 1050294 give 1000 zone tracking flags. Sufficient for large DAGs (60+ layers).
- **Category allocation**: Categories 1050290 and 1050294 are activated by our injected EMEVD events. No prior FogRando allocation needed, but if EMEVD injection fails these flags become silent no-ops.
- **FogRando legacy flags**: Flags 1040292051 and 1040299000-002 depend on FogRando's category allocation. Check on FogRando updates if these stop working.

## References

- Flag allocation in Python: `speedfog/graph_export.py` (`EVENT_FLAG_BASE` from `speedfog/constants.py`)
- Zone tracking injection: `writer/FogModWrapper/Racing/ZoneTrackingInjector.cs`
- FogRando flag allocation: `reference/fogrando-src/GameDataWriterE.cs` L124-135
- VirtualMemoryFlag reader (racing mod): `speedfog-racing/mod/src/eldenring/event_flags.rs`
