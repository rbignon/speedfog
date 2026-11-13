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
  one EquipParamGoods row per item, cloned from goods 250 (Flask of Wondrous
  Physick) and forced to a single auto-equipped consumable that is not
  droppable, sellable or depositable; one marker SpEffect per usable item
  (the goods row points at it), plus an effect SpEffect for the items that
  need one.
- **EMEVD events** (`RaceItemsEmevdInjector`, common.emevd): one use event
  and one removal event per item.
- **Texts** (`RaceItemsTextInjector`): name, info and caption in the base
  `GoodsName.fmg`, `GoodsInfo.fmg` and `GoodsCaption.fmg` of
  `item_dlc02.msgbnd.dcx`, for `engus` and `frafr` (English in both). The
  `_dlc01` / `_dlc02` FMG variants are left alone.
- **graph.json**: the `race_items` key (below), written only when the plugin
  is enabled.

The ranges are reserved in `SpeedFogIds` and documented in
`docs/event-flags.md`: goods and SpEffect ids 1450800-1450899, use flags
1050290100-1050290199, removal flags 1050290200-1050290299 (saved category
1050290), common.emevd events 755865700-755865799, icon ids 60385-60399.

## Items

n = goods id - 1450800. The marker SpEffect id is the goods id; the effect
SpEffect id is the goods id + 20; the use flag is 1050290100 + n; the removal
flag is 1050290200 + n.

| n | key | name | goods id | marker | effect | duration |
|---|---|---|---|---|---|---|
| 0 | `shield` | Shield | 1450800 | none (not usable, no use flag) | none | none |
| 1 | `rune_pouch_1` | Small Rune Pouch | 1450801 | 3269, `soul = 10000` | none | none |
| 2 | `rune_pouch_2` | Rune Pouch | 1450802 | 3269, `soul = 20000` | none | none |
| 3 | `rune_pouch_3` | Large Rune Pouch | 1450803 | 3269, `soul = 40000` | none | none |
| 4 | `banana` | Banana | 1450804 | 10600, `effectEndurance = 1` | none | none |
| 5 | `red_shell` | Red Shell | 1450805 | 10600, `effectEndurance = 1` | none | none |
| 6 | `mushroom` | Mushroom | 1450806 | 102360, `effectEndurance = 15` | none (the marker is the effect) | 15 s |
| 7 | `hammer` | Hammer | 1450807 | 10600, `effectEndurance = 1` | 1450827, 10600, `equipWeightChangeRate = 0.35`, `effectEndurance = 15` | 15 s |
| 8 | `ink` | Ink | 1450808 | 10600, `effectEndurance = 1` | none (overlay only) | 15 s |
| 9 | `blue_shell` | Blue Shell | 1450809 | 10600, `effectEndurance = 1` | 1450829, 10600, `maxHpRate = 0.5`, `effectEndurance = 15` | 15 s |
| 10 | `lightning` | Lightning | 1450810 | 10600, `effectEndurance = 1` | 1450830, 425, `effectEndurance = 15` | 15 s |
| 11 | `flask_stopper` | Flask Stopper | 1450811 | 10600, `effectEndurance = 1` | 1450831, 10600, `stateInfo = 154`, `effectEndurance = 15` | 15 s |
| 12 | `toxic_blossom` | Toxic Blossom | 1450812 | 10600, `effectEndurance = 1` | 1450832, 3370, `poizonAttackPower = 99999` | none |

Templates: 10600 is FogRando's Bell of Return marker (neutral, every rate at
1, self-targeted; `GameDataWriterE.cs:3857-3871`), 3269 is Golden Rune [1]
(`soul = 200`), 102360 is the Viridian Hidden Tear (15 s without stamina
consumption: `stateInfo = 499`, the only 1.17 regulation row using it, plus
its buff icon, visual effect and a chain to vanilla rows 102361 and 102363), 425 is the vanilla bullet that slows the player
(`moveType = 4`, the forced slow walk; 20 s in vanilla), 3370 is the poison row
the randomizer's trap copies.

A neutral marker sets `effectEndurance = 1`, so it lasts long enough for the
use event to see it. With the template's 0, the Hammer's use flag stayed off in
game, while the Rune Pouch markers (also 0, plus `soul`) raised theirs, and so
does FogRando's bell (0, but its goods row is not the Physick clone). The 1 s
window is the fix under test, not a proven cause.

Two dead ends from the first in-game pass: `consumeStaminaRate` only scales the
stamina cost of weapon attacks (rolls and sprints still cost stamina), and
`stateInfo` 286 / 287 ("movement slow" in the community enum) slowed nothing:
no row of the 1.17 regulation uses them.

## Events

Both shapes are adapted from FogRando's `common_bellofreturn`
(`data/fogevents.txt:389-406`) and restart themselves when they end.

- **Use event** (one per usable item, id `755865700 + n`): wait for the
  marker SpEffect on the player, set the use flag, wait for the marker to
  end, restart. The flag goes up once per use, including the Mushroom (whose
  marker lasts 15 s, so one use is one flag raise).
- **Removal event** (one per item, id `755865750 + n`): wait for the removal
  flag, remove one of the goods from the player, clear the flag, restart. The
  racing mod raises the flag; the event clears it, so the flag is a one-shot
  request.

