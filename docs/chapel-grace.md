# Chapel of Anticipation Site of Grace

**Date:** 2026-02-26
**Status:** Active

How SpeedFog injects a Site of Grace at Chapel of Anticipation (m10_01_00_00) so the player has a respawn anchor and fast travel point at the starting location.

## Purpose

SpeedFog starts the player at Chapel of Anticipation. Vanilla Elden Ring has no Site of Grace there -- the player is meant to die to the Grafted Scion and respawn elsewhere. SpeedFog needs:

1. A respawn anchor so the player returns to the chapel after death or reload
2. A fast travel destination so the player can warp back
3. An initial teleport so the player spawns at the grace on a fresh new-game start

FogRando has a `CustomBonfires` system for this (fog.txt chapel entry, GameDataWriterE.cs:4693-4818). ChapelGraceInjector reimplements the same logic as a post-processing step after `GameDataWriterE.Write()`.

## Call Site

In `Program.cs`, the injector runs at step 7h, gated by `graphData.ChapelGrace`:

```csharp
if (graphData.ChapelGrace)
{
    ChapelGraceInjector.Inject(modDir, config.GameDir, events);
}
```

## Four Subsystems

```
                    ChapelGraceInjector.Inject()
                              │
              ┌───────────────┼───────────────┐───────────────┐
              ▼               ▼               ▼               ▼
         MSB parts      BonfireWarpParam    EMEVD events    One-shot warp
        (grace asset,    (fast travel        (RegisterBonfire, (755864000:
         NPC, player,     row in              SetEventFlag,     WarpPlayer
         spawn region)    regulation.bin)     patch respawn)    on first load)
```

### 1. MSB Injection

Adds four MSB parts to `m10_01_00_00.msb.dcx`:

| Part | Model | Entity ID | Purpose |
|------|-------|-----------|---------|
| Grace asset | AEG099_060 | `BONFIRE_ENTITY_BASE` (10011952) | The visible grace flame |
| Grace NPC | c1000 | `bonfireEntity - 1000` (10010952) | Invisible bonfire controller NPC |
| Player warp target | c0000 | `bonfireEntity - 970` (10010982) | Where WarpPlayer places the player |
| SpawnPoint region | -- | 10012021 | Region for SetPlayerRespawnPoint |

Entity ID derivation follows FogRando convention (GameDataWriterE.cs:4696-4697):
- `chrEntity = bonfireEntity - 1000`
- `playerEntity = bonfireEntity - 970`

If the base entity conflicts with existing MSB parts, it auto-increments. All derived IDs shift accordingly.

Each part is deep-copied from a preferred source part (e.g., `AEG217_237_0501` for the asset, `c4690_9000` for the NPC, `c0000_0000` for the player). The grace NPC gets bonfire-specific params: `ThinkParamID=1`, `NPCParamID=10000000`, `TalkID=1000`, `CollisionPartName=h002000`.

The player warp target and SpawnPoint region are placed 2m forward from the grace position using `MoveInDirection()`.

If a grace already exists (e.g., created by Item Randomizer), the injector skips grace creation but still creates the SpawnPoint region for spawn redirection.

### 2. BonfireWarpParam

Adds a row to BonfireWarpParam in `regulation.bin` for fast travel support:

| Field | Value | Source |
|-------|-------|--------|
| Row ID | 100102 (base, auto-incremented if conflicts) | FogRando chapel convention |
| eventflagId | Allocated from template row, auto-incremented | Unique per bonfire |
| bonfireEntityId | 10011952 (or shifted) | From MSB injection |
| areaNo / gridX / gridZ | 10 / 1 / 0 | Parsed from `m10_01_00_00` |
| posX / posY / posZ | -32.574 / 21.331 / -91.523 | Grace coordinates from fog.txt |
| textId1 | 10010 | Vanilla PlaceName FMG for "Chapel of Anticipation" |
| bonfireSubCategorySortId | 9999 | Sorts last in the warp list |

