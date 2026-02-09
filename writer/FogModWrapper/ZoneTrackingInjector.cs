using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects EMEVD events for racing zone tracking:
/// A) Inject SetEventFlag before WarpPlayer in FogMod-generated per-instance warp events
/// B) Boss death monitor event that sets finish_event on final boss defeat
/// </summary>
public static class ZoneTrackingInjector
{
    private const int BOSS_DEATH_EVENT_ID = 755862000;

    /// <summary>
    /// FogMod allocates warp target region entity IDs starting from this base.
    /// Vanilla WarpPlayer events use map-specific entity IDs (e.g., 14003900, 16002701)
    /// which are well below this threshold. Filtering on region >= this value ensures
    /// we only modify FogMod-generated warp events, not vanilla ones.
    /// </summary>
    private const int FOGMOD_ENTITY_BASE = 755890000;

    /// <summary>
    /// Inject zone tracking events into EMEVD files.
    /// </summary>
    /// <param name="modDir">Path to mod output directory (contains event/ subdirectory)</param>
    /// <param name="events">Events instance for instruction parsing</param>
    /// <param name="connections">Connections from graph.json with flag_id per connection</param>
    /// <param name="areaMaps">Maps each area name to its internal map IDs (from FogMod Graph).
    /// Used to resolve entrance areas to their actual WarpPlayer destination maps,
    /// which may differ from the entrance_gate's map prefix (e.g., overworld tile vs dungeon interior).</param>
    /// <param name="finishEvent">The finish_event flag ID</param>
    /// <param name="bossDefeatFlag">The boss defeat flag from FogMod's Graph</param>
    public static void Inject(
        string modDir,
        Events events,
        List<Connection> connections,
        Dictionary<string, string> areaMaps,
        int finishEvent,
        int bossDefeatFlag)
    {
        // Part A: Inject SetEventFlag before WarpPlayer in per-instance warp events
        InjectFogGateFlags(modDir, events, connections, areaMaps);

        // Part B: Create boss death monitor in common.emevd
        if (finishEvent > 0 && bossDefeatFlag > 0)
        {
            InjectBossDeathEvent(modDir, events, finishEvent, bossDefeatFlag);
        }
        else
        {
            Console.WriteLine("Warning: Skipping boss death event (finishEvent={0}, bossDefeatFlag={1})",
                finishEvent, bossDefeatFlag);
        }
    }

    /// <summary>
    /// Scan all EMEVD files for WarpPlayer instructions with literal destination map bytes.
    /// When a destination matches a connection's entrance gate map, inject SetEventFlag before
    /// the WarpPlayer to set the zone tracking flag on traversal.
    ///
    /// FogMod's EventEditor compiles the fogwarp template (9005777) into per-instance events
    /// with unique IDs and literal WarpPlayer values. These events are placed in map EMEVD files
    /// and called via InitializeEvent (2000:0). We post-process them to add zone tracking.
    ///
    /// Matching strategy (see docs/specs/zone-tracking-accuracy.md):
    /// 1. Try compound key (source_map, dest_map) — resolves collisions when two connections
    ///    from different source maps target the same dest map.
    /// 2. Fall back to dest-only matching — because FogMod's getEventMap() may place events
    ///    in a different EMEVD file than the exit gate's map prefix.
    /// </summary>
    private static void InjectFogGateFlags(
        string modDir, Events events, List<Connection> connections,
        Dictionary<string, string> areaMaps)
    {
        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping fog gate flag injection");
            return;
        }

        // Build TWO lookups from connections:
        // 1. Compound key (source_map, dest_map) → flagId  — for collision resolution
        // 2. Dest-only dest_map → flagId  — fallback when compound key doesn't match
        //
        // FogMod's getEventMap() may place warp events in a different EMEVD file than the
        // exit gate's map prefix (e.g., parent maps for open world tiles, map deduplication).
        // The compound key works when EMEVD filename matches exit_gate map; dest-only handles
        // the rest. See docs/specs/zone-tracking-accuracy.md for design rationale.
        var compoundLookup = new Dictionary<((byte, byte, byte, byte), (byte, byte, byte, byte)), int>();
        var destOnlyLookup = new Dictionary<(byte, byte, byte, byte), int>();
        var destOnlyCollisions = new HashSet<(byte, byte, byte, byte)>();

