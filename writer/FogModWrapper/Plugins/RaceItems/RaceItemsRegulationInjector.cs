using FogModWrapper.Common;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>
/// Writes the race items' EquipParamGoods and SpEffectParam rows: one goods
/// row per catalog goods (with its marker SpEffect when it can be used) and
/// one SpEffect row per item effect.
/// Adapted from FogRando's Bell of Return, which adds a goods row and a
/// SpEffect row sharing one id
/// (reference/fogrando-src/GameDataWriterE.cs:3857-3871:
/// <c>GameEditor.AddRow(Params["EquipParamGoods"], 141626, 115)</c> and
/// <c>GameEditor.AddRow(Params["SpEffectParam"], 141626, 10600)</c>).
/// </summary>
public static class RaceItemsRegulationInjector
{
    /// <summary>Goods row cloned for every goods: Flask of Wondrous Physick.</summary>
    private const int GoodsTemplateId = 250;

    public static void Inject(RegulationEditor reg, RaceItemsCatalog catalog)
    {
        var goods = reg.GetParam("EquipParamGoods");
        var sp = reg.GetParam("SpEffectParam", "SpEffect");
        if (goods == null || sp == null)
            throw new InvalidOperationException("race_items: EquipParamGoods or SpEffectParam missing from regulation.bin");
        Apply(goods, sp, catalog);
    }

    /// <summary>Lower-level entry point used by tests; operates on loaded PARAMs.</summary>
    public static void Apply(PARAM goods, PARAM sp, RaceItemsCatalog catalog)
    {
        foreach (var g in catalog.Goods)
        {
            AddGoodsRow(goods, g);
            if (g.Marker != null)
                AddSpEffectRow(sp, g.Marker);
        }
        int effects = 0;
        foreach (var item in catalog.Items)
        {
            if (item.Effect != null)
            {
                AddSpEffectRow(sp, item.Effect);
                effects++;
            }
        }
        Console.WriteLine(
            $"race_items: injected {catalog.Goods.Count} goods row(s) and {effects} effect SpEffect row(s)");
    }

    private static void AddGoodsRow(PARAM goods, RaceItemGoods g)
    {
        var row = GameEditor.AddRow(goods, g.GoodsId, GoodsTemplateId);
        bool usable = g.Marker != null;

        Set(row, "EquipParamGoods", "goodsType", 0);
        Set(row, "EquipParamGoods", "replaceItemId", -1);
        Set(row, "EquipParamGoods", "maxNum", 1);
        Set(row, "EquipParamGoods", "isOnlyOne", 1);
        Set(row, "EquipParamGoods", "isDiscard", 0);
        Set(row, "EquipParamGoods", "isDeposit", 0);
        Set(row, "EquipParamGoods", "isDrop", 0);
        Set(row, "EquipParamGoods", "sellValue", -1);
        Set(row, "EquipParamGoods", "iconId", g.IconId);
        Set(row, "EquipParamGoods", "refCategory", usable ? 2 : 0);
        Set(row, "EquipParamGoods", "refId_default", usable ? g.Marker!.Id : -1);
        Set(row, "EquipParamGoods", "isConsume", usable ? 1 : 0);
        Set(row, "EquipParamGoods", "isAutoEquip", usable ? 1 : 0);
        // The Physick template refills at a grace and shows a tutorial: a used
        // item must not come back after resting. Same profile as the vanilla
        // Phantom Bloody Finger (107) and Phantom Great Rune (135).
        Set(row, "EquipParamGoods", "isFullSuppleItem", 0);
        Set(row, "EquipParamGoods", "suppleType", 0);
        Set(row, "EquipParamGoods", "itemGetTutorialFlagId", 0);
    }

    private static void AddSpEffectRow(PARAM sp, RaceItemSpEffect effect)
    {
        var row = GameEditor.AddRow(sp, effect.Id, effect.Template);
        foreach (var (field, value) in effect.Fields)
            Set(row, "SpEffectParam", field, value);
    }

    /// <summary>Writes <paramref name="value"/> converted to the cell's own type.</summary>
    private static void Set(PARAM.Row row, string param, string field, double value)
    {
        var cell = row.Cells.FirstOrDefault(c => c.Def.InternalName == field)
            ?? throw new InvalidDataException(
                $"race_items: unknown field '{field}' on {param} row {row.ID}");
        cell.Value = Convert.ChangeType(value, cell.Value.GetType());
    }
}
