using FogModWrapper.Common;
using FogModWrapper.Models;
using FogModWrapper.Pipeline;

namespace FogModWrapper.Plugins.Halloween;

/// <summary>
/// [plugin.halloween]: text theme, ambient gate decorations and spawns,
/// jumpscares, item icons and darkness (see docs/plugins/halloween-*.md).
/// Owns the two-phase guard state: <see cref="OnRegulation"/> writes the
/// NpcParam rows, <see cref="OnModDirMsb"/> places a flavor only if its row
/// exists (its parts would otherwise point at a missing row).
/// </summary>
internal sealed class HalloweenPlugin : SpeedFogPlugin
{
    public HalloweenPluginSettings.Settings Settings { get; }

    // Drawn in AfterFogModWrite (its mirror flags must reach
    // PatchEmevdPhase), placed by OnModDirMsb.
    private JumpscarePlan _jumpscarePlan = JumpscarePlan.Empty;

    // Set by OnRegulation. Stay false when the flavor is disabled or the
    // regulation phase warn-returned (OnRegulation never called).
    private bool _greeterRowApplied;
    private bool _ambusherRowsApplied;
    private bool _jumpscareRowApplied;

    public HalloweenPlugin(PluginConfig config) => Settings = HalloweenPluginSettings.Parse(config);

    public override string Name => "halloween";

    public override string? TextTheme => "halloween";

    public override void AfterFogModWrite(Context ctx)
    {
        // Jumpscares wait on private mirror flags set next to the
        // zone-tracking flags (the racing mod clears those right after
        // capture), so the plan must exist before PatchEmevdPhase injects
        // RegionToFlags. Without jumpscares the table is left untouched.
        if (Settings.Jumpscares <= 0)
            return;
        _jumpscarePlan = JumpscarePlanner.Plan(
            ctx.GraphData, JumpscarePlanner.BuildZoneMaps(ctx.Ann),
            ctx.Ann.Areas.Where(a => a.DefeatFlag > 0).Select(a => a.Name).ToHashSet(),
            Settings.Jumpscares, Console.WriteLine);
        JumpscarePlanner.AddMirrorFlags(ctx.InjectionResult.RegionToFlags, _jumpscarePlan);
    }

    public override void OnCommonEmevd(Context ctx)
    {
        // Darkness: keep the vanilla evergaol gparam override active
        // (opt-in via [plugin.halloween] darkness = true).
        if (Settings.Darkness)
            HalloweenDarknessInjector.InjectEmevdEvent(ctx.CommonEmevd, ctx.Events);
    }

    public override void OnRegulation(Context ctx, RegulationEditor reg)
    {
        // The greeter and ambusher rows are only consumed when their spawns
        // are actually placed (AmbientSpawnInjector gates greeters and ambush
        // packs on the same Settings.Greeters and Settings.Ambushes flags);
        // OnModDirMsb places a flavor only if its rows were written here.
        if (Settings.Greeters)
            _greeterRowApplied = AmbientSpawnInjector.ApplyHostileGreeterRow(reg, Settings.GreeterSpecies);
        if (Settings.Ambushes)
            _ambusherRowsApplied = AmbientSpawnInjector.ApplyDecorativeAmbusherRows(reg, Settings.AmbusherSpecies);
        if (Settings.Jumpscares > 0)
            _jumpscareRowApplied = HalloweenJumpscareInjector.ApplyJumpscareRow(reg);
        HalloweenIconInjector.ApplyTo(reg);
    }

    public override void OnModDirMsb(
        Context ctx, Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    {
        // Ambient layer at cluster exit gates: data-driven gate decorations
        // (data/plugins/halloween_decorations.toml, a no-op only if the
        // catalogue is emptied) plus optional hostile greeters and ambush
        // packs at the same gates. HalloweenAmbientPass reads/writes each
        // map's MSB once (decor placement first: the decor ground estimate
        // uses vanilla enemies as floor evidence, and AmbientSpawnInjector's
        // greeters/ambushers carry EntityID 0, which would pass its vanilla
        // filter). Plus the jumpscare actors in the dungeon layers
        // JumpscarePlanner draws.
        if (Settings.Greeters && !_greeterRowApplied)
            Console.WriteLine("Halloween spawns: regulation phase did not write the greeter rows, skipping greeters");
        if (Settings.Ambushes && !_ambusherRowsApplied)
            Console.WriteLine("Halloween spawns: regulation phase did not write the ambusher rows, skipping ambushers");
        // Planned in AfterFogModWrite (its mirror flags are already in the
        // fog warps); without the actor row, place nothing.
        var jumpscarePlan = _jumpscarePlan;
        if (jumpscarePlan.Actors.Count > 0 && !_jumpscareRowApplied)
        {
            Console.WriteLine("Halloween jumpscares: regulation phase did not write the jumpscare row, skipping jumpscares");
            jumpscarePlan = JumpscarePlan.Empty;
        }
        HalloweenAmbientPass.Inject(
            ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.Connections,
            ctx.GraphData.Nodes, gateSides, ctx.Events, ctx.Config.DataDir,
            PlaceableSettings(), jumpscarePlan);
    }

    public override void OnModDirLate(Context ctx)
    {
        // Darkness: repoint vanilla fast-loop gparam overrides to the
        // evergaol so they stop racing the darkness loop (see
        // docs/plugins/halloween-darkness.md).
        if (Settings.Darkness)
            HalloweenDarknessInjector.RepointFastLoopOverrides(ctx.ModDir);
    }

    /// <summary>Settings with each spawn flavor turned off whose NpcParam
    /// row <see cref="OnRegulation"/> did not write.</summary>
    internal HalloweenPluginSettings.Settings PlaceableSettings() => Settings with
    {
        Greeters = Settings.Greeters && _greeterRowApplied,
        Ambushes = Settings.Ambushes && _ambusherRowsApplied,
    };
}
