using FogModWrapper;
using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

public class PhantomCatalogLoaderTests
{
    [Fact]
    public void Parse_ValidCatalog_ReturnsAllSkins()
    {
        var toml = """
            [[skins]]
            id = 1450700
            name = "gold-aura"
            display_name = "Golden Phantom"
            edge_color = [255, 215, 0]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0

            [[skins]]
            id = 1450701
            name = "cyan-aura"
            display_name = "Cyan Phantom"
            edge_color = [0, 220, 255]
            edge_power = 0.6
            glow_scale = 0.1
            alpha = 0.9
            """;

        var skins = PhantomCatalogLoader.Parse(toml);

        Assert.Equal(2, skins.Count);
        Assert.Equal(1450700, skins[0].Id);
        Assert.Equal("gold-aura", skins[0].Name);
        Assert.Equal("Golden Phantom", skins[0].DisplayName);
        Assert.Equal((byte)255, skins[0].EdgeColorR);
        Assert.Equal((byte)215, skins[0].EdgeColorG);
        Assert.Equal((byte)0, skins[0].EdgeColorB);
        Assert.Equal(0.5f, skins[0].EdgePower);
        Assert.Equal(0.0f, skins[0].GlowScale);
        Assert.Equal(1.0f, skins[0].Alpha);

        Assert.Equal(1450701, skins[1].Id);
        Assert.Equal("cyan-aura", skins[1].Name);
        Assert.Equal(0.6f, skins[1].EdgePower);
        Assert.Equal(0.1f, skins[1].GlowScale);
        Assert.Equal(0.9f, skins[1].Alpha);
    }

    [Fact]
    public void Parse_DuplicateId_Throws()
    {
        var toml = """
            [[skins]]
            id = 1450700
            name = "a"
            display_name = "A"
            edge_color = [1, 2, 3]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0

            [[skins]]
            id = 1450700
            name = "b"
            display_name = "B"
            edge_color = [4, 5, 6]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0
            """;

        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("duplicate id 1450700", ex.Message);
    }

    [Fact]
    public void Parse_DuplicateName_Throws()
    {
        var toml = """
            [[skins]]
            id = 1450700
            name = "same"
            display_name = "A"
            edge_color = [1, 2, 3]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0

            [[skins]]
            id = 1450701
            name = "same"
            display_name = "B"
            edge_color = [4, 5, 6]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0
            """;

        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("duplicate name 'same'", ex.Message);
    }

    [Fact]
    public void Parse_IdBelowRange_Throws()
    {
        var toml = """
            [[skins]]
            id = 1450699
            name = "low"
            display_name = "Low"
            edge_color = [1, 2, 3]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0
            """;

        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("outside reserved range", ex.Message);
    }

    [Fact]
    public void Parse_IdAboveRange_Throws()
    {
        var toml = """
            [[skins]]
            id = 1450800
            name = "high"
            display_name = "High"
            edge_color = [1, 2, 3]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0
            """;

        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("outside reserved range", ex.Message);
    }

    [Fact]
    public void Parse_EdgeColorWrongLength_Throws()
    {
        var toml = """
            [[skins]]
            id = 1450700
            name = "bad"
            display_name = "Bad"
            edge_color = [1, 2]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0
            """;

        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("edge_color must have exactly 3 elements", ex.Message);
    }

    [Fact]
    public void Parse_EdgeColorOutOfRange_Throws()
    {
        var toml = """
            [[skins]]
            id = 1450700
            name = "bad"
            display_name = "Bad"
            edge_color = [256, 0, 0]
            edge_power = 0.5
            glow_scale = 0.0
            alpha = 1.0
            """;

        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("out of range 0-255", ex.Message);
    }

    [Fact]
    public void Parse_MissingSkinsArray_Throws()
    {
        var toml = "title = \"no skins here\"";
        var ex = Assert.Throws<InvalidDataException>(() => PhantomCatalogLoader.Parse(toml));
        Assert.Contains("[[skins]]", ex.Message);
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.toml");
        var skins = PhantomCatalogLoader.Load(path);
        Assert.Empty(skins);
    }
}
