# Boss Healthbar Names (promoted mobs)

When the enemy randomizer places a regular mob in a boss arena (an Aging
Untouchable in Rellana's arena, a Crucible Knight in a catacomb), the boss
healthbar still shows the arena's vanilla name ("Rellana, Twin Moon Knight").
`BossNameInjector` repoints the healthbar at the placed enemy's name.

## Mechanism

The healthbar name is the `nameId` argument of the EMEVD instruction
`DisplayBossHealthBar` (bank 2003, id 11: `on, entity, slot, nameId`), an id
into the `NpcName` FMG. It is not read from the NpcParam row.

What the Item Randomizer (RandomizerCommon) does with it:

- A source with a vanilla `Important.NpcName` in `enemy.txt` (every vanilla
  boss) brings its own healthbar events along when relocated, so the name
  follows the boss. Nothing to do for those.
- A source without one (`Class: Basic` mobs of the minor pool, see
  `data/boss_arena_tags.json`) is placed under the arena's entity id and the
  arena's own events keep running, `nameId` included.
- Its `editnames` option rewrites the name through `GetCleverName`
  (`EnemyRandomizer.cs`), composing "X, Twin Moon Knight"-style names and
  falling back to the arena's name in non-English languages. SpeedFog enabled
  it (commit 0fcf910) then dropped it (596cad7, June 2026) for that reason.

Measured on a full seed output (`tools/dump_emevd_warps dump --event all`):
213 of the 219 tagged arenas show their healthbar through a literal
`2003[11]` (entity = arena id), 212 of them in the EMEVD their entity id
encodes, 1 through an event parameter only (1043370340), 5 through none
(the Leda fight entities, which are not in the pool). Rewriting the literal
instruction covers the pool.

## Data flow

Python (`speedfog/enemy_data.py`, wired in `main.py` next to
`patch_graph_enemy_assignments`):

1. `parse_boss_npc_names()` scans `enemy.txt` for `Important.NpcName`
   (4-space indent; every `Class: Boss` entry has one).
2. `build_boss_names()` keeps the `enemy_assignments` whose source has no
   NpcName and pairs each arena with the name already resolved for
   `randomized_bosses` (`Names.Key`, then `ExtraName`, then the
   `boss_arena_tags.json` name, minus a trailing parenthetical such as
   "Divine Bird Warrior (Frost)") and the map whose EMEVD runs its boss
   events, `event_map_for_entity()`: vanilla entity ids encode it
   (`30010800` is `m30_01_00_00`, `1052380800` is `m60_52_38_00`,
   `2048440800` is `m61_48_44_00`). The enemy.txt `Map:` is the MSB part's
   map instead, a `_02` large tile for Fire Giant and Radahn whose events
   live in the `_00` tile EMEVD.
3. `patch_graph_boss_names()` writes them as graph.json v4.8 `boss_names`:
   `{"30010800": {"name": "Aging Untouchable", "map": "m30_01_00_00"}}`.

C# (`writer/FogModWrapper/BossNameInjector.cs`, mod-dir phase, after
`CopyLocalizedFmgs` and before `TextTheme`):

1. `ResolveNameIds`: each name becomes a NpcName id. An exact match of the
   English text in the vanilla engus `NpcName` FMGs (item + item_dlc02
   bnds) reuses the lowest vanilla id, so "Crucible Knight" (902500301)
   stays localized in every language for free. Otherwise a
   `SpeedFogIds.BossNameFmgIds` id (755890000+, capacity 100) is allocated,
   one per distinct name, in ascending arena id order.
2. New entries are written to the base `NpcName.fmg` inside
   `item_dlc02.msgbnd.dcx` for engus and frafr only, English text in both
   (`MsgBndEditor`, the same language policy and mod-copy layering as
   `TextTheme`; the promoted names are mostly proper nouns, and touching all
   languages would lengthen every build). Other languages show no name for
   those arenas. The `_dlc02` bundle is the one the game resolves text from:
   it carries a full copy of the base FMGs, FogMod and the Item Randomizer
   write only the `_dlc02` bundles, and the base `item.msgbnd.dcx` is not
   consulted when the DLC one is present. Any injector adding FMG entries
   must target `_dlc02` for the same reason.
3. `PatchEmevd`: in `<map>.emevd.dcx` of the mod dir, every `2003[11]` whose
   literal entity is the arena id gets the new `nameId` (enable and disable
   calls alike). Instructions whose `nameId` is bound to an event parameter
   are skipped: the literal bytes are dead there. Arenas the declared map
   does not cover (EMEVD absent from the mod dir, or no instruction there)
   fall back to one scan of every other EMEVD of the mod dir, patching each
   file that holds their instruction (one arena of the 213 measured,
   2050480860, has it in the neighbouring tile). An arena still unmatched
   logs a warning and keeps the vanilla name.

## Verification

```bash
# Healthbar instructions of the arena map in the seed output (nameId column)
cd tools/dump_emevd_warps && dotnet run -- dump <seed>/mods/fogmod/event/ \
  --map-filter m30_01_00_00 --event all | grep DisplayBossHP

# The new NpcName entries (engus/frafr copies in the seed)
wine tools/game_inspect/publish/win-x64/game_inspect.exe dump-fmg \
  <seed>/mods/fogmod/msg/engus/item_dlc02.msgbnd.dcx Untouchable
```

The FogModWrapper log lists one line per arena
(`m30_01_00_00: arena 30010800 -> "Aging Untouchable" (NpcName 755890000, new, 2 instruction(s))`)
and a summary (`Boss names: N/M arena(s) patched, K new NpcName entries in 2 language(s)`).

## Files

| File | Role |
|------|------|
| `speedfog/enemy_data.py` | `parse_boss_npc_names`, `event_map_for_entity`, `build_boss_names`, `patch_graph_boss_names` |
| `speedfog/main.py` | Wiring after `patch_graph_enemy_assignments` |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | `BossNames` / `BossNameEntry` (v4.8) |
| `writer/FogModWrapper.Core/SpeedFogIds.cs` | `BossNameFmgIds` (NpcName FMG namespace) |
| `writer/FogModWrapper/BossNameInjector.cs` | Resolution, FMG entries, EMEVD repoint |
| `writer/FogModWrapper/MsgBndEditor.cs` | Shared msgbnd edit helper (extracted from `TextTheme`) |
| `writer/FogModWrapper.Tests/BossNameInjectorTests.cs` | Synthetic msg tree + EMEVD coverage |
