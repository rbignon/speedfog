using System.Numerics;
using FogMod;
using SoulsIds;
using Xunit;
using Xunit.Abstractions;

namespace FogModWrapper.Tests;

/// <summary>
/// Test Plan Point 0: verify that entranceEdge.Side.Warp.Region is stable through
/// Graph.Connect() and Graph.DuplicateEntrance().
///
/// This is the critical hypothesis for the region-based zone tracking spec:
/// if Region changes during connection, the entire approach is invalid.
///
/// Strategy: build a real FogMod Graph from fog.txt, then manually assign WarpPoints
/// on entrance edges (simulating what GameDataWriterE does with MSB game data) before
/// exercising Connect() and DuplicateEntrance(). This tests the actual FogMod DLL code
/// on a real graph topology without requiring game files.
///
/// Requires data/fog.txt (gitignored, from FogRando extraction). Skips if missing.
/// </summary>
public class RegionStabilityTests
{
    private readonly ITestOutputHelper _output;

    public RegionStabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FindDataDir()
    {
        var envDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "fog.txt")))
            return envDir;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../..", "data"));
        if (File.Exists(Path.Combine(candidate, "fog.txt")))
            return candidate;

        return null;
    }

    /// <summary>
    /// Build a real FogMod Graph from fog.txt with the same options as Program.cs.
    /// Returns null if data files are missing.
    /// </summary>
    private Graph? BuildGraph()
    {
        var dataDir = FindDataDir();
        if (dataDir == null)
            return null;

        var fogPath = Path.Combine(dataDir, "fog.txt");
        var ann = AnnotationData.LoadLiteConfig(fogPath);

        ann.ConfigVars = new Dictionary<string, string>
        {
            { "scalepass", "FALSE" }, { "logicpass", "TRUE" },
            { "runes_leyndell", "TRUE" }, { "runes_rold", "TRUE" }, { "runes_end", "TRUE" },
            { "tier1", "FALSE" }, { "tier2", "FALSE" }, { "tier3", "FALSE" },
            { "tier4", "FALSE" }, { "tier5", "FALSE" }, { "tier6", "FALSE" },
            { "tier7", "FALSE" }, { "tier8", "FALSE" }, { "tier9", "FALSE" },
            { "treekindling", "TRUE" },
            { "imbued_base", "TRUE" }, { "imbued_base_any", "TRUE" },
            { "imbued_dlc", "TRUE" }, { "imbued_dlc_any", "TRUE" },
            { "rauhruins_high_seal", "TRUE" }, { "rauhbase_high_seal", "TRUE" },
            { "gravesite_seal", "TRUE" }, { "scadualtus_high_seal", "TRUE" },
            { "ymir_open", "TRUE" },
            { "academyglintstonekey", "TRUE" }, { "carianinvertedstatue", "TRUE" },
            { "cursemarkofdeath", "TRUE" }, { "darkmoonring", "TRUE" },
            { "dectusmedallionleft", "TRUE" }, { "dectusmedallionright", "TRUE" },
            { "discardedpalacekey", "TRUE" }, { "drawingroomkey", "TRUE" },
            { "haligtreesecretmedallionleft", "TRUE" }, { "haligtreesecretmedallionright", "TRUE" },
            { "imbuedswordkey", "TRUE" }, { "imbuedswordkey1", "TRUE" },
            { "imbuedswordkey2", "TRUE" }, { "imbuedswordkey3", "TRUE" },
            { "imbuedswordkey4", "TRUE" },
            { "purebloodknightsmedal", "TRUE" }, { "roldmedallion", "TRUE" },
            { "runegodrick", "TRUE" }, { "runemalenia", "TRUE" }, { "runemohg", "TRUE" },
            { "runemorgott", "TRUE" }, { "runeradahn", "TRUE" }, { "runerennala", "TRUE" },
            { "runerykard", "TRUE" }, { "rustykey", "TRUE" },
            { "omother", "TRUE" }, { "welldepthskey", "TRUE" },
            { "gaolupperlevelkey", "TRUE" }, { "gaollowerlevelkey", "TRUE" },
            { "holeladennecklace", "TRUE" }, { "messmerskindling", "TRUE" },
            { "messmerskindling1", "TRUE" },
            { "farumazula_maliketh", "TRUE" },
        };

        var opt = new RandomizerOptions(GameSpec.FromGame.ER);
        opt.InitFeatures();
        opt["crawl"] = true;
        opt["unconnected"] = true;
        opt["req_backportal"] = true;
        opt["roundtable"] = true;
        opt["newgraces"] = true;
        opt["dlc"] = true;
        opt["req_graveyard"] = true;
        opt["req_dungeon"] = true;
        opt["req_cave"] = true;
        opt["req_tunnel"] = true;
        opt["req_catacomb"] = true;
        opt["req_grave"] = true;
        opt["req_forge"] = true;
        opt["req_gaol"] = true;
        opt["req_legacy"] = true;
        opt["req_major"] = true;
        opt["req_underground"] = true;
        opt["req_minorwarp"] = true;
        opt["coupledminor"] = true;
        opt[Feature.AllowUnlinked] = true;
        opt[Feature.ForceUnlinked] = true;
        opt[Feature.SegmentFortresses] = true;

        var graph = new Graph();
        graph.Construct(opt, ann);

        // Disconnect trivial edges (same as Program.cs step 4b)
        foreach (var node in graph.Nodes.Values)
        {
            var edgesToDisconnect = node.To
                .Where(e => e.IsFixed && e.Link != null && !e.IsWorld && e.Name != null)
                .Where(e => graph.EntranceIds.TryGetValue(e.Name, out var entrance)
                            && entrance.HasTag("trivial"))
                .ToList();
            foreach (var edge in edgesToDisconnect)
                graph.Disconnect(edge);
        }

        return graph;
    }

    /// <summary>
    /// Assign a synthetic WarpPoint with a known Region to an entrance edge's Side.
    /// This simulates what GameDataWriterE does when processing MSB game files.
    /// LoadLiteConfig doesn't populate Warp data — that requires game files.
    /// </summary>
    private static void AssignSyntheticWarp(Graph.Edge entrance, int region)
    {
        entrance.Side.Warp = new Graph.WarpPoint
        {
            ID = 1,
            Map = "m10_00_00_00",
            Position = new Vector3(0, 0, 0),
            Region = region,
        };
    }

    [Fact]
    public void Region_IsStable_Through_Connect()
    {
        var graph = BuildGraph();
        if (graph == null)
        {
            _output.WriteLine("SKIP: data/fog.txt not found");
            return;
        }

        // Find unconnected exit/entrance pairs
        var entrances = graph.Nodes.Values
            .SelectMany(n => n.From)
            .Where(e => e.Link == null && e.Side != null)
            .Take(20)
            .ToList();

        var exits = graph.Nodes.Values
            .SelectMany(n => n.To)
            .Where(e => e.Link == null && e.Type == Graph.EdgeType.Exit)
            .Take(20)
            .ToList();

        int count = Math.Min(exits.Count, entrances.Count);
        Assert.True(count >= 10, $"Not enough edge pairs: {count}");

        // Assign synthetic WarpPoints with known Region values
        int baseRegion = 755890100;  // FogMod entity range
        for (int i = 0; i < count; i++)
        {
            AssignSyntheticWarp(entrances[i], baseRegion + i);
        }

        _output.WriteLine($"Testing {count} Connect() calls for region stability");

        int tested = 0;
        for (int i = 0; i < count; i++)
        {
            var exit = exits[i];
            var entrance = entrances[i];
            int expectedRegion = baseRegion + i;

            Assert.Equal(expectedRegion, entrance.Side.Warp.Region);

            try
            {
                graph.Connect(exit, entrance, ignorePair: true);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Connect failed for {exit.Name} -> {entrance.Name}: {ex.Message}");
                continue;
            }

            int regionAfter = entrance.Side.Warp.Region;

            _output.WriteLine($"  {exit.Name} -> {entrance.Name}: " +
                $"Region {expectedRegion} -> {regionAfter}");

            Assert.Equal(expectedRegion, regionAfter);
            tested++;
        }

        Assert.True(tested >= 5,
            $"Only {tested} pairs succeeded — need at least 5 for confidence");
        _output.WriteLine($"PASS: {tested} Connect() calls, all regions stable");
    }

    [Fact]
    public void Region_IsShared_Through_DuplicateEntrance()
    {
        var graph = BuildGraph();
        if (graph == null)
        {
            _output.WriteLine("SKIP: data/fog.txt not found");
            return;
        }

        // Find entrance edges
        var entrances = graph.Nodes.Values
            .SelectMany(n => n.From)
            .Where(e => e.Side != null && e.Type == Graph.EdgeType.Entrance)
            .Take(20)
            .ToList();

        Assert.True(entrances.Count >= 10, $"Not enough entrances: {entrances.Count}");

        // Assign synthetic WarpPoints
        int baseRegion = 755890200;
        for (int i = 0; i < entrances.Count; i++)
        {
            AssignSyntheticWarp(entrances[i], baseRegion + i);
        }

        _output.WriteLine($"Testing {entrances.Count} DuplicateEntrance() calls");

        int tested = 0;
        foreach (var entrance in entrances)
        {
            int originalRegion = entrance.Side.Warp.Region;

            Graph.Edge duplicate;
            try
            {
                duplicate = graph.DuplicateEntrance(entrance);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  DuplicateEntrance failed for {entrance.Name}: {ex.Message}");
                continue;
            }

            // Critical assertion: duplicate shares the SAME Side object (reference equality)
            Assert.True(ReferenceEquals(entrance.Side, duplicate.Side),
                $"DuplicateEntrance for {entrance.Name}: Side is NOT the same object reference");

            // Since Side is shared, Warp.Region is necessarily identical
            Assert.Equal(originalRegion, duplicate.Side.Warp.Region);

            // Original is unchanged
            Assert.Equal(originalRegion, entrance.Side.Warp.Region);

            _output.WriteLine($"  {entrance.Name}: Region={originalRegion}, " +
                $"sameRef={ReferenceEquals(entrance.Side, duplicate.Side)}");

            tested++;
        }

        Assert.True(tested >= 5,
            $"Only {tested} entrances succeeded — need at least 5 for confidence");
        _output.WriteLine($"PASS: {tested} DuplicateEntrance() calls, all Sides shared");
    }

    [Fact]
    public void Region_IsStable_Through_Connect_Then_DuplicateEntrance()
    {
        var graph = BuildGraph();
        if (graph == null)
        {
            _output.WriteLine("SKIP: data/fog.txt not found");
            return;
        }

        // Find unconnected entrance/exit pairs
        var entrances = graph.Nodes.Values
            .SelectMany(n => n.From)
            .Where(e => e.Link == null && e.Side != null
                     && e.Type == Graph.EdgeType.Entrance)
            .Take(10)
            .ToList();

        var exits = graph.Nodes.Values
            .SelectMany(n => n.To)
            .Where(e => e.Link == null && e.Type == Graph.EdgeType.Exit)
            .Take(10)
            .ToList();

        int count = Math.Min(exits.Count, entrances.Count);
        Assert.True(count >= 5, $"Not enough edge pairs: {count}");

        // Assign synthetic WarpPoints
        int baseRegion = 755890300;
        for (int i = 0; i < count; i++)
        {
            AssignSyntheticWarp(entrances[i], baseRegion + i);
        }

        _output.WriteLine($"Testing {count} Connect+DuplicateEntrance sequences");

        int tested = 0;
        for (int i = 0; i < count; i++)
        {
            var exit = exits[i];
            var entrance = entrances[i];
            int expectedRegion = baseRegion + i;

            try
            {
                graph.Connect(exit, entrance, ignorePair: true);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Connect failed: {ex.Message}");
                continue;
            }

            // DuplicateEntrance on an already-connected entrance
            // (mimics ConnectionInjector flow for shared entrances)
            Graph.Edge duplicate;
            try
            {
                duplicate = graph.DuplicateEntrance(entrance);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  DuplicateEntrance failed after Connect: {ex.Message}");
                continue;
            }

            _output.WriteLine($"  {exit.Name} -> {entrance.Name}: " +
                $"before={expectedRegion}, afterConnect={entrance.Side.Warp.Region}, " +
                $"duplicate={duplicate.Side.Warp.Region}, " +
                $"sameRef={ReferenceEquals(entrance.Side, duplicate.Side)}");

            Assert.Equal(expectedRegion, entrance.Side.Warp.Region);
            Assert.Equal(expectedRegion, duplicate.Side.Warp.Region);
            Assert.True(ReferenceEquals(entrance.Side, duplicate.Side));

            tested++;
        }

        Assert.True(tested >= 3,
            $"Only {tested} sequences succeeded — need at least 3 for confidence");
        _output.WriteLine($"PASS: {tested} Connect+DuplicateEntrance sequences, all regions stable");
    }

    [Fact]
    public void AlternateSide_Region_IsStable_Through_Connect()
    {
        var graph = BuildGraph();
        if (graph == null)
        {
            _output.WriteLine("SKIP: data/fog.txt not found");
            return;
        }

        // Find entrance edges that have an AlternateSide (e.g., flag 300/330 entrances)
        var entrancesWithAlt = graph.Nodes.Values
            .SelectMany(n => n.From)
            .Where(e => e.Link == null && e.Side?.AlternateSide != null)
            .Take(10)
            .ToList();

        if (entrancesWithAlt.Count == 0)
        {
            _output.WriteLine("No entrance edges with AlternateSide found — skipping");
            return;
        }

        var exits = graph.Nodes.Values
            .SelectMany(n => n.To)
            .Where(e => e.Link == null && e.Type == Graph.EdgeType.Exit)
            .Take(10)
            .ToList();

        int count = Math.Min(exits.Count, entrancesWithAlt.Count);

        // Assign synthetic WarpPoints to both primary and alternate sides
        int baseRegion = 755890400;
        int altBaseRegion = 755890500;
        for (int i = 0; i < count; i++)
        {
            var entrance = entrancesWithAlt[i];
            AssignSyntheticWarp(entrance, baseRegion + i);
            entrance.Side.AlternateSide.Warp = new Graph.WarpPoint
            {
                ID = 2,
                Map = "m11_05_00_00",
                Position = new Vector3(0, 0, 0),
                Region = altBaseRegion + i,
            };
        }

        _output.WriteLine($"Testing {count} Connect() calls with AlternateSide regions");

        int tested = 0;
        for (int i = 0; i < count; i++)
        {
            var exit = exits[i];
            var entrance = entrancesWithAlt[i];
            int expectedRegion = baseRegion + i;
            int expectedAltRegion = altBaseRegion + i;

            try
            {
                graph.Connect(exit, entrance, ignorePair: true);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Connect failed: {ex.Message}");
                continue;
            }

            _output.WriteLine($"  {exit.Name} -> {entrance.Name}: " +
                $"Region {expectedRegion} -> {entrance.Side.Warp.Region}, " +
                $"AltRegion {expectedAltRegion} -> {entrance.Side.AlternateSide.Warp.Region}");

            Assert.Equal(expectedRegion, entrance.Side.Warp.Region);
            Assert.Equal(expectedAltRegion, entrance.Side.AlternateSide.Warp.Region);

            tested++;
        }

        _output.WriteLine($"PASS: {tested} Connect() calls with AlternateSide, all regions stable");
    }
}
