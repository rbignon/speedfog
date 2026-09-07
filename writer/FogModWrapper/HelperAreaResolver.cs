using System.Numerics;
using System.Text.RegularExpressions;
using FogMod;
using SoulsFormats;
using static FogMod.AnnotationData;

namespace FogModWrapper;

/// <summary>
/// Fixes enemy scaling for helper enemies created by the enemy randomizer
/// (RandomizerCommon) before FogMod's writer runs.
///
/// FogMod resolves each enemy's scaling area by part name (foglocations2.txt
/// Enemies), then entity group, then collision, then the map's default area
/// (MainMap fallback, GameDataWriterE.cs:2081-2098). Randomizer-created
/// helpers (e.g. the Godskin Duo respawning backups) defeat all three
/// specific lookups: their part names are new, the transplant clears the
/// vanilla boss group and replaces it with a randomizer-allocated one, and
/// some boss arenas (volcano_rykard) declare Groups only, no Cols. They fall
/// through to the map default area, whose DAG tier can be wildly different
/// from the arena's (tier 4 vs 15 on a real seed).
///
/// The boss slot parts themselves keep their vanilla part names, so they
/// still resolve by name. Two passes propagate that resolution to the
/// clones, giving each an EnemyLoc entry pointing at the boss arena so
/// FogMod's name lookup (highest priority) treats it like any vanilla boss
/// part (arena tier, unique boss scaling): the group pass links a part
/// sharing a non-vanilla entity group with a name-resolved slot (only
/// sources whose main part declares Groups produce that signature), and
/// the model pass links clone-named parts whose model belongs to the
/// placed source's helpers (graph.json helper_models) to the nearest
/// claiming slot. See docs/item-randomizer.md, section "Helper enemy
/// scaling".
///
/// ApplyVanillaOverrides covers the converse, randomizer-independent case:
/// vanilla parts misfiled by foglocations2 outside their boss arena.
/// </summary>
public static class HelperAreaResolver
{
    // CollisionName is intentionally unused by the decision logic (see
    // ComputeAdditions); it is carried so the contract mirrors the MSB data
    // and tests can document the deliberate collision override.
    public sealed record EnemyPart(
        string Name,
        IReadOnlyList<uint> Groups,
        string? CollisionName,
        uint EntityId = 0,
        Vector3 Position = default);

    // Part names the randomizer's CloneEnemy produces: "{model}_{index:d4}",
    // optionally prefixed by an open-world tile ("m60_52_38_00-c0000_0109"),
    // index from helperModelBase (100) upwards. Vanilla parts use 9xxx
    // (four use 0000-0003), so an index in 100-8999 marks a clone.
    private static readonly Regex ClonePartName = new(
        @"^(?:m\d\d_\d\d_\d\d_\d\d-)?(c\d{4})_(\d{4})$", RegexOptions.Compiled);

