using FogModWrapper;
using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class PhantomCatalogInjectorTests
{
    private static PARAM BuildParamFromDef(string defXmlPath, params int[] templateRowIds)
    {
        var def = PARAMDEF.XmlDeserialize(defXmlPath);
        // Initialize Rows before calling ApplyParamdef so the foreach in
        // ApplyParamdef does not throw on a null collection. RowReader is
        // null for in-memory PARAMs, but that is fine because no rows exist
        // yet at ApplyParamdef time.
        var param = new PARAM { ParamType = def.ParamType, Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var id in templateRowIds)
        {
            param.Rows.Add(new PARAM.Row(id, "", def));
        }
        return param;
    }

    private static string DefsDir() =>
        Path.Combine(AppContext.BaseDirectory, "eldendata", "Defs");

    [Fact]
    public void Apply_CreatesThreeRowsPerSkin_WithSharedId()
    {
        var phantomParam = BuildParamFromDef(Path.Combine(DefsDir(), "PhantomParam.xml"), 260);
        var vfxParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffectVfx.xml"), 51508);
        var spParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffect.xml"), 13177);

        var skins = new List<PhantomSkin>
        {
            new(1450700, "gold", "Gold", 255, 215, 0, 0.5f, 0.0f, 1.0f),
            new(1450701, "cyan", "Cyan", 0, 220, 255, 0.6f, 0.1f, 0.9f),
        };

        PhantomCatalogInjector.Apply(phantomParam, vfxParam, spParam, skins);

        // Three rows created per skin (template rows still present)
        Assert.Equal(3, phantomParam.Rows.Count);
        Assert.Equal(3, vfxParam.Rows.Count);
        Assert.Equal(3, spParam.Rows.Count);

        Assert.NotNull(phantomParam[1450700]);
        Assert.NotNull(vfxParam[1450700]);
        Assert.NotNull(spParam[1450700]);
        Assert.NotNull(phantomParam[1450701]);
        Assert.NotNull(vfxParam[1450701]);
        Assert.NotNull(spParam[1450701]);
    }

    [Fact]
    public void Apply_PhantomParamCarriesEdgeColorAndAura()
    {
        var phantomParam = BuildParamFromDef(Path.Combine(DefsDir(), "PhantomParam.xml"), 260);
        var vfxParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffectVfx.xml"), 51508);
        var spParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffect.xml"), 13177);

        var skin = new PhantomSkin(1450700, "gold", "Gold", 255, 215, 0, 0.5f, 0.0f, 1.0f);
        PhantomCatalogInjector.Apply(phantomParam, vfxParam, spParam, new[] { skin });

        var row = phantomParam[1450700]!;
        Assert.Equal((byte)255, row["edgeColorR"].Value);
        Assert.Equal((byte)215, row["edgeColorG"].Value);
        Assert.Equal((byte)0, row["edgeColorB"].Value);
        Assert.Equal(1.0f, row["edgeColorA"].Value);  // alpha is written to edge alpha too
        Assert.Equal(0.5f, row["edgePower"].Value);
        Assert.Equal(0.0f, row["glowScale"].Value);
        Assert.Equal(1.0f, row["alpha"].Value);
    }

    [Fact]
    public void Apply_VfxRowPointsAtPhantomParam()
    {
        var phantomParam = BuildParamFromDef(Path.Combine(DefsDir(), "PhantomParam.xml"), 260);
        var vfxParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffectVfx.xml"), 51508);
        var spParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffect.xml"), 13177);

        var skin = new PhantomSkin(1450700, "gold", "Gold", 255, 215, 0, 0.5f, 0.0f, 1.0f);
        PhantomCatalogInjector.Apply(phantomParam, vfxParam, spParam, new[] { skin });

        var row = vfxParam[1450700]!;
        Assert.Equal(1450700, row["phantomParamOverwriteId"].Value);
    }

    [Fact]
    public void Apply_SpEffectRowPointsAtVfx()
    {
        var phantomParam = BuildParamFromDef(Path.Combine(DefsDir(), "PhantomParam.xml"), 260);
        var vfxParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffectVfx.xml"), 51508);
        var spParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffect.xml"), 13177);

        var skin = new PhantomSkin(1450700, "gold", "Gold", 255, 215, 0, 0.5f, 0.0f, 1.0f);
        PhantomCatalogInjector.Apply(phantomParam, vfxParam, spParam, new[] { skin });

        var row = spParam[1450700]!;
        Assert.Equal(1450700, row["vfxId"].Value);
    }

    [Fact]
    public void Apply_EmptyCatalog_NoOp()
    {
        var phantomParam = BuildParamFromDef(Path.Combine(DefsDir(), "PhantomParam.xml"), 260);
        var vfxParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffectVfx.xml"), 51508);
        var spParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffect.xml"), 13177);

        PhantomCatalogInjector.Apply(phantomParam, vfxParam, spParam, Array.Empty<PhantomSkin>());

        Assert.Single(phantomParam.Rows);
        Assert.Single(vfxParam.Rows);
        Assert.Single(spParam.Rows);
    }

    [Fact]
    public void Apply_ZeroesNonAuraColorComponents()
    {
        // Aura-only intent: frontColor*, diffMulColor*, specMulColor*, lightColor*
        // must not carry over template defaults that could tint the model.
        var phantomParam = BuildParamFromDef(Path.Combine(DefsDir(), "PhantomParam.xml"), 260);
        var vfxParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffectVfx.xml"), 51508);
        var spParam = BuildParamFromDef(Path.Combine(DefsDir(), "SpEffect.xml"), 13177);

        // Pre-fill template row 260 with non-zero values to simulate a "dirty" template.
        // diffMulColor* defaults to 255 in vanilla, so this simulates real game data.
        var template = phantomParam[260]!;
        template["frontColorR"].Value = (byte)200;
        template["diffMulColorR"].Value = (byte)100;
        template["specMulColorR"].Value = (byte)180;

        var skin = new PhantomSkin(1450700, "gold", "Gold", 255, 215, 0, 0.5f, 0.0f, 1.0f);
        PhantomCatalogInjector.Apply(phantomParam, vfxParam, spParam, new[] { skin });

        var row = phantomParam[1450700]!;
        Assert.Equal((byte)0, row["frontColorR"].Value);
        Assert.Equal((byte)0, row["diffMulColorR"].Value);
        // specMulColor is also zeroed (different field group; proves the full array is iterated)
        Assert.Equal((byte)0, row["specMulColorR"].Value);
    }
}
