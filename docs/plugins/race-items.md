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
| 6 | `mushroom` | Mushroom | 1450806 | 10600, `effectEndurance = 1`, cycles 102360 every 0.5 s | none (the marker applies it) | 15 s |
| 7 | `hammer` | Hammer | 1450807 | 10600, `effectEndurance = 1` | 1450827, 10600, `equipWeightChangeRate = 0.35` (rewritten per target by the racing mod), `effectEndurance = 15` | 15 s |
| 8 | `ink` | Ink | 1450808 | 10600, `effectEndurance = 1` | none (overlay only) | 15 s |
| 9 | `blue_shell` | Blue Shell | 1450809 | 10600, `effectEndurance = 1` | 1450829, 10600, `maxHpRate = 0.5`, `effectEndurance = 15` | 15 s |
| 10 | `lightning` | Lightning | 1450810 | 10600, `effectEndurance = 1` | 1450830, 10600, `effectEndurance = 15`, cycles 4101 every 0.06 s | 15 s |
| 11 | `flask_stopper` | Flask Stopper | 1450811 | 10600, `effectEndurance = 1` | 1450831, 10600, `stateInfo = 154`, `effectEndurance = 15` | 15 s |
| 12 | `toxic_blossom` | Toxic Blossom | 1450812 | 10600, `effectEndurance = 1` | 1450832, 3370, `poizonAttackPower = 99999` | none |

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
item's row lasts, as vanilla row 20011920 does with 4101. The Mushroom marker
lasts 1 s and cycles 102360 every 0.5 s; 102360 lasts 15 s from its last
application, so about 15 to 16 s in all. The Lightning effect cycles 4101
(0.1 s each) for 15 s.

Neutral markers set `effectEndurance = 1` rather than the template's one-frame
0, a margin for the use event; every use flag rises in game.

Dead ends from the in-game passes: `consumeStaminaRate` only scales the stamina
cost of weapon attacks; `stateInfo` 286 / 287 ("movement slow" in the
community enum) slowed nothing, and no row of the 1.17 regulation uses them;
copies of 102360 and 425 kept their param fields but not the c0000.hks
behaviors.

## Events

Both shapes are adapted from FogRando's `common_bellofreturn`
(`data/fogevents.txt:389-406`) and restart themselves when they end.

- **Use event** (one per usable item, id `755865700 + n`): wait for the
  marker SpEffect on the player, set the use flag, wait for the marker to
  end, restart, so one use is one flag raise.
- **Removal event** (one per item, id `755865750 + n`): wait for the removal
  flag, remove one of the goods from the player, clear the flag, restart. The
  racing mod raises the flag; the event clears it, so the flag is a one-shot
  request.

Items with an effect SpEffect (Hammer, Blue Shell, Lightning, Flask Stopper,
Toxic Blossom) are not applied by an event: when the server resolves a use,
the target's racing mod applies the effect SpEffect itself. The Mushroom and
the Rune Pouches take effect through the game's own use: the Pouch marker is
the effect, the Mushroom marker applies 102360.

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

## Icons

The 11 icons are images downloaded at bootstrap, not files of the repository.
`tools/fetch_race_item_icons.py` lists one source per icon (`ICON_SOURCES`:
URL and SHA-256) and downloads each into
`data/plugins/race_items_icon_<icon>.png` (gitignored), `<icon>` being a name
of the catalog's `[icons]` table: `shield`, `rune_pouch`, `banana`,
`red_shell`, `mushroom`, `hammer`, `ink`, `blue_shell`, `lightning`,
`flask_stopper`, `toxic_blossom` (ids 60385-60395; the three Rune Pouches
share one icon).

- **When**: `tools/bootstrap.py` downloads them right before StaticModBuilder;
  an icon already on disk with the expected hash is not downloaded again. A
  failed download, or a file whose hash differs (the source changed), leaves
  that icon's file as it was: the overlay is then built from the previous art,
  or skipped when the file is missing. The rest of the bootstrap goes on (it
  does not need the icons) and its last line says the icons are not ready;
  rerun the bootstrap once the download works. `python
  tools/fetch_race_item_icons.py` alone only refreshes the PNGs, not the
  overlay (StaticModBuilder builds it, see halloween-icons.md for a direct
  run).
- **Format**: any PNG whose longer side is at least 160 pixels, ideally with
  a transparent background. `ItemIconPatcher` crops each one to its visible
  pixels and centers them on a transparent square with a margin, their longer
  side spanning 80% of it (`RACE_ICON_FILL`: the downloaded art fills its
  whole canvas, which the game crops when drawn edge to edge; the former
  placeholders spanned 71-90%, median 78%), then derives every in-game
  texture from it by resizing. A missing or unreadable file, or one too
  small, makes `ItemIconPatcher` skip the whole overlay with a warning.
- **Changing an icon**: put the new URL and its SHA-256 (`sha256sum` of the
  downloaded file) in `ICON_SOURCES`, re-run `tools/bootstrap.py` on the
  generation host and regenerate the pools. `speedfog` refuses to generate a
  Halloween or race items seed while the overlay is missing or older than the
  icon PNGs or `race_items.toml`, and a race items seed while an icon of the
  catalog is missing (a host that pulled without re-running the bootstrap). speedfog-racing's own
  `tools/fetch_race_item_icons.py` lists the same images for the racing mod's
  overlay: change both, then release the mod.

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
or race items is enabled. With the downloaded art it weighs 0.3 MB per seed
(153 KB per variant, 2026-11-14; 83 KB with the former flat placeholders:
the DCX compression of the BC7 textures depends on the art).

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
  `0x40000000 | goods id` in hex (`40162337` for the Hammer), then
  `> Spawn Item`. The practice tool's item spawner cannot give these items, its
  list is fixed.
- `Event Flags > Event Flags by ID` reads and writes a use or removal flag.
  Its `State` is read from the game only when the `Event Flag ID` changes: type
  (or re-type) the ID after the action, or `State` shows a stale value. The
  `Examples > Print Flags in Range` script reads them live.
- `Scripts > Functions > ApplyEffect` applies an effect SpEffect (`SpEffectId`,
  then `> Add SpEffect`). In a race the effects land on the other runners, so
  this is the only way to see them alone.

Passes on seed 603810141, 2026-11-13:

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
  Icons). A test build showed it resolves by name like the small
  icon; the 1024x1024 build was checked in game (seed v7, 2026-11-13), the
  shipped 160x160 one is still to check.

To check on the next seed:

- the large icon of every item in the inventory's detail panel;
- the Mushroom: buff icon, how long rolls stay free, and no flicker or repeated
  sound from the 102360 re-applications; a second Mushroom during the first;
- Lightning: its effect ending at 15 s, and no hitch over its 250 re-applications
  of 4101;
- each Rune Pouch still raising its flag and granting its runes once.