        foreach (var conn in connections)
        {
            if (conn.FlagId <= 0)
                continue;
            var exitMapBytesList = ParseMapBytesFromGateName(conn.ExitGate);
            var entranceMapBytesList = ParseMapBytesFromGateName(conn.EntranceGate);

            // Register compound keys (all exit × entrance combinations for cross-tile gates)
            foreach (var exitBytes in exitMapBytesList)
            {
                var srcKey = (exitBytes[0], exitBytes[1], exitBytes[2], exitBytes[3]);
                foreach (var entranceBytes in entranceMapBytesList)
                {
                    var destKey = (entranceBytes[0], entranceBytes[1], entranceBytes[2], entranceBytes[3]);
                    compoundLookup.TryAdd((srcKey, destKey), conn.FlagId);
                }
            }

            // Register dest-only keys from gate name (track collisions)
            RegisterDestKeys(entranceMapBytesList, conn.FlagId, destOnlyLookup, destOnlyCollisions);

            // Also register internal maps from areaMaps — FogMod may warp to the dungeon
            // interior map (e.g., m30_02) instead of the overworld entrance tile (e.g., m60_41_37)
            if (areaMaps.TryGetValue(conn.EntranceArea, out var mapsStr) && !string.IsNullOrEmpty(mapsStr))
            {
                var internalMapBytes = ParseMapBytesFromMapString(mapsStr);
                RegisterDestKeys(internalMapBytes, conn.FlagId, destOnlyLookup, destOnlyCollisions);

                // Also register compound keys for internal maps
                foreach (var exitBytes in exitMapBytesList)
                {
                    var srcKey = (exitBytes[0], exitBytes[1], exitBytes[2], exitBytes[3]);
                    foreach (var intBytes in internalMapBytes)
                    {
                        var destKey = (intBytes[0], intBytes[1], intBytes[2], intBytes[3]);
                        compoundLookup.TryAdd((srcKey, destKey), conn.FlagId);
                    }
                }
            }
        }

        Console.WriteLine($"Zone tracking: {compoundLookup.Count} compound keys, " +
                          $"{destOnlyLookup.Count} dest maps ({destOnlyCollisions.Count} collisions)");

        int totalInjected = 0;
        int compoundMatches = 0;
        int destOnlyMatches = 0;

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            // Parse source map from EMEVD filename (e.g., "m10_01_00_00.emevd.dcx")
            var sourceMapBytes = ParseMapBytesFromFileName(emevdPath);
            var sourceMap = sourceMapBytes != null
                ? ((byte, byte, byte, byte)?)(sourceMapBytes[0], sourceMapBytes[1], sourceMapBytes[2], sourceMapBytes[3])
                : null;

            var emevd = EMEVD.Read(emevdPath);
            bool fileModified = false;

