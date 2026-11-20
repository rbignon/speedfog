using FogModWrapper.Common;
using FogModWrapper.Graces;
using FogModWrapper.Models;
using FogModWrapper.Pipeline;
using SoulsFormats;

namespace FogModWrapper.Plugins.Relay;

/// <summary>
/// [plugin.relay]: lets an outside driver (the racing mod) hold a player in
/// place and move them along the graph by raising flags, see
/// docs/plugins/relay.md; the same lock closes fast travel from the Chapel
/// map. The only setting is <c>enabled</c>. The warp
/// destinations are collected while PatchEmevdPhase scans the EMEVD files
/// and turned into common.emevd events afterwards.
/// </summary>
internal sealed class RelayPlugin : SpeedFogPlugin
{
    private readonly Dictionary<int, RelayInjector.WarpTarget> _targets = new();
    private int _locks;

    public RelayPlugin(PluginConfig config)
    {
        foreach (var (key, _) in config.Extra)
            throw new InvalidDataException($"relay: unknown parameter '{key}'");
    }

    public override bool OnPatchEmevd(Context ctx, EMEVD emevd)
    {
        int n = RelayInjector.PatchEmevdFile(
            emevd, ctx.Events, ctx.InjectionResult.RegionToFlags, _targets);
        _locks += n;
        return n > 0;
    }

    public override void OnRegulation(Context ctx, RegulationEditor reg)
    {
        var mapDefaultInfo = reg.GetParam("MapDefaultInfoParam")
            ?? throw new InvalidOperationException("relay: MapDefaultInfoParam unavailable");
        RelayInjector.ApplyFastTravelGate(mapDefaultInfo);
        Console.WriteLine($"relay: Chapel fast travel gated by flag {SpeedFogIds.RelayFastTravelFlag}");
    }

    public override void OnCommonEmevd(Context ctx)
    {
        RelayInjector.InjectFastTravelEvent(ctx.CommonEmevd, ctx.Events);
        Console.WriteLine($"relay: {_locks} graph warp(s) locked behind flag {SpeedFogIds.RelayLockFlag}");
        RelayInjector.InjectWarpEvents(
            ctx.CommonEmevd, ctx.Events,
            ctx.InjectionResult.RegionToFlags, _targets,
            ctx.GraphData.EventMap,
            ctx.GraphData.ChapelGrace ? ChapelGraceInjector.DefaultPlayerEntity : null);
    }
}
