# Care Package System

**Date:** 2026-02-26
**Status:** Active

Randomized starting build system that gives players a curated set of weapons, armor, spells, and other equipment at game start. Designed for short SpeedFog runs where players skip the early scavenging phase.

## Determinism

The care package uses `random.Random(seed)` with the run seed. Same seed always produces the same build. This is critical for racing: all participants get identical starting equipment.

## Item Pool

Items are defined in `data/care_package_items.toml`. Each entry has a `name` (display) and `id` (EquipParam row ID, base before upgrade encoding).

### Categories

| Category | Item Type | Upgrade | TOML Key |
|----------|-----------|---------|----------|
| Weapons | 0 (Weapon) | Standard (+0 to +25) | `weapons` |
| Shields | 0 (Weapon) | Standard | `shields` |
| Catalysts (standard) | 0 (Weapon) | Standard | `catalysts.standard` |
| Catalysts (somber) | 0 (Weapon) | Somber | `catalysts.somber` |
| Armor (head) | 1 (Protector) | None | `armor.head` |
| Armor (body) | 1 (Protector) | None | `armor.body` |
| Armor (arms) | 1 (Protector) | None | `armor.arm` |
| Armor (legs) | 1 (Protector) | None | `armor.leg` |
| Talismans | 2 (Accessory) | None | `talismans` |
| Sorceries | 3 (Goods) | None | `sorceries` |
| Incantations | 3 (Goods) | None | `incantations` |
| Crystal Tears | 3 (Goods) | None | `crystal_tears` |
| Ashes of War | 4 (Gem) | None | `ashes_of_war` |

Catalysts have sub-pools (`standard`/`somber`) that are merged before sampling. Weapons use a flat list of standard-upgrade items.

## Configuration

`[care_package]` section in `config.toml`:

| Field | Default | Description |
|-------|---------|-------------|
| `enabled` | `false` | Enable care package system |
| `weapon_upgrade` | `8` | Standard upgrade level (0-25) |
| `weapons` | `5` | Number of weapons to sample |
| `shields` | `2` | Number of shields |
| `catalysts` | `2` | Number of catalysts (staves/seals) |
| `talismans` | `4` | Number of talismans |
| `sorceries` | `5` | Number of sorceries |
| `incantations` | `5` | Number of incantations |
| `head_armor` | `2` | Number of head pieces |
| `body_armor` | `2` | Number of body pieces |
| `arm_armor` | `2` | Number of arm pieces |
| `leg_armor` | `2` | Number of leg pieces |
| `crystal_tears` | `5` | Number of crystal tears |
| `ashes_of_war` | `0` | Number of ashes of war |

Per-category count is clamped to pool size (`min(count, len(pool))`).

## Weapon Upgrade Calculation

Standard upgrade level is configured directly (`weapon_upgrade`, range 0-25). Weapons always use standard upgrade. Catalysts may use somber upgrade, which is derived:

```
somber_upgrade = floor(standard_upgrade / 2.5)
```

| Standard | Somber |
|----------|--------|
| +0 | +0 |
| +5 | +2 |
| +8 | +3 |
| +10 | +4 |
| +15 | +6 |
| +25 | +10 |

Upgrade is encoded into the weapon param ID: `final_id = base_id + upgrade_level`. For example, Longsword (base 2000000) at +8 becomes 2000008.

## Item Types

Maps to EMEDF bank 2003 index 43 (`DirectlyGivePlayerItem`):

| Value | Type | Used For |
|-------|------|----------|
| 0 | Weapon | Weapons, shields, catalysts |
| 1 | Protector | Armor pieces |
| 2 | Accessory | Talismans |
| 3 | Goods | Sorceries, incantations, crystal tears |
| 4 | Gem | Ashes of War |

### Gem/Ash of War Limitation

Type 4 (Gem) is not supported by the `DirectlyGivePlayerItem` EMEVD instruction. Attempting to use raw type 4 gives a broken item (`?WeaponName?`). The C# `StartingItemInjector` filters out items with `Type >= 4`:

```csharp
foreach (var item in carePackage.Where(i => i.Type < 4))
```

Gem items are logged as skipped and must be given via alternative means (e.g., `ShopLineupParam` with `equipType=4` or runtime injection by the racing mod). The default `ashes_of_war` count is 0 for this reason.

## Validation

`load_item_pool()` rejects any item with `id=0`, which catches placeholder entries that were never filled in with real param IDs. The check is recursive across all categories and sub-categories.

## Pipeline

1. **Python** (`care_package.py`): `sample_care_package()` samples items from the TOML pool using seed-based RNG.
2. **Python** (`output.py`): Items are serialized into `graph.json` under `care_package` as `[{type, id, name}, ...]`.
3. **C#** (`GraphData.cs`): Deserialized into `List<CarePackageItem>`.
4. **C#** (`StartingItemInjector.cs`): Injected into `common.emevd` as `DirectlyGivePlayerItem` instructions. Items are given once (guarded by flag 1040299001) after the player picks up the Tarnished's Wizened Finger (flag 1040292051).

## Spoiler Log

When `--spoiler` is passed, `export_spoiler_log()` appends a `CARE PACKAGE` section to `spoiler.txt`:

```
============================================================
CARE PACKAGE (starting build)
============================================================
  [Weapon] Claymore +8 (id=3180008)
  [Armor] Scale Armor (id=40100)
  [Talisman] Erdtree's Favor (id=1040)
  [Spell/Item] Rock Sling (id=4710)
```

## Files

| File | Role |
|------|------|
| `speedfog/care_package.py` | Pool loading, validation, sampling logic |
| `speedfog/config.py` | `CarePackageConfig` dataclass |
| `data/care_package_items.toml` | Item pool definitions |
| `speedfog/output.py` | Serialization to `graph.json` and spoiler log |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | C# `CarePackageItem` model |
| `writer/FogModWrapper/StartingItemInjector.cs` | EMEVD injection (gives items in-game) |

See also `docs/starting-items.md` for the full EMEVD injection pipeline and auxiliary flag requirements.
