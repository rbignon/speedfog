using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class StartingRuneInjectorTests
{
    private static PARAM MakeCharaInitParam(params int[] rowIds)
    {
        var def = new PARAMDEF { ParamType = "CHARA_INIT_PARAM_ST" };
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "soul"));
        var param = new PARAM { Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var id in rowIds)
        {
            var row = new PARAM.Row(id, "", def);
            row["soul"].Value = 0;
            param.Rows.Add(row);
        }
        return param;
    }

    [Fact]
    public void Apply_SetsSoulOnClassRowsOnly()
    {
        // 3000: Vagabond origin row; 3010/3120: Idus Knight (1.17); 24700: an NPC row.
        var chara = MakeCharaInitParam(3000, 3010, 3120, 24700);
        var classRows = new SortedSet<int> { 3000, 3010, 3120 };

        int updated = StartingRuneInjector.Apply(chara, classRows, 100_000);

        Assert.Equal(3, updated);
        Assert.Equal(100_000, (int)chara.Rows.Find(r => r.ID == 3010)!["soul"].Value);
        Assert.Equal(100_000, (int)chara.Rows.Find(r => r.ID == 3120)!["soul"].Value);
        Assert.Equal(0, (int)chara.Rows.Find(r => r.ID == 24700)!["soul"].Value);
    }
}
