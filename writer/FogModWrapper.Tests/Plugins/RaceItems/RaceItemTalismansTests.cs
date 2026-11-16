using FogModWrapper.Plugins.RaceItems;
using Xunit;

namespace FogModWrapper.Tests.Plugins.RaceItems;

public class RaceItemTalismansTests
{
    private static string ShippedTablePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "plugins", "race_items_talismans.toml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "data/plugins/race_items_talismans.toml not found above " + AppContext.BaseDirectory);
    }

    private static string Entry(int id, string name = "T") => $"[[talisman]]\nid = {id}\nname = \"{name}\"\n";

    [Fact]
    public void Shipped_table_fits_the_reserved_flags_and_numbers_them_in_order()
    {
        var talismans = RaceItemTalismans.Load(ShippedTablePath());

        Assert.InRange(talismans.Count, 1, SpeedFogIds.RaceItemTalismanRemoveFlagCount);
        Assert.Equal(
            Enumerable.Range(SpeedFogIds.RaceItemTalismanRemoveFlagBase, talismans.Count),
            talismans.Select(t => t.RemoveFlag));
    }

    [Fact]
    public void Flags_follow_the_file_order()
    {
        var talismans = RaceItemTalismans.Parse(Entry(1000, "A") + Entry(1001, "B") + Entry(8240, "C"));

        Assert.Equal(new[] { 1000, 1001, 8240 }, talismans.Select(t => t.Id));
        Assert.Equal(new[] { "A", "B", "C" }, talismans.Select(t => t.Name));
        Assert.Equal(
            new[] { 1050290300, 1050290301, 1050290302 },
            talismans.Select(t => t.RemoveFlag));
    }

    [Fact]
    public void An_empty_table_has_no_talismans()
    {
        Assert.Empty(RaceItemTalismans.Parse("# nothing\n"));
    }

    [Theory]
    [InlineData(1001, 1000)] // descending
    [InlineData(1000, 1000)] // duplicate
    public void Rejects_ids_not_strictly_ascending(int first, int second)
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            RaceItemTalismans.Parse(Entry(first) + Entry(second)));
        Assert.Contains("ascending", ex.Message);
    }

    [Fact]
    public void Rejects_more_talismans_than_reserved_flags()
    {
        var toml = string.Concat(Enumerable.Range(1, SpeedFogIds.RaceItemTalismanRemoveFlagCount + 1)
            .Select(i => Entry(i)));
        Assert.Throws<InvalidDataException>(() => RaceItemTalismans.Parse(toml));
    }

    [Theory]
    [InlineData("[[talisman]]\nname = \"T\"\n")] // no id
    [InlineData("[[talisman]]\nid = 1000\n")] // no name
    [InlineData("[[talisman]]\nid = 1000\nname = \"\"\n")] // empty name
    [InlineData("[[talisman]]\nid = 0\nname = \"T\"\n")] // id not positive
    [InlineData("talisman = 3\n")] // not an array of tables
    public void Rejects_malformed_entries(string toml)
    {
        Assert.Throws<InvalidDataException>(() => RaceItemTalismans.Parse(toml));
    }
}
