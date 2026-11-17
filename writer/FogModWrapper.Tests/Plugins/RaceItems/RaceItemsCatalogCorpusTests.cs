using FogModWrapper.Plugins.RaceItems;
using Xunit;

namespace FogModWrapper.Tests.Plugins.RaceItems;

/// <summary>
/// Replays tests/fixtures/race_items/ (see its README): the catalogs the
/// Python loader replays too (tests/test_race_items.py), so the two loaders
/// accept and reject the same ones.
/// </summary>
public class RaceItemsCatalogCorpusTests
{
    private const string ExpectPrefix = "# expect: ";

    private static string CorpusDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "race_items");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/race_items not found above " + AppContext.BaseDirectory);
    }

    public static TheoryData<string> Catalogs(string prefix)
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(CorpusDir(), prefix + "_*.toml").OrderBy(p => p, StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Fact]
    public void Corpus_has_both_kinds()
    {
        Assert.NotEmpty(Catalogs("valid"));
        Assert.NotEmpty(Catalogs("invalid"));
    }

    [Theory]
    [MemberData(nameof(Catalogs), "valid")]
    public void Accepts_the_shared_valid_catalogs(string file)
    {
        RaceItemsCatalog.Parse(File.ReadAllText(Path.Combine(CorpusDir(), file)));
    }

    [Theory]
    [MemberData(nameof(Catalogs), "invalid")]
    public void Rejects_the_shared_invalid_catalogs(string file)
    {
        var toml = File.ReadAllText(Path.Combine(CorpusDir(), file));
        var firstLine = toml.Split('\n')[0].TrimEnd('\r');
        Assert.StartsWith(ExpectPrefix, firstLine);

        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
        Assert.Contains(firstLine[ExpectPrefix.Length..], ex.Message);
    }
}
