using FogMod;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper.Pipeline;

/// <summary>
/// Mutable state shared across the pipeline phases.
/// Each field is populated by exactly one phase and read by later phases.
/// </summary>
internal sealed class Context
{
    public Config Config { get; }
    public string ModDir { get; }
    public string EventDir { get; }
    public string CommonEmevdPath { get; }

    // Populated by LoadInputsPhase
    public GraphData GraphData = null!;
    public GameTweaks Tweaks = null!;
    public AnnotationData Ann = null!;
    // Not thread-safe: parallel callers must serialize ParseAdd calls
    // with lock(Events) (see DeathMarkerInjector).
    public Events Events = null!;
    public EventConfig EventConfig = null!;
    public List<PhantomSkin> PhantomSkins = new();
    public MapSplits MapSplits = MapSplits.Empty;

    // Populated by ConstructGraphPhase
    public RandomizerOptions Opt = null!;
    public Graph Graph = null!;

    // Populated by InjectConnectionsPhase
    public InjectionResult InjectionResult = null!;

    // Populated by PatchEmevdPhase, written by CommonEmevdPhase
    public EMEVD CommonEmevd = null!;

    // Per-class hand-slot swaps recorded by ClassLoadoutInjector
    // (RegulationPhase), consumed by ClassLoadoutTextPatcher (ModDirPhase)
    // to fix the class-selection equipment text.
    public List<ClassLoadoutInjector.LoadoutSwap> LoadoutSwaps = new();

    // Resolved by PatchEmevdPhase, consumed by CommonEmevdPhase.
    // 0 when zone tracking is disabled or no defeat flag is known.
    public int BossDefeatFlag;

    // Set by RegulationPhase (UntouchableBossInjector.ApplyParams'
    // result), consumed by ModDirPhase to decide whether the MSB repoint
    // may run. Stays false when the boss was never placed (ApplyParams not
    // called) or the regulation phase warn-returned (NpcParam/SpEffectParam
    // unavailable).
    public bool UntouchableBossParamsApplied;

    // Set by RegulationPhase (UntouchableBossInjector.ApplyParams'
    // Moveset flag): the boss think row exists, so the MSB phase may
    // repoint ThinkParamID at it. False keeps the vanilla AI.
    public bool UntouchableBossMovesetApplied;

    // Set by RegulationPhase (AmbientSpawnInjector's row writers),
    // consumed by ModDirPhase: a Halloween spawn flavor is only placed
    // when its NpcParam row exists. Stay false when the flavor is disabled
    // or the regulation phase warn-returned.
    public bool GreeterRowApplied;
    public bool AmbusherRowsApplied;

    // Set by RegulationPhase when [plugin.halloween] jumpscares > 0;
    // ModDirPhase places no actor without the row.
    public bool JumpscareRowApplied;

    // Drawn in WriteFogModPhase, right after BuildRegionToFlags, so its
    // mirror flags reach PatchEmevdPhase; placed by ModDirPhase.
    public JumpscarePlan JumpscarePlan = JumpscarePlan.Empty;

    // Parsed lazily on first access: CommonEmevdPhase (darkness),
    // RegulationPhase (ambusher row gating) and ModDirPhase
    // (HalloweenAmbientPass) need the [plugin.halloween] settings, but
    // HalloweenPluginSettings.Parse should only run once per build. Only
    // valid when the halloween plugin is enabled (every call site guards on
    // that).
    private HalloweenPluginSettings.Settings? _halloweenSettings;
    public HalloweenPluginSettings.Settings HalloweenSettings =>
        _halloweenSettings ??= HalloweenPluginSettings.Parse(GraphData.Plugins["halloween"]);

    public Context(Config config)
    {
        Config = config;
        ModDir = Path.Combine(config.OutputDir, "mods", "fogmod");
        EventDir = Path.Combine(ModDir, "event");
        CommonEmevdPath = Path.Combine(EventDir, "common.emevd.dcx");
    }
}
