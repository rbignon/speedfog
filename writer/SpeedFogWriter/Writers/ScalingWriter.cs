// writer/SpeedFogWriter/Writers/ScalingWriter.cs
using SoulsFormats;
using SoulsIds;

namespace SpeedFogWriter.Writers;

public class ScalingWriter
{
    private const int VanillaScalingBase = 7000;
    private const int CustomScalingBase = 7900000;

    // Scaling multipliers per tier (simplified, 28 tiers)
    private static readonly double[] HealthMultipliers = GenerateMultipliers(1.0, 4.5, 28);
    private static readonly double[] DamageMultipliers = GenerateMultipliers(1.0, 3.5, 28);
    private static readonly double[] DefenseMultipliers = GenerateMultipliers(1.0, 2.0, 28);
    private static readonly double[] SoulMultipliers = GenerateMultipliers(1.0, 10.0, 28);

    private readonly ParamDictionary _params;
    private int _nextSpEffectId;

    public Dictionary<(int From, int To), int> TierTransitions { get; } = new();

    public ScalingWriter(ParamDictionary gameParams)
    {
        _params = gameParams;
        _nextSpEffectId = CustomScalingBase;
    }

    public void GenerateScalingEffects()
    {
        var spEffectParam = _params["SpEffectParam"];
        var templateRow = spEffectParam[VanillaScalingBase];

        for (int fromTier = 1; fromTier <= 28; fromTier++)
        {
            for (int toTier = 1; toTier <= 28; toTier++)
            {
                if (fromTier == toTier) continue;

                var spEffectId = CreateScalingEffect(spEffectParam, templateRow, fromTier, toTier);
                TierTransitions[(fromTier, toTier)] = spEffectId;
            }
        }

        spEffectParam.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        Console.WriteLine($"  Created {TierTransitions.Count} scaling SpEffects");
    }

    private int CreateScalingEffect(PARAM spEffectParam, PARAM.Row template, int fromTier, int toTier)
    {
        var id = _nextSpEffectId++;
        var row = new PARAM.Row(id, "", spEffectParam.AppliedParamdef);

        foreach (var cell in template.Cells)
        {
            row[cell.Def.InternalName].Value = cell.Value;
        }

        double healthFactor = HealthMultipliers[toTier - 1] / HealthMultipliers[fromTier - 1];
        double damageFactor = DamageMultipliers[toTier - 1] / DamageMultipliers[fromTier - 1];
        double defenseFactor = DefenseMultipliers[toTier - 1] / DefenseMultipliers[fromTier - 1];
        double soulFactor = SoulMultipliers[toTier - 1] / SoulMultipliers[fromTier - 1];

        row["maxHpRate"].Value = (float)healthFactor;
        row["maxStaminaRate"].Value = (float)healthFactor;

        row["physicsAttackPowerRate"].Value = (float)damageFactor;
        row["magicAttackPowerRate"].Value = (float)damageFactor;
        row["fireAttackPowerRate"].Value = (float)damageFactor;
        row["thunderAttackPowerRate"].Value = (float)damageFactor;
        row["darkAttackPowerRate"].Value = (float)damageFactor;

        row["physicsDiffenceRate"].Value = (float)defenseFactor;
        row["magicDiffenceRate"].Value = (float)defenseFactor;
        row["fireDiffenceRate"].Value = (float)defenseFactor;
        row["thunderDiffenceRate"].Value = (float)defenseFactor;
        row["darkDiffenceRate"].Value = (float)defenseFactor;

        row["haveSoulRate"].Value = (float)soulFactor;

        spEffectParam.Rows.Add(row);
        return id;
    }

    public int GetTransitionEffect(int fromTier, int toTier)
    {
        if (fromTier == toTier) return -1;
        return TierTransitions.GetValueOrDefault((fromTier, toTier), -1);
    }

    private static double[] GenerateMultipliers(double min, double max, int count)
    {
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / (count - 1);
            result[i] = min * Math.Pow(max / min, t);
        }
        return result;
    }
}
