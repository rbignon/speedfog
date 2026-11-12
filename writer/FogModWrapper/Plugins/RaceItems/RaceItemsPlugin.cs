using FogModWrapper.Common;
using FogModWrapper.Models;
using FogModWrapper.Pipeline;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>
/// [plugin.race_items]: the Mario Kart style race items (goods rows and
/// SpEffects, common.emevd use and removal events, item texts), see
/// docs/plugins/race-items.md. The only setting is <c>enabled</c>. The
/// catalog (data/plugins/race_items.toml) is loaded once, on the first hook,
/// because the data dir comes from the Context. Not a text theme.
/// </summary>
internal sealed class RaceItemsPlugin : SpeedFogPlugin
{
    private IReadOnlyList<RaceItem>? _items;

    public RaceItemsPlugin(PluginConfig config)
    {
        foreach (var (key, _) in config.Extra)
            throw new InvalidDataException($"race_items: unknown parameter '{key}'");
    }

    private IReadOnlyList<RaceItem> Items(Context ctx) =>
        _items ??= RaceItemsCatalog.Load(Path.Combine(ctx.Config.DataDir, "plugins", "race_items.toml"));

    public override void OnRegulation(Context ctx, RegulationEditor reg) =>
        RaceItemsRegulationInjector.Inject(reg, Items(ctx));

    public override void OnCommonEmevd(Context ctx) =>
        RaceItemsEmevdInjector.Inject(ctx.CommonEmevd, ctx.Events, Items(ctx));

    public override void OnModDirLate(Context ctx) =>
        RaceItemsTextInjector.Apply(ctx.ModDir, ctx.Config.GameDir, Items(ctx));
}
