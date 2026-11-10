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
    /// <summary>Config namespace name: [plugin.&lt;Name&gt;].</summary>
    public abstract string Name { get; }

    /// <summary>Text theme catalogue (data/plugins/&lt;theme&gt;.toml) that
    /// ModDirPhase applies after BossNameInjector, or null for none.</summary>
    public virtual string? TextTheme => null;

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
    /// Untouchable boss MSB repoint and VanillaWarpRemover (MSB edits).</summary>
    public virtual void OnModDirMsb(
        Context ctx, Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    { }

    /// <summary>ModDirPhase, after EventDisabler and before
    /// VanillaWarpRemover.</summary>
    public virtual void OnModDirLate(Context ctx) { }
}
