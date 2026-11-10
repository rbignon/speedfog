using FogModWrapper.Common;
using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper.Start;

/// <summary>
/// Replaces world placements of goods the player already holds at cap from
/// game start (starting items + care package) with Rune Arcs, so those item
/// lots and shop rows stay real rewards instead of dead pickups.
///
/// A good is "saturated" when the granted count reaches its EquipParamGoods
/// maxNum (inventory cap): every world copy is then unobtainable (or, for
/// goods with a storage cap, a wasted storage-box pickup). In practice this
/// covers the crystal tears drawn by the care package (maxNum 1), the
/// whetblades (maxNum 1), and the Talisman Pouches (maxNum 3, three granted
/// by default). Stackable grants (spells, Golden Seeds, Larval Tears...) and
/// uncapped item types (talismans, weapons, armor) are left alone.
///
/// Runs on the merged regulation, so with the Item Randomizer active it
/// rewrites the randomizer's placements (including the key/boss slots the
/// item preset routes tears and pouches to), and without it the vanilla lots.
/// </summary>
public static class UniqueItemNeutralizer
{
    public const int RuneArcGoodsId = 190;

    // ER ItemLotParam lotItemCategory for goods (RandomizerCommon ErLotItemTypes).
    private const int LotGoodsCategory = 1;

    // ER ShopLineupParam equipType for goods (RandomizerCommon ShopItemTypes).
    private const int ShopGoodsEquipType = 3;

    // CarePackageItem.Type for goods (EMEDF ItemType enum).
    private const int CarePackageGoodsType = 3;

    private const int LotSlotCount = 8;

    private static readonly string[] LotCategoryFields;
    private static readonly string[] LotItemIdFields;
    private static readonly string[] LotFlagFields;

    static UniqueItemNeutralizer()
    {
        LotCategoryFields = new string[LotSlotCount];
        LotItemIdFields = new string[LotSlotCount];
        LotFlagFields = new string[LotSlotCount];
        for (int slot = 1; slot <= LotSlotCount; slot++)
        {
            LotCategoryFields[slot - 1] = $"lotItemCategory0{slot}";
            LotItemIdFields[slot - 1] = $"lotItemId0{slot}";
            LotFlagFields[slot - 1] = $"getItemFlagId0{slot}";
        }
    }

    /// <summary>
    /// Counts every good granted at game start for this seed: starting goods
    /// (one entry per copy, e.g. three Talisman Pouches), care package goods,
    /// the starting resource counts, and the Torrent regalia goods when the
    /// showcase unlock is on. Non-goods care package items (weapons, armor,
    /// talismans, ashes of war) have no possession cap read from
    /// EquipParamGoods and are not counted.
    /// </summary>
    public static Dictionary<int, int> CollectGrantedGoods(GraphData graph)
    {
        var counts = new Dictionary<int, int>();
        void Add(int id, int count)
        {
            if (count <= 0)
                return;
            counts.TryGetValue(id, out int current);
            counts[id] = current + count;
        }

        foreach (int id in graph.StartingGoods)
            Add(id, 1);
        foreach (var item in graph.CarePackage)
        {
            if (item.Type == CarePackageGoodsType)
                Add(item.Id, 1);
        }
        if (graph.TorrentSkins?.Unlock == true)
        {
            foreach (int id in StartingItemInjector.RegaliaGoods)
                Add(id, 1);
        }
        Add(StartingResourcesInjector.GOLDEN_SEED_GOOD_ID, graph.StartingGoldenSeeds);
        Add(StartingResourcesInjector.SACRED_TEAR_GOOD_ID, graph.StartingSacredTears);
        Add(StartingResourcesInjector.LARVAL_TEAR_GOOD_ID, graph.StartingLarvalTears);
        Add(StartingResourcesInjector.STONESWORD_KEY_GOOD_ID, graph.StartingStoneswordKeys);
        return counts;
    }

    /// <summary>
    /// Keeps the granted goods whose count reaches their EquipParamGoods
    /// maxNum. A good without a row (lookup null) or with a non-positive
    /// maxNum is skipped, as is the Rune Arc itself (a swap must never be an
    /// identity rewrite).
    /// </summary>
    public static HashSet<int> ComputeSaturatedGoods(
        IReadOnlyDictionary<int, int> grantedCounts, Func<int, int?> maxNumLookup)
    {
        var saturated = new HashSet<int>();
        foreach (var (id, count) in grantedCounts)
        {
            if (id == RuneArcGoodsId)
                continue;
            int? maxNum = maxNumLookup(id);
            if (maxNum is > 0 && count >= maxNum)
                saturated.Add(id);
        }
        return saturated;
    }