            foreach (var evt in emevd.Events)
            {
                // Find WarpPlayer instruction (bank 2003, id 14) with literal map bytes
                for (int i = 0; i < evt.Instructions.Count; i++)
                {
                    var instr = evt.Instructions[i];
                    if (instr.Bank != 2003 || instr.ID != 14)
                        continue;

                    var a = instr.ArgData;
                    if (a.Length < 8)
                        continue;

                    // Skip parameterized WarpPlayer (all-zero map = template placeholder)
                    var destMap = (a[0], a[1], a[2], a[3]);
                    if (destMap == (0, 0, 0, 0))
                        continue;

                    // Only match FogMod-generated warp events, not vanilla ones.
                    // FogMod allocates warp target regions from FOGMOD_ENTITY_BASE;
                    // vanilla events use map-specific IDs well below that range.
                    int region = BitConverter.ToInt32(a, 4);
                    if (region < FOGMOD_ENTITY_BASE)
                        continue;

                    // Strategy 1: Try compound key (source_map, dest_map) — resolves collisions
                    int flagId = 0;
                    bool matched = false;
                    if (sourceMap.HasValue)
                    {
                        var compoundKey = (sourceMap.Value, destMap);
                        if (compoundLookup.TryGetValue(compoundKey, out flagId))
                        {
                            matched = true;
                            compoundMatches++;
                        }
                    }

                    // Strategy 2: Fall back to dest-only matching
                    if (!matched)
                    {
                        if (!destOnlyLookup.TryGetValue(destMap, out flagId))
                            continue;
                        matched = true;
                        destOnlyMatches++;
                        if (destOnlyCollisions.Contains(destMap))
                        {
                            var destMapStr = $"m{destMap.Item1}_{destMap.Item2:D2}_{destMap.Item3:D2}_{destMap.Item4:D2}";
                            Console.WriteLine($"Warning: Zone tracking dest-only fallback with collision " +
                                              $"on {destMapStr} — flag {flagId} may be inaccurate");
                        }
                    }

                    // Insert SetEventFlag(flagId, ON) before WarpPlayer
                    var setFlagInstr = events.ParseAdd(
                        $"SetEventFlag(TargetEventFlagType.EventFlag, {flagId}, ON)");
                    evt.Instructions.Insert(i, setFlagInstr);

                    // Shift Parameter entries for instructions at or after insertion point
                    foreach (var param in evt.Parameters)
                    {
                        if (param.InstructionIndex >= i)
                        {
                            param.InstructionIndex++;
                        }
                    }

                    totalInjected++;
                    fileModified = true;
                    break;  // Alt-warp uses same destination map; indices shifted, so stop
                }
            }

