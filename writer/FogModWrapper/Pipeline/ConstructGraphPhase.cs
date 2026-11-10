using FogMod;
using SoulsIds;

namespace FogModWrapper.Pipeline;

/// <summary>Build FogMod RandomizerOptions and construct the Graph.</summary>
internal static class ConstructGraphPhase
{
    public static void Run(Context ctx)
    {
        ctx.Opt = BuildRandomizerOptions(ctx.GraphData.Seed, ctx.GraphData.Options);

        Console.WriteLine($"Options: seed={ctx.Opt.Seed}, crawl={ctx.Opt["crawl"]}, scale={ctx.Opt["scale"]}, newgraces={ctx.Opt["newgraces"]}");

        Console.WriteLine("Constructing FogMod graph...");
        ctx.Graph = new Graph();
        ctx.Graph.Construct(ctx.Opt, ctx.Ann);
        Console.WriteLine($"Graph constructed: {ctx.Graph.Nodes.Count} nodes");

        DisconnectTrivialEdges(ctx.Graph);
        ExcludeEvergaolZones(ctx.Ann, ctx.Graph);
    }

    static RandomizerOptions BuildRandomizerOptions(int seed, Dictionary<string, bool> graphOptions)
    {
        var opt = new RandomizerOptions(GameSpec.FromGame.ER);
        opt.Seed = seed;

        // Apply options from graph.json
        foreach (var (key, value) in graphOptions)
        {
            opt[key] = value;
        }

        // Initialize features first (creates internal structures)
        opt.InitFeatures();

        // Required options for SpeedFog
        opt["crawl"] = true;  // Dungeon crawler mode - enables AllowUnlinked, tier progression
        opt["unconnected"] = true;  // Allow unconnected edges in the graph
        opt["req_backportal"] = true;  // Enable backportals so boss rooms have return warps as edges
        opt["roundtable"] = true;  // Make Roundtable Hold available from the start

        opt["newgraces"] = true;  // Enable additional Sites of Grace
        opt["dlc"] = true;  // Include DLC areas in fog graph

        // Mark dungeon types as "core" so their edges are processed
        // Note: Do NOT use req_all as it includes evergaols which lack StakeAsset definitions
        opt["req_graveyard"] = true;  // Required when req_dungeon + shuffle are both enabled
        opt["req_dungeon"] = true;  // Caves, tunnels, catacombs, graves
        opt["req_cave"] = true;
        opt["req_tunnel"] = true;
        opt["req_catacomb"] = true;
        opt["req_grave"] = true;
        opt["req_forge"] = true;   // DLC forges (Taylew's, Starfall Past, Lava Intake)
        opt["req_gaol"] = true;    // DLC gaols (Belurat, Bonny, Lamenter's)
        opt["req_legacy"] = true;  // Legacy dungeons
        opt["req_major"] = true;   // Major bosses
        opt["req_underground"] = true;  // Underground areas (Siofra, Ainsel, Nokron, etc.)
        opt["req_minorwarp"] = true;  // Minor warps (transporter chests)
        opt["req_rauhruins"] = true;  // Promote rauhruins-tagged sides to core in crawl mode (Graph.cs:1177)
        opt["coupledminor"] = true;  // Keep uniqueminor warps as coupled pairs (not unique)
        // Without this, Graph.Construct converts uniqueminor→unique, then crawl mode marks
        // warps with mixed open/neveropen sides as unused (e.g., Redmane Castle sending gates).
        // Explicitly NOT setting req_evergaol - evergaols lack StakeAsset in fog.txt

        // Configure features that depend on crawl mode (normally done by Randomizer)
        opt[Feature.AllowUnlinked] = true;  // Allow edges without connections
        opt[Feature.ForceUnlinked] = true;  // Force unlinked mode
        opt[Feature.SegmentFortresses] = true;  // Treat fortresses as segments

        return opt;
    }

    // In crawl mode, FogMod marks entrances with "trivial" tag as IsFixed and connects them.
    // SpeedFog needs these edges available for our custom graph, so we disconnect them.
    static void DisconnectTrivialEdges(Graph graph)
    {
        var disconnectedCount = 0;
        foreach (var node in graph.Nodes.Values)
        {
            var edgesToDisconnect = node.To
                .Where(e => e.IsFixed && e.Link != null && !e.IsWorld && e.Name != null)
                .Where(e => graph.EntranceIds.TryGetValue(e.Name, out var entrance) && entrance.HasTag("trivial"))
                .ToList();

            foreach (var edge in edgesToDisconnect)
            {
                graph.Disconnect(edge);
                disconnectedCount++;
            }
        }
        if (disconnectedCount > 0)
        {
            Console.WriteLine($"Disconnected {disconnectedCount} trivial edges for SpeedFog graph");
        }
    }

    // In crawl mode, FogRando replaces evergaols with "fake evergaol" connections,
    // but SpeedFog doesn't use evergaols. Without StakeAsset in fog.txt, they cause errors.
    // Setting BossTrigger=0 prevents FogMod from trying to create Stakes for these zones.
    static void ExcludeEvergaolZones(AnnotationData ann, Graph graph)
    {
        foreach (var area in ann.Areas.Where(a => a.Name.Contains("evergaol")))
        {
            area.IsExcluded = true;
            area.BossTrigger = 0;
        }
        var evergaolCount = 0;
        foreach (var kvp in graph.Areas.Where(a => a.Key.Contains("evergaol")))
        {
            kvp.Value.IsExcluded = true;
            kvp.Value.BossTrigger = 0;
            evergaolCount++;
        }
        Console.WriteLine($"Excluded {evergaolCount} evergaol zones from stake processing");
    }
}
