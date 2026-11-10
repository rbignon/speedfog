using FogMod;
using FogModWrapper.FogGraph;
using FogModWrapper.Plugins;
using SoulsIds;
using YamlDotNet.Serialization;

namespace FogModWrapper.Pipeline;

/// <summary>Load all input files (graph.json + fog.txt + events + ...).</summary>
internal static class LoadInputsPhase
{
    public static void Run(Context ctx)
    {
        ctx.GraphData = GraphLoader.Load(ctx.Config.GraphPath);

        var fogPath = Path.Combine(ctx.Config.DataDir, "fog.txt");
        var fogeventsPath = Path.Combine(ctx.Config.DataDir, "fogevents.txt");
        var emedfPath = Path.Combine(ctx.Config.DataDir, "er-common.emedf.json");
        var foglocationsPath = Path.Combine(ctx.Config.DataDir, "foglocations2.txt");
        var phantomCatalogPath = Path.Combine(ctx.Config.DataDir, "phantom_skins.toml");
        var zoneMetadataPath = Path.Combine(ctx.Config.DataDir, "zone_metadata.toml");

        Console.WriteLine($"Loading fog.txt from: {fogPath}");
        ctx.Ann = AnnotationData.LoadLiteConfig(fogPath);

        // LoadLiteConfig only loads Areas/Warps/Entrances/DungeonItems (via internal LiteConfig class).
        // CustomBonfires are needed for newgraces - load them separately.
        LoadCustomBonfires(ctx.Ann, fogPath);

        // Initialize ConfigVars - LoadLiteConfig doesn't load them, but Graph.Construct needs them
        // for condition evaluation. These are FogRando's dungeon crawler mode variables.
        ctx.Tweaks = GameTweaksLoader.Load(Path.Combine(ctx.Config.DataDir, "game_tweaks.toml"));
        ctx.Ann.ConfigVars = ctx.Tweaks.ConfigVars;

        // Load foglocations for enemy area info (needed for scaling).
        LoadFoglocations(ctx.Ann, foglocationsPath);

        Console.WriteLine($"Loading events from: {emedfPath}");
        ctx.Events = new Events(emedfPath, darkScriptMode: true, paramAwareMode: true);

        Console.WriteLine($"Loading event config from: {fogeventsPath}");
        using (var input = File.OpenText(fogeventsPath))
        {
            var deserializer = new DeserializerBuilder().Build();
            ctx.EventConfig = deserializer.Deserialize<EventConfig>(input);
            ctx.EventConfig.MakeWarpCommands(ctx.Events);
        }

        // Phantom skins catalog (optional; absent file = no-op).
        ctx.PhantomSkins = PhantomCatalogLoader.Load(phantomCatalogPath);

        // Opensplit overrides (zone_metadata.toml -> entrance tags).
        // Must run before Graph.Construct: FogMod's IsCore + opensplit logic
        // (Graph.cs:1167-1272) consumes these tags during graph construction.
        var openSplitIds = OpenSplitOverrideLoader.Load(zoneMetadataPath);
        OpenSplitInjector.Apply(ctx.Ann, openSplitIds);

        // Inject synthetic zones/fogs (map splits) into the annotation data so
        // Graph.Construct sees them like any fog.txt gate. Also splits
        // EnemyAreas for per-half scaling. See docs/map-splits.md.
        var mapSplitsPath = Path.Combine(ctx.Config.DataDir, "map_splits.toml");
        ctx.MapSplits = MapSplitsLoader.Load(mapSplitsPath);
        MapSplitsInjector.Apply(ctx.Ann, ctx.MapSplits);

        // One object per enabled [plugin.*] table; each parses its settings
        // here, so an invalid table aborts before FogMod runs.
        ctx.Plugins = PluginRegistry.CreateEnabled(ctx.GraphData);
    }

    static void LoadCustomBonfires(AnnotationData ann, string fogPath)
    {
        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        using var input = File.OpenText(fogPath);
        var bonfireConfig = deserializer.Deserialize<BonfireConfig>(input);
        ann.CustomBonfires = bonfireConfig?.CustomBonfires;
        if (ann.CustomBonfires != null)
        {
            Console.WriteLine($"Loaded {ann.CustomBonfires.Count} custom bonfires for newgraces");
        }
        else
        {
            Console.WriteLine("Warning: No CustomBonfires found in fog.txt - newgraces will have no effect");
        }
    }

    static void LoadFoglocations(AnnotationData ann, string path)
    {
        if (!File.Exists(path))
            return;
        Console.WriteLine($"Loading foglocations from: {path}");
        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        using var input = File.OpenText(path);
        ann.Locations = deserializer.Deserialize<AnnotationData.FogLocations>(input);
    }

    /// <summary>
    /// Minimal YAML wrapper to load CustomBonfires from fog.txt.
    /// LoadLiteConfig uses an internal LiteConfig class that omits CustomBonfires,
    /// so we load them separately with this focused class.
    /// </summary>
    class BonfireConfig
    {
        public List<AnnotationData.CustomBonfire>? CustomBonfires { get; set; }
    }
}
