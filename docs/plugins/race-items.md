# Race Items Plugin

Mario Kart style items for speedfog-racing. The plugin only creates the
items in the game data (goods, SpEffects, EMEVD events, texts) and exports
their ids in `graph.json`; the racing server decides who gets what and the
racing mod hands items out, reads their use and removes them. Opt-in via
config:

```toml
[plugin.race_items]
enabled = true
```

`enabled` is the only setting; any other key aborts the build
(`InvalidDataException`). The catalog lives in `data/plugins/race_items.toml`,
read by the C# plugin and by the Python loader (`speedfog/race_items.py`).
It is not a text theme: it never counts toward the "more than one text theme"
warning.

## What it adds

- **Goods and SpEffects** (`RaceItemsRegulationInjector`, regulation.bin):
  two EquipParamGoods rows, the passive Shield and the usable Race Item that
  carries every other item, cloned from goods 250 (Flask of Wondrous Physick)
  and forced to a single consumable that is not droppable, sellable or
  depositable. The Race Item is auto-equipped and points at its marker
  SpEffect; the Shield is neither. One effect SpEffect per item that needs
  one, applied by the racing mod.
- **EMEVD events** (`RaceItemsEmevdInjector`, common.emevd): one use event
  (the Race Item), one removal event per goods, and one removal event per
  talisman of the talisman table (below), for the Talisman Thief.
- **Texts** (`RaceItemsTextInjector`): each goods' name, info and caption in
  the base `GoodsName.fmg`, `GoodsInfo.fmg` and `GoodsCaption.fmg` of
  `item_dlc02.msgbnd.dcx`, for `engus` and `frafr` (English in both); the
  items have no texts of their own. The `_dlc01` / `_dlc02` FMG variants are
  left alone.
- **graph.json**: the `race_items` key (below), written only when the plugin
  is enabled.

The ranges are reserved in `SpeedFogIds` and documented in
`docs/event-flags.md`: goods and SpEffect ids 1450800-1450899 (goods ids and
their markers in 1450800-1450819, item effect SpEffects in 1450820-1450849),
use flags 1050290100-1050290199, removal flags 1050290200-1050290299,
talisman removal flags 1050290300-1050290499 (saved category 1050290),
common.emevd events 755865700-755865799 and 755865800-755865999 (talismans),
icon ids 60385-60414.

## Goods and items

Two goods carry every item. A runner holds at most one item, so at most one of
the two goods is ever in the inventory. A single usable goods keeps the quick
slots clean: one goods per item filled a slot with each item ever granted.
The Race Item shows its own icon, the item box, until the racing mod writes
the held item's icon id into its EquipParamGoods row (in memory, before each
grant's spawn and before each inventory report, which a loading exit or a
reconnection while connected triggers); the race overlay names the held item.

### Goods

n = goods id - 1450800. The marker SpEffect id is the goods id; the use flag
is 1050290100 + n; the removal flag is 1050290200 + n.

| n | key | name | goods id | icon | marker | use flag | removal flag |
|---|---|---|---|---|---|---|---|
| 0 | `shield` | Shield | 1450800 | `shield` (60385) | none (not usable, not auto-equipped) | none | 1050290200 |
| 1 | `race_item` | Race Item | 1450801 | `item_box` (60396) | 10600, `effectEndurance = 1` | 1050290101 | 1050290201 |

### Items

Each item points at one goods and has a name and an icon of its own (the icon
the racing mod writes into the Race Item). An item's effect SpEffect sits in
1450820-1450849, at 1450820 + the item's index in the v1 catalog (one goods
per item, 1450800 + index), so the v1 effect ids keep their meaning.