    /// <summary>
    /// Scans the merge directory's MSBs (the maps the item/enemy randomizer
    /// actually modified) and appends EnemyLoc entries to
    /// <paramref name="ann"/>.Locations.Enemies for randomizer helper parts.
    /// Must run before GameDataWriterE.Write and after area tiers are applied
    /// to <paramref name="graph"/>.
    /// </summary>
    /// <returns>The number of entries added.</returns>
    public static int Resolve(
        AnnotationData ann,
        Graph graph,
        string mergeDir,
        IReadOnlyDictionary<string, List<string>> helperModels,
        Action<string> log)
    {
        var msbDir = Path.Combine(mergeDir, "map", "mapstudio");
        if (!Directory.Exists(msbDir) || ann.Locations == null)
            return 0;

        bool isEligibleBossArea(string area) =>
            graph.Areas.TryGetValue(area, out var a)
            && a.DefeatFlag > 0
            && graph.AreaTiers != null
            && graph.AreaTiers.ContainsKey(area);
        bool isBossArea(string area) =>
            graph.Areas.TryGetValue(area, out var a) && a.DefeatFlag > 0;

        var eligibleMaps = EligibleMaps(ann.Locations, isEligibleBossArea);
        var arenaHelperModels = new Dictionary<uint, IReadOnlyList<string>>();
        foreach (var (arena, models) in helperModels)
        {
            if (uint.TryParse(arena, out var id) && models is { Count: > 0 })
                arenaHelperModels[id] = models;
        }
        int total = 0, maps = 0;
        foreach (var msbPath in Directory.EnumerateFiles(msbDir, "*.msb.dcx").Order())
        {
            var map = Path.GetFileName(msbPath)[..^".msb.dcx".Length];
            if (!eligibleMaps.Contains(map))
                continue;
            MSBE msb;
            try
            {
                msb = MSBE.Read(msbPath);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Failed to parse merge-dir MSB {msbPath}", e);
            }
            var parts = msb.Parts.Enemies
                .Select(e => new EnemyPart(
                    e.Name, e.EntityGroupIDs, e.CollisionPartName, e.EntityID, e.Position))
                .ToList();
            maps++;

            var added = ComputeAdditions(map, parts, ann.Locations, isEligibleBossArea);
            foreach (var loc in added)
            {
                // Appending also feeds FogMod's per-area boss-representative
                // selection, but vanilla slots still win there (it prefers
                // the part whose EntityID matches the area's DefeatFlag).
                ann.Locations.Enemies.Add(loc);
                log($"  Helper area (group): {map} {loc.ID} -> {loc.ActualArea}");
            }
            total += added.Count;

            // Second pass sees the first pass's entries as name-resolved.
            var byModel = ComputeModelAdditions(
                map, parts, ann.Locations, isEligibleBossArea, isBossArea, arenaHelperModels);
            foreach (var loc in byModel)
            {
                ann.Locations.Enemies.Add(loc);
                log($"  Helper area (model): {map} {loc.ID} -> {loc.ActualArea} ({loc.DebugText})");
            }
            total += byModel.Count;
        }

        log($"HelperAreaResolver: added {total} enemy location entries " +
            $"({maps} maps scanned)");
        return total;
    }

    /// <summary>
    /// Pure core: computes the EnemyLoc entries to add for one map.
    /// A part qualifies when it is not resolvable by FogMod's name or group
    /// lookups and shares a non-vanilla entity group with a part that
    /// resolves by name to an eligible boss area. Collisions are deliberately
    /// ignored: the boss-group signal outranks a collision (arena floors can
    /// belong to another area's Cols), and name entries win over collisions
    /// in FogMod's resolution order anyway.
    /// </summary>
    public static List<EnemyLoc> ComputeAdditions(
        string map,
        IReadOnlyList<EnemyPart> parts,
        FogLocations locations,
        Func<string, bool> isEligibleBossArea)
    {
        var (knownGroups, enemyAreaNames, locByName) = IndexLocations(map, locations);

        // Non-vanilla group -> boss area of the name-resolved part carrying
        // it. A group seen on slots of two different areas is ambiguous and
        // dropped (null).
        var bossGroups = new Dictionary<uint, string?>();
        foreach (var part in parts)
        {
            if (!locByName.TryGetValue(part.Name, out var loc))
                continue;
            var area = loc.ActualArea;
            if (!isEligibleBossArea(area) || !enemyAreaNames.Contains(area))
                continue;
            foreach (var group in part.Groups)
            {
                if (group == 0 || knownGroups.Contains(group))
                    continue;
                if (bossGroups.TryGetValue(group, out var existing))
                {
                    if (existing != area)
                        bossGroups[group] = null;
                }
                else
                {
                    bossGroups[group] = area;
                }
            }
        }

        // FogMod builds its name dictionary with ToDictionary, which throws
        // on duplicate (Map, ID) pairs; never emit the same part name twice.
        var emitted = new HashSet<string>();
        var added = new List<EnemyLoc>();
        foreach (var part in parts)
        {
            if (locByName.ContainsKey(part.Name) || emitted.Contains(part.Name))
                continue;
            if (part.Groups.Any(g => g != 0 && knownGroups.Contains(g)))
                continue;

            var area = part.Groups
                .Where(g => g != 0)
                .Select(g => bossGroups.GetValueOrDefault(g))
                .FirstOrDefault(a => a != null);
            if (area == null)
                continue;

            emitted.Add(part.Name);
            added.Add(new EnemyLoc { Map = map, ID = part.Name, Area = area });
        }
        return added;
    }

