using FogModWrapper.Models;
using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Loads and validates a theme text catalogue (data/plugins/&lt;theme&gt;.toml).
/// Mirrors PhantomCatalogLoader. Returns an empty catalogue when the file is
/// absent so the theme becomes a silent no-op.
/// </summary>
public static class TextThemeCatalogLoader
{
    // GR_MenuText id repurposed by RunCompleteInjector; must not be overridden.
    public const int RunCompleteReservedFmgId = 331314;

    public static ThemeCatalog Parse(string toml, string theme)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException($"{theme}: top-level TOML must be a table");

        var bosses = new List<ThemeBossEntry>();
        if (root.TryGetValue("bosses", out var bObj))
        {
            if (bObj is not TomlTableArray bArr)
                throw new InvalidDataException($"{theme}: [[bosses]] must be an array of tables");
            foreach (var entry in bArr)
                bosses.Add(new ThemeBossEntry(
                    ToIntOpt(entry, "npc_name_id", theme), ToStringOpt(entry, "boss_name"), ToStringOpt(entry, "name") ?? "",
                    ToString(entry, "en", theme), ToStringOpt(entry, "fr")));
        }

        var ui = new List<ThemeUiEntry>();
        if (root.TryGetValue("ui", out var uObj))
        {
            if (uObj is not TomlTableArray uArr)
                throw new InvalidDataException($"{theme}: [[ui]] must be an array of tables");
            foreach (var entry in uArr)
                ui.Add(new ThemeUiEntry(
                    ToString(entry, "bnd", theme), ToString(entry, "fmg", theme), ToInt(entry, "id", theme),
                    ToString(entry, "en", theme), ToStringOpt(entry, "fr")));
        }

        Validate(bosses, ui, theme);
        return new ThemeCatalog(bosses, ui);
    }

    public static ThemeCatalog Load(string path, string theme)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"{theme} theme: no catalogue at {path}, skipping");
            return ThemeCatalog.Empty;
        }

        var catalog = Parse(File.ReadAllText(path), theme);
        Console.WriteLine(
            $"{theme} theme: loaded {catalog.Bosses.Count} boss + {catalog.Ui.Count} UI entries from {path}");
        return catalog;
    }

    private static void Validate(List<ThemeBossEntry> bosses, List<ThemeUiEntry> ui, string theme)
    {
        var seenIds = new HashSet<int>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var allocated = SpeedFogIds.BossNameFmgIds;
        foreach (var b in bosses)
        {
            var key = b.NpcNameId is int id ? $"npc_name_id {id}" : $"boss_name \"{b.BossName}\"";
            if ((b.NpcNameId == null) == (b.BossName == null))
                throw new InvalidDataException(
                    $"{theme}: boss (en \"{b.En}\") needs exactly one of npc_name_id and boss_name");
            if (string.IsNullOrWhiteSpace(b.En))
                throw new InvalidDataException($"{theme}: boss {key} has empty 'en'");
            // Ids in BossNameInjector's range change per seed: use boss_name.
            if (b.NpcNameId is int ranged && ranged >= allocated.Base && ranged < allocated.End)
                throw new InvalidDataException(
                    $"{theme}: boss {key} is a per-seed BossNameInjector id, key it by boss_name instead");
            // BossNameInjector allocates trimmed names and drops a trailing
            // parenthetical, so any other key would never match.
            if (b.BossName is string name
                && (name.Length == 0 || name != name.Trim() || name.EndsWith(')')))
                throw new InvalidDataException(
                    $"{theme}: boss {key} can never match: use the name as BossNameInjector logs it "
                    + "(trimmed, without a trailing parenthetical)");
            if (b.NpcNameId is int nid ? !seenIds.Add(nid) : !seenNames.Add(b.BossName!))
                throw new InvalidDataException($"{theme}: duplicate boss {key}");
        }

        foreach (var u in ui)
        {
            if (string.IsNullOrWhiteSpace(u.En))
                throw new InvalidDataException($"{theme}: ui {u.Fmg}[{u.Id}] has empty 'en'");
            if (string.Equals(u.Fmg, "GR_MenuText", StringComparison.OrdinalIgnoreCase) && u.Id == RunCompleteReservedFmgId)
                throw new InvalidDataException(
                    $"{theme}: ui GR_MenuText[{RunCompleteReservedFmgId}] is reserved by RunCompleteInjector");
        }
    }

    private static int ToInt(TomlTable e, string key, string theme) => TomlHelpers.ToInt(e, key, $"{theme}:");

    private static int? ToIntOpt(TomlTable e, string key, string theme) => TomlHelpers.ToIntOpt(e, key, $"{theme}:");

    private static string ToString(TomlTable e, string key, string theme) => TomlHelpers.ToString(e, key, $"{theme}:");

    private static string? ToStringOpt(TomlTable e, string key)
        => e.TryGetValue(key, out var v) && v is string s ? s : null;
}
