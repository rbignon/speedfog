using SoulsFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// Collects the matbin stems used by skinned meshes over every model binder of the game. The
/// snow shaders have no skinned permutation, so those matbins must stay vanilla.
/// </summary>
public static class SkinnedMaterialScanner
{
    /// <summary>Binders read with both FLVER readers at the start of a scan to check the fast one.</summary>
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
                throw new WinterBuildException($"skinned scan: no *{suffix} under {dir}");
            }
            binders.AddRange(found);
        }
        return binders;
    }

    public static void Collect(BND4 binder, ISet<string> skinned, bool useFastReader, WinterReport report)
    {
        foreach (var file in binder.Files.Where(IsFlver))
        {
            foreach (var entry in ReadEntries(file, useFastReader, report))
            {
                if (entry.Skinned)
                {
                    skinned.Add(entry.MatbinStem);
                }
            }
        }
    }

    public static HashSet<string> Scan(string gameDir, WinterReport report, Action<int, int>? progress = null)
    {
        var binders = EnumerateBinders(gameDir).ToList();
        var skinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool useFastReader = true;
        int flvers = 0;
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
                throw new WinterBuildException($"skinned scan: {path}: {e.Message}");
            }

            try
            {
                if (useFastReader && i < SelfCheckBinders && !ReadersAgree(binder, path, report))
                {
                    useFastReader = false;
                }
                Collect(binder, skinned, useFastReader, report);
                flvers += binder.Files.Count(IsFlver);
            }
            catch (WinterBuildException e)
            {
                throw new WinterBuildException($"skinned scan: {path}: {e.Message}");
            }

            int done = i + 1;
            if (done % ProgressInterval == 0 || done == binders.Count)
            {
                progress?.Invoke(done, binders.Count);
            }
        }

        string reader = useFastReader
            ? $"table reader, self-check passed on {Math.Min(SelfCheckBinders, binders.Count)} binders"
            : "FLVER2.Read after a self-check mismatch";
        report.Line($"skinned scan: {binders.Count} binders, {flvers} FLVERs, {skinned.Count} skinned matbins ({reader})");
        return skinned;
    }

    private static bool IsFlver(BinderFile file) =>
        file.Name?.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Fast reader first when allowed; on its failure the full reader decides. A FLVER the full
    /// reader cannot read is a hard error: a partial scan would let a skinned matbin through.
    /// </summary>
    private static IReadOnlyList<FlverMaterialEntry> ReadEntries(BinderFile file, bool useFastReader, WinterReport report)
    {
        if (useFastReader)
        {
            try
            {
                return FlverMaterialTable.Read(file.Bytes);
            }
            catch (Exception e)
            {
                report.Line($"warning: skinned scan: {file.Name}: table reader failed ({e.Message}), read with FLVER2.Read");
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
    private static bool ReadersAgree(BND4 binder, string path, WinterReport report)
    {
        foreach (var file in binder.Files.Where(IsFlver))
        {
            var expected = ReadEntries(file, useFastReader: false, report);
            string? difference;
            try
            {
                var actual = FlverMaterialTable.Read(file.Bytes);
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
                report.Line($"warning: skinned scan: table reader disagrees with FLVER2.Read on {path} ({file.Name}: {difference}), FLVER2.Read for the rest of the scan");
                return false;
            }
        }
        return true;
    }
}
