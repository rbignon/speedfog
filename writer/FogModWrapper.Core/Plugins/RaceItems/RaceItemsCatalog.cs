using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>A SpEffectParam row cloned from <see cref="Template"/> with
/// <see cref="Fields"/> (paramdef field name to value) overridden.</summary>
public sealed record RaceItemSpEffect(int Id, int Template, IReadOnlyDictionary<string, double> Fields);

/// <summary>One race item. <see cref="Marker"/> is the SpEffect the goods row
/// applies when used (its id equals <see cref="GoodsId"/>); both it and
/// <see cref="UseFlag"/> are null for the unusable Shield. <see cref="Effect"/>
/// is the extra SpEffect the item's EMEVD event applies, when there is one.</summary>
public sealed record RaceItem(
    string Key, string Icon, int IconId, string Name, string Info, string Caption,
    int GoodsId, int? UseFlag, int RemoveFlag,
    RaceItemSpEffect? Marker, RaceItemSpEffect? Effect);

/// <summary>
/// Loads and validates the race items catalog (data/plugins/race_items.toml),
/// the same file the Python loader (speedfog/race_items.py) reads, with the
/// same validation. Items come back ordered by goods id.
/// </summary>
public static class RaceItemsCatalog
{
    private const string Prefix = "race_items:";

    public static IReadOnlyList<RaceItem> Load(string path)
    {
        var items = Parse(File.ReadAllText(path));
        Console.WriteLine($"{Prefix} loaded {items.Count} items from {path}");
        return items;
    }

    public static IReadOnlyList<RaceItem> Parse(string toml)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException($"{Prefix} top-level TOML must be a table");

        var icons = new Dictionary<string, int>();
        if (root.TryGetValue("icons", out var iObj))
        {
            if (iObj is not TomlTable iTable)
                throw new InvalidDataException($"{Prefix} [icons] must be a table");
            foreach (var (name, _) in iTable)
                icons[name] = TomlHelpers.ToInt(iTable, name, Prefix);
        }

        var items = new List<RaceItem>();
        if (root.TryGetValue("item", out var itemsObj))
        {
            if (itemsObj is not TomlTableArray arr)
                throw new InvalidDataException($"{Prefix} [[item]] must be an array of tables");
            foreach (var entry in arr)
                items.Add(ParseItem(entry, icons));
        }

        var keys = new HashSet<string>();
        var goodsIds = new HashSet<int>();
        foreach (var item in items)
        {
            if (!keys.Add(item.Key))
                throw new InvalidDataException($"{Prefix} duplicate key '{item.Key}'");
            if (!goodsIds.Add(item.GoodsId))
                throw new InvalidDataException($"{Prefix} {item.Key}: duplicate goods_id {item.GoodsId}");
        }

        return items.OrderBy(i => i.GoodsId).ToList();
    }

    private static RaceItem ParseItem(TomlTable e, Dictionary<string, int> icons)
    {
        string key = TomlHelpers.ToString(e, "key", Prefix);
        string icon = TomlHelpers.ToString(e, "icon", Prefix);
        if (!icons.TryGetValue(icon, out int iconId))
            throw new InvalidDataException($"{Prefix} {key}: unknown icon '{icon}'");

        int goodsId = TomlHelpers.ToInt(e, "goods_id", Prefix);
        CheckRange("goods_id", key, goodsId, SpeedFogIds.RaceItemParamIdMin, SpeedFogIds.RaceItemParamIdMax);

        int removeFlag = TomlHelpers.ToInt(e, "remove_flag", Prefix);
        CheckRange("remove_flag", key, removeFlag,
            SpeedFogIds.RaceItemRemoveFlagBase, SpeedFogIds.RaceItemRemoveFlagBase + 99);

        int? useFlag = TomlHelpers.ToIntOpt(e, "use_flag", Prefix);
        if ((useFlag == null) != !e.ContainsKey("marker"))
            throw new InvalidDataException($"{Prefix} {key}: use_flag and marker go together");
        if (useFlag is int uf)
            CheckRange("use_flag", key, uf,
                SpeedFogIds.RaceItemUseFlagBase, SpeedFogIds.RaceItemUseFlagBase + 99);

        RaceItemSpEffect? marker = e.ContainsKey("marker")
            ? ParseSpEffect(e, "marker", key, goodsId)
            : null;

        RaceItemSpEffect? effect = null;
        if (e.ContainsKey("effect"))
        {
            effect = ParseSpEffect(e, "effect", key, null);
            CheckRange("effect id", key, effect.Id, SpeedFogIds.RaceItemParamIdMin, SpeedFogIds.RaceItemParamIdMax);
        }

        return new RaceItem(
            key, icon, iconId,
            TomlHelpers.ToString(e, "name", Prefix),
            TomlHelpers.ToString(e, "info", Prefix),
            TomlHelpers.ToString(e, "caption", Prefix),
            goodsId, useFlag, removeFlag, marker, effect);
    }

    /// <summary>Reads a { [id,] template, fields } inline table. The marker has
    /// no id of its own (<paramref name="fixedId"/>, the goods id).</summary>
    private static RaceItemSpEffect ParseSpEffect(TomlTable e, string field, string key, int? fixedId)
    {
        if (e[field] is not TomlTable t)
            throw new InvalidDataException($"{Prefix} {key}: '{field}' must be a table");

        int id = fixedId ?? TomlHelpers.ToInt(t, "id", Prefix);
        int template = TomlHelpers.ToInt(t, "template", Prefix);

        var fields = new Dictionary<string, double>();
        if (t.TryGetValue("fields", out var fObj))
        {
            if (fObj is not TomlTable fTable)
                throw new InvalidDataException($"{Prefix} {key}: '{field}.fields' must be a table");
            foreach (var (name, value) in fTable)
            {
                fields[name] = value switch
                {
                    double d => d,
                    long l => l,
                    int i => i,
                    _ => throw new InvalidDataException(
                        $"{Prefix} {key}: '{field}.fields.{name}' must be numeric, got {value?.GetType().Name}")
                };
            }
        }

        return new RaceItemSpEffect(id, template, fields);
    }

    private static void CheckRange(string label, string key, int value, int min, int max)
    {
        if (value < min || value > max)
            throw new InvalidDataException($"{Prefix} {key}: {label} {value} outside {min}-{max}");
    }
}
