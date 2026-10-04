using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class ShopInjectorTests
{
    private static PARAM BuildShop(params (int id, uint releaseFlag)[] rows)
    {
        var param = ParamTestHelper.BuildParamFromDef("ShopLineupParam", rows[0].id);
        foreach (var (id, _) in rows.Skip(1))
            ParamTestHelper.AddRowFromTemplate(param, id);
        foreach (var (id, releaseFlag) in rows)
            param.Rows.First(r => r.ID == id)["eventFlag_forRelease"].Value = releaseFlag;
        return param;
    }

    [Fact]
    public void RemovePackGatedRows_RemovesTarnishedPackRows_KeepsOthers()
    {
        // 1.17 vanilla: 101896 (Reverse-Bladed Sword, Twin Maiden Husks) and
        // 100568 (Hefty Scimitar, Nomadic Merchant) unlock on DLC flag 6953;
        // neighbours unlock on a bell bearing flag or are always sold.
        var shop = BuildShop((100568, 6953), (101895, 11109798), (101896, 6953), (101898, 0));

        int removed = ShopInjector.RemovePackGatedRows(shop);

        Assert.Equal(2, removed);
        Assert.Equal(new[] { 101895, 101898 }, shop.Rows.Select(r => r.ID));
    }
}
