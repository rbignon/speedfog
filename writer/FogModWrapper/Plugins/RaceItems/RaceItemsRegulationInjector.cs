using FogModWrapper.Common;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>
/// Writes the race items' EquipParamGoods and SpEffectParam rows.
/// Adapted from FogRando's Bell of Return, which adds a goods row and a
/// SpEffect row sharing one id
/// (reference/fogrando-src/GameDataWriterE.cs:3857-3871:
/// <c>GameEditor.AddRow(Params["EquipParamGoods"], 141626, 115)</c> and
/// <c>GameEditor.AddRow(Params["SpEffectParam"], 141626, 10600)</c>).
/// </summary>
public static class RaceItemsRegulationInjector
{
    /// <summary>Goods row cloned for every item: Flask of Wondrous Physick.</summary>
    private const int GoodsTemplateId = 250;

    public static void Inject(RegulationEditor reg, IReadOnlyList<RaceItem> items)
    {
        var goods = reg.GetParam("EquipParamGoods");
        var sp = reg.GetParam("SpEffectParam", "SpEffect");
        if (goods == null || sp == null)
            throw new InvalidOperationException("race_items: EquipParamGoods or SpEffectParam missing from regulation.bin");
        Apply(goods, sp, items);
    }

    /// <summary>Lower-level entry point used by tests; operates on loaded PARAMs.</summary>
    public static void Apply(PARAM goods, PARAM sp, IReadOnlyList<RaceItem> items)
    {
        foreach (var item in items)
        {
            AddGoodsRow(goods, item);
            if (item.Marker != null)
                AddSpEffectRow(sp, item.Marker);
            if (item.Effect != null)
                AddSpEffectRow(sp, item.Effect);
        }
        Console.WriteLine($"race_items: injected {items.Count} goods row(s) into EquipParamGoods/SpEffectParam");
    }

    private static void AddGoodsRow(PARAM goods, RaceItem item)
    {
        var row = GameEditor.AddRow(goods, item.GoodsId, GoodsTemplateId);
        bool usable = item.Marker != null;

        Set(row, "goodsType", 0);
        Set(row, "replaceItemId", -1);
        Set(row, "maxNum", 1);
        Set(row, "isOnlyOne", 1);
        Set(row, "isDiscard", 0);
        Set(row, "isDeposit", 0);
        Set(row, "isDrop", 0);
        Set(row, "sellValue", -1);
        Set(row, "iconId", item.IconId);
        Set(row, "refCategory", usable ? 2 : 0);
        Set(row, "refId_default", usable ? item.Marker!.Id : -1);
        Set(row, "isConsume", usable ? 1 : 0);
        Set(row, "isAutoEquip", usable ? 1 : 0);
    }

    private static void AddSpEffectRow(PARAM sp, RaceItemSpEffect effect)
    {
        var row = GameEditor.AddRow(sp, effect.Id, effect.Template);
        foreach (var (field, value) in effect.Fields)
            Set(row, field, value);
    }

    /// <summary>Writes <paramref name="value"/> converted to the cell's own type.</summary>
    private static void Set(PARAM.Row row, string field, double value)
    {
        var cell = row[field];
        cell.Value = Convert.ChangeType(value, cell.Value.GetType());
    }
}
