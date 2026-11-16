using FogModWrapper.Common;
using FogModWrapper.Plugins.Halloween;
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
        PARAM? npc = null, think = null;
        if (catalog.Hunter != null)
        {
            npc = reg.GetParam("NpcParam");
            think = reg.GetParam("NpcThinkParam");
            if (npc == null || think == null)
                throw new InvalidOperationException("race_items: NpcParam or NpcThinkParam missing from regulation.bin");
        }
        Apply(goods, sp, catalog, npc, think);
    }

    /// <summary>Lower-level entry point used by tests; operates on loaded PARAMs.</summary>
    public static void Apply(
        PARAM goods, PARAM sp, RaceItemsCatalog catalog, PARAM? npc = null, PARAM? think = null)
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

        if (catalog.Hunter is { } hunter)
        {
            if (npc == null || think == null)
                throw new ArgumentException("race_items: the catalog has a [hunter] but no NpcParam or NpcThinkParam was given");
            AddHunterRows(npc, think, hunter);
        }
    }

    /// <summary>
    /// The Hunter: Margit cloned once per enemy tier plus one think row. The
    /// racing mod spawns him through the game's debug character creator, which
    /// skips FogMod's per-seed boss scaling (EMEVD SpEffects on the boss
    /// entity), so each clone carries the game's own area scaling row of its
    /// tier and the server picks the row from the leader's zone tier. No runes
    /// and no drop on any row, whatever the seed's randomizers did to the
    /// source. The other cells (behaviorVariationId, teamType, resident
    /// SpEffects) stay the source's.
    /// </summary>
    private static void AddHunterRows(PARAM npc, PARAM think, RaceItemHunter hunter)
    {
        for (int tier = 1; tier <= SpeedFogIds.RaceItemHunterTierCount; tier++)
        {
            int id = hunter.NpcParamBase + tier - 1;
            AmbientSpawnInjector.CloneAtTier(npc, id, hunter.SourceNpcParam, tier); // getSoul 0, itemLotId_enemy -1
            var row = npc[id]!;
            // His 12000 runes are paid by the map event, not by the row; belt and braces.
            Set(row, "NpcParam", "isSoulGetByBoss", 0);
            Set(row, "NpcParam", "itemLotId_map", -1);
            Set(row, "NpcParam", "sleepCollectorItemLotId_enemy", -1);
            Set(row, "NpcParam", "sleepCollectorItemLotId_map", -1);
        }

        // The enemy randomizer caps every think row (eye and nose at 100,
        // maxBackhomeDist at 200) as soon as any enemy is randomized, which
        // would send him home after 200 m: restore the vanilla Margit values.
        var thinkRow = GameEditor.AddRow(think, hunter.ThinkParam, hunter.SourceThink);
        Set(thinkRow, "NpcThinkParam", "maxBackhomeDist", HunterNeverHome);
        Set(thinkRow, "NpcThinkParam", "backhomeDist", HunterNeverHome);
        Set(thinkRow, "NpcThinkParam", "backhomeBattleDist", HunterNeverHome);
        Set(thinkRow, "NpcThinkParam", "eye_dist", HunterEyeDist);

        Console.WriteLine(
            $"race_items: Hunter rows NpcParam {hunter.NpcParamBase}-" +
            $"{hunter.NpcParamBase + SpeedFogIds.RaceItemHunterTierCount - 1} (clones of {hunter.SourceNpcParam}, " +
            $"one per tier) and NpcThinkParam {hunter.ThinkParam} (clone of {hunter.SourceThink})");
    }

    /// <summary>Margit's own think distances: he never goes home, and sees 60 m.</summary>
    private const int HunterNeverHome = 9999;
    private const int HunterEyeDist = 60;

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
