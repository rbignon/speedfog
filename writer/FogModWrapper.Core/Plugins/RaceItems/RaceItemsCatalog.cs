using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>A SpEffectParam row cloned from <see cref="Template"/> with
/// <see cref="Fields"/> (paramdef field name to value) overridden.</summary>
public sealed record RaceItemSpEffect(int Id, int Template, IReadOnlyDictionary<string, double> Fields);

/// <summary>One goods row carrying race items. <see cref="Marker"/> is the
/// SpEffect the goods row applies when used (its id equals
/// <see cref="GoodsId"/>); both it and <see cref="UseFlag"/> are null for the
/// unusable Shield. The flags are derived from the goods id, not read from
/// the catalog: <see cref="RemoveFlag"/> is
/// <see cref="SpeedFogIds.RaceItemRemoveFlagBase"/> + n and
/// <see cref="UseFlag"/> <see cref="SpeedFogIds.RaceItemUseFlagBase"/> + n,
/// n = goods id - <see cref="SpeedFogIds.RaceItemParamIdMin"/>. The texts
/// (name, info, caption) are the goods' own.</summary>
public sealed record RaceItemGoods(
    string Key, string Icon, int IconId, string Name, string Info, string Caption,
    int GoodsId, int? UseFlag, int RemoveFlag, RaceItemSpEffect? Marker);

/// <summary>One race item, carried by the goods keyed <see cref="Goods"/>.
/// <see cref="Effect"/> is the SpEffect the racing mod applies when the item
/// is used (to its targets, or to the sender), when there is one.</summary>
public sealed record RaceItem(
    string Key, string Goods, string Icon, int IconId, string Name, RaceItemSpEffect? Effect);

/// <summary>The Hunter's rows: NpcParam <see cref="SourceNpcParam"/> and
/// NpcThinkParam <see cref="SourceThink"/> (Margit, the Fell Omen) are cloned
/// into one NpcParam row per enemy tier from <see cref="NpcParamBase"/> and
/// one think row <see cref="ThinkParam"/>; the racing mod spawns model
/// <see cref="ChrId"/> (c2130).</summary>
public sealed record RaceItemHunter(int ChrId, int SourceNpcParam, int SourceThink, int NpcParamBase, int ThinkParam);

