using FogModWrapper.Common;
using FogModWrapper.Pipeline;

namespace FogModWrapper.Plugins;

/// <summary>
/// C# side of one [plugin.&lt;name&gt;] config namespace. Every hook is a
/// no-op unless overridden. The pipeline phases call each enabled plugin,
/// in <see cref="PluginRegistry"/> order, at the fixed points below; see
/// docs/plugins/README.md for the hook table and why each point sits where
/// it does.
/// </summary>
internal abstract class SpeedFogPlugin
{
    /// <summary>Config namespace name: [plugin.&lt;Name&gt;]. Assigned by
    /// <see cref="PluginRegistry"/> from its entry, so plugin classes do not
    /// repeat their config key (not set yet inside a constructor).</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Name of the text theme catalogue (data/plugins/&lt;theme&gt;.toml)
    /// that ModDirPhase applies after BossNameInjector, or null for none.</summary>
    public virtual string? TextThemeName => null;

    /// <summary>WriteFogModPhase, right after BuildRegionToFlags: last
    /// chance to add flags to RegionToFlags before PatchEmevdPhase injects
    /// them.</summary>
    public virtual void AfterFogModWrite(Context ctx) { }

    /// <summary>CommonEmevdPhase, after the core injectors and before
    /// AlternateFlagPatcher and the single common.emevd write.</summary>
    public virtual void OnCommonEmevd(Context ctx) { }

    /// <summary>RegulationPhase, inside the single regulation.bin batch.
    /// Not called when regulation.bin is unavailable.</summary>
    public virtual void OnRegulation(Context ctx, RegulationEditor reg) { }

    /// <summary>ModDirPhase, after DeathMarkerInjector and before the
    /// Untouchable boss MSB repoint and VanillaWarpRemover (MSB edits).
    /// <see cref="Context.GateSides"/> is available.</summary>
    public virtual void OnModDirMsb(Context ctx) { }

    /// <summary>ModDirPhase, after EventDisabler and before
    /// VanillaWarpRemover.</summary>
    public virtual void OnModDirLate(Context ctx) { }
}