Items with an effect SpEffect (Hammer, Blue Shell, Lightning, Flask Stopper,
Toxic Blossom) are not applied by an event: when the server resolves a use,
the target's racing mod applies the effect SpEffect itself. The Mushroom and
the Rune Pouches take effect through the game's own use, their marker being
the effect.

## graph.json `race_items`

Present only when the plugin is enabled (`GRAPH_JSON_VERSION` 4.11). One
entry per item key; `use_flag`, `effect_speffect` and `duration_ms` are `null`
where the table above says none.

```json
"race_items": {
  "hammer": {"name": "Hammer", "icon": "hammer", "goods_id": 1450807, "use_flag": 1050290107,
             "remove_flag": 1050290207, "effect_speffect": 1450827, "duration_ms": 15000}
}
```

## Replacing the icons

The 11 icons ship as placeholder flat shapes. The art is replaced by dropping
PNG files in, nothing else changes. For the graphic artist and for Roger:

- **Format**: PNG, 160x160, RGBA with a transparent background.
- **Files**: `data/plugins/race_items_icon_<icon>.png`, `<icon>` being a name of
  the catalog's `[icons]` table: `shield`, `rune_pouch`, `banana`, `red_shell`,
  `mushroom`, `hammer`, `ink`, `blue_shell`, `lightning`, `flask_stopper`,
  `toxic_blossom` (ids 60385-60395; the three Rune Pouches share one icon).
- **After the drop**: overwrite the files (never re-run
  `tools/generate_race_item_icons.py` afterwards, it would overwrite the art),
  re-run `tools/bootstrap.py` on the generation host (it also needs a re-run
  after the rename to `speedfog-icons`; `speedfog` refuses to generate a
  Halloween or race items seed while the overlay is missing or older than the
  icon PNGs or `race_items.toml`; a missing or non-160x160 file
  makes `ItemIconPatcher` skip the whole overlay with a warning), regenerate the
  pools, then in speedfog-racing run `tools/sync_race_item_icons.py` and release
  the mod.

The icons ship in the shared `data/mods/speedfog-icons/` overlay, together with
the Halloween icons (see [halloween-icons.md](halloween-icons.md)): one
`05_dummy.tpf.dcx`, shipped when Halloween or race items is enabled.

## Calibration values

Starting values Roger tunes in game, then bakes into the pools (effect values
are part of every seed): Hammer `equipWeightChangeRate = 0.35`, the Rune Pouch
amounts 10000 / 20000 / 40000. The Hammer value is a multiplier of the maximum
equip load, so it cannot put every build in the fat-roll band: at 0.35 a build
between 24.5 % and 35 % of its load fat-rolls, a heavier one is overloaded (a
32 / 49.8 build ends at 32 / 17.4 in game).

## In-game checks

How to run them, on a test seed without the racing mod: Cheat Engine with The
Grand Archives table (`ER_TGA_v1.17.0.CT`).

- `Scripts > Build Creation > ItemGib` gives an item: `Item` is
  `0x40000000 | goods id` in hex (`40162337` for the Hammer), then
  `> Spawn Item`. The practice tool's item spawner cannot give these items, its
  list is fixed.
- `Event Flags > Event Flags by ID` reads and writes a use or removal flag.
- `Scripts > Functions > ApplyEffect` applies an effect SpEffect (`SpEffectId`,
  then `> Add SpEffect`). In a race the effects land on the other runners, so
  this is the only way to see them alone.

First pass (seed 603810141, 2026-11-13):

- **Passed.**
  - **Inventory.** The item lands in a quick slot (with every quick slot full,
    it only goes to the inventory). It shows its small icon, name and caption.
    It is consumed on use and does not come back at a grace.
  - **Flags and pouches.** A removal flag removes the goods and clears itself.
    The Rune Pouches grant their runes and raise their use flag.
  - **Effects.** Flask Stopper (`stateInfo = 154`) blocks flasks. The Hammer
    effect works. Blue Shell halves max HP. Toxic Blossom poisons, and
    Neutralizing Boluses cure it.
- **Failed, fixed since.**
  - The Hammer's use flag stayed off: neutral markers now last 1 s.
  - The Mushroom left rolls and sprints costing stamina: its marker is now the
    Viridian Hidden Tear row.
  - Lightning (`stateInfo = 287`) slowed nothing: its effect is now cloned
    from 425.
- **Not fixed.** The large icon of the inventory's detail panel is blank, as
  for the Halloween icons. It is a separate texture,
  `MENU_Knowledge_<iconId>.tpf.dcx` inside `menu/{hi,low}/00_solo.tpfbhd`
  (whose `.tpfbdt` weighs 1.4 GB), which the `05_dummy` overlay does not
  provide.

To check on the next seed:

- the use flag of the Hammer and of the other neutral markers;
- the Mushroom over rolls and sprints, its use flag rising once, and its
  effect ending at 15 s (the clone keeps the tear's chain to 102361 and
  102363);
- Lightning: how slow, rolls included, its visual effect, and the effect
  ending at 15 s (the vanilla bullet row lasts 20 s);
- each Rune Pouch still raising its flag and granting its runes once.