| key | name | goods | icon | effect | duration |
|---|---|---|---|---|---|
| `shield` | Shield | `shield` | `shield` | none | none |
| `rune_pouch_1` | Small Rune Pouch | `race_item` | `rune_pouch` | 1450821, 3269, `soul = 10000` | none |
| `rune_pouch_2` | Rune Pouch | `race_item` | `rune_pouch` | 1450822, 3269, `soul = 20000` | none |
| `rune_pouch_3` | Large Rune Pouch | `race_item` | `rune_pouch` | 1450823, 3269, `soul = 40000` | none |
| `banana` | Banana | `race_item` | `banana` | none | none |
| `red_shell` | Red Shell | `race_item` | `red_shell` | none | none |
| `mushroom` | Mushroom | `race_item` | `mushroom` | 1450826, 10600, `effectEndurance = 15`, cycles 102360 every 0.5 s | 30 s |
| `hammer` | Hammer | `race_item` | `hammer` | 1450827, 10600, `equipWeightChangeRate = 0.35` (rewritten per target by the racing mod), `effectEndurance = 15` | 15 s |
| `ink` | Ink | `race_item` | `ink` | none (overlay only; the racing mod keeps it waiting until the runner stands in a zone whose node has `unused_exits` above 0; in such a boss arena, the exits hidden through the fight, its time runs from the boss's kill) | 30 s |
| `blue_shell` | Blue Shell | `race_item` | `blue_shell` | 1450829, 10600, `maxHpRate = 0.5`, `effectEndurance = 15` | 15 s |
| `lightning` | Lightning | `race_item` | `lightning` | 1450830, 10600, `effectEndurance = 15`, cycles 4101 every 0.06 s | 15 s |
| `flask_stopper` | Flask Stopper | `race_item` | `flask_stopper` | 1450831, 10600, `stateInfo = 154`, `effectEndurance = 15` | 15 s |
| `toxic_blossom` | Toxic Blossom | `race_item` | `toxic_blossom` | 1450832, 3370, `poizonAttackPower = 99999` | none |
| `star` | Star | `race_item` | `star` | 1450833, 10600, `noDead = 1`, the eight damage cut rates and `toughnessDamageCutRate` at 0, the seven status `disable*` bits at 1 and `*DefDamageRate` at 0, `effectEndurance = 15` | 15 s |
| `vanish` | Vanish | `race_item` | `vanish` | none (applied by the racing mod through memory writes) | 15 s |
| `unseen_foes` | Unseen Foes | `race_item` | `unseen_foes` | none (memory writes) | 15 s |
| `slow_mo` | Slow-mo | `race_item` | `slow_mo` | none (memory writes) | 15 s |
| `mini` | Mini | `race_item` | `mini` | none (memory writes) | 15 s |
| `giant` | Giant | `race_item` | `giant` | none (memory writes) | 15 s |
| `camera` | Camera | `race_item` | `camera` | none (memory writes) | 15 s |
| `talisman_thief` | Talisman Thief | `race_item` | `talisman_thief` | none (the racing mod picks a talisman and raises its removal flag) | none |
| `junk_weapon` | Junk Weapon | `race_item` | `junk_weapon` | none (the racing mod equips one of the junk weapons below) | none |
| `hunter` | Hunter | `race_item` | `hunter` | none (the racing mod spawns him from the Hunter rows below) | 60 s |

The Junk Weapon forces one of five +0 standard weapons into the victim's right
hand: Club (11010000), Large Club (12000000), Great Club (23020000),
Astrologer's Staff (33130000) and Finger Seal (34000000), the `[[junk_weapon]]`
entries of `race_items.toml` (names from the Item Randomizer's
`EquipParamWeapon.txt`). The Torch is left out: it lights dark areas, which
helps its holder.

The Hunter is Margit, the Fell Omen (model `c2130`) sent after the leader. The
`[hunter]` table of `race_items.toml` names his source rows (NpcParam 21300014,
NpcThinkParam 21300000, read by the writer only) and where the clones go:
NpcParam `1450850 + (tier - 1)` for tiers 1 to 34 (1450850-1450883, each
carrying the area scaling row of its tier) and NpcThinkParam 1450850. The
range NpcParam and NpcThinkParam 1450850-1450899 is reserved
(`docs/event-flags.md`).

Why clones. The racing mod spawns him through the game's debug character
creator, which runs none of the events of the map he lives in, and FogMod
scales its bosses per seed through EMEVD SpEffects on the boss entity, so a
spawned Margit would always fight at his own row's tier 3 scaling. The writer
therefore clones his NpcParam row (21300014) once per enemy tier, the way the
Halloween greeter clone does (`AmbientSpawnInjector.CloneAtTier`): the template's
own area scaling SpEffect is swapped for the game's row of the tier (7010-7200
for tiers 1 to 20, 20007000-20007130 for the DLC tiers 21 to 34, see
`docs/enemy-scaling.md`), and the racing server picks the row from the zone
tier of the leader's node. These are not FogMod's dampened boss rows: he is a
bit stronger than a placed boss of the same tier in the base game tiers, and in
the DLC tiers he hits like tier 20 (the DLC damage curve is flat). Every clone
also gets no runes (`getSoul` 0, `isSoulGetByBoss` 0; his 12000 runes are paid
by a map event, not by the row) and no drop (`itemLotId_enemy`, `itemLotId_map`,
`sleepCollectorItemLotId_enemy` and `sleepCollectorItemLotId_map` at -1), so a
seed's randomizers cannot give him loot. `behaviorVariationId` (his moveset),
`teamType` and the resident SpEffects stay the source's.

