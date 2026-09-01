using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests;

public class HalloweenIconInjectorTests
{
    [Fact]
    public void Apply_RepointsAllIconIds()
    {
        var goods = BuildParamFromDef("EquipParamGoods", templateId: 10010);
        var row8185 = AddRowFromTemplate(goods, 8185);
        var row10020 = AddRowFromTemplate(goods, 10020);
        goods[10010]!["iconId"].Value = (ushort)383;
        row8185["iconId"].Value = (ushort)3075;
        row10020["iconId"].Value = (ushort)384;

        int changed = HalloweenIconInjector.Apply(goods);

        Assert.Equal(3, changed);
        Assert.Equal((ushort)SpeedFogIds.HalloweenGoldenSeedIcon, (ushort)goods[10010]!["iconId"].Value);
        Assert.Equal((ushort)SpeedFogIds.HalloweenLarvalTearIcon, (ushort)goods[8185]!["iconId"].Value);
        Assert.Equal((ushort)SpeedFogIds.HalloweenSacredTearIcon, (ushort)goods[10020]!["iconId"].Value);
    }

    [Fact]
    public void Apply_MissingRowIsWarnedNotFatal()
    {
        var goods = BuildParamFromDef("EquipParamGoods", templateId: 10010);
        goods[10010]!["iconId"].Value = (ushort)383;

        int changed = HalloweenIconInjector.Apply(goods);

        Assert.Equal(1, changed);
    }
}
