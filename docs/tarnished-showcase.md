# Tarnished Pack Showcase

**Date:** 2026-09-01
**Status:** Active. Flag-to-skin mapping (`SKIN_FLAGS`) still awaits an in-game confirmation pass; see below.

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
starting_loadout = false  # pack hand item + full pack armor set on every starting class (covering shuffles)
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

## graph.json v4.6 contract

Two OPTIONAL top-level fields, both absent when their features are off (a
seed without them behaves exactly as before). Both are mechanism-named, not
pack-named: `[tarnished]` is their first producer, a future mode (themed
seeds, racing rewards, custom loadouts) could feed the same fields.

```json
"class_loadout": {
  "hand_items": [{"id": 3560000, "slot": "right", "name": "Leontiel's Greatsword"}, ...],
  "armor_sets": [[5350000, 5350100, 5350200, 5350300], ...]
},
"torrent_skins": {"unlock": true, "default_flag": 6702}
```

- `hand_items`: one shuffled permutation of the 8 pack hand items, each
  annotated with its slot (weapons right, shields left) and display name so
  C# stays dumb. `armor_sets`: one shuffled permutation of the 4 sets as
  head/body/arms/legs quadruples.
- The C# consumer (`ClassLoadoutInjector`) walks the starting class groups
  from `StartingClassRows.ResolveGroups` in menu order and applies
  `seq[i % len]` to each: the hand-item index and the armor-set index wrap
  independently against their own list length, so full coverage of the 8
  hand items and the 4 armor sets is guaranteed by construction and Python
  never needs to know the class list.
- `torrent_skins.unlock`: whether the three regalia goods are given at start.
- `torrent_skins.default_flag`: the resolved event flag (6701-6703); absent
  or 0 means vanilla Torrent appearance.
- Version string is `"4.6"` (`speedfog/constants.py:GRAPH_JSON_VERSION`).
  The fields are additive; speedfog-racing is unaffected.

## Draw model

`speedfog/tarnished.py` owns the constants and the draws:

- `HAND_ITEMS`: the 8 pack hand items (6 right-hand weapons, 2 left-hand
  shields), each a `HandItem(id, slot, name)`.
- `ARMOR_SETS`: the 4 pack armor sets, each `[head, body, arms, legs]`.
- `SKIN_FLAGS`: `{"tree-sentinel": 6701, "carian-silver": 6702,
  "funereal-night": 6703}`, the single place to adjust the name-to-flag
  mapping (see "Verified 1.17 facts" below).
- `tarnished_rng(seed)` returns `random.Random(f"{seed}-tarnished")`: a
  dedicated RNG derived from the run seed, seeded independently from the
  care package's own `random.Random(seed)` in `care_package.py`, so the two
  draws never correlate.
- `build_class_loadout(rng)` returns a copy of `HAND_ITEMS` and `ARMOR_SETS`,
  each independently shuffled with `rng.shuffle`. These are covering
  shuffles (every item/set used exactly once per permutation), not samples:
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

- `GraphLoader`/`GraphData` parse the two optional v4.6 fields into
  `ClassLoadoutData?` and `TorrentSkinsData?`; absent in the JSON means
  `null`, all existing behavior unchanged.
- `ClassLoadoutInjector` (`writer/FogModWrapper/ClassLoadoutInjector.cs`),
  pack-agnostic: for each class group from `StartingClassRows.ResolveGroups`
  (menu order), overwrites `equip_Wep_Right` or `equip_Wep_Left` (whichever
  slot the drawn hand item names), that field's `wepParamType_Right1` or
  `wepParamType_Left1` companion (forced to 0, EquipParamWeapon), and the
  four armor fields (`equip_Helm`/`equip_Armer`/`equip_Gaunt`/`equip_Leg`) on
  every CharaInitParam row of that group. The type-field reset matters
  because CharacterWriter (merged item-randomizer output) may have left it
  at 1 (EquipParamCustomWeapon) from drawing an ash-of-war weapon into that
  same slot; left stale, it sends WeaponUpgradeInjector down the wrong path
  for a plain EquipParamWeapon ID. Every other field is left untouched, so
  CharacterWriter's own randomization still applies everywhere else. This is
  disjoint-fields co-residency on CharaInitParam alongside
  `StartingRuneInjector` (writes only `soul`); see
  `writer/FogModWrapper/RegulationEditor.cs`'s documented invariant.
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
  starting goods and the care package.
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
  62520000 Ritual Thrusting Shield). See `speedfog/tarnished.py:HAND_ITEMS`.
