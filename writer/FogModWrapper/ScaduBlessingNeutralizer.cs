using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Neutralizes the Scadutree Blessing and Revered Spirit Ash player buffs by
/// copying the level-0 SpEffect row over every higher level (Scadutree
/// 20000101-20000120, Spirit Ash 20000201-20000210), replicating FogMod's
/// GameDataWriterE behavior.
///
/// FogMod only does this when Graph.ExcludeMode == None, the same condition
/// that clamps scaling target tiers 21-28 down to 21 (see
/// docs/enemy-scaling.md). SpeedFog forces ExcludeMode = Base before Write()
/// to disable that clamp, which also skips FogMod's neutralization, so it is
/// re-applied here. Without it, Scadutree fragments placed by the item
/// randomizer would buff the player inside DLC-map zones only, skewing
/// difficulty between zones.
/// </summary>
public static class ScaduBlessingNeutralizer
{
    private const int ScaduLevel0 = 20000100;
    private const int ScaduMaxLevel = 20000120;
    private const int SpiritAshLevel0 = 20000200;
    private const int SpiritAshMaxLevel = 20000210;

    public static void ApplyTo(RegulationEditor reg)
    {
        var spParam = reg.GetParam("SpEffectParam", "SpEffect");
        if (spParam == null)
        {
            Console.WriteLine("Scadu blessings: SpEffectParam unavailable, skipping neutralization");
            return;
        }
        Apply(spParam);
    }

    /// <summary>
    /// Lower-level entry point used by tests. Operates on an already-loaded PARAM.
    /// </summary>
    public static void Apply(PARAM spParam)
    {
        var copied = Neutralize(spParam, ScaduLevel0, ScaduMaxLevel)
                   + Neutralize(spParam, SpiritAshLevel0, SpiritAshMaxLevel);
        Console.WriteLine($"Scadu blessings: neutralized {copied} blessing level row(s)");
    }

    private static int Neutralize(PARAM spParam, int level0Id, int maxLevelId)
    {
        var level0 = spParam[level0Id];
        if (level0 == null)
        {
            Console.WriteLine($"Scadu blessings: row {level0Id} not found, skipping range");
            return 0;
        }

        var copied = 0;
        for (var id = level0Id + 1; id <= maxLevelId; id++)
        {
            var row = spParam[id];
            if (row == null)
                continue;
            GameEditor.CopyRow(level0, row);
            copied++;
        }
        return copied;
    }
}
