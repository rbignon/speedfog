# Unique Item Neutralization

**Date:** 2026-10-04
**Status:** Active (in-game verification pending)

Why and how SpeedFog replaces world copies of start-granted, possession-capped
goods with Rune Arcs.

## Problem

Starting items and the care package grant goods the player can only hold a
limited number of (`EquipParamGoods.maxNum`). The Item Randomizer still places
those goods in the world, and our item presets deliberately route several of
them to key/boss locations (`CoreMechanics` lists Talisman Pouch and
`Group: crystaltears`). A pickup of a good already held at cap is dead: the
corpse lot stays untakeable, a boss drop is wasted. On racing seeds that turns
prime reward slots into nothing.

Measured scope per seed with default config: 5 care package crystal tears
(half of the curated pool, `maxNum 1`), 3 Talisman Pouches (`maxNum 3`, three
granted), 5 whetblades (`maxNum 1`, unconstrained by the presets so they land
anywhere), plus the norandom key items (Physick, Lantern, Academy Key...)
sitting dead at their vanilla slots when their zone is in the DAG.

Items verified NOT to collide (2026-10-04, vanilla 1.17 regulation):

| Category | Why it is fine |
|----------|----------------|
| Sorceries / incantations | `maxNum 99`, duplicates stack |
| Talismans (care package) | `EquipParamAccessory` has no possession cap field, duplicates are held |
| Weapons, armor, catalysts, shields | No cap |
| Golden Seeds, Sacred Tears, Larval Tears, Stonesword Keys | Stackable (`maxNum` 99/12/99/99); the generic rule still covers a config that grants a full stack |
| Great Runes | We grant the restored items (191-196); the randomizer places the unrestored ones (8148-8153, 10080), different IDs the player does not own |

## Mechanism

`UniqueItemNeutralizer` (FogModWrapper, `RegulationPhase`, inside the shared
`RegulationEditor` batch):

1. `CollectGrantedGoods` counts every good granted at start for this seed:
   `graph.json` `starting_goods` (one entry per copy, so three pouches count
   as 3), care package items with `type == 3` (Goods), the four starting
   resource counts, and the three Torrent regalia goods (2009600/10/20) when
   `torrent_skins.unlock` is on (showcase seeds; their world copies sit in
   Tarnished Pack-gated chests, dead for pack owners).
2. `ComputeSaturatedGoods` keeps the goods whose granted count reaches their
   `EquipParamGoods.maxNum`. Goods without a param row, with a non-positive
   `maxNum`, or the Rune Arc itself are skipped.
3. Every `ItemLotParam_map` / `ItemLotParam_enemy` slot (`lotItemCategory0X
   == 1` = goods, `lotItemId0X` saturated) is rewritten to Rune Arc
   (`goods:190`), keeping the slot's category, quantity, drop weight and the
   lot's `getItemFlagId`. Every `ShopLineupParam` goods row (`equipType ==
   3`) selling a saturated good is rewritten to sell a Rune Arc at the same
   price and stock flag.
4. Two repeatability guards keep the swap from minting renewable Rune Arc
   sources: `ItemLotParam_enemy` rows with no acquisition flag at all
   (row-level and per-slot `getItemFlagId` zero, i.e. a respawning enemy's
   farmable drop) and unlimited-stock shop rows (`sellQuantity` -1) are
   skipped and logged. Neither occurs for true uniques; the guards matter
   for full-stack resource grants (e.g. 99 Stonesword Keys), where a
   repeatable source becomes useful again as the player spends the stack.
   Flagless `ItemLotParam_map` rows are NOT skipped: the vanilla 999xxxx
   range (Physick, Erdtree avatar crystal tear awards...) carries no lot
   flag because those lots are awarded by one-shot EMEVD events
   (`AwardItemLot`), whose own flag enforces one-shot-ness; they are real
   dead pickups. Verified on a seed: guarding them left 3 dead scripted
   awards (rows 9990260/9990300/9990310).

The pass runs on the merged regulation, so with the Item Randomizer active it
rewrites the randomizer's placements (including the key/boss slots), and
without it the vanilla lots. Rune Arc was chosen as the replacement because it
is stackable, already an established key-slot reward
(`item_preset_without_weapons.yaml` "Boss consumables"), and pinned in shops
by the randomizer's `norandomshop` group, so the swap never fights the
randomizer.

## Design notes

- **Saturation uses `maxNum` only, ignoring `maxRepositoryNum`.** Crystal
  tears have a storage cap of 1, so a duplicate pickup might technically land
  in the storage box instead of being refused; either way the slot is a
  wasted reward, so it is replaced.
- **The randomizer's own spoiler log is not shipped** (only SpeedFog's
  `spoiler.txt`, which lists zones and bosses), so the post-hoc swap
  contradicts no player-facing document.
- **Unrestored Great Runes are left alone by construction**: they never match
  the granted list, and the randomizer's key item logic still needs them
  placed.
- **Replacement is ID-exact and category-checked**: a non-goods lot slot or
  shop row with the same numeric ID is never touched, and rows added by
  `ShopInjector` (smithing stones, Sentry's Torch) never match a granted
  good.
- An alternative was assessed and rejected: a per-seed generated item preset
  using the randomizer's `RemoveItems` section. Cleaner on paper (the items
  never exist), but `RemoveItems` ignores items covered by a placement that
  excludes `missable` (`AnnotationData.cs:655`), which CoreMechanics does, so
  Python would have to expand and edit the preset's item groups per seed:
  more coupling to randomizer internals for the same outcome.

## In-game checks owed

- A swapped boss drop awards a Rune Arc normally.
- A swapped ground lot is pickable (one-shot flag behaves).
- A finite shop row that received a swap sells a Rune Arc at the old price.
- Baseline confirmation of the premise: what a duplicate crystal tear pickup
  does on an unpatched seed (refused vs sent to storage).

## References

- Injector: `writer/FogModWrapper/Start/UniqueItemNeutralizer.cs`
- Tests: `writer/FogModWrapper.Tests/UniqueItemNeutralizerTests.cs`
- Granted goods source: `speedfog/config.py` `get_starting_goods()`,
  `speedfog/care_package.py`
- Starting item delivery (no ItemLotParam involvement, so the swap can never
  break it): `docs/starting-items.md`
- Preset routing of tears/pouches to key slots: `data/item_preset.yaml`,
  `docs/item-randomizer.md` "Item Preset Placement"
