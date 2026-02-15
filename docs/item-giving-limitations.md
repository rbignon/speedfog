# Item Giving Limitations

How Elden Ring's EMEVD system constrains item delivery, and how SpeedFog works around them.

## EMEVD Item Types

The EMEVD instruction `DirectlyGivePlayerItem` (bank 2003, index 43) accepts an `ItemType` enum:

| EMEDF Value | Name | Param Table | Examples |
|-------------|------|-------------|----------|
| 0 | Weapon | EquipParamWeapon | Swords, shields, catalysts |
| 1 | Armor | EquipParamProtector | Head, body, arm, leg pieces |
| 2 | Ring | EquipParamAccessory | Talismans |
| 3 | Goods | EquipParamGoods | Sorceries, incantations, key items, consumables |

**There is no type 4 (Gem/Ash of War) in the EMEDF enum.** The enum stops at 3.

## What Happens with Type 4

Attempting to use `DirectlyGivePlayerItem` with raw type value 4:
- The instruction interprets 4 as an unknown/invalid type
- The game gives an item with display name `?WeaponName?`
- The actual Ash of War is never granted

## Alternative: AwardItemLot

`AwardItemLot` (bank 2003, index 4) uses ItemLotParam rows. ItemLotParam has its own category enum:

| ITEMLOT_ITEMCATEGORY | Name |
|----------------------|------|
| 0 | None |
| 1 | Weapon |
| 2 | Protector |
| 3 | Accessory |
| 4 | **Gem** |
| 5 | Goods |

Creating an ItemLotParam row with category=4 (Gem) and calling `AwardItemLot` **does not work correctly**: the game reinterprets the Gem ID as a Weapon ID, giving the wrong item entirely.

## Solution: ShopLineupParam

`ShopLineupParam` has a broader type enum that includes Gems:

| SHOP_LINEUP_EQUIPTYPE | Name |
|-----------------------|------|
| 0 | Weapon |
| 1 | Protector |
| 2 | Accessory |
| 3 | Goods |
| 4 | **Gem** |

Adding a row to ShopLineupParam with `equipType=4`, `price=0` makes the Ash of War appear for free in the Twin Maiden Husks shop at Roundtable Hold.

## SpeedFog Implementation

### Care Package Items (types 0-3)

`StartingItemInjector.cs` gives weapons, armor, talismans, and spells via EMEVD:

```
Event 755860000 (common.emevd):
  Wait for flag 1040292051 (start trigger)
  Skip if flag 1040299001 already set
  For each item with type < 4:
    DirectlyGivePlayerItem(type, id, 6001, 1)
  SetEventFlag(1040299001)  // prevent re-giving
```

### Care Package Ashes of War (type 4)

Items with type=4 are skipped by `StartingItemInjector` and instead added to `ShopLineupParam` in `regulation.bin`:

```
For each item with type >= 4:
  Add ShopLineupParam row:
    equipId = gem_id
    equipType = 4 (Gem)
    value = 0 (free)
    sellQuantity = -1 (unlimited)
```

Shop ID allocation: 101820+ (SmithingStoneShopInjector uses 101800-101817).

### Smithing Stones

`SmithingStoneShopInjector.cs` uses the same ShopLineupParam mechanism with `equipType=3` (Goods) for smithing stones. Shop IDs: 101800-101817 (8 normal + 9 somber + 1 Ancient Dragon).

### Starting Resources

`StartingResourcesInjector.cs` gives consumables via `DirectlyGivePlayerItem` with type=3 (Goods):
- Golden Seeds (10010), Sacred Tears (10020), Lord's Runes (2919), Larval Tears (8185)

## Weapon Upgrade Encoding

Weapon param IDs encode upgrade level directly: `final_id = base_id + upgrade_level`.

Standard vs. somber conversion: `somber_level = floor(standard_level / 2.5)`.

Examples:
- Uchigatana base=1130000, +8 → 1130008
- Moonveil base=4020000 (somber), standard +8 → somber +3 → 4020003

## Key Takeaways

1. **EMEVD cannot give Gems** — the instruction enum simply doesn't support it
2. **ItemLotParam category=4 is broken** — reinterprets Gem as Weapon
3. **ShopLineupParam is the workaround** — supports equipType=4 (Gem) correctly
4. **Price=0 makes items free** — effectively "giving" them via shop
5. **RandoCalypse mod** uses runtime memory injection for gems — a different approach entirely

## References

- EMEVD instruction definitions: `data/er-common.emedf.json`
- StartingItemInjector: `writer/FogModWrapper/StartingItemInjector.cs`
- SmithingStoneShopInjector: `writer/FogModWrapper/SmithingStoneShopInjector.cs`
- Care package sampling: `speedfog/care_package.py`
- Item pool definitions: `data/care_package_items.toml`
