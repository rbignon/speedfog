using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class UniqueItemNeutralizerTests
{
    // --- ComputeSaturatedGoods ---

    private static int? MaxNumLookup(int id) => id switch
    {
        11000 => 1,   // Crimsonspill Crystal Tear
        8970 => 1,    // Iron Whetblade
        10040 => 3,   // Talisman Pouch
        4000 => 99,   // Glintstone Pebble (sorcery, stackable)
        190 => 1,     // Rune Arc (hypothetical cap, must still be ignored)
        9500 => 0,    // Hypothetical uncapped good (maxNum 0)
        _ => null,
    };

    [Fact]
    public void ComputeSaturatedGoods_IncludesGoodsGrantedAtOrAboveMaxNum()
    {
        var granted = new Dictionary<int, int> { [11000] = 1, [10040] = 3, [8970] = 2 };

        var saturated = UniqueItemNeutralizer.ComputeSaturatedGoods(granted, MaxNumLookup);

        Assert.Equal(new[] { 8970, 10040, 11000 }, saturated.OrderBy(i => i));
    }

    [Fact]
    public void ComputeSaturatedGoods_ExcludesGoodsBelowCapOrStackable()
    {
        var granted = new Dictionary<int, int> { [10040] = 2, [4000] = 5 };

        var saturated = UniqueItemNeutralizer.ComputeSaturatedGoods(granted, MaxNumLookup);

        Assert.Empty(saturated);
    }

    [Fact]
    public void ComputeSaturatedGoods_ExcludesUnknownNonPositiveCapAndRuneArc()
    {
        // 999999: no EquipParamGoods row (lookup null). 9500: maxNum 0 (no
        // meaningful cap). 190: the replacement item itself, excluded so a
        // swap is never an identity rewrite.
        var granted = new Dictionary<int, int> { [999999] = 1, [9500] = 1, [190] = 1 };

        var saturated = UniqueItemNeutralizer.ComputeSaturatedGoods(granted, MaxNumLookup);

        Assert.Empty(saturated);
    }

    // --- CollectGrantedGoods ---

    [Fact]
    public void CollectGrantedGoods_CountsStartingGoodsCarePackageGoodsAndResources()
    {
        var graph = new GraphData
        {
            StartingGoods = new List<int> { 10040, 10040, 10040, 8970 },
            CarePackage = new List<CarePackageItem>
            {
                new() { Type = 3, Id = 11000, Name = "Crimsonspill Crystal Tear" },
                new() { Type = 2, Id = 1000, Name = "Crimson Amber Medallion" },
                new() { Type = 0, Id = 2000008, Name = "Longsword +8" },
            },
            StartingGoldenSeeds = 2,
            StartingSacredTears = 1,
            StartingLarvalTears = 10,
            StartingStoneswordKeys = 6,
        };

        var granted = UniqueItemNeutralizer.CollectGrantedGoods(graph);

        Assert.Equal(3, granted[10040]);
        Assert.Equal(1, granted[8970]);
        Assert.Equal(1, granted[11000]);
        Assert.Equal(2, granted[10010]);
        Assert.Equal(1, granted[10020]);
        Assert.Equal(10, granted[8185]);
        Assert.Equal(6, granted[8000]);
        // Accessory and weapon care package items are not goods.
        Assert.DoesNotContain(1000, granted.Keys);
        Assert.DoesNotContain(2000008, granted.Keys);
    }

    [Fact]
    public void CollectGrantedGoods_SkipsZeroResourceCounts()
    {
        var graph = new GraphData
        {
            StartingGoldenSeeds = 0,
            StartingSacredTears = 0,
            StartingLarvalTears = 0,
            StartingStoneswordKeys = 0,
        };

        var granted = UniqueItemNeutralizer.CollectGrantedGoods(graph);

        Assert.Empty(granted);
    }

    [Fact]
    public void CollectGrantedGoods_CountsTorrentRegaliaOnlyWhenUnlocked()
    {
        // GraphData defaults larval tears and stonesword keys to non-zero
        // (mirroring config defaults), zero them to isolate the regalia.
        static GraphData Bare(TorrentSkinsData? skins) => new()
        {
            StartingLarvalTears = 0,
            StartingStoneswordKeys = 0,
            TorrentSkins = skins,
        };

        var grantedUnlocked = UniqueItemNeutralizer.CollectGrantedGoods(
            Bare(new TorrentSkinsData { Unlock = true }));
        foreach (int regalia in StartingItemInjector.RegaliaGoods)
            Assert.Equal(1, grantedUnlocked[regalia]);

        Assert.Empty(UniqueItemNeutralizer.CollectGrantedGoods(
            Bare(new TorrentSkinsData { Unlock = false })));
        Assert.Empty(UniqueItemNeutralizer.CollectGrantedGoods(Bare(null)));
    }

    // --- ReplaceInLots ---

    private static PARAM.Row AddLotRow(PARAM param, int id, uint getItemFlagId,
        params (int slot, int category, int itemId, byte num)[] slots)
    {
        var row = param.Rows.Find(r => r.ID == id)
            ?? ParamTestHelper.AddRowFromTemplate(param, id);
        row["getItemFlagId"].Value = getItemFlagId;
        foreach (var (slot, category, itemId, num) in slots)
        {
            row[$"lotItemCategory0{slot}"].Value = category;
            row[$"lotItemId0{slot}"].Value = itemId;
            row[$"lotItemNum0{slot}"].Value = num;
        }
        return row;
    }

    [Fact]
    public void ReplaceInLots_ReplacesMatchingGoodsSlots_KeepsEverythingElse()
    {
        var param = ParamTestHelper.BuildParamFromDef("ItemLotParam", 100);
        // Slot 1 holds a granted tear, slot 8 a granted pouch (slot bounds),
        // slot 2 an unrelated good.
        var dead = AddLotRow(param, 100, 60100,
            (1, 1, 11000, (byte)3), (2, 1, 4000, (byte)1), (8, 1, 10040, (byte)1));
        var untouched = AddLotRow(param, 200, 60200, (1, 1, 4000, (byte)1));

        int replaced = UniqueItemNeutralizer.ReplaceInLots(
            param, new HashSet<int> { 11000, 10040 }, "ItemLotParam_map",
            skipFlaglessLots: false);

        Assert.Equal(2, replaced);
        Assert.Equal(190, (int)dead["lotItemId01"].Value);
        Assert.Equal(1, (int)dead["lotItemCategory01"].Value);
        Assert.Equal(3, Convert.ToInt32(dead["lotItemNum01"].Value));
        Assert.Equal(190, (int)dead["lotItemId08"].Value);
        Assert.Equal(4000, (int)dead["lotItemId02"].Value);
        Assert.Equal(4000, (int)untouched["lotItemId01"].Value);
    }

    [Fact]
    public void ReplaceInLots_IgnoresNonGoodsCategoriesWithSameNumericId()
    {
        var param = ParamTestHelper.BuildParamFromDef("ItemLotParam", 100);
        // Category 4 = accessory: same numeric ID as a granted good must not match.
        var row = AddLotRow(param, 100, 60100, (1, 4, 10040, (byte)1));

        int replaced = UniqueItemNeutralizer.ReplaceInLots(
            param, new HashSet<int> { 10040 }, "ItemLotParam_map",
            skipFlaglessLots: false);

        Assert.Equal(0, replaced);
        Assert.Equal(10040, (int)row["lotItemId01"].Value);
    }

    [Fact]
    public void ReplaceInLots_SkipsFlaglessEnemyLots_KeepsFlaggedOnes()
    {
        var param = ParamTestHelper.BuildParamFromDef("ItemLotParam", 100);
        // Enemy lot without any acquisition flag: a respawning enemy's
        // farmable drop, which must not become a renewable Rune Arc source.
        var farmable = AddLotRow(param, 100, 0, (1, 1, 8000, (byte)1));
        // A per-slot acquisition flag alone marks the lot one-shot.
        var slotFlagged = AddLotRow(param, 200, 0, (1, 1, 8000, (byte)1));
        slotFlagged["getItemFlagId01"].Value = 60201u;

        int replaced = UniqueItemNeutralizer.ReplaceInLots(
            param, new HashSet<int> { 8000 }, "ItemLotParam_enemy", skipFlaglessLots: true);

        Assert.Equal(1, replaced);
        Assert.Equal(8000, (int)farmable["lotItemId01"].Value);
        Assert.Equal(190, (int)slotFlagged["lotItemId01"].Value);
    }

    [Fact]
    public void ReplaceInLots_ReplacesFlaglessMapLots()
    {
        var param = ParamTestHelper.BuildParamFromDef("ItemLotParam", 9990310);
        // Vanilla map lots in the 999xxxx range have no acquisition flag:
        // they are awarded by one-shot EMEVD events (AwardItemLot), where
        // the event's own flag enforces one-shot-ness. They must be swapped.
        var scripted = AddLotRow(param, 9990310, 0, (1, 1, 250, (byte)1));

        int replaced = UniqueItemNeutralizer.ReplaceInLots(
            param, new HashSet<int> { 250 }, "ItemLotParam_map", skipFlaglessLots: false);

        Assert.Equal(1, replaced);
        Assert.Equal(190, (int)scripted["lotItemId01"].Value);
    }

    // --- ReplaceInShops ---

    private static PARAM.Row AddShopRow(PARAM param, int id, byte equipType, int equipId,
        short sellQuantity)
    {
        var row = param.Rows.Find(r => r.ID == id)
            ?? ParamTestHelper.AddRowFromTemplate(param, id);
        row["equipType"].Value = equipType;
        row["equipId"].Value = equipId;
        row["sellQuantity"].Value = sellQuantity;
        return row;
    }

    [Fact]
    public void ReplaceInShops_ReplacesFiniteGoodsRows_KeepsOtherTypesAndIds()
    {
        var param = ParamTestHelper.BuildParamFromDef("ShopLineupParam", 101700);
        var deadGood = AddShopRow(param, 101700, 3, 10040, 1);
        var otherGood = AddShopRow(param, 101701, 3, 4000, 1);
        var weapon = AddShopRow(param, 101702, 0, 10040, 1);

        int replaced = UniqueItemNeutralizer.ReplaceInShops(
            param, new HashSet<int> { 10040 });

        Assert.Equal(1, replaced);
        Assert.Equal(190, (int)deadGood["equipId"].Value);
        Assert.Equal(4000, (int)otherGood["equipId"].Value);
        Assert.Equal(10040, (int)weapon["equipId"].Value);
    }

    [Fact]
    public void ReplaceInShops_SkipsInfiniteStockRows()
    {
        var param = ParamTestHelper.BuildParamFromDef("ShopLineupParam", 101700);
        // sellQuantity -1 = unlimited stock: swapping would create an
        // infinite Rune Arc source.
        var infinite = AddShopRow(param, 101700, 3, 8000, -1);

        int replaced = UniqueItemNeutralizer.ReplaceInShops(
            param, new HashSet<int> { 8000 });

        Assert.Equal(0, replaced);
        Assert.Equal(8000, (int)infinite["equipId"].Value);
    }
}
