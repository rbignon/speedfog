# FogMod EMEVD Compilation Model

**Date:** 2026-02-26
**Status:** Active

How FogMod compiles fog gate event templates into per-instance EMEVD events at build time, and why SpeedFog post-processors must scan compiled events rather than modifying templates.

This is the foundational mental model for `ZoneTrackingInjector`, `ErdtreeWarpPatcher`, and `SealingTreeWarpPatcher`.

## Template Compilation (Not Runtime Dispatch)

FogMod's `EventEditor.Process()` compiles the `fogwarp` template (ID 9005777) from `fogevents.txt` into **per-instance events** with unique IDs (typically 1040290xxx). The template is never called at runtime. Each compiled event contains literal values baked in -- destination map bytes, region entity IDs, and alt-warp parameters are all resolved at build time.

```
fogevents.txt                    EventEditor.Process()               Output EMEVDs
─────────────                    ─────────────────────               ─────────────
fogwarp (9005777)    ──build──►  Per-instance events        ──►     m10_01_00_00.emevd.dcx
  X0_4  = entity                   Event 1040290001                 m31_05_00_00.emevd.dcx
  X4_4  = region                     WarpPlayer(31,5,0,0,...)       common.emevd.dcx
  X8_4  = map bytes                Event 1040290002                 ...
  X24_4 = alt flag                   WarpPlayer(60,13,13,...)
  ...                              ...
                                 (literal values, never dispatched)
```

```
Build time (EventEditor.Process):
  fogwarp template 9005777  +  per-entrance parameters
    --> Event 1040290001 (literal WarpPlayer to m31_06)
    --> Event 1040290002 (literal WarpPlayer to m10_01)
    --> ...

Runtime:
  Each map EMEVD initializes its local events via Event 0.
  Template 9005777 in common_func.emevd.dcx is NEVER invoked.
```

This means there is no single template to patch. Any post-processing must scan all EMEVD files for compiled warp instructions.

## Warp Instruction Families

FogMod uses two instruction families for zone transitions. Both carry literal destination data after compilation.

### WarpPlayer (bank 2003, id 14)

```
ArgData layout:
  [0]  area   (byte)     e.g. 31
  [1]  block  (byte)     e.g. 06
  [2]  sub    (byte)     e.g. 00
  [3]  sub2   (byte)     e.g. 00
  [4-7] region (int32)   warp target entity ID
  [8-11] unk   (int32)
```

Used by most fogwarp events and WarpBonfire portal events.

### PlayCutsceneToPlayerAndWarp (bank 2002, id 11/12)

```
ArgData layout:
  [0-3]  cutsceneId (int32)
  [4-7]  playback   (int32)
  [8-11] region     (int32)   warp target entity ID
  [12-15] mapPacked (int32)   area*1000000 + block*10000 + sub*100 + sub2
```

ID 12 is `PlayCutsceneToPlayerAndWarpWithWeatherAndTime` (same layout for the first 16 bytes). Used by cutscene-based transitions (e.g., Erdtree burning at the Forge of the Giants).

### Packed Map Format

For `PlayCutsceneToPlayerAndWarp`, the destination map is a single int32:

```
m13_00_00_00  -->  13000000
m31_06_00_00  -->  31060000
m11_05_00_00  -->  11050000

Decode:
  area  = mapInt / 1000000
  block = (mapInt % 1000000) / 10000
  sub   = (mapInt % 10000) / 100
  sub2  = mapInt % 100
```

### Other Warp Instructions (Not Scannable)

`WarpCharacterAndCopyFloorWithFadeout` (bank 2003, id 74) has a region arg but **no map arg**. The destination map cannot be derived from this instruction alone. SpeedFog's post-processors do not scan for it.

## Event Placement

Compiled events are placed into map-specific EMEVD files, but **not necessarily the exit gate's map**. FogMod's `getEventMap()` may place an event in:

- A parent map (overworld parent for open world tiles)
- A deduplicated map (when multiple tiles share the same EMEVD)
- `common.emevd` (for WarpBonfire vanilla events like Erdtree burning Event 901)

This is why `ZoneTrackingInjector` cannot assume the EMEVD filename corresponds to the exit gate's map prefix. It must build lookups from multiple map sources and fall back through matching strategies.

## AlternateSide Branches

The fogwarp template has built-in alt-warp support. The template parameters at build time:

| Template Offset | Size | Purpose |
|-----------------|------|---------|
| `X24_4` | 4 bytes | AlternateFlag (game progress flag to check) |
| `X28_4` | 4 bytes | Alt region (warp target when flag is ON) |
| `X32_4` | 4 bytes | Alt map bytes (destination map when flag is ON) |

FogMod compiles this into a branch within the per-instance event:

```
Compiled event pseudocode:
  IfActionButtonInArea(fog_gate_entity)
  if AlternateFlag is ON:
      WarpPlayer(altMap, altRegion)
  else:
      WarpPlayer(primaryMap, primaryRegion)
```

Both `WarpPlayer` instructions exist as literal compiled instructions in the event. The flag check uses an `IfEventFlag` condition group.

### Known AlternateFlag Pairs

| Flag | Primary | Alternate | Context | Set By |
|------|---------|-----------|---------|--------|
| 300 | m11_00 (Leyndell) | m11_05 (Ashen Leyndell) | Erdtree | Maliketh death (Event 900) |
| 330 | m61_44_45_00 (Romina) | m61_44_45_10 (post-burning) | DLC Sealing Tree | Dancing Lion death (Event 915) |

Flag 300 controls engine map tile loading (which physical map is present at Leyndell). Flag 330 only controls the fogwarp destination branch.

