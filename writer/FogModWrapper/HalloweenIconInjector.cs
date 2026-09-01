using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Repoints Golden Seed / Sacred Tear inventory icons to
/// the Halloween icons shipped by the speedfog-halloween overlay
/// (05_dummy.tpf.dcx superset built by StaticModBuilder). The engine
/// resolves the new MENU_ItemIcon_{60383,60384} names via its
/// loaded-TPF fallback lookup;
/// see docs/plugins/halloween-icons.md, including the in-game check this
/// experiment still owes and its revert path. Opt-in via
/// [plugin.halloween]; without the plugin, iconId stays vanilla and the
/// overlay is not even shipped.
/// </summary>
public static class HalloweenIconInjector
{
    // (goods row, halloween icon id) pairs; vanilla ids in SpeedFogIds docs.
    private static readonly (int Row, int IconId)[] Redirects =
    {
        (10010, SpeedFogIds.HalloweenGoldenSeedIcon),
        (10020, SpeedFogIds.HalloweenSacredTearIcon),
    };

    public static void ApplyTo(RegulationEditor reg)
    {
        var goods = reg.GetParam("EquipParamGoods");
        if (goods == null)
        {
            Console.WriteLine("Halloween icons: EquipParamGoods unavailable, icons stay vanilla");
            return;
        }
        int changed = Apply(goods);
        Console.WriteLine($"Halloween icons: repointed {changed} item icon id(s)");
    }

    public static int Apply(PARAM goods)
    {
        int changed = 0;
        foreach (var (rowId, iconId) in Redirects)
        {
            var row = goods[rowId];
            if (row == null)
            {
                Console.WriteLine($"Halloween icons: EquipParamGoods row {rowId} missing, skipping");
                continue;
            }
            row["iconId"].Value = (ushort)iconId; // u16 per Defs
            changed++;
        }
        return changed;
    }
}
