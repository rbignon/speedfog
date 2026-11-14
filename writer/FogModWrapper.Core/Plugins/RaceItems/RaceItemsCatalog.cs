using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>A SpEffectParam row cloned from <see cref="Template"/> with
/// <see cref="Fields"/> (paramdef field name to value) overridden.</summary>
public sealed record RaceItemSpEffect(int Id, int Template, IReadOnlyDictionary<string, double> Fields);

/// <summary>One goods row carrying race items. <see cref="Marker"/> is the
/// SpEffect the goods row applies when used (its id equals
/// <see cref="GoodsId"/>); both it and <see cref="UseFlag"/> are null for the
/// unusable Shield. The texts (name, info, caption) are the goods'
/// own.</summary>
public sealed record RaceItemGoods(
    string Key, string Icon, int IconId, string Name, string Info, string Caption,
    int GoodsId, int? UseFlag, int RemoveFlag, RaceItemSpEffect? Marker);

/// <summary>One race item, carried by the goods keyed <see cref="Goods"/>.
/// <see cref="Effect"/> is the SpEffect the racing mod applies when the item
/// is used (to its targets, or to the sender), when there is one.</summary>
public sealed record RaceItem(
    string Key, string Goods, string Icon, int IconId, string Name, RaceItemSpEffect? Effect);

/// <summary>
/// The race items catalog (data/plugins/race_items.toml): the goods rows and
/// the items pointing at them. Loaded and validated as the Python loader
/// (speedfog/race_items.py) does. Goods come back ordered by goods id, items
/// in catalog order.
/// </summary>
public sealed record RaceItemsCatalog(IReadOnlyList<RaceItemGoods> Goods, IReadOnlyList<RaceItem> Items)
{
    private const string Prefix = "race_items:";

    public static RaceItemsCatalog Load(string path)
    {
        var catalog = Parse(File.ReadAllText(path));
        Console.WriteLine($"{Prefix} loaded {catalog.Goods.Count} goods and {catalog.Items.Count} items from {path}");
        return catalog;
    }

    public static RaceItemsCatalog Parse(string toml)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException($"{Prefix} top-level TOML must be a table");

        var icons = new Dictionary<string, int>();
        if (root.TryGetValue("icons", out var iObj))
        {
            if (iObj is not TomlTable iTable)
                throw new InvalidDataException($"{Prefix} [icons] must be a table");
            var seenIconIds = new Dictionary<int, string>();
            foreach (var (name, _) in iTable)
            {
                int id = TomlHelpers.ToInt(iTable, name, Prefix);
                CheckRange("icon id", name, id, SpeedFogIds.RaceItemIconIdMin, SpeedFogIds.RaceItemIconIdMax);
                if (seenIconIds.TryGetValue(id, out var other))
                    throw new InvalidDataException($"{Prefix} icons '{name}' and '{other}' share id {id}");
                seenIconIds[id] = name;
                icons[name] = id;
            }
        }

        var goods = new Dictionary<string, RaceItemGoods>();
        var goodsIds = new HashSet<int>();
        foreach (var entry in Tables(root, "goods"))
        {
            var g = ParseGoods(entry, icons);
            if (!goods.TryAdd(g.Key, g))
                throw new InvalidDataException($"{Prefix} duplicate goods key '{g.Key}'");
            if (!goodsIds.Add(g.GoodsId))
                throw new InvalidDataException($"{Prefix} {g.Key}: duplicate goods_id {g.GoodsId}");
        }

        var items = new List<RaceItem>();
        var itemKeys = new HashSet<string>();
        var effectIds = new HashSet<int>();
        foreach (var entry in Tables(root, "item"))
        {
            var item = ParseItem(entry, icons, goods);
            if (!itemKeys.Add(item.Key))
                throw new InvalidDataException($"{Prefix} duplicate item key '{item.Key}'");
            if (item.Effect != null && !effectIds.Add(item.Effect.Id))
                throw new InvalidDataException($"{Prefix} {item.Key}: duplicate effect id {item.Effect.Id}");
            items.Add(item);
        }

        return new RaceItemsCatalog(goods.Values.OrderBy(g => g.GoodsId).ToList(), items);
    }

    /// <summary>The tables of the [[<paramref name="name"/>]] array, none when absent.</summary>
    private static IEnumerable<TomlTable> Tables(TomlTable root, string name)
    {
        if (!root.TryGetValue(name, out var obj))
            return Array.Empty<TomlTable>();
        if (obj is not TomlTableArray arr)
            throw new InvalidDataException($"{Prefix} [[{name}]] must be an array of tables");
        return arr;
    }

    private static RaceItemGoods ParseGoods(TomlTable e, Dictionary<string, int> icons)
    {
        string key = TomlHelpers.ToString(e, "key", Prefix);
        var (icon, iconId) = ParseIcon(e, key, icons);

        int goodsId = TomlHelpers.ToInt(e, "goods_id", Prefix);
        CheckRange("goods_id", key, goodsId, SpeedFogIds.RaceItemParamIdMin, SpeedFogIds.RaceItemGoodsIdMax);

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

        return new RaceItemGoods(
            key, icon, iconId,
            TomlHelpers.ToString(e, "name", Prefix),
            TomlHelpers.ToString(e, "info", Prefix),
            TomlHelpers.ToString(e, "caption", Prefix),
            goodsId, useFlag, removeFlag, marker);
    }

    private static RaceItem ParseItem(
        TomlTable e, Dictionary<string, int> icons, Dictionary<string, RaceItemGoods> goods)
    {
        string key = TomlHelpers.ToString(e, "key", Prefix);
        string goodsKey = TomlHelpers.ToString(e, "goods", Prefix);
        if (!goods.TryGetValue(goodsKey, out var carrier))
            throw new InvalidDataException($"{Prefix} {key}: unknown goods '{goodsKey}'");
        var (icon, iconId) = ParseIcon(e, key, icons);

        RaceItemSpEffect? effect = null;
        if (e.ContainsKey("effect"))
        {
            if (carrier.UseFlag == null)
                throw new InvalidDataException(
                    $"{Prefix} {key}: effect on goods '{goodsKey}', which cannot be used");
            effect = ParseSpEffect(e, "effect", key, null);
            CheckRange("effect id", key, effect.Id, SpeedFogIds.RaceItemEffectIdMin, SpeedFogIds.RaceItemEffectIdMax);
        }

        return new RaceItem(key, goodsKey, icon, iconId, TomlHelpers.ToString(e, "name", Prefix), effect);
    }

    private static (string Icon, int IconId) ParseIcon(TomlTable e, string key, Dictionary<string, int> icons)
    {
        string icon = TomlHelpers.ToString(e, "icon", Prefix);
        if (!icons.TryGetValue(icon, out int iconId))
            throw new InvalidDataException($"{Prefix} {key}: unknown icon '{icon}'");
        return (icon, iconId);
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
