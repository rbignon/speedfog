using SoulsFormats;
using TableReader = System.Func<byte[], System.Collections.Generic.IReadOnlyList<StaticModBuilder.Winter.FlverMaterialEntry>>;

namespace StaticModBuilder.Winter;

/// <summary>
/// Collects, over every model binder of the game, what the vertex layouts provide per matbin
/// stem: the matbins used by skinned meshes (the snow shaders have no skinned permutation, so
/// those must stay vanilla) and the UV sets every mesh provides (a twin whose vertex shaders
/// read more leaves the mesh undrawn).
/// </summary>
public static class MaterialLayoutScanner
{
    /// <summary>
    /// About how many binders are read with both FLVER readers to check the fast one, spread over
    /// the whole list so geombnds and mapbnds are both sampled (see <see cref="InSelfCheckSample"/>).
    /// </summary>
    public const int SelfCheckBinders = 200;

    private const int ProgressInterval = 1000;

    private static readonly (string Root, string Suffix)[] BinderRoots =
    {
        (Path.Combine("asset", "aeg"), ".geombnd.dcx"),
        ("map", ".mapbnd.dcx"),
    };

    public static IEnumerable<string> EnumerateBinders(string gameDir)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        var binders = new List<string>();
        foreach (var (root, suffix) in BinderRoots)
        {
            string dir = Path.Combine(gameDir, root);
            var found = Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*" + suffix, options)
                    .Where(p => p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList()
                : new List<string>();
            if (found.Count == 0)
            {
                throw new WinterBuildException($"layout scan: no *{suffix} under {dir}");
            }
            binders.AddRange(found);
        }
        return binders;
    }

    public static void Collect(BND4 binder, MaterialLayouts layouts, bool useFastReader, WinterReport report) =>
        Collect(binder, layouts, useFastReader ? FlverMaterialTable.Read : null, report);

    /// <summary>
    /// Whether binder <paramref name="index"/> of <paramref name="count"/> is self-checked: every
    /// (count / SelfCheckBinders)-th binder from the first, every binder of a short list.
    /// </summary>
    public static bool InSelfCheckSample(int index, int count) =>
        index % Math.Max(1, count / SelfCheckBinders) == 0;

    /// <summary>
    /// Scans every binder; the report line counts the matbins with fewer than
    /// <paramref name="reportedUvSets"/> UV sets (the most a twin reads).
    /// </summary>
    public static MaterialLayouts Scan(string gameDir, WinterReport report, Action<int, int>? progress, int reportedUvSets) =>
        Scan(gameDir, report, progress, reportedUvSets, FlverMaterialTable.Read);

    /// <summary>
    /// Scans with <paramref name="tableReader"/> (the fast reader, a seam for tests), self-checking
    /// it against FLVER2.Read on the sample. On a mismatch the binders already read relied on a
    /// reader now known to be wrong somewhere, so the scan starts over with FLVER2.Read only.
    /// </summary>
    internal static MaterialLayouts Scan(
        string gameDir, WinterReport report, Action<int, int>? progress, int reportedUvSets, TableReader tableReader)
    {
        var binders = EnumerateBinders(gameDir).ToList();
        var layouts = new MaterialLayouts();
        TableReader? fastReader = tableReader;
        int flvers = 0;
        int selfChecked = 0;
        for (int i = 0; i < binders.Count; i++)
        {
            string path = binders[i];
            BND4 binder;
            try
            {
                binder = BND4.Read(path);
            }
            catch (Exception e)
            {
                throw new WinterBuildException($"layout scan: {path}: {e.Message}");
            }

            try
            {
                if (fastReader != null && InSelfCheckSample(i, binders.Count))
                {
                    selfChecked++;
                    if (!ReadersAgree(binder, path, fastReader, report))
                    {
                        fastReader = null;
                        layouts.Clear();
                        flvers = 0;
                        i = -1;
                        continue;
                    }
                }
                Collect(binder, layouts, fastReader, report);
                flvers += binder.Files.Count(IsFlver);
            }
            catch (WinterBuildException e)
            {
                throw new WinterBuildException($"layout scan: {path}: {e.Message}");
            }

            int done = i + 1;
            if (done % ProgressInterval == 0 || done == binders.Count)
            {
                progress?.Invoke(done, binders.Count);
            }
        }

        string reader = fastReader != null
            ? $"table reader, self-check passed on {selfChecked} binders"
            : "FLVER2.Read for every binder after a self-check mismatch";
        report.Line($"layout scan: {binders.Count} binders, {flvers} FLVERs, {layouts.Skinned.Count} skinned matbins, "
            + $"{layouts.CountWithFewerUvSetsThan(reportedUvSets)} matbins with fewer than {reportedUvSets} UV sets ({reader})");
        return layouts;
    }

    private static void Collect(BND4 binder, MaterialLayouts layouts, TableReader? fastReader, WinterReport report)
    {
        foreach (var file in binder.Files.Where(IsFlver))
        {
            foreach (var entry in ReadEntries(file, fastReader, report))
            {
                layouts.Add(entry);
            }
        }
    }

    private static bool IsFlver(BinderFile file) =>
        file.Name?.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Fast reader first when given; on its failure the full reader decides. A FLVER the full
    /// reader cannot read is a hard error: a partial scan would let a skinned matbin through.
    /// </summary>
    private static IReadOnlyList<FlverMaterialEntry> ReadEntries(BinderFile file, TableReader? fastReader, WinterReport report)
    {
        if (fastReader != null)
        {
            try
            {
                return fastReader(file.Bytes);
            }
            catch (Exception e)
            {
                report.Line($"warning: layout scan: {file.Name}: table reader failed ({e.Message}), read with FLVER2.Read");
            }
        }
        try
        {
            return FlverMaterialTable.ReadWithSoulsFormats(file.Bytes);
        }
        catch (Exception e)
        {
            throw new WinterBuildException($"{file.Name}: {e.Message}");
        }
    }

    /// <summary>Compares the two readers on every FLVER of the binder; logs the first difference.</summary>
    private static bool ReadersAgree(BND4 binder, string path, TableReader fastReader, WinterReport report)
    {
        foreach (var file in binder.Files.Where(IsFlver))
        {
            var expected = ReadEntries(file, fastReader: null, report);
            string? difference;
            try
            {
                var actual = fastReader(file.Bytes);
                difference = actual.SequenceEqual(expected)
                    ? null
                    : $"table reader [{string.Join(", ", actual)}], FLVER2.Read [{string.Join(", ", expected)}]";
            }
            catch (Exception e)
            {
                difference = e.Message;
            }
            if (difference != null)
            {
                report.Line($"warning: layout scan: table reader disagrees with FLVER2.Read on {path} ({file.Name}: {difference}), every binder read again with FLVER2.Read");
                return false;
            }
        }
        return true;
    }
}