/// <summary>
/// The race items catalog (data/plugins/race_items.toml): the goods rows and
/// the items pointing at them. Loaded and validated as the Python loader
/// (speedfog/race_items.py) does: both replay the catalogs of
/// tests/fixtures/race_items/, and both reject an unknown key. The
/// [[junk_weapon]] entries are the exception: only Python reads them (they
/// reach the racing mod through graph.json), so only their key is known here.
/// Goods come back ordered by goods id, items in catalog order.
/// </summary>
public sealed record RaceItemsCatalog(
    IReadOnlyList<RaceItemGoods> Goods, IReadOnlyList<RaceItem> Items, RaceItemHunter? Hunter = null)
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
        TomlHelpers.CheckKeys(root, "top level", Prefix, "icons", "goods", "item", "junk_weapon", "hunter");
        Tables(root, "junk_weapon");

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

        return new RaceItemsCatalog(
            goods.Values.OrderBy(g => g.GoodsId).ToList(), items, ParseHunter(root));
    }

    /// <summary>The optional [hunter] table; every row it writes must sit in
    /// the reserved NpcParam range.</summary>
    private static RaceItemHunter? ParseHunter(TomlTable root)
    {
        if (!root.TryGetValue("hunter", out var obj))
            return null;
        if (obj is not TomlTable t)
            throw new InvalidDataException($"{Prefix} [hunter] must be a table");
        TomlHelpers.CheckKeys(t, "hunter", Prefix,
            "chr_id", "source_npc_param", "source_think", "npc_param_base", "think_param");

        int chrId = TomlHelpers.ToInt(t, "chr_id", Prefix);
        int sourceNpc = TomlHelpers.ToInt(t, "source_npc_param", Prefix);
        int sourceThink = TomlHelpers.ToInt(t, "source_think", Prefix);
        int baseId = TomlHelpers.ToInt(t, "npc_param_base", Prefix);
        int thinkParam = TomlHelpers.ToInt(t, "think_param", Prefix);
        if (chrId <= 0)
            throw new InvalidDataException($"{Prefix} hunter chr id {chrId} must be positive");
        CheckRange("npc_param_base", "hunter", baseId,
            SpeedFogIds.RaceItemNpcParamIdMin, SpeedFogIds.RaceItemNpcParamIdMax);
        CheckRange("last tier row", "hunter", baseId + SpeedFogIds.RaceItemHunterTierCount - 1,
            SpeedFogIds.RaceItemNpcParamIdMin, SpeedFogIds.RaceItemNpcParamIdMax);
        CheckRange("think_param", "hunter", thinkParam,
            SpeedFogIds.RaceItemNpcParamIdMin, SpeedFogIds.RaceItemNpcParamIdMax);
        return new RaceItemHunter(chrId, sourceNpc, sourceThink, baseId, thinkParam);
    }

    private static IReadOnlyList<TomlTable> Tables(TomlTable root, string name) =>
        TomlHelpers.Tables(root, name, Prefix);

    private static RaceItemGoods ParseGoods(TomlTable e, Dictionary<string, int> icons)
    {
        string key = TomlHelpers.ToString(e, "key", Prefix);
        TomlHelpers.CheckKeys(e, key, Prefix, "key", "icon", "name", "info", "caption", "goods_id", "marker");
        var (icon, iconId) = ParseIcon(e, key, icons);

        int goodsId = TomlHelpers.ToInt(e, "goods_id", Prefix);
        CheckRange("goods_id", key, goodsId, SpeedFogIds.RaceItemParamIdMin, SpeedFogIds.RaceItemGoodsIdMax);

        // A goods can be used when it has a marker; its flags follow its id.
        RaceItemSpEffect? marker = e.ContainsKey("marker")
            ? ParseSpEffect(e, "marker", key, goodsId)
            : null;
        int n = goodsId - SpeedFogIds.RaceItemParamIdMin;
        int? useFlag = marker != null ? SpeedFogIds.RaceItemUseFlagBase + n : null;
        int removeFlag = SpeedFogIds.RaceItemRemoveFlagBase + n;

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
        TomlHelpers.CheckKeys(e, key, Prefix, "key", "goods", "icon", "name", "duration_s", "effect");
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

        // The duration only reaches graph.json (Python); checked here too, so
        // both loaders reject the same catalogs.
        // At least 1 ms once rounded: the export is in milliseconds.
        if (e.TryGetValue("duration_s", out var dObj)
            && !(AsNumber(dObj) is double duration && Math.Round(duration * 1000) >= 1))
            throw new InvalidDataException(
                $"{Prefix} {key}: duration_s must be a finite number of at least 1 ms");

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
        if (fixedId == null)
            TomlHelpers.CheckKeys(t, $"{key}: {field}", Prefix, "id", "template", "fields");
        else
            TomlHelpers.CheckKeys(t, $"{key}: {field}", Prefix, "template", "fields");

        int id = fixedId ?? TomlHelpers.ToInt(t, "id", Prefix);
        int template = TomlHelpers.ToInt(t, "template", Prefix);

        var fields = new Dictionary<string, double>();
        if (t.TryGetValue("fields", out var fObj))
        {
            if (fObj is not TomlTable fTable)
                throw new InvalidDataException($"{Prefix} {key}: '{field}.fields' must be a table");
            foreach (var (name, value) in fTable)
            {
                fields[name] = AsNumber(value) ?? throw new InvalidDataException(
                    $"{Prefix} {key}: '{field}.fields.{name}' must be numeric, got {value?.GetType().Name}");
            }
        }

        return new RaceItemSpEffect(id, template, fields);
    }

    /// <summary>A finite TOML number (not a boolean, inf or nan), else null.</summary>
    private static double? AsNumber(object? value) => value switch
    {
        double d when double.IsFinite(d) => d,
        long l => l,
        int i => i,
        _ => null
    };

    private static void CheckRange(string label, string key, int value, int min, int max)
    {
        if (value < min || value > max)
            throw new InvalidDataException($"{Prefix} {key}: {label} {value} outside {min}-{max}");
    }
}
