using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class MaterialLayoutsTests
{
    [Fact]
    public void Add_KeepsTheSmallestUvSetCountPerStemCaseInsensitively()
    {
        var layouts = new MaterialLayouts();

        layouts.Add(new FlverMaterialEntry("AEG001_001", Skinned: false, UvSets: 2));
        layouts.Add(new FlverMaterialEntry("aeg001_001", Skinned: false, UvSets: 1));
        layouts.Add(new FlverMaterialEntry("AEG001_001", Skinned: false, UvSets: 3));
        layouts.Add(new FlverMaterialEntry("m10_00_519", Skinned: true, UvSets: null));

        Assert.Equal(1, layouts.UvSets["aeg001_001"]);
        Assert.Equal(new[] { "m10_00_519" }, layouts.Skinned);
        Assert.False(layouts.UvSets.ContainsKey("m10_00_519"));
        Assert.True(layouts.Skinned.Contains("M10_00_519"));
    }

    [Fact]
    public void LacksUvSets_HoldsBelowTheNeedAndNeverForStemsNoMeshDraws()
    {
        var layouts = new MaterialLayouts();
        layouts.Add(new FlverMaterialEntry("one", Skinned: false, UvSets: 1));
        layouts.Add(new FlverMaterialEntry("two", Skinned: false, UvSets: 2));

        Assert.False(layouts.LacksUvSets("one", 1, out _));
        Assert.True(layouts.LacksUvSets("ONE", 2, out int sets));
        Assert.Equal(1, sets);
        Assert.False(layouts.LacksUvSets("two", 2, out _));
        Assert.False(layouts.LacksUvSets("unknown", 2, out _));
        Assert.Equal(1, layouts.CountWithFewerUvSetsThan(2));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var layouts = new MaterialLayouts();
        layouts.Add(new FlverMaterialEntry("a", Skinned: true, UvSets: 1));

        layouts.Clear();

        Assert.Empty(layouts.Skinned);
        Assert.Empty(layouts.UvSets);
    }
}