    /// <summary>
    /// Rewrites every goods slot of <paramref name="lotParam"/> holding a
    /// saturated good to a Rune Arc, keeping the slot's category, quantity,
    /// drop weight and the lot's acquisition flag. With
    /// <paramref name="skipFlaglessLots"/> (enemy lots), rows with no
    /// acquisition flag at all (row-level and per-slot getItemFlagId zero)
    /// are skipped: a respawning enemy's farmable drop must not become a
    /// renewable Rune Arc source. Map lots pass false: their flagless rows
    /// (999xxxx range) are awarded by one-shot EMEVD events whose own flag
    /// enforces one-shot-ness, so they are real dead pickups to swap.
    /// Returns the number of slots rewritten.
    /// </summary>
    public static int ReplaceInLots(PARAM lotParam, IReadOnlySet<int> saturatedGoods,
        string paramName, bool skipFlaglessLots)
    {
        int replaced = 0;
        foreach (var row in lotParam.Rows)
        {
            bool? flagged = null;
            for (int slot = 1; slot <= LotSlotCount; slot++)
            {
                if (Convert.ToInt32(row[LotCategoryFields[slot - 1]].Value) != LotGoodsCategory)
                    continue;
                var idCell = row[LotItemIdFields[slot - 1]];
                int goodId = Convert.ToInt32(idCell.Value);
                if (!saturatedGoods.Contains(goodId))
                    continue;
                if (skipFlaglessLots)
                {
                    flagged ??= HasAcquisitionFlag(row);
                    if (!flagged.Value)
                    {
                        Console.WriteLine(
                            $"  {paramName}[{row.ID}] slot {slot}: goods {goodId} kept (flagless farmable drop)");
                        continue;
                    }
                }
                idCell.Value = RuneArcGoodsId;
                replaced++;
                Console.WriteLine(
                    $"  {paramName}[{row.ID}] slot {slot}: goods {goodId} -> Rune Arc");
            }
        }
        return replaced;
    }

    private static bool HasAcquisitionFlag(PARAM.Row row)
    {
        if (Convert.ToInt64(row["getItemFlagId"].Value) != 0)
            return true;
        foreach (var field in LotFlagFields)
        {
            if (Convert.ToInt64(row[field].Value) != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Rewrites every finite goods shop row of <paramref name="shopParam"/>
    /// selling a saturated good to sell a Rune Arc instead, keeping the
    /// row's price and stock flag. Unlimited-stock rows (sellQuantity -1)
    /// are skipped for the same reason as repeatable lots. Returns the
    /// number of rows rewritten.
    /// </summary>
    public static int ReplaceInShops(PARAM shopParam, IReadOnlySet<int> saturatedGoods)
    {
        int replaced = 0;
        foreach (var row in shopParam.Rows)
        {
            if (Convert.ToInt32(row["equipType"].Value) != ShopGoodsEquipType)
                continue;
            var idCell = row["equipId"];
            int goodId = Convert.ToInt32(idCell.Value);
            if (!saturatedGoods.Contains(goodId))
                continue;
            if (Convert.ToInt32(row["sellQuantity"].Value) < 0)
            {
                Console.WriteLine(
                    $"  ShopLineupParam[{row.ID}]: goods {goodId} kept (unlimited stock)");
                continue;
            }
            idCell.Value = RuneArcGoodsId;
            replaced++;
            Console.WriteLine(
                $"  ShopLineupParam[{row.ID}]: goods {goodId} -> Rune Arc");
        }
        return replaced;
    }

    /// <summary>
    /// Entry point for RegulationPhase: computes the saturated goods for this seed
    /// and rewrites their item lots (map + enemy) and shop rows.
    /// </summary>
    public static void ApplyTo(RegulationEditor reg, GraphData graph)
    {
        var granted = CollectGrantedGoods(graph);
        if (granted.Count == 0)
            return;

        var goodsParam = reg.GetParam("EquipParamGoods");
        if (goodsParam == null)
            return;
        var rowsById = new Dictionary<int, PARAM.Row>();
        foreach (var row in goodsParam.Rows)
            rowsById.TryAdd(row.ID, row);
        int? MaxNum(int id) =>
            rowsById.TryGetValue(id, out var row) ? Convert.ToInt32(row["maxNum"].Value) : null;

        var saturated = ComputeSaturatedGoods(granted, MaxNum);
        if (saturated.Count == 0)
        {
            Console.WriteLine("Unique items: no granted good reaches its possession cap, nothing to neutralize");
            return;
        }

        Console.WriteLine(
            $"Neutralizing world copies of {saturated.Count} granted good(s) at cap: " +
            string.Join(", ", saturated.OrderBy(i => i)));

        int lots = 0;
        var mapLots = reg.GetParam("ItemLotParam_map", "ItemLotParam");
        if (mapLots != null)
            lots += ReplaceInLots(mapLots, saturated, "ItemLotParam_map", skipFlaglessLots: false);
        var enemyLots = reg.GetParam("ItemLotParam_enemy", "ItemLotParam");
        if (enemyLots != null)
            lots += ReplaceInLots(enemyLots, saturated, "ItemLotParam_enemy", skipFlaglessLots: true);

        int shops = 0;
        var shopParam = reg.GetParam("ShopLineupParam");
        if (shopParam != null)
            shops = ReplaceInShops(shopParam, saturated);

        Console.WriteLine($"  Replaced {lots} item lot slot(s) and {shops} shop row(s) with Rune Arcs");
    }
}
