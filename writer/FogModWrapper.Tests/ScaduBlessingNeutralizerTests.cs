using FogModWrapper;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class ScaduBlessingNeutralizerTests
{
    private static PARAM BuildSpEffectParam(params int[] rowIds)
    {
        var defPath = Path.Combine(AppContext.BaseDirectory, "eldendata", "Defs", "SpEffect.xml");
        var def = PARAMDEF.XmlDeserialize(defPath);
        var param = new PARAM { ParamType = def.ParamType, Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var id in rowIds)
        {
            param.Rows.Add(new PARAM.Row(id, "", def));
        }
        return param;
    }

    private static void SetBlessingFields(PARAM.Row row, float atkRate, float cutRate)
    {
        row["atkPlayerDmgCorrectRate_Physics"].Value = atkRate;
        row["slashDamageCutRate"].Value = cutRate;
    }

    [Fact]
    public void Apply_CopiesLevel0OverAllBlessingLevels()
    {
        var param = BuildSpEffectParam(
            20000100, 20000105, 20000120,   // Scadutree Blessing levels 0, 5, 20
            20000200, 20000210);            // Revered Spirit Ash levels 0, 10

        SetBlessingFields(param[20000100]!, 1.0f, 1.0f);
        SetBlessingFields(param[20000105]!, 1.35f, 0.7407407f);
        SetBlessingFields(param[20000120]!, 2.05f, 0.4878049f);
        SetBlessingFields(param[20000200]!, 1.0f, 1.0f);
        SetBlessingFields(param[20000210]!, 2.0f, 0.5f);

        ScaduBlessingNeutralizer.Apply(param);

        foreach (var id in new[] { 20000105, 20000120, 20000200, 20000210 })
        {
            Assert.Equal(1.0f, param[id]!["atkPlayerDmgCorrectRate_Physics"].Value);
            Assert.Equal(1.0f, param[id]!["slashDamageCutRate"].Value);
        }
    }

    [Fact]
    public void Apply_MissingLevel0Row_LeavesRangeUntouched()
    {
        // Scadutree level 0 absent: its levels must keep their values.
        // Spirit Ash range is complete and must still be neutralized.
        var param = BuildSpEffectParam(20000110, 20000200, 20000205);
        SetBlessingFields(param[20000110]!, 1.65f, 0.6060606f);
        SetBlessingFields(param[20000200]!, 1.0f, 1.0f);
        SetBlessingFields(param[20000205]!, 1.5f, 0.66f);

        ScaduBlessingNeutralizer.Apply(param);

        Assert.Equal(1.65f, param[20000110]!["atkPlayerDmgCorrectRate_Physics"].Value);
        Assert.Equal(1.0f, param[20000205]!["atkPlayerDmgCorrectRate_Physics"].Value);
    }

    [Fact]
    public void Apply_SparseLevelRows_NeutralizesExistingOnes()
    {
        var param = BuildSpEffectParam(20000100, 20000107);
        SetBlessingFields(param[20000100]!, 1.0f, 1.0f);
        SetBlessingFields(param[20000107]!, 1.47f, 0.68f);

        ScaduBlessingNeutralizer.Apply(param);

        Assert.Equal(1.0f, param[20000107]!["atkPlayerDmgCorrectRate_Physics"].Value);
        Assert.Equal(1.0f, param[20000107]!["slashDamageCutRate"].Value);
    }
}