- 4 coherent armor sets, all `EquipParamProtector`: Gold Tattoo with Broken
  Gold Mask (5340000/5340100/5340200/5340300), Silver Grooved
  (5350000/5350100/5350200/5350300), Leontiel's
  (5360000/5360100/5360200/5360300), Steel
  (5370000/5370100/5370200/5370300). The two "Altered" variants are not
  used. See `speedfog/tarnished.py:ARMOR_SETS`.
- Torrent skins are goods items ("Spectral Steed Regalia"): 2009600 Tree
  Sentinel, 2009610 Carian Silver, 2009620 Funereal Night.
- Skin selection is flag-driven: `common.emevd` event 780 (1.17) tests flags
  6700-6703 and sets 6700 (vanilla appearance) if none is on. 6701-6703 are
  assumed to map to the three regalia in that order (`SKIN_FLAGS` in
  `speedfog/tarnished.py`); **this mapping is PENDING in-game confirmation**.
  If the in-game skin does not match the configured name, only that one
  constants table needs adjusting.

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

- Flag-to-skin mapping: set `default_torrent_skin = "carian-silver"`,
  observe Torrent's actual appearance in-game, adjust `SKIN_FLAGS` in
  `speedfog/tarnished.py` if the observed skin does not match.
- Class-selection screen shows the pack loadout (weapon in hand, armor worn)
  on every class.
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
| `speedfog/tarnished.py` | Constants (`HAND_ITEMS`, `ARMOR_SETS`, `SKIN_FLAGS`), `tarnished_rng`, `build_class_loadout`, `build_torrent_skins` |
| `speedfog/config.py` | `TarnishedConfig` dataclass, validation rules, the `[item_randomizer] tarnished` migration error |
| `speedfog/main.py` | Draws the showcase and feeds `GraphExportOptions` |
| `speedfog/graph_export.py` | Serializes `class_loadout`/`torrent_skins` into graph.json v4.6 |
| `speedfog/spoiler.py` | `TARNISHED SHOWCASE` spoiler section |
| `speedfog/item_randomizer.py` | Reads `config.tarnished.enabled` for `opt["tarnished"]` |
| `tests/test_tarnished.py` | Python tests: config validation, draws, graph export |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | `ClassLoadoutData`, `HandItemData`, `TorrentSkinsData` |
| `writer/FogModWrapper.Core/GraphLoader.cs` | Parses the two v4.6 fields |
| `writer/FogModWrapper/StartingClassRows.cs` | `ResolveGroups`: per-class CharaInitParam row groups from `BaseChrSelectMenuParam` |
| `writer/FogModWrapper/ClassLoadoutInjector.cs` | Writes hand items + armor onto CharaInitParam per class group |
| `writer/FogModWrapper/StartingItemInjector.cs` | Regalia delivery + one-shot default-skin flag |
| `writer/FogModWrapper/Program.cs` | Phase 7 ordering: `ClassLoadoutInjector` before `WeaponUpgradeInjector` |
| `writer/FogModWrapper.Tests/ClassLoadoutInjectorTests.cs` | Slot writes, modulo wrap, armor fields, untouched other fields |
| `writer/FogModWrapper.Tests/StartingClassRowsTests.cs` | Group resolution from `BaseChrSelectMenuParam` |

See also `docs/care-package.md` for the starting-build pipeline this feature
sits alongside, and `docs/item-randomizer.md` for the `opt["tarnished"]`
randomizer flag and non-owner error dialog.
