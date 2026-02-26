# Starting Item Injection

**Date:** 2026-02-26
**Status:** Active

How SpeedFog gives key items, resources, and care package gear at game start.

## Overview

Two injectors write events into `common.emevd.dcx` that fire once when the player picks up the Tarnished's Wizened Finger (flag `1040292051`). Each uses a one-shot flag to prevent re-giving on reload.

| Injector | Event ID | Gift Flag | Contents |
|----------|----------|-----------|----------|
| `StartingItemInjector` | 755860000 | 1040299001 | Key items, Great Runes, care package |
| `StartingResourcesInjector` | 755861000 | 1040299000 | Runes, seeds, tears, keys |

Both events are registered in Event 0 via `InitializeEvent` (bank 2000, id 0).

```
New Game → Pick up Wizened Finger → flag 1040292051 ON
                                          │
                              ┌───────────┴───────────┐
                              ▼                       ▼
                     Event 755861000            Event 755860000
                     (Resources)               (Items + Care Package)
                              │                       │
                              ▼                       ▼
                     flag 1040299000 ON        flag 1040299001 ON
                     (never re-give)           (never re-give)
```

## Item Types and Limitations

Items are given with `DirectlyGivePlayerItem` (EMEDF instruction 2003:43). The EMEDF `ItemType` enum supports four types:

| Value | Name | Param Table |
|-------|------|-------------|
| 0 | Weapon | EquipParamWeapon |
| 1 | Armor | EquipParamProtector |
| 2 | Ring | EquipParamAccessory (talismans) |
| 3 | Goods | EquipParamGoods (consumables, spells, keys) |

**Type >= 4 (Gem / Ash of War) is not supported.** `DirectlyGivePlayerItem` with type 4 produces a broken `?WeaponName?` item. `AwardItemLot` with ItemLotParam category 4 reinterprets gems as weapons and gives the wrong item. Care package items with `type >= 4` are skipped by the injector and logged as "runtime-spawned by mod."

## Auxiliary Flags -- Whetblades

`DirectlyGivePlayerItem` puts items in inventory but does **not** set the game's mechanic-unlock flags. The Ashes of War infusion menu checks these flags (not inventory) to determine which affinities are available:

| Good ID | Whetblade | Aux Flag | Affinities Unlocked |
|---------|-----------|----------|---------------------|
| 8970 | Iron Whetblade | 65610 | Heavy, Keen, Quality |
| 8971 | Red-Hot Whetblade | 65640 | Fire, Flame Art |
| 8972 | Sanctified Whetblade | 65660 | Lightning, Sacred |
| 8973 | Glintstone Whetblade | 65680 | Magic, Cold |
| 8974 | Black Whetblade | 65720 | Poison, Blood, Occult |

`StartingItemInjector` emits a `SetEventFlag` immediately after each `DirectlyGivePlayerItem` for matching Good IDs. Without this, the player has the whetblades in inventory but the infusion menu shows no additional affinities.

Source: Item Randomizer `itemevents.txt` event 1450 / `CharacterWriter.cs:2447-2449`.

## Auxiliary Flags -- Great Runes

Flag `182` controls two vanilla mechanics:
- Sending gates (e.g., Deeproot Depths to Leyndell, event 12032500 in `fogevents.txt`)
- Leyndell capital barrier

The vanilla game sets flag 182 only when the player activates 2+ Great Runes at Divine Towers. `DirectlyGivePlayerItem` with restored Great Rune Good IDs (191-196) bypasses Divine Towers entirely, so flag 182 is never set.

`StartingItemInjector` counts how many restored Great Runes are in the goods list. If >= 2, it emits `SetEventFlag(182, ON)` so vanilla gate checks pass.

## Starting Resources (StartingResourcesInjector)

Consumable resources are given as individual `DirectlyGivePlayerItem` calls (one per item instance).

| Resource | Good ID | Value | Max | Notes |
|----------|---------|-------|-----|-------|
| Lord's Rune | 2919 | 50,000 runes each | 200 (10M runes) | `starting_runes` config value is converted via ceiling division |
| Golden Seed | 10010 | +1 flask use | 99 | Upgrade flask charges at graces |
| Sacred Tear | 10020 | Flask potency upgrade | 12 | Vanilla max meaningful count |
| Larval Tear | 8185 | 1 rebirth | 99 | Used for stat reallocation at graces |
| Stonesword Key | 8000 | Unlock 1 imp seal | 99 | Opens optional sealed areas |

Rune conversion: `starting_runes` (raw value) is converted to Lord's Runes via `ResourceCalculations.ConvertRunesToLordsRunes()` using ceiling division (`(runes + 49999) / 50000`). All values are clamped before injection; warnings are logged if clamped.

## Gift Tracking Flags

| Flag | Category | Purpose |
|------|----------|---------|
| 1040299000 | 1040299 | Resources already given (StartingResourcesInjector) |
| 1040299001 | 1040299 | Items already given (StartingItemInjector) |

Both flags are in category 1040299, pre-allocated by FogRando. The events check `EndIfEventFlag(End, ON, ...)` immediately after the finger pickup wait, so items are given exactly once even across save/reload cycles.

## Care Package Integration

When `[care_package]` is enabled in config, the Python layer samples items from `data/care_package_items.toml` per category (weapons, shields, catalysts, talismans, sorceries, incantations, armor, crystal tears, ashes of war) and passes them to `StartingItemInjector` as typed `CarePackageItem` objects.

Each item carries a `type` field matching the EMEDF ItemType enum. The injector uses this type to call the correct `DirectlyGivePlayerItem` variant. Items with `type >= 4` (Gem/Ash of War) are skipped.

**Weapon upgrade encoding**: Standard weapons are upgraded by adding the upgrade level to the base ID (e.g., Longsword +8 = `2000000 + 8 = 2000008`). Somber weapons use the same scheme but cap at +10. The `weapon_upgrade` config value (0-25) controls the upgrade level applied to all care package weapons.

## Configuration

All starting item settings live under `[starting_items]` in `config.toml`:

```toml
[starting_items]
academy_key = true          # Academy Glintstone Key (8109)
drawing_room_key = true     # Drawing-Room Key (8134)
lantern = true              # Lantern (2070)
physick_flask = true        # Flask of Wondrous Physick (250)
whetblades = true           # Whetstone Knife (8590) + all Whetblades (8970-8974)
great_runes = true          # All restored Great Runes (191-196)
talisman_pouches = 3        # Talisman Pouch (10040), max 3
golden_seeds = 0            # Golden Seeds (10010)
sacred_tears = 0            # Sacred Tears (10020)
starting_runes = 0          # Converted to Lord's Runes (2919)
larval_tears = 10           # Larval Tears (8185)
stonesword_keys = 6         # Stonesword Keys (8000)
```

See `speedfog/config.py` `StartingItemsConfig` for full field list including DLC keys and individual Great Rune toggles.

## References

- Starting item injection: `writer/FogModWrapper/StartingItemInjector.cs`
- Starting resource injection: `writer/FogModWrapper/StartingResourcesInjector.cs`
- Resource calculations: `writer/FogModWrapper.Core/ResourceCalculations.cs`
- Python config: `speedfog/config.py` (`StartingItemsConfig`, `CarePackageConfig`)
- Care package item pool: `data/care_package_items.toml`
- Item giving limitations: `docs/item-giving-limitations.md`
- Event flag allocation: `docs/event-flags.md`
- Item Randomizer whetblade flags: `itemevents.txt` event 1450