    /// <summary>
    /// Pure core of the model pass: links randomizer helper clones to the
    /// arena of the boss they were cloned for, using graph.json
    /// <c>helper_models</c> (arena entity id -> models of the placed
    /// source's helpers, from enemy.txt). A part qualifies when its name is
    /// a CloneEnemy name (see <see cref="ClonePartName"/>), it is not
    /// resolvable by name or known groups, and its model is among the
    /// helper models of an eligible, name-resolved slot of this map. The
    /// nearest claiming slot wins, unless a boss slot of another area (DAG
    /// or not: the randomizer randomizes every boss slot) is nearer still,
    /// since clones are placed inside their own arena.
    /// </summary>
    public static List<EnemyLoc> ComputeModelAdditions(
        string map,
        IReadOnlyList<EnemyPart> parts,
        FogLocations locations,
        Func<string, bool> isEligibleBossArea,
        Func<string, bool> isBossArea,
        IReadOnlyDictionary<uint, IReadOnlyList<string>> arenaHelperModels)
    {
        var added = new List<EnemyLoc>();
        if (arenaHelperModels.Count == 0)
            return added;

        var (knownGroups, enemyAreaNames, locByName) = IndexLocations(map, locations);

        // Every name-resolved boss slot of the map competes for clones by
        // distance: the randomizer also randomizes slots outside the DAG,
        // and their clones share the map. Only eligible slots that received
        // a source with helpers can claim a clone (Models != null).
        var slots = new List<(string Name, string Area, Vector3 Position, HashSet<string>? Models)>();
        foreach (var part in parts)
        {
            if (!locByName.TryGetValue(part.Name, out var loc))
                continue;
            var area = loc.ActualArea;
            if (!isBossArea(area))
                continue;
            HashSet<string>? models = null;
            if (part.EntityId != 0
                && arenaHelperModels.TryGetValue(part.EntityId, out var arenaModels)
                && isEligibleBossArea(area)
                && enemyAreaNames.Contains(area))
            {
                models = new HashSet<string>(arenaModels);
            }
            slots.Add((part.Name, area, part.Position, models));
        }
        if (slots.All(s => s.Models == null))
            return added;

        var emitted = new HashSet<string>();
        foreach (var part in parts)
        {
            if (locByName.ContainsKey(part.Name) || emitted.Contains(part.Name))
                continue;
            if (part.Groups.Any(g => g != 0 && knownGroups.Contains(g)))
                continue;
            var m = ClonePartName.Match(part.Name);
            if (!m.Success)
                continue;
            var index = int.Parse(m.Groups[2].Value);
            if (index < 100 || index >= 9000)
                continue;
            var model = m.Groups[1].Value;

            (string Name, string Area, Vector3 Position, HashSet<string>? Models)? claimant = null;
            float claimantDistance = float.MaxValue;
            string? nearestArea = null;
            float nearestDistance = float.MaxValue;
            foreach (var slot in slots)
            {
                var distance = Vector3.DistanceSquared(slot.Position, part.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestArea = slot.Area;
                }
                if (slot.Models != null && slot.Models.Contains(model) && distance < claimantDistance)
                {
                    claimantDistance = distance;
                    claimant = slot;
                }
            }
            // Clones sit inside their arena: a boss slot of another area
            // being nearer means the clone belongs to that slot's boss.
            if (claimant == null || nearestArea != claimant.Value.Area)
                continue;

            emitted.Add(part.Name);
            added.Add(new EnemyLoc
            {
                Map = map,
                ID = part.Name,
                Area = claimant.Value.Area,
                DebugText = $"{model} clone {Math.Sqrt(claimantDistance):F1}m from {claimant.Value.Name}",
            });
        }
        return added;
    }

