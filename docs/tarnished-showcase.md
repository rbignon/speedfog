# Tarnished Pack Showcase

**Date:** 2026-09-01
**Status:** Active. In-game validated 2026-09-01 (flag-to-skin mapping confirmed; loadout model revised the same day after the pass, see the contract section).

Pack-owner-only seeds that showcase the Tarnished Pack (ER 1.17 paid DLC):
every starting class carries pack equipment, the three Torrent regalia are
unlocked from the start, and a default Torrent skin can be pre-selected.
Driven by a dedicated `[tarnished]` config section, which also absorbs the
pack-ownership toggle previously living under `[item_randomizer]`.

Non-goals: forcing the class choice (player agency, and the class list is
exe-gated anyway), custom loadouts from user-supplied item lists (the
graph.json contract is shaped so a future feature could feed it), care
package changes (the operator zeroes its equipment categories in the mode's
config themselves).

## Config: `[tarnished]`

```toml
[tarnished]
enabled = false           # feeds opt["tarnished"]: pack items/invaders in the randomizer pools + non-owner startup error dialog
starting_loadout = false  # pack weapon on every class, the 2 shields on occupied left hands, armor per occupied slot
unlock_torrent_skins = false  # the 3 Spectral Steed Regalia goods given at start
default_torrent_skin = "" # "", "tree-sentinel", "carian-silver", "funereal-night", "random"
```

- `enabled` replaces the old `[item_randomizer] tarnished` key (added
  2026-08-31, removed the same week with no external users): a config that
  still carries `[item_randomizer] tarnished` fails strict validation with a
  dedicated message pointing at `[tarnished] enabled`.
  `generate_item_config()` reads `config.tarnished.enabled` for
  `opt["tarnished"]`.
- Validation rules (`TarnishedConfig.__post_init__` in `speedfog/config.py`):
  - `starting_loadout`, `unlock_torrent_skins`, and a non-empty
    `default_torrent_skin` each require `enabled = true`.
  - A non-empty `default_torrent_skin` also requires
    `unlock_torrent_skins = true` (forcing a skin without owning its regalia
    is untested engine behavior, so it is not expressible).
  - `default_torrent_skin` outside `{"", "tree-sentinel", "carian-silver",
    "funereal-night", "random"}` is an error.
- `"random"` resolves to one concrete skin on the Python side, deterministically per seed.
- Non-owner safety comes for free: with `opt["tarnished"]` on, the Item
  Randomizer v0.12 final already injects a startup error dialog (checking
  flag 6953) for players without the pack, so `[tarnished] enabled = true`
  alone is safe to ship; it is the sub-options that make the seed
  pack-owner-only in practice.

## graph.json v4.7 contract

Two OPTIONAL top-level fields, both absent when their features are off (a
seed without them behaves exactly as before). Both are mechanism-named, not
pack-named: `[tarnished]` is their first producer, a future mode (themed
seeds, racing rewards, custom loadouts) could feed the same fields. The
fields were added in v4.6; v4.7 reshaped `class_loadout` (the single
`hand_items` list gave some classes a shield INSTEAD of a weapon, revised
after the 2026-09-01 in-game pass).

```json
"class_loadout": {
  "weapons": [{"id": 3560000, "name": "Leontiel's Greatsword"}, ...],
  "shields": [{"id": 31540000, "name": "Silver Grooved Shield"}, ...],
  "armor_sets": [[5350000, 5350100, 5350200, 5350300], ...]
},
"torrent_skins": {"unlock": true, "default_flag": 6702}
```

- `weapons`: one shuffled permutation of the 6 pack weapons; `shields`: one
  shuffled permutation of the 2 pack shields; `armor_sets`: one shuffled
  permutation of the 4 sets as head/body/arms/legs quadruples. Display
  names ride along so C# stays dumb.
- The C# consumer (`ClassLoadoutInjector`) walks the starting class groups
  from `StartingClassRows.ResolveGroups` in menu order: every class gets
  `weapons[i % count]` in the right hand; each shield is placed exactly
  once, on the next class in order whose left-hand slot is occupied; the
  armor set `armor_sets[i % count]` replaces only the armor slots the class
  already fills. Coverage of the 6 weapons and 4 sets is guaranteed by the
  wrapping, and Python never needs to know the class list.
- `torrent_skins.unlock`: whether the three regalia goods are given at start
  (this also pre-sets the grace-ESD "attire announced" flag, see the C#
  section).
