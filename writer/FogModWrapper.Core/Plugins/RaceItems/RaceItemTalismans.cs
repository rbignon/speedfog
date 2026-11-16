using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>A talisman the Talisman Thief can take: its EquipParamAccessory
/// id, English name and saved removal flag.</summary>
public sealed record RaceItemTalisman(int Id, string Name, int RemoveFlag);

/// <summary>
/// The talisman table of the race items (data/plugins/race_items_talismans.toml,
/// a [[talisman]] array of { id, name }). Ids are strictly ascending, and
/// talisman n (from 0, in file order) gets the removal flag
/// <see cref="SpeedFogIds.RaceItemTalismanRemoveFlagBase"/> + n.
/// </summary>
public static class RaceItemTalismans
{
    private const string Prefix = "race_items talismans:";

    public static IReadOnlyList<RaceItemTalisman> Load(string path)
    {
        var talismans = Parse(File.ReadAllText(path));
        Console.WriteLine($"{Prefix} loaded {talismans.Count} talismans from {path}");
        return talismans;
    }

    public static IReadOnlyList<RaceItemTalisman> Parse(string toml)
    {
        if (Toml.ToModel(toml) is not TomlTable root)
            throw new InvalidDataException($"{Prefix} top-level TOML must be a table");
        if (!root.TryGetValue("talisman", out var obj))
            return Array.Empty<RaceItemTalisman>();
        if (obj is not TomlTableArray entries)
            throw new InvalidDataException($"{Prefix} [[talisman]] must be an array of tables");
        if (entries.Count > SpeedFogIds.RaceItemTalismanRemoveFlagCount)
            throw new InvalidDataException(
                $"{Prefix} {entries.Count} talismans, more than the "
                + $"{SpeedFogIds.RaceItemTalismanRemoveFlagCount} reserved flags");

        var talismans = new List<RaceItemTalisman>();
        foreach (var entry in entries)
        {
            int id = TomlHelpers.ToInt(entry, "id", Prefix);
            string name = TomlHelpers.ToString(entry, "name", Prefix);
            if (id <= 0)
                throw new InvalidDataException($"{Prefix} talisman id {id} must be positive");
            if (name.Length == 0)
                throw new InvalidDataException($"{Prefix} talisman {id}: empty name");
            if (talismans.Count > 0 && id <= talismans[^1].Id)
                throw new InvalidDataException(
                    $"{Prefix} talisman {id} after {talismans[^1].Id}: ids must be strictly ascending");
            talismans.Add(new RaceItemTalisman(
                id, name, SpeedFogIds.RaceItemTalismanRemoveFlagBase + talismans.Count));
        }
        return talismans;
    }
}