## Parameterized Entities in Manual Events

Not all fog gates use the fogwarp template. FogMod creates hand-crafted "manual" events for special gate types (lie-down warps like Placidusax, coffin warps, etc.).

These manual events use `InitializeEvent` (bank 2000, id 0) to pass actual entity IDs as parameters:

```
Event 0 (initializer):
  InitializeEvent(slot=0, eventId=1040290055, entityId=34122840, ...)

Event 1040290055:
  IfActionButtonInArea(condGroup, actionParam, entity_id=0)  // 0 = placeholder
  WarpPlayer(...)
  Parameters: [SourceStartByte=0, InstructionIndex=0, TargetStartByte=8, ByteCount=4]
```

The `Parameter` list maps init arg offsets to instruction arg offsets. At runtime, the engine substitutes `entity_id=0` with the actual value from `InitializeEvent` args.

To resolve these at post-processing time:

1. Build a map of `eventId -> InitializeEvent arg bytes` from Event 0
2. For each event with `IfActionButtonInArea` where `entity_id == 0`:
   - Find the `Parameter` entry targeting `InstructionIndex` + `TargetStartByte=8`
   - Read the actual entity from init args at `8 + SourceStartByte`

### Entity ID Sources

Two different entity IDs are associated with each fog gate:

| Entity | Source | Used In |
|--------|--------|---------|
| Asset entity | `fog_data.json` `entity_id` | MSB asset placement |
| Action entity | Gate name suffix (numeric gates) or fog_data entity | `IfActionButtonInArea` instruction |

For AEG099 gates (e.g., `m10_01_00_00_AEG099_001_9000`), the asset entity is used in the action button check. For numeric gates (e.g., `m34_12_00_00_34122840`), the gate name suffix IS the action entity, distinct from the asset entity.

## Filtering FogMod Events from Vanilla

EMEVD files contain both vanilla events and FogMod-generated events. Two filters identify FogMod events:

| Filter | Condition | Catches |
|--------|-----------|---------|
| Region threshold | `region >= 755890000` (FOGMOD_ENTITY_BASE) | Most FogMod fogwarp events (FogMod allocates warp target entities from this base) |
| Entity match | Event contains `IfActionButtonInArea` with a known exit gate entity | Manual fogwarp events that reuse vanilla destination regions (e.g., Placidusax lie-down uses region 13002834, well below the threshold) |

An event qualifies as FogMod-generated if **either** filter matches. This is why `ZoneTrackingInjector` pre-scans each event for entity candidates before checking warp instructions.

## Implications for SpeedFog Post-Processing

Because FogMod compiles templates into literal per-instance events:

1. **No template patching possible** -- There is no single fogwarp template to modify at runtime. Post-processors must scan compiled events across all EMEVD files.

2. **Must handle both warp families** -- `WarpPlayer` (2003:14) and `PlayCutsceneToPlayerAndWarp` (2002:11/12) carry destination data in different layouts. Any code scanning for warp destinations must handle both.

3. **Event placement is unpredictable** -- FogMod's `getEventMap()` may place events in a different EMEVD file than the exit gate's map. Post-processors must scan ALL files and build multi-strategy lookups for matching.

4. **Parameterized entities require resolution** -- Manual events store placeholder zeros. Matching against known exit gate entities requires resolving through `InitializeEvent` args and the event's `Parameter` list.

5. **AlternateSide branches produce multiple warp instructions** -- A single event may contain both primary and alternate `WarpPlayer` instructions. Post-processors must handle all warp instructions in an event, not just the first.

### Post-Processor Summary

| Post-Processor | What It Does | Scans For |
|----------------|-------------|-----------|
| `ZoneTrackingInjector` | Inserts `SetEventFlag` before warp instructions to track zone entry | Both warp families in all EMEVD files; uses 4-strategy matching (entity, compound, dest-only, common event) |
| `ErdtreeWarpPatcher` | Replaces primary (m11_00) destination with alternate (m11_05); inserts `SetEventFlag(300, ON)` | Both warp families matching primary region |
| `SealingTreeWarpPatcher` | Replaces alternate (m61_44_45_10) destination with primary (m61_44_45_00) | Both warp families matching alternate region |
| `AlternateFlagPatcher` | Neutralizes `SetEventFlag(300/330, ON)` in Events 900/915 | Specific events in common.emevd (not a warp scanner) |

All warp-scanning post-processors run after `GameDataWriterE.Write()` (step 7 in Program.cs).

## References

| Resource | Location |
|----------|----------|
| fogwarp template | `data/fogevents.txt` (template 9005777) |
| EventEditor compilation | `reference/fogrando-src/GameDataWriterE.cs` L1781-1852 |
| AlternateSide compilation | `reference/fogrando-src/GameDataWriterE.cs` L3333-3348 |
| AlternateOf parsing | `reference/fogrando-src/GameDataWriterE.cs` L595-605 |
| getEventMap logic | `reference/fogrando-src/GameDataWriterE.cs` L4939-4976 |
| ZoneTrackingInjector | `writer/FogModWrapper/ZoneTrackingInjector.cs` |
| ErdtreeWarpPatcher | `writer/FogModWrapper/ErdtreeWarpPatcher.cs` |
| SealingTreeWarpPatcher | `writer/FogModWrapper/SealingTreeWarpPatcher.cs` |
| AlternateFlagPatcher | `writer/FogModWrapper/AlternateFlagPatcher.cs` |
| Alternate warp patching doc | `docs/alternate-warp-patching.md` |
| Zone tracking doc | `docs/zone-tracking.md` |
| Event flags doc | `docs/event-flags.md` |