The think row is a clone of NpcThinkParam 21300000 with `maxBackhomeDist`,
`backhomeDist` and `backhomeBattleDist` at 9999 and `eye_dist` at 60. When any
enemy is randomized the enemy randomizer caps every think row (eye and nose
distance at 100, `maxBackhomeDist` at 200), which would send him home after
200 m; the clone restores his vanilla values. Every cell name is looked up in
the loaded paramdefs and an unknown one fails the build instead of being
skipped. A catalog without a `[hunter]` table adds no NpcParam or NpcThinkParam
row.

To check a built seed in game, dump the rows with the Tarnished tool's
`game_inspect dump-param` (NpcParam 1450850, 1450867 and 1450883, NpcThinkParam
1450850) and compare the scaling SpEffect slot and the cells above.

The Star's row makes its user unkillable for its 15 s: `noDead` keeps the
character out of the death state and every damage cut rate (slash, blow,
thrust, neutral, magic, fire, thunder, dark) and the poise damage cut rate are
0, so no hit lands and none staggers. Nor does any status: the seven immunity
bits (`disablePoison`, `disableDisease` for scarlet rot, `disableBlood`,
`disableCurse` for death blight, `disableFreeze`, `disableSleep`,
`disableMadness`) keep each from taking hold, and the seven defender build-up
rates (`poisonDefDamageRate` and the others) at 0 keep its bar empty, so none
fires once the Star ends (a status already on the user when the Star starts
may keep ticking: not checked in game). It uses this row rather than the
character debug flags (`ChrDbgFlags`), which the racing mod's cheat detection
reads. The six other new items have no row: the racing mod applies them by
writing the player's or the foes' memory for their duration, so the catalog
only gives them a name, an icon and a duration.
An effect on an item of the Shield goods is rejected when the catalog loads:
the Shield is never used, so nothing would apply it.

Templates: 10600 is FogRando's Bell of Return marker (neutral, every rate at
1, self-targeted; `GameDataWriterE.cs:3857-3871`), 3269 is Golden Rune [1]
(`soul = 200`), 3370 is the poison row the randomizer's trap copies.

Some behaviors are not in the SpEffect row but in the player's behavior script
(`action/script/c0000.hks`), which tests vanilla SpEffect ids.

- **Observed in game.** Rolls and sprints are free while 102360 (Viridian
  Hidden Tear) is on the player, and 4101 forces a slow walk. Copies of 102360
  and 425 under another id kept their param fields but neither behavior.
- **Read from the script's constants.** 102360 sits next to the stamina costs
  of rolls, jumps and quick steps; 425, 4100 and 4101 sit next to the move
  speed and equip weight code.

