namespace StaticModBuilder.Winter;

/// <summary>
/// What the vertex layouts of the game's meshes provide, per matbin stem (case-insensitive):
/// the stems some skinned mesh draws, and for each stem the UV sets every mesh drawing it
/// provides (the smallest count over those meshes). A stem no mesh draws is in neither.
/// </summary>
public sealed class MaterialLayouts
{
    private readonly HashSet<string> _skinned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _uvSets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Skinned => _skinned;

    public IReadOnlyDictionary<string, int> UvSets => _uvSets;

    public void Add(FlverMaterialEntry entry)
    {
        if (entry.Skinned)
        {
            _skinned.Add(entry.MatbinStem);
        }
        if (entry.UvSets is int sets)
        {
            _uvSets[entry.MatbinStem] = _uvSets.TryGetValue(entry.MatbinStem, out int known) ? Math.Min(known, sets) : sets;
        }
    }

    public void Clear()
    {
        _skinned.Clear();
        _uvSets.Clear();
    }

    /// <summary>
    /// Whether some mesh drawing the stem provides fewer than <paramref name="required"/> UV
    /// sets; <paramref name="sets"/> is then that smallest count. False for a stem no mesh
    /// draws: there is no evidence against it.
    /// </summary>
    public bool LacksUvSets(string stem, int required, out int sets) =>
        _uvSets.TryGetValue(stem, out sets) && sets < required;

    public int CountWithFewerUvSetsThan(int uvSets) => _uvSets.Count(kv => kv.Value < uvSets);
}