    /// <summary>
    /// Shared lookups of both passes: groups declared on some area (parts
    /// carrying them resolve via FogMod's group lookup and need no help),
    /// area names (FogMod indexes EnemyAreas by name and would throw on an
    /// EnemyLoc pointing at an area with no EnemyLocArea entry), and the
    /// map's name-resolved parts.
    /// </summary>
    private static (HashSet<uint> KnownGroups, HashSet<string> AreaNames, Dictionary<string, EnemyLoc> ByName)
        IndexLocations(string map, FogLocations locations)
    {
        var knownGroups = new HashSet<uint>();
        var enemyAreaNames = new HashSet<string>();
        foreach (var area in locations.EnemyAreas)
        {
            enemyAreaNames.Add(area.Name);
            foreach (var group in SplitIds(area.Groups))
                knownGroups.Add(group);
        }

        var locByName = new Dictionary<string, EnemyLoc>();
        foreach (var loc in locations.Enemies)
        {
            if (loc.Map == map)
                locByName.TryAdd(loc.ID, loc);
        }
        return (knownGroups, enemyAreaNames, locByName);
    }

    // Vanilla foglocations2 assignments that are wrong for SpeedFog's DAG
    // model: parts living inside a boss arena but filed under the surrounding
    // area. FogRando always tiers both areas so the misfiling is harmless
    // there; in SpeedFog the surrounding area can be absent from the DAG, and
    // FogMod then silently skips the rescale (AllowUnlinked), leaving the part
    // at vanilla stats. Sole known case: "Mini Midra" (28000801), the phase-1
    // Midra inside the arena, sharing the boss slot's entity groups and
    // collision.
    private static readonly (string Map, string Id, string Area)[] VanillaAreaOverrides =
    {
        ("m28_00_00_00", "c5050_9000", "midramanse_boss"),
    };

    /// <summary>
    /// Re-points misfiled vanilla EnemyLoc entries at their boss area. Unlike
    /// <see cref="Resolve"/> this is independent of the enemy randomizer and
    /// must run on every seed, before Resolve and before GameDataWriterE.Write.
    /// </summary>
    /// <returns>The number of entries re-pointed.</returns>
    public static int ApplyVanillaOverrides(FogLocations locations, Action<string> log)
    {
        var enemyAreaNames = locations.EnemyAreas.Select(a => a.Name).ToHashSet();
        int changed = 0;
        foreach (var (map, id, area) in VanillaAreaOverrides)
        {
            // FogMod indexes EnemyAreas by name and would throw on an EnemyLoc
            // pointing at an area with no EnemyLocArea entry.
            if (!enemyAreaNames.Contains(area))
            {
                log($"  Vanilla area override skipped: {area} not in EnemyAreas");
                continue;
            }
            foreach (var loc in locations.Enemies)
            {
                if (loc.Map != map || loc.ID != id || loc.ActualArea == area)
                    continue;
                log($"  Vanilla area override: {map} {id} {loc.ActualArea} -> {area}");
                loc.Area = area;
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// Maps that can yield additions: those with at least one name-resolvable
    /// enemy in an eligible boss area. Helpers are always created in the same
    /// MSB as the boss slot they belong to, so scanning other maps (hundreds
    /// of open-world tiles in a typical merge dir) is wasted work.
    /// </summary>
    public static HashSet<string> EligibleMaps(
        FogLocations locations, Func<string, bool> isEligibleBossArea)
    {
        return locations.Enemies
            .Where(l => isEligibleBossArea(l.ActualArea))
            .Select(l => l.Map)
            .ToHashSet();
    }

    private static IEnumerable<uint> SplitIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            yield break;
        foreach (var token in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(token, out var id))
                yield return id;
        }
    }
}