- `torrent_skins.default_flag`: the resolved event flag (6701-6703); absent
  or 0 means vanilla Torrent appearance.
- Version string is `"4.7"` (`speedfog/constants.py:GRAPH_JSON_VERSION`).
  The fields are additive; speedfog-racing is unaffected.

## Draw model

`speedfog/tarnished.py` owns the constants and the draws:

- `WEAPONS` (6) and `SHIELDS` (2): the pack hand items, each a
  `PackItem(id, name)`; the slot is implied by the pool.
- `ARMOR_SETS`: the 4 pack armor sets, each `[head, body, arms, legs]`.
- `SKIN_FLAGS`: `{"tree-sentinel": 6701, "carian-silver": 6702,
  "funereal-night": 6703}`, the single place to adjust the name-to-flag
  mapping (see "Verified 1.17 facts" below).
- `tarnished_rng(seed)` returns `random.Random(f"{seed}-tarnished")`: a
  dedicated RNG derived from the run seed, seeded independently from the
  care package's own `random.Random(seed)` in `care_package.py`, so the two
  draws never correlate.
- `build_class_loadout(rng)` returns copies of `WEAPONS`, `SHIELDS` and
  `ARMOR_SETS`, each independently shuffled with `rng.shuffle`. These are
  covering shuffles (every item/set used per permutation), not samples:
  the point is variety across classes, not selection.
- `build_torrent_skins(config, rng)` returns `None` when
  `unlock_torrent_skins` is false. Otherwise `{"unlock": True}`, plus a
  `"default_flag"` key when `default_torrent_skin` names a fixed skin or
  requests `"random"` (resolved here with `rng.choice`).

`speedfog/main.py` draws `class_loadout`/`torrent_skins` only when the
corresponding sub-option is on, passes them into `GraphExportOptions`, and
`graph_export.py` writes the two graph.json fields only when non-`None`. The
shuffled sequences and the resolved skin are also logged to the spoiler
(`export_spoiler_log`, `TARNISHED SHOWCASE` section) when `--logs` is passed.

## C# side

- `GraphLoader`/`GraphData` parse the two optional v4.7 fields into
  `ClassLoadoutData?` and `TorrentSkinsData?`; absent in the JSON means
  `null`, all existing behavior unchanged.
- `ClassLoadoutInjector` (`writer/FogModWrapper/ClassLoadoutInjector.cs`),
  pack-agnostic: for each class group from `StartingClassRows.ResolveGroups`
  (menu order), it ALWAYS overwrites `equip_Wep_Right` with the wrapped
  weapon; hands each shield once to the next class whose `equip_Wep_Left`
  is occupied (empty-slot sentinel is -1); and replaces only the occupied
  armor fields among `equip_Helm`/`equip_Armer`/`equip_Gaunt`/`equip_Leg`
  (Wretch stays bare; a class without gauntlets keeps none, like vanilla
  row 3008). Presence decisions read the first row of the group and writes
  land uniformly on every row (origin, chrInit, odd twin). For each hand
  slot written, the `wepParamType_Right1`/`wepParamType_Left1` companion is
  forced to 0 (EquipParamWeapon): CharacterWriter (merged item-randomizer
  output) may have left it at 1 (EquipParamCustomWeapon) from drawing an
  ash-of-war weapon into that same slot; left stale, it sends
  WeaponUpgradeInjector down the wrong path for a plain EquipParamWeapon
  ID. Every other field is left untouched, so CharacterWriter's own
  randomization still applies everywhere else. This is disjoint-fields
  co-residency on CharaInitParam alongside `StartingRuneInjector` (writes
  only `soul`); see `writer/FogModWrapper/RegulationEditor.cs`'s documented
  invariant.