An item reaches those behaviors by cycling the vanilla row:
`cycleOccurrenceSpEffectId` applies it every `motionInterval` seconds while the
item's row lasts, as vanilla row 20011920 does with 4101. The Mushroom effect
lasts 15 s and cycles 102360 every 0.5 s; 102360 lasts 15 s from its last
application, so 30 s in all. The Lightning effect cycles 4101 (0.1 s each) for
15 s.

The Race Item's neutral marker sets `effectEndurance = 1` rather than the
template's one-frame 0, a margin for the use event; its use flag rises in
game.

Dead ends from the in-game passes: `consumeStaminaRate` only scales the stamina
cost of weapon attacks; `stateInfo` 286 / 287 ("movement slow" in the
community enum) slowed nothing, and no row of the 1.17 regulation uses them;
copies of 102360 and 425 kept their param fields but not the c0000.hks
behaviors.

## Events

Both shapes are adapted from FogRando's `common_bellofreturn`
(`data/fogevents.txt:389-406`) and restart themselves when they end.

- **Use event** (one per usable goods, id `755865700 + n`: only the Race
  Item, 755865701): wait for the marker SpEffect on the player, set the use
  flag, wait for the marker to end, restart, so one use is one flag raise.
- **Removal event** (one per goods, id `755865750 + n`: 755865750 for the
  Shield, 755865751 for the Race Item): wait for the removal flag, remove one
  of the goods from the player, clear the flag, restart. The racing mod
  raises the flag; the event clears it, so the flag is a one-shot request.
