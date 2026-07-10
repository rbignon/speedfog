using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class PlayRegionPatcherTests
{
    private const string Field = "pcPositionSaveLimitEventFlagId";

    private static PARAM BuildParam(params (int id, uint flag)[] rows)
    {
        var defPath = Path.Combine(AppContext.BaseDirectory, "eldendata", "Defs", "PlayRegionParam.xml");
        var def = PARAMDEF.XmlDeserialize(defPath);
        var param = new PARAM { ParamType = def.ParamType, Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var (id, flag) in rows)
        {
            var row = new PARAM.Row(id, "", def);
            row[Field].Value = flag;
            param.Rows.Add(row);
        }
        return param;
    }

    [Fact]
    public void Restore_RestoresConstantFlagRows_LeavesBossRowsAlone()
    {
        // Vanilla: row 0 (default ground) + row 100 on always-ON 6001,
        // row 200 on always-OFF 6000, row 300 gated by a boss defeat flag.
        var vanilla = BuildParam((0, 6001), (100, 6001), (200, 6000), (300, 10000800), (400, 0));
        // Modded: FogMod remapped every nonzero flag to temp flags.
        var modded = BuildParam((0, 1040292100), (100, 1040292100), (200, 1040292101), (300, 1040292102), (400, 0));

        int restored = PlayRegionPatcher.Restore(modded, vanilla);

        Assert.Equal(3, restored);
        Assert.Equal(6001u, (uint)modded.Rows.First(r => r.ID == 0)[Field].Value);
        Assert.Equal(6001u, (uint)modded.Rows.First(r => r.ID == 100)[Field].Value);
        Assert.Equal(6000u, (uint)modded.Rows.First(r => r.ID == 200)[Field].Value);
        // Boss-gated row keeps FogMod's temp flag (arena entrance anchoring).
        Assert.Equal(1040292102u, (uint)modded.Rows.First(r => r.ID == 300)[Field].Value);
        Assert.Equal(0u, (uint)modded.Rows.First(r => r.ID == 400)[Field].Value);
    }

    [Fact]
    public void Restore_SkipsVanillaRowsMissingFromModded()
    {
        var vanilla = BuildParam((0, 6001), (500, 6001));
        var modded = BuildParam((0, 1040292100));

        int restored = PlayRegionPatcher.Restore(modded, vanilla);

        Assert.Equal(1, restored);
        Assert.Equal(6001u, (uint)modded.Rows.First(r => r.ID == 0)[Field].Value);
    }

    [Fact]
    public void Restore_NoConstantFlagRows_IsNoOp()
    {
        var vanilla = BuildParam((300, 10000800), (400, 0));
        var modded = BuildParam((300, 1040292102), (400, 0));

        int restored = PlayRegionPatcher.Restore(modded, vanilla);

        Assert.Equal(0, restored);
        Assert.Equal(1040292102u, (uint)modded.Rows.First(r => r.ID == 300)[Field].Value);
    }
}
