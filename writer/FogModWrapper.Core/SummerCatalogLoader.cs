using FogModWrapper.Models;
using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Loads and validates the summer theme catalogue (data/plugins/summer.toml).
/// Mirrors PhantomCatalogLoader. Returns an empty catalogue when the file is
/// absent so the theme becomes a silent no-op.
/// </summary>
public static class SummerCatalogLoader
{
    // GR_MenuText id repurposed by RunCompleteInjector; must not be overridden.
    public const int RunCompleteReservedFmgId = 331314;

    public static SummerCatalog Parse(string toml)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException("summer: top-level TOML must be a table");

        var bosses = new List<SummerBossEntry>();
        if (root.TryGetValue("bosses", out var bObj))
        {
            if (bObj is not TomlTableArray bArr)
                throw new InvalidDataException("summer: [[bosses]] must be an array of tables");
            foreach (var entry in bArr)
                bosses.Add(new SummerBossEntry(
                    ToInt(entry, "npc_name_id"), ToStringOpt(entry, "name") ?? "",
                    ToString(entry, "en"), ToStringOpt(entry, "fr")));
        }

        var ui = new List<SummerUiEntry>();
        if (root.TryGetValue("ui", out var uObj))
        {
            if (uObj is not TomlTableArray uArr)
                throw new InvalidDataException("summer: [[ui]] must be an array of tables");
            foreach (var entry in uArr)
                ui.Add(new SummerUiEntry(
                    ToString(entry, "bnd"), ToString(entry, "fmg"), ToInt(entry, "id"),
                    ToString(entry, "en"), ToStringOpt(entry, "fr")));
        }

        Validate(bosses, ui);
        return new SummerCatalog(bosses, ui);
    }

    public static SummerCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Summer theme: no catalogue at {path}, skipping");
            return SummerCatalog.Empty;
        }

        var catalog = Parse(File.ReadAllText(path));
        Console.WriteLine(
            $"Summer theme: loaded {catalog.Bosses.Count} boss + {catalog.Ui.Count} UI entries from {path}");
        return catalog;
    }

    private static void Validate(List<SummerBossEntry> bosses, List<SummerUiEntry> ui)
    {
        var seen = new HashSet<int>();
        foreach (var b in bosses)
        {
            if (string.IsNullOrWhiteSpace(b.En))
                throw new InvalidDataException($"summer: boss npc_name_id {b.NpcNameId} has empty 'en'");
            if (!seen.Add(b.NpcNameId))
                throw new InvalidDataException($"summer: duplicate boss npc_name_id {b.NpcNameId}");
        }

        foreach (var u in ui)
        {
            if (string.IsNullOrWhiteSpace(u.En))
                throw new InvalidDataException($"summer: ui {u.Fmg}[{u.Id}] has empty 'en'");
            if (string.Equals(u.Fmg, "GR_MenuText", StringComparison.OrdinalIgnoreCase) && u.Id == RunCompleteReservedFmgId)
                throw new InvalidDataException(
                    $"summer: ui GR_MenuText[{RunCompleteReservedFmgId}] is reserved by RunCompleteInjector");
        }
    }

    private static int ToInt(TomlTable e, string key)
    {
        if (!e.TryGetValue(key, out var v))
            throw new InvalidDataException($"summer: missing field '{key}'");
        return v switch
        {
            long l => checked((int)l),
            int i => i,
            _ => throw new InvalidDataException($"summer: field '{key}' must be integer")
        };
    }

    private static string ToString(TomlTable e, string key)
    {
        if (!e.TryGetValue(key, out var v) || v is not string s)
            throw new InvalidDataException($"summer: missing or non-string field '{key}'");
        return s;
    }

    private static string? ToStringOpt(TomlTable e, string key)
        => e.TryGetValue(key, out var v) && v is string s ? s : null;
}
