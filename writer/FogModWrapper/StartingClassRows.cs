using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// CharaInitParam rows that define the selectable starting classes.
///
/// BaseChrSelectMenuParam references two rows per class with identical
/// content: originChrInitParam (3000-3011) and chrInitParam (3100-3122, even
/// IDs); each chrInitParam row also has an unreferenced odd twin (3101-3123)
/// that RandomizerCommon edits alongside it, so it is included too. Elden
/// Ring 1.17 added Idus Knight and Heavy Knight (3010/3120 and 3011/3122),
/// outside the 3000-3009 range the injectors used to hard-code, which is why
/// the rows are read from the menu param instead of assumed.
///
/// <see cref="FromBaseChrSelectMenu"/>/<see cref="Resolve"/> flatten all
/// classes into one set; <see cref="GroupsFromBaseChrSelectMenu"/>/
/// <see cref="ResolveGroups"/> keep classes apart for callers that need to
/// tell them apart.
/// </summary>
public static class StartingClassRows
{
    public static SortedSet<int> FromBaseChrSelectMenu(PARAM baseChrSelectMenu)
    {
        var rows = new SortedSet<int>();
        foreach (var row in baseChrSelectMenu.Rows)
        {
            var (origin, chrInit) = ReadMenuRow(row);
            if (origin > 0)
                rows.Add(origin);
            if (chrInit > 0)
            {
                rows.Add(chrInit);
                rows.Add(chrInit + 1);
            }
        }
        return rows;
    }

    /// <summary>
    /// Same source data as <see cref="FromBaseChrSelectMenu"/>, but grouped
    /// per menu class instead of unioned into one flat set: one entry per
    /// menu row, in ascending menu-row order, each listing that class's
    /// existing CharaInitParam rows as [origin, chrInit, chrInit+1] (missing
    /// rows dropped, empty groups dropped). Used where the caller needs to
    /// tell classes apart (e.g. per-class starting builds) rather than just
    /// enumerating every affected row.
    /// </summary>
    public static List<List<int>> GroupsFromBaseChrSelectMenu(PARAM baseChrSelectMenu, PARAM charaInit)
    {
        var existing = ExistingRowIds(charaInit);
        var groups = new List<List<int>>();
        foreach (var row in baseChrSelectMenu.Rows.OrderBy(r => r.ID))
        {
            var (origin, chrInit) = ReadMenuRow(row);
            var group = new List<int>();
            if (origin > 0 && existing.Contains(origin))
                group.Add(origin);
            if (chrInit > 0)
            {
                if (existing.Contains(chrInit))
                    group.Add(chrInit);
                if (existing.Contains(chrInit + 1))
                    group.Add(chrInit + 1);
            }
            if (group.Count > 0)
                groups.Add(group);
        }
        if (groups.Count == 0)
            throw new InvalidOperationException("BaseChrSelectMenuParam references no CharaInitParam row");
        return groups;
    }

    /// <summary>
    /// Class rows from the regulation's BaseChrSelectMenuParam, restricted to
    /// rows that exist in CharaInitParam. Throws when either param cannot be
    /// loaded: both are vanilla params and the defs ship with the exe, so a
    /// failure here is a packaging or regulation problem, and falling back to
    /// a hard-coded range would silently drop the 1.17 classes again.
    /// </summary>
    public static SortedSet<int> Resolve(RegulationEditor reg)
    {
        var menu = reg.GetParam("BaseChrSelectMenuParam")
            ?? throw new InvalidOperationException("BaseChrSelectMenuParam unavailable: cannot resolve the starting class rows");
        var chara = reg.GetParam("CharaInitParam")
            ?? throw new InvalidOperationException("CharaInitParam unavailable: cannot resolve the starting class rows");
        var referenced = FromBaseChrSelectMenu(menu);
        referenced.IntersectWith(ExistingRowIds(chara));
        if (referenced.Count == 0)
            throw new InvalidOperationException("BaseChrSelectMenuParam references no CharaInitParam row");
        return referenced;
    }

    /// <summary>
    /// Grouped counterpart to <see cref="Resolve"/>: same params, same
    /// missing-param exceptions, but rows are kept apart per menu class
    /// instead of unioned.
    /// </summary>
    public static List<List<int>> ResolveGroups(RegulationEditor reg)
    {
        var menu = reg.GetParam("BaseChrSelectMenuParam")
            ?? throw new InvalidOperationException("BaseChrSelectMenuParam unavailable: cannot resolve the starting class rows");
        var chara = reg.GetParam("CharaInitParam")
            ?? throw new InvalidOperationException("CharaInitParam unavailable: cannot resolve the starting class rows");
        return GroupsFromBaseChrSelectMenu(menu, chara);
    }

    // (originChrInitParam, chrInitParam) for one BaseChrSelectMenuParam row.
    private static (int origin, int chrInit) ReadMenuRow(PARAM.Row row) =>
        (RowReference(row, "originChrInitParam"), RowReference(row, "chrInitParam"));

    private static HashSet<int> ExistingRowIds(PARAM param) =>
        new(param.Rows.Select(r => r.ID));

    // u32 in the Paramdex def; 0 and 0xFFFFFFFF both mean "no row".
    private static int RowReference(PARAM.Row row, string field)
    {
        long id = Convert.ToInt64(row[field].Value);
        return id > 0 && id <= int.MaxValue ? (int)id : 0;
    }
}