Cosmetic fields (`forbiddenIconId`, `iconId`, `dispMask*`, etc.) are copied from the template bonfire row (entity 10001950, the existing Chapel grace).

### 3. EMEVD Events

Three modifications to `m10_01_00_00.emevd.dcx`:

**RegisterBonfire in Event 0**: Adds `RegisterBonfire(flagId, bonfireEntityId)` (bank 2009, id 3) to the map init event so the game recognizes the grace. Also adds `SetEventFlag(flagId, ON)` to pre-activate it (the player doesn't need to discover it).

**Patch Event 10010020** ("Game start"): The vanilla event calls `SetPlayerRespawnPoint(10012020)` which sets the respawn to the Grafted Scion area. The injector replaces region 10012020 with the grace SpawnPoint region (10012021) so subsequent respawns land at the grace.

**One-shot warp event (755864000)**: Teleports the player to the grace on first map load:

```
EndIfEventFlag(End, ON, EventFlag, 1040299002)   // skip if already done
WaitFixedTimeFrames(1)                             // wait for entity loading
WarpPlayer(10, 1, 0, 0, playerEntity, 0)          // teleport to grace
SetEventFlag(EventFlag, 1040299002, ON)            // mark done (one-shot)
```

Registered in Event 0 via `InitializeEvent(slot=0, eventId=755864000)`.

### 4. Critical Game Mechanic: Initial Spawn

`SetPlayerRespawnPoint` (bank 2003, id 23) only controls where the player respawns after death or loading a save. The initial new-game spawn location is engine-controlled (tied to the opening cutscene). There is no EMEVD instruction to override the engine's first-load spawn.

`WarpPlayer` (bank 2003, id 14) is the only way to relocate the player on first load. The one-shot event fires once after the map loads, teleports the player to the grace position, then sets flag 1040299002 so it never fires again. On subsequent loads, `SetPlayerRespawnPoint` (patched in Event 10010020) handles respawning at the grace normally.

## Filesystem Handling

FogMod under Wine writes lowercase directory names (`mapstudio`), while vanilla Elden Ring uses PascalCase (`MapStudio`). On Linux with a case-sensitive filesystem, both must be checked.

`FindMsbPath()` tries both `map/mapstudio/` and `map/MapStudio/` when looking for an MSB file. `FindOrCreateMsbDir()` reuses whichever case FogMod already created, defaulting to lowercase.

The same pattern applies to EMEVD files in `event/` and talk scripts (handled by other injectors with `script/talk/` vs `script/Talk/` variants).

## Helper Functions

| Function | Purpose | FogRando source |
|----------|---------|-----------------|
| `MoveInDirection(x, y, z, rotY, dist)` | Translate a position forward by `dist` meters along the Y-rotation heading | GameDataWriterE.cs:5326-5330 |
| `SetNameIdent(part)` | Set `Unk08` from the numeric suffix of a part's Name (e.g., `AEG099_060_9900` -> `Unk08=9900`). Required for entity identity resolution. | GameDataWriterE.cs:5263-5268 |
| `GeneratePartName(existing, model)` | Generate unique part name in the 9900+ range to avoid vanilla/FogMod conflicts | -- |
| `EnsureAssetModel` / `EnsureEnemyModel` | Add model definitions to MSB if not already present | -- |
| `FindMsbPath(baseDir, fileName)` | Check both `MapStudio` and `mapstudio` directory variants | -- |

## File References

| File | Role |
|------|------|
| `writer/FogModWrapper/ChapelGraceInjector.cs` | All injection logic |
| `writer/FogModWrapper/Program.cs` | Call site (step 7h) |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | `ChapelGrace` bool field |
| `reference/fogrando-src/GameDataWriterE.cs` | FogRando custom bonfire logic (L4693-4818) |
| `docs/event-flags.md` | Flag ranges (1040299002 one-shot, 755864000 event ID) |
