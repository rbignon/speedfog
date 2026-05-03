using Xunit;

namespace FogModWrapper.Tests;

public class OpenSplitOverrideLoaderTests
{
    [Fact]
    public void Parse_NoWarpsSection_ReturnsEmpty()
    {
        var toml = """
            [defaults]
            legacy_dungeon = 3
            """;

        var ids = OpenSplitOverrideLoader.Parse(toml);

        Assert.Empty(ids);
    }

    [Fact]
    public void Parse_OpensplitTrue_ReturnsId()
    {
        var toml = """
            [warps."15002600"]
            opensplit = true
            """;

        var ids = OpenSplitOverrideLoader.Parse(toml);

        Assert.Contains("15002600", ids);
        Assert.Single(ids);
    }

    [Fact]
    public void Parse_OpensplitFalse_Excluded()
    {
        var toml = """
            [warps."12052020"]
            opensplit = false
            """;

        var ids = OpenSplitOverrideLoader.Parse(toml);

        Assert.Empty(ids);
    }

    [Fact]
    public void Parse_WarpWithoutOpensplitKey_Excluded()
    {
        var toml = """
            [warps."12052020"]
            note = "kept for future use"
            """;

        var ids = OpenSplitOverrideLoader.Parse(toml);

        Assert.Empty(ids);
    }

    [Fact]
    public void Parse_MultipleWarps_FiltersOnlyOpensplitTrue()
    {
        var toml = """
            [warps."15002600"]
            opensplit = true

            [warps."12052020"]
            opensplit = false

            [warps."99999999"]
            opensplit = true
            """;

        var ids = OpenSplitOverrideLoader.Parse(toml);

        Assert.Equal(new HashSet<string> { "15002600", "99999999" }, ids);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.toml");

        var ids = OpenSplitOverrideLoader.Load(path);

        Assert.Empty(ids);
    }

    [Fact]
    public void Load_IgnoresUnrelatedSections()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensplit-{Guid.NewGuid():N}.toml");
        try
        {
            File.WriteAllText(path, """
                [defaults]
                legacy_dungeon = 3

                [zones.haligtree]
                weight = 8

                [warps."15002600"]
                opensplit = true
                """);

            var ids = OpenSplitOverrideLoader.Load(path);

            Assert.Equal(new HashSet<string> { "15002600" }, ids);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