            if (fileModified)
            {
                emevd.Write(emevdPath);
            }
        }

        var expectedFlags = connections.Where(c => c.FlagId > 0).Select(c => c.FlagId).Distinct().Count();
        Console.WriteLine($"Zone tracking: injected {totalInjected} fog gate tracking flags " +
                          $"(compound: {compoundMatches}, dest-only: {destOnlyMatches}, " +
                          $"expected unique flags: {expectedFlags})");
    }

    /// <summary>
    /// Parse map bytes from a gate name.
    /// Simple gate: "m31_05_00_00_AEG099_230_9001" → [[31, 5, 0, 0]]
    /// Cross-tile gate: "m60_13_13_02_m60_52_53_00-AEG099_003_9001" → [[60,13,13,2], [60,52,53,0]]
    /// Returns all possible map byte arrays so the caller can register each in the lookup.
    /// </summary>
    private static List<byte[]> ParseMapBytesFromGateName(string gateName)
    {
        var results = new List<byte[]>();
        if (string.IsNullOrEmpty(gateName))
            return results;

        // Split on 'm' prefix boundaries to find all map coordinate groups.
        // Cross-tile gates encode two tiles: "m60_13_13_02_m60_52_53_00-AEG099..."
        // The second tile starts at "_m" within the name.
        var mapParts = new List<string>();
        var rest = gateName;
        while (rest.Length > 0)
        {
            if (!rest.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                break;

            // Find next "_m" boundary (start of another map prefix)
            int nextM = rest.IndexOf("_m", 1, StringComparison.OrdinalIgnoreCase);
            if (nextM > 0)
            {
                mapParts.Add(rest.Substring(0, nextM));
                rest = rest.Substring(nextM + 1); // skip the underscore, keep the 'm'
            }
            else
            {
                mapParts.Add(rest);
                break;
            }
        }

        foreach (var mapPart in mapParts)
        {
            // Strip leading 'm', split, take first 4 as bytes
            var name = mapPart.TrimStart('m');
            // Remove anything after a hyphen (entity suffix on last map part)
            var hyphen = name.IndexOf('-');
            if (hyphen >= 0)
                name = name.Substring(0, hyphen);
            var parts = name.Split('_');
            if (parts.Length < 4)
                continue;
            try
            {
                results.Add(new byte[]
                {
                    byte.Parse(parts[0]),
                    byte.Parse(parts[1]),
                    byte.Parse(parts[2]),
                    byte.Parse(parts[3]),
                });
            }
            catch (FormatException)
            {
                Console.WriteLine($"Warning: Could not parse map bytes from gate part: {mapPart} (gate: {gateName})");
            }
        }

        if (results.Count == 0)
            Console.WriteLine($"Warning: No map bytes parsed from gate name: {gateName}");

        return results;
    }

    /// <summary>
    /// Parse map bytes from an EMEVD filename (e.g., "m10_01_00_00.emevd.dcx" → [10,1,0,0]).
    /// Returns null for non-map files (e.g., "common.emevd.dcx").
    /// </summary>
    private static byte[]? ParseMapBytesFromFileName(string emevdPath)
    {
        // Strip extensions: "m10_01_00_00.emevd.dcx" → "m10_01_00_00"
        var fileName = Path.GetFileNameWithoutExtension(emevdPath);  // "m10_01_00_00.emevd"
        fileName = Path.GetFileNameWithoutExtension(fileName);        // "m10_01_00_00"

        if (!fileName.StartsWith("m", StringComparison.OrdinalIgnoreCase))
            return null;

        var name = fileName.TrimStart('m');
        var parts = name.Split('_');
        if (parts.Length < 4)
            return null;

        try
        {
            return new byte[]
            {
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]),
                byte.Parse(parts[3]),
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Register map byte tuples in the dest-only lookup, tracking collisions.
    /// </summary>
    private static void RegisterDestKeys(
        List<byte[]> mapBytesList, int flagId,
        Dictionary<(byte, byte, byte, byte), int> destOnlyLookup,
        HashSet<(byte, byte, byte, byte)> destOnlyCollisions)
    {
        foreach (var bytes in mapBytesList)
        {
            var destKey = (bytes[0], bytes[1], bytes[2], bytes[3]);
            if (destOnlyLookup.TryGetValue(destKey, out int existing) && existing != flagId)
            {
                destOnlyCollisions.Add(destKey);
                var destMapStr = $"m{destKey.Item1}_{destKey.Item2:D2}_{destKey.Item3:D2}_{destKey.Item4:D2}";
                Console.WriteLine($"Zone tracking: dest-only collision on {destMapStr} " +
                                  $"(flag {flagId} vs {existing})");
            }
            destOnlyLookup.TryAdd(destKey, flagId);
        }
    }

    /// <summary>
    /// Parse map bytes from a space-separated map string (e.g., "m31_19_00_00 m31_19_00_01").
    /// Used to resolve FogMod's internal area maps, which may differ from gate name prefixes.
    /// </summary>
    private static List<byte[]> ParseMapBytesFromMapString(string mapsStr)
    {
        var results = new List<byte[]>();
        foreach (var mapId in mapsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!mapId.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = mapId.TrimStart('m');
            var parts = name.Split('_');
            if (parts.Length < 4)
                continue;
            try
            {
                results.Add(new byte[]
                {
                    byte.Parse(parts[0]),
                    byte.Parse(parts[1]),
                    byte.Parse(parts[2]),
                    byte.Parse(parts[3]),
                });
            }
            catch (FormatException)
            {
                // Skip unparseable map IDs
            }
        }
        return results;
    }

    /// <summary>
    /// Create a boss death monitor event in common.emevd that sets finish_event
    /// when the final boss defeat flag is triggered.
    /// </summary>
    private static void InjectBossDeathEvent(
        string modDir, Events events, int finishEvent, int bossDefeatFlag)
    {
        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine("Warning: common.emevd.dcx not found, skipping boss death event");
            return;
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping boss death event");
            return;
        }

        // Create boss death monitor event
        var evt = new EMEVD.Event(BOSS_DEATH_EVENT_ID);
        // Wait for boss defeat flag
        evt.Instructions.Add(events.ParseAdd(
            $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {bossDefeatFlag})"));
        // Set our finish tracking flag
        evt.Instructions.Add(events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {finishEvent}, ON)"));

        emevd.Events.Add(evt);

        // Register in event 0 (InitializeEvent: bank 2000, id 0)
        var initArgs = new byte[8];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);                     // slot = 0
        BitConverter.GetBytes(BOSS_DEATH_EVENT_ID).CopyTo(initArgs, 4);   // eventId
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);

        Console.WriteLine($"Zone tracking: boss death monitor event {BOSS_DEATH_EVENT_ID} " +
                          $"(defeat flag {bossDefeatFlag} -> finish event {finishEvent})");
    }
}
