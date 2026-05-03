using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Loads the set of FogMod warp Names flagged <c>opensplit = true</c> in
/// <c>data/zone_metadata.toml</c>.
///
/// The same overrides feed Python cluster generation
/// (<c>tools/generate_clusters.py</c>); see <c>docs/opensplit-overrides.md</c>
/// for the rationale and FogMod reference points (Graph.cs:1257-1266).
/// </summary>
public static class OpenSplitOverrideLoader
{
    /// <summary>
    /// Parses zone metadata TOML content and returns the set of warp Names
    /// (e.g., "15002600") whose <c>[warps."&lt;name&gt;"]</c> table sets
    /// <c>opensplit = true</c>. Sections unrelated to warps are ignored.
    /// </summary>
    public static HashSet<string> Parse(string toml)
    {
        var ids = new HashSet<string>();

        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            return ids;

        if (!root.TryGetValue("warps", out var warpsObj) || warpsObj is not TomlTable warps)
            return ids;

        foreach (var (name, entry) in warps)
        {
            if (entry is TomlTable table
                && table.TryGetValue("opensplit", out var flag)
                && flag is bool b && b)
            {
                ids.Add(name);
            }
        }

        return ids;
    }

    /// <summary>
    /// Loads overrides from a file path. Returns an empty set when the file
    /// is absent (the injector becomes a no-op).
    /// </summary>
    public static HashSet<string> Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Opensplit overrides: no metadata at {path}, skipping");
            return new HashSet<string>();
        }

        var ids = Parse(File.ReadAllText(path));
        if (ids.Count > 0)
        {
            Console.WriteLine($"Opensplit overrides: loaded {ids.Count} entries from {path}: {string.Join(", ", ids.OrderBy(s => s))}");
        }
        return ids;
    }
}
