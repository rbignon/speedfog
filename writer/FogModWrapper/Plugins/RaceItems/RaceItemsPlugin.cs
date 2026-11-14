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
    private RaceItemsCatalog? _catalog;

    public RaceItemsPlugin(PluginConfig config)
    {
        foreach (var (key, _) in config.Extra)
            throw new InvalidDataException($"race_items: unknown parameter '{key}'");
    }

    private RaceItemsCatalog Catalog(Context ctx) =>
        _catalog ??= RaceItemsCatalog.Load(Path.Combine(ctx.Config.DataDir, "plugins", "race_items.toml"));

    public override void OnRegulation(Context ctx, RegulationEditor reg) =>
        RaceItemsRegulationInjector.Inject(reg, Catalog(ctx));

    public override void OnCommonEmevd(Context ctx) =>
        RaceItemsEmevdInjector.Inject(ctx.CommonEmevd, ctx.Events, Catalog(ctx).Goods);

    public override void OnModDirLate(Context ctx) =>
        RaceItemsTextInjector.Apply(ctx.ModDir, ctx.Config.GameDir, Catalog(ctx).Goods);
}