- `ClassLoadoutTextPatcher` fixes the class-selection TEXT: the creation
  screen lists each class's gear as text that CharacterWriter wrote to
  `GR_LineHelp[297130 + classIndex]` (menu order, Vagabond..Heavy Knight) in
  every language's `menu.msgbnd.dcx`, built from the weapon names it drew.
  `ClassLoadoutInjector` records one `LoadoutSwap` per class (pre-write and
  forced weapon/shield ids); the patcher then replaces the old names with
  the new ones per language (`WeaponName` lookups from `item.msgbnd.dcx` +
  `item_dlc02.msgbnd.dcx` and the installed game's `item_dlc01`), the
  old stat-diff suffix is swallowed, an unlisted old name falls back to
  appending the new one, and the entry is re-wrapped to its original width.
  When `menu_dlc02.msgbnd.dcx` exists only in the merge dir (the Item Randomizer
  wrote it), it is copied into the FogMod output first (mods/fogmod wins
  the load order). Runs in Phase 8, after `CopyLocalizedFmgs`.
- Ordering constraint in `Program.cs` (Phase 7, batched regulation.bin
  edits): `ClassLoadoutInjector.ApplyTo` runs BEFORE
  `WeaponUpgradeInjector.ApplyTo`. This is a deliberate OVERLAP, not
  disjoint-fields co-residency: ClassLoadoutInjector writes the raw weapon
  IDs and zeroed type fields first, then WeaponUpgradeInjector reads those
  same fields and rewrites them with upgrade encoding, so the forced weapons
  go through the same weapon-upgrade initialization pass as any other
  starting weapon. Reversing the order would leave the drawn weapon IDs at
  their raw (unupgraded) form.
- Regalia: when `torrentSkins.Unlock` is true, `StartingItemInjector` gives
  the three regalia Good IDs (`RegaliaGoods = {2009600, 2009610, 2009620}`)
  inside its existing start-of-run delivery event, alongside the other
  starting goods and the care package. It also sets flag 69560
  (`TORRENT_ATTIRE_ANNOUNCED_FLAG`): the grace talk ESD (machine 2147483562
  of `t000001000`) shows the one-time "The spectral steed's appearance can
  now be changed" dialog at the first grace sit when the player owns a
  regalia and 69560 is OFF, then sets 69560 and adds the attire menu entry;
  with 69560 pre-set the menu entry appears directly and the popup never
  interrupts the run. Non-showcase seeds are untouched: a pack owner picking
  regalia up organically keeps the vanilla announce.
- Default skin one-shot guard: when `torrentSkins.DefaultFlag > 0`,
  `StartingItemInjector` first appends a `SetEventFlag(6700, OFF)`
  instruction, then `SetEventFlag(DefaultFlag, ON)`, right before the event
  sets its own `ITEMS_GIVEN_FLAG` guard (`SpeedFogIds.ItemsGivenFlag`).
  Vanilla `common.emevd` event 780 sets flag 6700 (vanilla appearance) when
  none of 6700-6703 is on, and it has already run by the time this delivery
  event fires on a fresh save; clearing 6700 restores the engine's
  exactly-one-of-6700-6703 invariant instead of leaving both 6700 and the
  configured default flag on. Because the `ITEMS_GIVEN_FLAG` guard already
  makes the whole event a one-shot (it bails out on re-entry once the flag
  is set), the clear-and-set pair rides that same one-shot for free: it
  fires exactly once per save, so the player's later choice in the grace
  "Torrent attire" menu is never overwritten by a reload.

## Verified 1.17 facts

- 8 hand items, all `EquipParamWeapon`: 6 right-hand weapons (3560000
  Leontiel's Greatsword, 8530000 Hefty Scimitar, 13510000 Golden Order
  Flail, 64530000 Reverse-Bladed Sword, 66530000 Reed Great Katana, 67530000
  Idus Sword) and 2 left-hand shields (31540000 Silver Grooved Shield,
  62520000 Ritual Thrusting Shield). See `speedfog/tarnished.py:WEAPONS`/`SHIELDS`.
- 4 coherent armor sets, all `EquipParamProtector`: Gold Tattoo with Broken
  Gold Mask (5340000/5340100/5340200/5340300), Silver Grooved
  (5350000/5350100/5350200/5350300), Leontiel's
  (5360000/5360100/5360200/5360300), Steel
  (5370000/5370100/5370200/5370300). The two "Altered" variants are not
  used. See `speedfog/tarnished.py:ARMOR_SETS`.
- Torrent skins are goods items ("Spectral Steed Regalia"): 2009600 Tree
  Sentinel, 2009610 Carian Silver, 2009620 Funereal Night.
- Skin selection is flag-driven: `common.emevd` event 780 (1.17) tests flags
  6700-6703 and sets 6700 (vanilla appearance) if none is on. 6701-6703 map
  to the three regalia in that order (`SKIN_FLAGS` in
  `speedfog/tarnished.py`); confirmed in-game 2026-09-01 (`carian-silver`
  applied the Carian Silver attire). A second flag family (4828-4830) is
  set by the grace ESD's selection submenu; leave it to the ESD.
- The first-grace announce dialog (`EventTextForTalk` 20011070) is guarded
  by flag 69560, read and set by grace-ESD machine 2147483562 (see the C#
  section for the suppression).

## E2e evidence (2026-09-01)

- Showcase seed `924231136` (`[tarnished] enabled/starting_loadout/
  unlock_torrent_skins = true`, `default_torrent_skin = "carian-silver"`):
  the CharaInitParam class rows dumped from the output regulation.bin match
  the shuffled draws logged in the spoiler; the regalia goods and the
  default-skin flag both appear in the `755860000` starting-items event;
  injector ordering was proven directly by setting `weapon_upgrade = 8` and
  observing the drawn weapon ID land upgraded (`64530000` -> `64530008`)
  rather than raw.
- Non-regression seed `604193649` (no `[tarnished]` section): clean, no
  `ClassLoadoutInjector` output, no regalia/flag instructions, generation
  green.

## In-game validation (operator, still owed)

- Flag-to-skin mapping: DONE 2026-09-01 (`carian-silver` -> Carian Silver).
- Class-selection screen shows the pack loadout: a pack weapon in the right
  hand of EVERY class; the two shields on the first two classes with an
  occupied left hand; armor only on slots the class fills (Wretch stays
  bare, a class without gauntlets keeps none).
- No "spectral steed's appearance" dialog at the first grace sit; the
  attire entry is present in the grace menu directly.
- The class-selection screen's equipment TEXT names the forced pack gear
  (not CharacterWriter's picks), in the game language used.
- Forced weapons are upgraded (matches `care_package.weapon_upgrade`); for a
  class whose randomized slot held an ash-of-war weapon before the loadout
  overwrote it, confirm the forced pack weapon still carries the upgrade
  (the `wepParamType` reset case: a stale companion field would have sent
  `WeaponUpgradeInjector` down the EquipParamCustomWeapon path instead).
- The default skin applies on a new save (the injector clears vanilla flag
  6700 and sets the default flag in the same one-shot; flags 6700-6703
  should show exactly one ON), AND a manual skin change at the grace
  "Torrent attire" menu survives a reload (proves the one-shot guard does
  not fight the player's later choice).
- One pack-owner pass (full showcase) and one non-owner pass (only needs to
  hit the Item Randomizer's startup error dialog).

## Files

| File | Role |
|------|------|
| `speedfog/tarnished.py` | Constants (`WEAPONS`, `SHIELDS`, `ARMOR_SETS`, `SKIN_FLAGS`), `tarnished_rng`, `build_class_loadout`, `build_torrent_skins` |
| `speedfog/config.py` | `TarnishedConfig` dataclass, validation rules, the `[item_randomizer] tarnished` migration error |
| `speedfog/main.py` | Draws the showcase and feeds `GraphExportOptions` |
| `speedfog/graph_export.py` | Serializes `class_loadout`/`torrent_skins` into graph.json v4.7 |
| `speedfog/spoiler.py` | `TARNISHED SHOWCASE` spoiler section |
| `speedfog/item_randomizer.py` | Reads `config.tarnished.enabled` for `opt["tarnished"]` |
| `tests/test_tarnished.py` | Python tests: config validation, draws, graph export |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | `ClassLoadoutData`, `PackItemData`, `TorrentSkinsData` |
| `writer/FogModWrapper.Core/GraphLoader.cs` | Parses the two v4.7 fields |
| `writer/FogModWrapper/StartingClassRows.cs` | `ResolveGroups`: per-class CharaInitParam row groups from `BaseChrSelectMenuParam` |
| `writer/FogModWrapper/ClassLoadoutInjector.cs` | Writes weapons/shields + armor onto CharaInitParam per class group |
| `writer/FogModWrapper/StartingItemInjector.cs` | Regalia delivery + attire-announced flag 69560 + one-shot default-skin flag |
| `writer/FogModWrapper/ClassLoadoutTextPatcher.cs` | Class-selection equipment text (GR_LineHelp) updated to the forced gear, per language |
| `writer/FogModWrapper/Program.cs` | Phase 7 ordering: `ClassLoadoutInjector` before `WeaponUpgradeInjector` |
| `writer/FogModWrapper.Tests/ClassLoadoutInjectorTests.cs` | Slot writes, modulo wrap, armor fields, untouched other fields |
| `writer/FogModWrapper.Tests/StartingClassRowsTests.cs` | Group resolution from `BaseChrSelectMenuParam` |

See also `docs/care-package.md` for the starting-build pipeline this feature
sits alongside, and `docs/item-randomizer.md` for the `opt["tarnished"]`
randomizer flag and non-owner error dialog.
