using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects EMEVD events for racing zone tracking:
/// A) Inject SetEventFlag before warp instructions in FogMod-generated events
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
    /// A candidate entity-to-flag mapping, enriched with destination maps for disambiguation.
    /// When two connections share the same exit fog gate (allow_entry_as_exit), the same entity
    /// maps to multiple flags. The DestMaps allow disambiguation by comparing against the warp
    /// instruction's actual destination.
    /// </summary>
    internal readonly struct EntityCandidate
    {
        public readonly int FlagId;
        public readonly HashSet<(byte, byte, byte, byte)> DestMaps;

        public EntityCandidate(int flagId, HashSet<(byte, byte, byte, byte)> destMaps)
        {
            FlagId = flagId;
            DestMaps = destMaps;
        }
    }

    /// <summary>
    /// Destination map and region extracted from a warp instruction.
    /// </summary>
    private readonly struct WarpInfo
    {
        public readonly (byte, byte, byte, byte) DestMap;
        public readonly int Region;

        public WarpInfo((byte, byte, byte, byte) destMap, int region)
        {
            DestMap = destMap;
            Region = region;
        }
    }

    /// <summary>
    /// Try to extract warp destination info from an EMEVD instruction.
    /// Handles two instruction families that FogMod uses for zone transitions:
    ///
    /// 1. WarpPlayer (bank 2003, id 14):
    ///    ArgData layout: [area(1), block(1), sub(1), sub2(1), region(4), unk(4)]
    ///    Used by fogwarp template events and WarpBonfire portal events.
    ///
    /// 2. PlayCutsceneToPlayerAndWarp (bank 2002, id 11/12):
    ///    ArgData layout: [cutsceneId(4), playback(4), region(4), mapId(4), ...]
    ///    mapId is packed decimal: area*1000000 + block*10000 + sub*100 + sub2
    ///    Used by cutscene-based transitions (e.g., Erdtree burning at Forge of the Giants).
    ///    FogMod's EventEditor replaces the region and map in these instructions.
    /// </summary>
    private static WarpInfo? TryExtractWarpInfo(EMEVD.Instruction instr)
    {
        var a = instr.ArgData;

        // WarpPlayer (bank 2003, id 14)
        if (instr.Bank == 2003 && instr.ID == 14)
        {
            if (a.Length < 8)
                return null;
            var destMap = (a[0], a[1], a[2], a[3]);
            if (destMap == (0, 0, 0, 0))
                return null; // parameterized template
            int region = BitConverter.ToInt32(a, 4);
            return new WarpInfo(destMap, region);
        }

        // PlayCutsceneToPlayerAndWarp (bank 2002, id 11)
        // PlayCutsceneToPlayerAndWarpWithWeatherAndTime (bank 2002, id 12)
        if (instr.Bank == 2002 && (instr.ID == 11 || instr.ID == 12))
        {
            if (a.Length < 16)
                return null;
            int region = BitConverter.ToInt32(a, 8);
            int mapInt = BitConverter.ToInt32(a, 12);
            if (mapInt == 0)
                return null; // parameterized template
            var destMap = UnpackMapId(mapInt);
            return new WarpInfo(destMap, region);
        }

        return null;
    }

    /// <summary>
    /// Unpack a packed map ID int32 into 4 map bytes.
    /// Format: area * 1000000 + block * 10000 + sub * 100 + sub2
    /// (e.g., m13_00_00_00 = 13000000, m31_06_00_00 = 31060000)
    /// </summary>
    private static (byte, byte, byte, byte) UnpackMapId(int mapInt)
    {
        byte area = (byte)(mapInt / 1000000);
        byte block = (byte)((mapInt % 1000000) / 10000);
        byte sub = (byte)((mapInt % 10000) / 100);
        byte sub2 = (byte)(mapInt % 100);
        return (area, block, sub, sub2);
    }

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
        // Part A: Inject SetEventFlag before warp instructions in FogMod-modified events
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
    /// Scan all EMEVD files for warp instructions with literal destination map bytes.
    /// When a destination matches a connection's entrance gate map, inject SetEventFlag before
    /// the warp to set the zone tracking flag on traversal.
    ///
    /// Handles two warp instruction families:
    /// - WarpPlayer (2003:14): fogwarp template events and WarpBonfire portal events
    /// - PlayCutsceneToPlayerAndWarp (2002:11/12): cutscene-based transitions like the
    ///   Erdtree burning at the Forge of the Giants
    ///
    /// FogMod's EventEditor compiles the fogwarp template (9005777) into per-instance events
    /// with unique IDs and literal WarpPlayer values. It also modifies vanilla cutscene warp
    /// events to redirect to FogMod destinations. Both types are placed in map EMEVD files
    /// and we post-process them to add zone tracking.
    ///
    /// Matching strategy:
    /// 0. Entity match — if the event contains IfActionButtonInArea with a known exit gate
    ///    entity, match directly by entity_id. Most reliable, handles cases where FogMod
    ///    reuses vanilla destination entities (e.g., Placidusax lie-down uses region 13002834).
    /// 1. Try compound key (source_map, dest_map) — resolves collisions when two connections
    ///    from different source maps target the same dest map. Source maps include both
    ///    exit_gate map bytes AND exit_area areaMaps (FogMod may place events in either).
    ///    Dest maps include entrance_gate map bytes AND entrance_area areaMaps.
    /// 2. Fall back to dest-only matching — because FogMod's getEventMap() may place events
    ///    in a different EMEVD file than any known source map.
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
        // the rest. See docs/zone-tracking.md for design rationale.
        var compoundLookup = new Dictionary<((byte, byte, byte, byte), (byte, byte, byte, byte)), int>();
        var destOnlyLookup = new Dictionary<(byte, byte, byte, byte), int>();
        var destOnlyCollisions = new HashSet<(byte, byte, byte, byte)>();
        var compoundCollisions = new HashSet<((byte, byte, byte, byte), (byte, byte, byte, byte))>();

        // Entity-based disambiguation: exit gate entity_id → list of candidates.
        // When two connections share the same exit fog gate (allow_entry_as_exit),
        // the same entity maps to multiple candidates. Each candidate carries its
        // destination maps for disambiguation against the warp instruction's target.
        var entityToFlag = new Dictionary<int, List<EntityCandidate>>();

        foreach (var conn in connections)
        {
            if (conn.FlagId <= 0)
                continue;

            // Build all possible source maps (exit_gate maps + exit_area areaMaps).
            // FogMod's getEventMap() may place the warp event in the exit area's internal
            // map file instead of the gate's tile map, so we need both as potential sources.
            var allSourceMaps = ParseMapBytesFromGateName(conn.ExitGate);
            if (areaMaps.TryGetValue(conn.ExitArea, out var exitMapsStr) && !string.IsNullOrEmpty(exitMapsStr))
                allSourceMaps.AddRange(ParseMapBytesFromMapString(exitMapsStr));

            // Build all possible dest maps (entrance_gate maps + entrance_area areaMaps).
            // FogMod may warp to the dungeon interior map (e.g., m31_01) instead of the
            // overworld entrance tile (e.g., m60_44_34).
            var allDestMaps = ParseMapBytesFromGateName(conn.EntranceGate);
            if (areaMaps.TryGetValue(conn.EntranceArea, out var entranceMapsStr) && !string.IsNullOrEmpty(entranceMapsStr))
                allDestMaps.AddRange(ParseMapBytesFromMapString(entranceMapsStr));

            // Build entity lookup for disambiguation (after allDestMaps so we can attach them).
            // Two entity sources:
            // 1. ExitEntityId — the fog gate asset entity from fog_data.json
            // 2. Gate name suffix — for numeric gates (e.g., m34_12_00_00_34122840),
            //    the suffix IS the action entity used by FogMod in IfActionButtonInArea.
            //    For AEG099 gates, this is not a valid entity (skipped by TryParse).
            var destMapSet = new HashSet<(byte, byte, byte, byte)>(
                allDestMaps.Select(b => (b[0], b[1], b[2], b[3])));
            if (conn.ExitEntityId > 0)
                RegisterEntity(entityToFlag, conn.ExitEntityId, conn.FlagId, destMapSet);
            int gateActionEntity = ParseGateActionEntity(conn.ExitGate);
            if (gateActionEntity > 0)
                RegisterEntity(entityToFlag, gateActionEntity, conn.FlagId, destMapSet);

            // Register compound keys (all source × dest combinations)
            // Track collisions where TryAdd would keep a different flag
            foreach (var srcBytes in allSourceMaps)
            {
                var srcKey = (srcBytes[0], srcBytes[1], srcBytes[2], srcBytes[3]);
                foreach (var destBytes in allDestMaps)
                {
                    var destKey = (destBytes[0], destBytes[1], destBytes[2], destBytes[3]);
                    var compoundKey = (srcKey, destKey);
                    if (compoundLookup.TryGetValue(compoundKey, out int existing) && existing != conn.FlagId)
                    {
                        compoundCollisions.Add(compoundKey);
                        Console.WriteLine($"Zone tracking: compound key collision on " +
                                          $"{FormatMap(srcKey)} -> {FormatMap(destKey)} " +
                                          $"(flag {conn.FlagId} vs {existing})");
                    }
                    compoundLookup.TryAdd(compoundKey, conn.FlagId);
                }
            }

            // Register dest-only keys (fallback when compound key doesn't match)
            RegisterDestKeys(allDestMaps, conn.FlagId, destOnlyLookup, destOnlyCollisions);
        }

        // Common event lookup: for WarpBonfire connections whose vanilla events
        // live in common.emevd. These events have no IfActionButtonInArea and no
        // source map, so Strategies 0-2 can't match them reliably when dest maps
        // collide. Strategy 3 uses this dedicated lookup.
        var commonEventLookup = new Dictionary<(byte, byte, byte, byte), int>();
        var commonEventCollisions = new HashSet<(byte, byte, byte, byte)>();

        foreach (var conn in connections)
        {
            if (conn.FlagId <= 0 || !conn.HasCommonEvent)
                continue;

            var destMaps = ParseMapBytesFromGateName(conn.EntranceGate);
            if (areaMaps.TryGetValue(conn.EntranceArea, out var mapsStr) && !string.IsNullOrEmpty(mapsStr))
                destMaps.AddRange(ParseMapBytesFromMapString(mapsStr));

            foreach (var destBytes in destMaps)
                RegisterCommonEventKeys(destBytes, conn.FlagId, commonEventLookup, commonEventCollisions);
        }

        Console.WriteLine($"Zone tracking: {compoundLookup.Count} compound keys, " +
                          $"{destOnlyLookup.Count} dest maps ({destOnlyCollisions.Count} dest collisions, " +
                          $"{compoundCollisions.Count} compound collisions, " +
                          $"{entityToFlag.Values.Sum(v => v.Count)} entity candidates across {entityToFlag.Count} entities, " +
                          $"{commonEventLookup.Count} common event keys)");

        int totalInjected = 0;
        int compoundMatches = 0;
        int entityMatches = 0;
        int destOnlyMatches = 0;
        int commonEventMatches = 0;
        int skippedCollisions = 0;
        var injectedFlags = new HashSet<int>();

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            // Parse source map from EMEVD filename (e.g., "m10_01_00_00.emevd.dcx")
            var sourceMapBytes = ParseMapBytesFromFileName(emevdPath);
            var sourceMap = sourceMapBytes != null
                ? ((byte, byte, byte, byte)?)(sourceMapBytes[0], sourceMapBytes[1], sourceMapBytes[2], sourceMapBytes[3])
                : null;

            var emevd = EMEVD.Read(emevdPath);
            bool fileModified = false;

            // Build map from event ID → init args for resolving parameterized event values.
            // FogMod's manual events use InitializeEvent (bank 2000, id 0) to pass
            // actual entity IDs as parameters. The event template stores 0 as placeholder
            // and the Parameter list maps init arg offsets to instruction arg offsets.
            var initArgsMap = new Dictionary<long, List<byte[]>>();
            var evt0 = emevd.Events.Find(e => e.ID == 0);
            if (evt0 != null)
            {
                foreach (var initInstr in evt0.Instructions)
                {
                    if (initInstr.Bank == 2000 && initInstr.ID == 0 && initInstr.ArgData.Length >= 8)
                    {
                        int evtId = BitConverter.ToInt32(initInstr.ArgData, 4);
                        if (!initArgsMap.TryGetValue(evtId, out var list))
                        {
                            list = new List<byte[]>();
                            initArgsMap[evtId] = list;
                        }
                        list.Add(initInstr.ArgData);
                    }
                }
            }

            foreach (var evt in emevd.Events)
            {
                // First pass: find all matching warp positions and their flag IDs.
                // Some events (e.g., lie-down warps like Placidusax teleport) have multiple
                // warp instructions on different execution paths. We must inject
                // SetEventFlag before ALL of them, not just the first.
                var warpPositions = new List<(int index, int flagId)>();

                // Pre-scan: check if this event contains an IfActionButtonInArea with a known
                // exit gate entity. If so, it's a FogMod-generated manual fogwarp event, even
                // if it uses vanilla region entity IDs (e.g., Placidusax lie-down uses region
                // 13002834 which is below FOGMOD_ENTITY_BASE). The entity match gives us the
                // flag_id directly (or a list of candidates if the gate is shared).
                //
                // Also resolves parameterized entity values: FogMod's manual events use
                // InitializeEvent to pass actual entity IDs as parameters, with 0 as
                // placeholder in the instruction. We resolve these via the event's
                // Parameter list and the init args.
                List<byte[]>? evtInitArgs = null;
                initArgsMap.TryGetValue(evt.ID, out evtInitArgs);
                var entityCandidates = TryMatchEntityCandidates(evt, entityToFlag, evtInitArgs);

                for (int i = 0; i < evt.Instructions.Count; i++)
                {
                    var instr = evt.Instructions[i];
                    var warpInfo = TryExtractWarpInfo(instr);
                    if (warpInfo == null)
                        continue;

                    var destMap = warpInfo.Value.DestMap;
                    int region = warpInfo.Value.Region;

                    // Filter: only match FogMod-generated warp events, not vanilla ones.
                    // Two ways an event qualifies as FogMod-generated:
                    // 1. Region >= FOGMOD_ENTITY_BASE (FogMod-allocated warp target entity)
                    // 2. Event contains IfActionButtonInArea with a known exit gate entity
                    //    (FogMod manual fogwarp that reuses vanilla destination regions)
                    if (region < FOGMOD_ENTITY_BASE && entityCandidates == null)
                        continue;

                    // Strategy 0: Direct entity match — the event's IfActionButtonInArea
                    // references a known exit gate. Resolve candidates against the warp
                    // destination map for disambiguation when the gate is shared.
                    int flagId = 0;
                    bool matched = false;
                    if (entityCandidates != null)
                    {
                        var resolved = ResolveEntityCandidate(entityCandidates, destMap);
                        if (resolved.HasValue)
                        {
                            flagId = resolved.Value;
                            entityMatches++;
                            matched = true;
                        }
                        // else: multiple candidates, none matched dest map — fall through
                    }

                    // Strategy 1: Try compound key (source_map, dest_map) — resolves collisions
                    if (!matched && sourceMap.HasValue)
                    {
                        var compoundKey = (sourceMap.Value, destMap);
                        if (compoundLookup.TryGetValue(compoundKey, out flagId))
                        {
                            // If this compound key has a collision, try entity-based matching
                            // for more precise disambiguation. Reuse entityCandidates from
                            // the pre-scan rather than re-scanning the event's instructions.
                            if (compoundCollisions.Contains(compoundKey) && entityCandidates != null)
                            {
                                var resolved = ResolveEntityCandidate(entityCandidates, destMap);
                                if (resolved.HasValue)
                                {
                                    flagId = resolved.Value;
                                    entityMatches++;
                                    matched = true;
                                }
                                // else: fall through to use the compound-matched flag
                            }
                            if (!matched)
                            {
                                matched = true;
                                compoundMatches++;
                            }
                        }
                    }

                    // Strategy 2: Fall back to dest-only matching.
                    // When dest map has a collision AND source map is known (map-specific
                    // EMEVD), skip — these are typically back-portal return warps whose
                    // source EMEVD doesn't match any registered exit map.
                    // When source map is unknown (common.emevd) AND dest map has a common
                    // event entry, skip dest-only — let Strategy 3 handle it more precisely.
                    // When source map is unknown AND no common event entry, inject anyway —
                    // FogMod places forward warps for vanilla gate types in common.emevd.
                    if (!matched)
                    {
                        if (!destOnlyLookup.TryGetValue(destMap, out flagId))
                        {
                            // Dest map not in dest-only lookup at all. Fall through to
                            // Strategy 3 (common event) — don't continue, as the warp
                            // may still be matchable via common event lookup.
                        }
                        else if (destOnlyCollisions.Contains(destMap))
                        {
                            if (sourceMap.HasValue)
                            {
                                Console.WriteLine($"Zone tracking: skipped collided dest-only " +
                                                  $"on {FormatMap(destMap)} from EMEVD {FormatMap(sourceMap.Value)} (event {evt.ID})");
                                skippedCollisions++;
                                continue;
                            }
                            if (commonEventLookup.ContainsKey(destMap))
                            {
                                // Dest collision in common.emevd with a common event entry:
                                // don't match via dest-only, fall through to Strategy 3.
                                skippedCollisions++;
                            }
                            else
                            {
                                // Dest collision in common.emevd without common event entry:
                                // no better strategy available, inject via dest-only.
                                matched = true;
                                destOnlyMatches++;
                            }
                        }
                        else
                        {
                            matched = true;
                            destOnlyMatches++;
                        }
                    }

                    // Strategy 3: Common event matching — for WarpBonfire connections whose
                    // vanilla events (e.g., Erdtree burning Event 901) live in common.emevd.
                    // Only checked when sourceMap is null (common.emevd) and all other
                    // strategies failed.
                    if (!matched && !sourceMap.HasValue)
                    {
                        if (commonEventLookup.TryGetValue(destMap, out flagId) &&
                            !commonEventCollisions.Contains(destMap))
                        {
                            matched = true;
                            commonEventMatches++;
                        }
                    }

                    if (!matched)
                        continue;

                    warpPositions.Add((i, flagId));
                }

                if (warpPositions.Count == 0)
                    continue;

                // Second pass: insert SetEventFlag before each warp, from last to first
                // to avoid index shifting affecting earlier positions.
                for (int j = warpPositions.Count - 1; j >= 0; j--)
                {
                    var (warpIdx, flagId) = warpPositions[j];

                    var setFlagInstr = events.ParseAdd(
                        $"SetEventFlag(TargetEventFlagType.EventFlag, {flagId}, ON)");
                    evt.Instructions.Insert(warpIdx, setFlagInstr);

                    // Shift Parameter entries for instructions at or after insertion point
                    foreach (var param in evt.Parameters)
                    {
                        if (param.InstructionIndex >= warpIdx)
                        {
                            param.InstructionIndex++;
                        }
                    }
                }

                foreach (var (_, fid) in warpPositions)
                    injectedFlags.Add(fid);
                totalInjected += warpPositions.Count;
                fileModified = true;
            }

            if (fileModified)
            {
                emevd.Write(emevdPath);
            }
        }

        var expectedFlagSet = connections.Where(c => c.FlagId > 0).Select(c => c.FlagId).Distinct().ToHashSet();
        Console.WriteLine($"Zone tracking: injected {totalInjected} fog gate tracking flags " +
                          $"(compound: {compoundMatches}, entity: {entityMatches}, dest-only: {destOnlyMatches}, " +
                          $"common-event: {commonEventMatches}, skipped collisions: {skippedCollisions}, " +
                          $"expected unique flags: {expectedFlagSet.Count})");

        var missingFlags = expectedFlagSet.Except(injectedFlags).OrderBy(f => f).ToList();
        if (missingFlags.Count > 0)
        {
            throw new Exception(
                $"Zone tracking: {missingFlags.Count} flags NOT injected: " +
                string.Join(", ", missingFlags) +
                ". Cannot produce a valid mod output.");
        }
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
    /// Register map byte tuples in the common event lookup, tracking collisions.
    /// Used for connections whose vanilla warp event lives in common.emevd
    /// (WarpBonfire gates like the Erdtree burning).
    /// </summary>
    internal static void RegisterCommonEventKeys(
        byte[] destBytes, int flagId,
        Dictionary<(byte, byte, byte, byte), int> commonEventLookup,
        HashSet<(byte, byte, byte, byte)> commonEventCollisions)
    {
        var destKey = (destBytes[0], destBytes[1], destBytes[2], destBytes[3]);
        if (commonEventLookup.TryGetValue(destKey, out int existing) && existing != flagId)
        {
            commonEventCollisions.Add(destKey);
            Console.WriteLine($"Zone tracking: common event collision on {FormatMap(destKey)} " +
                              $"(flag {flagId} vs {existing})");
        }
        commonEventLookup.TryAdd(destKey, flagId);
    }

    /// <summary>
    /// Register an entity ID → candidate mapping, appending to the list for shared entities.
    /// </summary>
    internal static void RegisterEntity(
        Dictionary<int, List<EntityCandidate>> entityToFlag,
        int entityId, int flagId, HashSet<(byte, byte, byte, byte)> destMaps)
    {
        if (!entityToFlag.TryGetValue(entityId, out var list))
        {
            list = new List<EntityCandidate>();
            entityToFlag[entityId] = list;
        }
        list.Add(new EntityCandidate(flagId, destMaps));
    }

    /// <summary>
    /// Try to match an event to connection candidates by scanning its instructions for a known
    /// exit gate entity ID in IfActionButtonInArea (bank 3, id 24).
    ///
    /// ArgData layout for IfActionButtonInArea (3:24):
    ///   [0..3] Condition Group (int)
    ///   [4..7] Action Button Parameter ID (int)
    ///   [8..11] Target Entity ID (uint) ← the fog gate entity
    ///
    /// Handles two cases:
    /// 1. Literal entity ID baked directly into the instruction (non-parameterized events).
    /// 2. Parameterized entity ID: the instruction stores 0 as placeholder, and the actual
    ///    value is passed via InitializeEvent args. FogMod's manual fogwarp events for
    ///    numeric-named gates (e.g., 34122840) use this pattern. We resolve the parameter
    ///    by finding the Parameter entry that targets this instruction's offset 8, then
    ///    reading the actual value from the InitializeEvent args at the source offset.
    /// </summary>
    /// <returns>The matching candidate list, or null if no entity_id matches.</returns>
    /// <remarks>
    /// Must be called before any instruction insertions into the event, since
    /// Parameter.InstructionIndex values refer to the original instruction list.
    /// </remarks>
    internal static List<EntityCandidate>? TryMatchEntityCandidates(
        EMEVD.Event evt, Dictionary<int, List<EntityCandidate>> entityToFlag, List<byte[]>? initArgsList)
    {
        for (int instrIdx = 0; instrIdx < evt.Instructions.Count; instrIdx++)
        {
            var instr = evt.Instructions[instrIdx];
            // IfActionButtonInArea (bank 3, id 24): entity_id at ArgData offset 8
            if (instr.Bank != 3 || instr.ID != 24 || instr.ArgData.Length < 12)
                continue;

            int entityId = BitConverter.ToInt32(instr.ArgData, 8);

            // Case 1: literal entity ID
            if (entityId != 0 && entityToFlag.TryGetValue(entityId, out var candidates))
                return candidates;

            // Case 2: parameterized entity (placeholder = 0). Resolve from init args.
            if (entityId == 0 && initArgsList != null)
            {
                // Find the Parameter entry that maps to this instruction's offset 8
                foreach (var param in evt.Parameters)
                {
                    if (param.InstructionIndex == instrIdx &&
                        param.TargetStartByte == 8 &&
                        param.ByteCount == 4)
                    {
                        // Read the actual entity value from each init args set
                        foreach (var initArgs in initArgsList)
                        {
                            // InitializeEvent args layout: [slot(4), eventId(4), params...]
                            // param.SourceStartByte is offset into the params portion,
                            // which starts at byte 8 in the init instruction's ArgData.
                            int srcOffset = 8 + (int)param.SourceStartByte;
                            if (srcOffset + 4 <= initArgs.Length)
                            {
                                int resolvedEntity = BitConverter.ToInt32(initArgs, srcOffset);
                                if (resolvedEntity != 0 &&
                                    entityToFlag.TryGetValue(resolvedEntity, out candidates))
                                    return candidates;
                            }
                        }
                    }
                }

                // Diagnostic: parameterized entity with no init args to resolve.
                // This happens for roundtable exit events where entity_id=0 but no
                // InitializeEvent args are available in this EMEVD file.
                if (initArgsList.Count == 0)
                {
                    Console.WriteLine($"Zone tracking: event {evt.ID} has IfActionButtonInArea " +
                                      $"with entity_id=0 but no init args to resolve (possible roundtable exit)");
                }
            }
            else if (entityId == 0 && initArgsList == null)
            {
                Console.WriteLine($"Zone tracking: event {evt.ID} has IfActionButtonInArea " +
                                  $"with entity_id=0 and no init args map entry (possible roundtable exit)");
            }
        }
        return null;
    }

    /// <summary>
    /// Resolve a list of entity candidates to a single flag ID by matching against the
    /// warp instruction's destination map. When only one candidate exists, returns it
    /// directly. When multiple exist (shared exit gate), disambiguates by destination map.
    /// </summary>
    /// <returns>The matched flag_id, or null if disambiguation fails.</returns>
    internal static int? ResolveEntityCandidate(
        List<EntityCandidate> candidates, (byte, byte, byte, byte) warpDestMap)
    {
        if (candidates.Count == 1)
            return candidates[0].FlagId;

        // Multiple candidates: find the one whose DestMaps contains the warp destination.
        var matches = candidates.Where(c => c.DestMaps.Contains(warpDestMap)).ToList();
        if (matches.Count == 1)
            return matches[0].FlagId;

        // 0 or N matches — ambiguous, fall through to compound/dest-only strategies.
        return null;
    }

    /// <summary>
    /// Extract the action entity ID from a gate name's numeric suffix.
    /// For numeric-named gates (e.g., "m34_12_00_00_34122840"), the suffix after the
    /// 4-part map prefix IS the action entity used by FogMod in IfActionButtonInArea.
    /// For AEG099 gates (e.g., "m10_01_00_00_AEG099_001_9000"), returns 0 (not numeric).
    /// Assumes numeric gates are always single-tile (no cross-tile "mXX_..._mXX_..." format).
    /// </summary>
    internal static int ParseGateActionEntity(string gateName)
    {
        if (string.IsNullOrEmpty(gateName))
            return 0;

        // Gate format: "m{area}_{block}_{sub}_{sub2}_{suffix}"
        // Split and take everything after the 4th underscore
        var parts = gateName.Split('_');
        if (parts.Length < 5)
            return 0;

        // The suffix is the 5th part (index 4). For numeric gates it's a pure number.
        // For AEG099 gates it's "AEG099" which won't parse as int.
        if (int.TryParse(parts[4], out int entityId))
            return entityId;

        return 0;
    }

    /// <summary>
    /// Format a map byte tuple as a human-readable string (e.g., "m10_01_00_00").
    /// </summary>
    private static string FormatMap((byte, byte, byte, byte) map)
    {
        return $"m{map.Item1}_{map.Item2:D2}_{map.Item3:D2}_{map.Item4:D2}";
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