- **Talisman removal event** (one per talisman, id `755865800 + n`, n = the
  talisman's index in the talisman table): wait for the talisman's removal
  flag (`1050290300 + n`), remove the talisman from the player (Remove Item
  From Player, item type Ring), clear the flag, restart. The racing mod will
  raise it for the Talisman Thief.

The talisman table, `data/plugins/race_items_talismans.toml`, lists every
EquipParamAccessory row with an English name (155: base game and Shadow of
the Erdtree; the two unnamed rows, 204000 and 999999999, are left out), in
ascending id order. Ids and names come from the Item Randomizer's talisman
name list (`writer/ItemRandomizerWrapper/diste/Names/EquipParamAccessory.txt`,
extracted by the bootstrap). Its position
numbers the flags and events, so an insertion renumbers the talismans after
it in the seeds generated from then on. It sits apart from `race_items.toml`,
whose edits trip the stale icon overlay check (see Replacing the icons).

No item takes effect through an event or the game's own use: the Race Item's
use flag tells the racing server which item was used (the one the runner
holds), and the racing mod applies the item's effect SpEffect, to its targets
or, for the Mushroom and the Rune Pouches, to the sender.

## graph.json `race_items`

Present only when the plugin is enabled (`GRAPH_JSON_VERSION` 4.14; 4.11
had one goods per item; 4.14 added `talismans`, `junk_weapons` and `hunter`). `goods` has one entry per goods key, `items` one per
item key, each item naming its goods by key; `use_flag`, `effect_speffect`
and `duration_ms` are `null` where the tables above say none. The real value
lists every item; this excerpt shows two:

```json
"race_items": {
  "goods": {
    "race_item": {"goods_id": 1450801, "use_flag": 1050290101, "remove_flag": 1050290201, "icon": "item_box", "icon_id": 60396},
    "shield":    {"goods_id": 1450800, "use_flag": null,       "remove_flag": 1050290200, "icon": "shield",   "icon_id": 60385}
  },
  "items": {
    "hammer": {"name": "Hammer", "icon": "hammer", "icon_id": 60390, "goods": "race_item", "effect_speffect": 1450827, "duration_ms": 15000},
    "shield": {"name": "Shield", "icon": "shield", "icon_id": 60385, "goods": "shield", "effect_speffect": null, "duration_ms": null}
  },
  "talismans": [{"id": 1000, "name": "Crimson Amber Medallion", "flag": 1050290300}],
  "junk_weapons": [{"id": 11010000, "name": "Club"}],
  "hunter": {"chr_id": 2130, "think_param": 1450850, "npc_params": [1450850, 1450851]}
}
```

`talismans` is the talisman table (155 entries in ascending id order; the
excerpt shows one) with each talisman's removal flag, `1050290300 + its index`.
The generator exports the flags so the racing server and mod take them from
the seed instead of recomputing the numbering: the mod knows which flag to
raise for the talisman it picks, and an insertion in the table cannot
desynchronise them. `junk_weapons` lists the weapons above and `hunter` the
model, the think row and the 34 NpcParam rows, `npc_params[t - 1]` being the
row of tier `t`. A catalog without a `[hunter]` table or talisman file
exports `null` / empty values; the writer-only source rows never reach
graph.json.

## Replacing the icons

The 22 icons ship as placeholder flat shapes. The art is replaced by dropping
PNG files in, nothing else changes. For the graphic artist and for Roger:

- **Format**: PNG, RGBA with a transparent background, square, at least
  160x160; 1024x1024 asked of the artist, so sharper large icons stay one
  setting away (below). One file per icon: every in-game texture is derived
  from it by resizing.
- **Files**: `data/plugins/race_items_icon_<icon>.png`, `<icon>` being a name of
  the catalog's `[icons]` table: `shield`, `rune_pouch`, `banana`, `red_shell`,
  `mushroom`, `hammer`, `ink`, `blue_shell`, `lightning`, `flask_stopper`,
  `toxic_blossom`, `item_box`, `star`, `vanish`, `unseen_foes`, `slow_mo`,
  `mini`, `giant`, `camera`, `talisman_thief`, `junk_weapon`, `hunter` (ids
  60385-60406; the three Rune Pouches share
  one icon; `item_box` is the Race Item's own icon, shown until the racing
  mod writes the held item's icon).
- **After the drop**: overwrite the files (never re-run
  `tools/generate_race_item_icons.py` afterwards, it would overwrite the art),
  re-run `tools/bootstrap.py` on the generation host (it also needs a re-run
  after the rename to `speedfog-icons`; `speedfog` refuses to generate a
  Halloween or race items seed while the overlay is missing or older than the
  icon PNGs or `race_items.toml`; a missing or unreadable file, or one that is
  not a square of at least 160x160, makes `ItemIconPatcher` skip the whole
  overlay with a warning), regenerate the pools, then in speedfog-racing run
  `uv run tools/sync_race_item_icons.py` and release the mod. A detailed
  1024x1024 PNG can pass the 500 KB limit of the `check-added-large-files`
  pre-commit hook: commit the drop with `SKIP=check-added-large-files`.

Each PNG becomes two textures, both found by name like any texture of the
overlay:

- `MENU_ItemIcon_<id>`, 160x160 (the size of every vanilla item icon): the
  inventory list and the quick slots.
- `MENU_Knowledge_<id>`, the large icon of the inventory's item detail panel
  (the vanilla ones live in `menu/{hi,low}/00_solo.tpfbhd`, too big to ship).
  It is 160x160 as well, the small texture's own encode, so it looks blurrier
  than the vanilla ones (1024x1024 in `hi`, 512x512 in `low`).

Each texture is encoded in BC7, serially (BCnEncoder's parallel encode crashes
under Wine). At 160x160 the overlay builds in seconds; the vanilla large sizes
took about 35 s per icon, about 8 minutes per bootstrap (486 s under Wine on
2026-11-13), a cost judged too high for a panel few players open.
`Variants` in `ItemIconPatcher` holds the large sizes to restore for sharper
large icons.

The icons ship in the shared `data/mods/speedfog-icons/` overlay, together with
the Halloween icons (see [halloween-icons.md](halloween-icons.md)): one
`05_dummy.tpf.dcx` per variant (`menu/hi`, `menu/low`), shipped when Halloween
or race items is enabled. With the flat placeholders it weighs about 0.17 MB
per seed (83 KB per variant), measured with 11 icons, before the item box.

## Calibration values

Starting values Roger tunes in game, then bakes into the pools (effect values
are part of every seed): the Rune Pouch amounts 10000 / 20000 / 40000.

The Hammer's `equipWeightChangeRate` multiplies the maximum equip load, so no
fixed value puts every build in the fat-roll band (at 0.35 a 32 / 49.8 build
ended at 32 / 17.4 in game, overloaded). The racing mod therefore rewrites the
row in memory for each target, just before applying it, so the target's
current load sits at 97 % of its max (a server setting; see speedfog-racing
`docs/RACE_ITEMS.md`, effect `equip_load`). The seed's 0.35 only applies when
the mod cannot read the load.

## In-game checks

How to run them, on a test seed without the racing mod: Cheat Engine with The
Grand Archives table (`ER_TGA_v1.17.0.CT`).

- `Scripts > Build Creation > ItemGib` gives an item: `Item` is
  `0x40000000 | goods id` in hex (`40162331` for the Race Item), then
  `> Spawn Item`. The practice tool's item spawner cannot give these items, its
  list is fixed.
- `Event Flags > Event Flags by ID` reads and writes a use or removal flag.
  Its `State` is read from the game only when the `Event Flag ID` changes: type
  (or re-type) the ID after the action, or `State` shows a stale value. The
  `Examples > Print Flags in Range` script reads them live.
- `Scripts > Functions > ApplyEffect` applies an effect SpEffect (`SpEffectId`,
  then `> Add SpEffect`). In a race the effects land on the other runners, so
  this is the only way to see them alone.

Passes on seed 603810141, 2026-11-13 (v1 catalog: one goods per item, the
Mushroom and the Rune Pouches applied by their use markers):

- **Passed.**
  - **Inventory.** The item lands in a quick slot (with every quick slot full,
    it only goes to the inventory). It shows its small icon, name and caption.
    It is consumed on use and does not come back at a grace.
  - **Flags and pouches.** A removal flag removes the goods and clears itself.
    The Rune Pouches grant their runes and raise their use flag.
  - **Effects.** Flask Stopper (`stateInfo = 154`) blocks flasks. The Hammer
    effect works. Blue Shell halves max HP. Toxic Blossom poisons, and
    Neutralizing Boluses cure it. The Mushroom (cycling 102360) makes rolls and
    sprints free. Lightning (cycling 4101) forces a slow walk.
- **Use flags.** Every usable item raises its use flag, the neutral markers
  included. Earlier passes read some flags as 0 because the ID was typed before
  the action (see `State` above), which sent the debugging after markers that
  worked.
- **Large icon.** The inventory's detail panel draws a separate texture,
  `MENU_Knowledge_<iconId>`, which the `05_dummy` overlay now ships too (see
  Replacing the icons). A test build showed it resolves by name like the small
  icon; the 1024x1024 build was checked in game (seed v7, 2026-11-13), the
  shipped 160x160 one is still to check.

To check on the next seed:

- the large icon of every item in the inventory's detail panel, the item box
  included;
- the Race Item: one quick slot only after many grants and uses, its use flag
  raised on every use; the Shield goods out of the quick slots;
- the Mushroom effect (1450826, applied with ApplyEffect): buff icon, rolls
  free for 30 s, and no flicker or repeated sound from the 102360
  re-applications; a second Mushroom during the first;
- Lightning: its effect ending at 15 s, and no hitch over its 250 re-applications
  of 4101;
- each Rune Pouch effect (1450821-1450823, applied with ApplyEffect) granting
  its runes once;
- a talisman removal flag on an equipped talisman (the Talisman Thief's
  prerequisite): give the Crimson Amber Medallion with ItemGib (`200003E8`,
  `0x20000000 | 1000`), equip it, note the max HP, then raise its flag
  1050290300 with Event Flags by ID. Expected: the talisman leaves the
  inventory and its slot, the max HP drops back, the flag reads OFF again
  (re-type the ID), and a reload keeps it all. Then the same with a talisman
  in the fourth slot (Talisman of All Crucibles, `20002030`, flag
  1050290454), and with one held but not equipped. If the slot keeps the
  talisman or its effect, the racing mod will have to empty the slot before
  raising the flag.
