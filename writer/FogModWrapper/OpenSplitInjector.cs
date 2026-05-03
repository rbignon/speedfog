using FogMod;

namespace FogModWrapper;

/// <summary>
/// Tags selected FogMod entrances/warps with <c>opensplit</c> before
/// <c>Graph.Construct</c> runs.
///
/// Used to promote unique warps that have one core and one non-core (open)
/// side so FogMod keeps an edge for the core side instead of dropping the
/// whole warp (Graph.cs:1257-1266). Required for Sending Gate to Haligtree
/// (15002600), which is otherwise marked unused in crawl mode and leaves the
/// haligtree zone with no usable entrance edge other than Loretta's fog.
///
/// Source of truth: <c>data/zone_metadata.toml</c>, loaded by
/// <see cref="OpenSplitOverrideLoader"/>. The same overrides drive Python's
/// cluster generator so both layers stay in sync.
/// </summary>
public static class OpenSplitInjector
{
    /// <summary>
    /// Adds the <c>opensplit</c> tag to every entrance/warp whose Name is in
    /// <paramref name="warpIds"/>. Already-tagged entries are skipped.
    /// Unknown ids are logged but not treated as errors (e.g., a warp may be
    /// referenced for a future FogRando version).
    /// </summary>
    /// <returns>The number of entrances actually tagged.</returns>
    public static int Apply(AnnotationData ann, IReadOnlySet<string> warpIds)
    {
        if (warpIds.Count == 0)
            return 0;

        // Warps and Entrances live in different name spaces in fog.txt
        // (numeric IDs vs AEG099_* model names) so first match wins is safe
        // and matches the override list semantics.
        var remaining = new HashSet<string>(warpIds);
        int applied = 0;

        foreach (var entrance in ann.Warps.Concat(ann.Entrances))
        {
            if (entrance.Name == null)
                continue;
            if (!remaining.Contains(entrance.Name))
                continue;

            remaining.Remove(entrance.Name);
            if (entrance.HasTag("opensplit"))
                continue;

            entrance.AddTag("opensplit");
            applied++;
            Console.WriteLine($"Opensplit: tagged entrance '{entrance.Name}' ({entrance.ASide?.Area} -> {entrance.BSide?.Area})");
        }

        foreach (var unmatched in remaining)
        {
            Console.Error.WriteLine($"Opensplit: warning, override id '{unmatched}' did not match any entrance");
        }

        return applied;
    }
}
