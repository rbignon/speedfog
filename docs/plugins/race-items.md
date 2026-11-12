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
| 4 | `banana` | Banana | 1450804 | 10600 | none | none |
| 5 | `red_shell` | Red Shell | 1450805 | 10600 | none | none |
| 6 | `mushroom` | Mushroom | 1450806 | 10600, `consumeStaminaRate = 0`, `effectEndurance = 15` | none (the marker is the effect) | 15 s |
| 7 | `hammer` | Hammer | 1450807 | 10600 | 1450827, 10600, `equipWeightChangeRate = 0.35`, `effectEndurance = 15` | 15 s |
| 8 | `ink` | Ink | 1450808 | 10600 | none (overlay only) | 15 s |
| 9 | `blue_shell` | Blue Shell | 1450809 | 10600 | 1450829, 10600, `maxHpRate = 0.5`, `effectEndurance = 15` | 15 s |
| 10 | `lightning` | Lightning | 1450810 | 10600 | 1450830, 10600, `stateInfo = 287`, `effectEndurance = 15` | 15 s |
| 11 | `flask_stopper` | Flask Stopper | 1450811 | 10600 | 1450831, 10600, `stateInfo = 154`, `effectEndurance = 15` | 15 s |
| 12 | `toxic_blossom` | Toxic Blossom | 1450812 | 10600 | 1450832, 3370, `poizonAttackPower = 99999` | none |

Templates: 10600 is FogRando's Bell of Return marker (neutral, every rate at
1, self-targeted; `GameDataWriterE.cs:3857-3871`), 3269 is Golden Rune [1]
(`soul = 200`), 3370 is the poison row the randomizer's trap copies.

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
are part of every seed): Hammer `equipWeightChangeRate = 0.35`, Lightning
`stateInfo = 287` (286 is the alternative), the Rune Pouch amounts 10000 /
20000 / 40000.

## In-game checks

On one test seed with the plugin enabled:

- An item given by `func_item_inject` lands in a quick slot (auto-equip), as a
  picked-up flask does; and what happens when every quick slot is full.
- Every item shows its icon, name and caption. The icon route relies on the
  texture-name fallback that [halloween-icons.md](halloween-icons.md) still
  lists as not verified in game: if icons render blank, see that doc's
  fallbacks.
- The slot empties when the single item is used.
- Use an item, rest at a grace: it does not come back (the goods rows clear
  `isFullSuppleItem`, `suppleType` and the Physick tutorial flag they inherit
  from goods 250).
- The use flag is set once per use, Mushroom included.
- A removal flag removes the goods and clears itself.
- `stateInfo` 287 and 286 (Lightning) and 154 (Flask Stopper) do what their
  names say.
- Hammer 0.35 makes a light build fat-roll, and what a medium build gets.
- Blue Shell (`maxHpRate = 0.5`) halves max HP and caps current HP without
  killing.
- Toxic Blossom poisons the player, and whether a bolus cures it is open: its
  effect row clones SpEffect 3370, the vanilla poison status (`effectEndurance`
  60, buildup re-applied every second for 60 s), so a bolus may or may not clear
  it.
- A Rune Pouch raises its use flag (its marker template is 3269, Golden Rune
  [1], not the Bell-proven 10600).
- Each Rune Pouch grants its runes (10000 / 20000 / 40000).
- The Mushroom removes the stamina cost for 15 s.
