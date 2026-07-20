using FogMod;

namespace FogModWrapper;

/// <summary>
/// Injects synthetic zones and fog gates from data/map_splits.toml into
/// FogMod's AnnotationData before Graph.Construct, so FogMod creates the
/// gates (MakeFrom), trigger regions and warps through its standard path.
/// Also splits EnemyAreas by collision so each half of a split map scales
/// with its own tier. Mirrors the Python-side injection in
/// tools/generate_clusters.py (docs/map-splits.md).
/// </summary>
public static class MapSplitsInjector
{
    public static void Apply(AnnotationData ann, MapSplits splits)
    {
        foreach (var zone in splits.Zones)
        {
            if (ann.Areas.Any(a => a.Name == zone.Name))
                throw new InvalidDataException(
                    $"map_splits: zone '{zone.Name}' already exists in fog.txt");
            ann.Areas.Add(new AnnotationData.Area
            {
                Name = zone.Name,
                Text = zone.DisplayName,
                Maps = zone.Map,
                // null, not "": Taggable's Tags setter splits on ' ', so ""
                // would produce TagList = [""] (a phantom empty tag). fog.txt
                // areas with no tags have Tags = null.
                Tags = zone.Tags.Count > 0 ? string.Join(' ', zone.Tags) : null,
            });
            Console.WriteLine($"MapSplits: added area '{zone.Name}' ({zone.Map})");
        }

        foreach (var fog in splits.Fogs)
        {
            if (ann.Entrances.Any(x => x.Name == fog.Name) || ann.Warps.Any(x => x.Name == fog.Name))
                throw new InvalidDataException(
                    $"map_splits: fog '{fog.Name}' already exists in fog.txt");
            ann.Entrances.Add(new AnnotationData.Entrance
            {
                Name = fog.Name,
                ID = fog.Id,
                Area = fog.Map,
                Text = fog.Text,
                MakeFrom = fog.MakeFrom,
                ASide = new AnnotationData.Side { Area = fog.ASideArea, Text = fog.ASideText },
                BSide = new AnnotationData.Side { Area = fog.BSideArea, Text = fog.BSideText },
            });
            Console.WriteLine(
                $"MapSplits: added synthetic gate '{fog.Name}' ({fog.ASideArea} -> {fog.BSideArea})");
        }

        SplitEnemyAreas(ann, splits);
    }

    /// <summary>
    /// Moves the supplement-listed collision names from the source EnemyArea
    /// to a new one named after the synthetic zone, and reassigns per-enemy
    /// entries accordingly. EnemyLocArea.Cols are map-prefixed
    /// ("m20_01_00_00_h001800"); EnemyLoc.Col is unprefixed ("h001800").
    /// No-op while the supplement's cols list is empty.
    /// </summary>
    private static void SplitEnemyAreas(AnnotationData ann, MapSplits splits)
    {
        if (ann.Locations?.EnemyAreas == null)
        {
            if (splits.Zones.Any(z => z.Cols.Count > 0))
                Console.WriteLine("MapSplits: warning, cols configured but no foglocations loaded; EnemyArea split skipped");
            return;
        }
        foreach (var zone in splits.Zones.Where(z => z.Cols.Count > 0))
        {
            var src = ann.Locations.EnemyAreas.FirstOrDefault(a => a.Name == zone.SplitFrom)
                ?? throw new InvalidDataException(
                    $"map_splits: EnemyArea '{zone.SplitFrom}' not found for split '{zone.Name}'");
            var prefixed = zone.Cols.Select(c => $"{zone.Map}_{c}").ToHashSet();
            var srcCols = (src.Cols ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var moved = srcCols.Where(prefixed.Contains).ToList();
            if (moved.Count != prefixed.Count)
                throw new InvalidDataException(
                    $"map_splits: cols missing from EnemyArea '{zone.SplitFrom}': "
                    + string.Join(", ", prefixed.Except(moved)));
            src.Cols = string.Join(' ', srcCols.Where(c => !prefixed.Contains(c)));
            ann.Locations.EnemyAreas.Add(new AnnotationData.EnemyLocArea
            {
                Name = zone.Name,
                Cols = string.Join(' ', moved),
                ScalingTier = src.ScalingTier,
            });

            var colSet = zone.Cols.ToHashSet();
            int reassigned = 0;
            foreach (var loc in ann.Locations.Enemies)
            {
                if (loc.Map == zone.Map && loc.Col != null && colSet.Contains(loc.Col)
                    && loc.ActualArea == zone.SplitFrom)
                {
                    loc.Area = zone.Name;
                    reassigned++;
                }
            }
            Console.WriteLine(
                $"MapSplits: EnemyArea '{zone.SplitFrom}' -> '{zone.Name}': "
                + $"{moved.Count} cols, {reassigned} enemies reassigned");
        }
    }
}
