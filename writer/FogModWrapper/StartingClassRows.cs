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
/// </summary>
public static class StartingClassRows
{
    public static SortedSet<int> FromBaseChrSelectMenu(PARAM baseChrSelectMenu)
    {
        var rows = new SortedSet<int>();
        foreach (var row in baseChrSelectMenu.Rows)
        {
            int origin = RowReference(row, "originChrInitParam");
            if (origin > 0)
                rows.Add(origin);
            int chrInit = RowReference(row, "chrInitParam");
            if (chrInit > 0)
            {
                rows.Add(chrInit);
                rows.Add(chrInit + 1);
            }
        }
        return rows;
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
        var existing = new HashSet<int>(chara.Rows.Select(r => r.ID));
        referenced.IntersectWith(existing);
        if (referenced.Count == 0)
            throw new InvalidOperationException("BaseChrSelectMenuParam references no CharaInitParam row");
        return referenced;
    }

    // u32 in the Paramdex def; 0 and 0xFFFFFFFF both mean "no row".
    private static int RowReference(PARAM.Row row, string field)
    {
        long id = Convert.ToInt64(row[field].Value);
        return id > 0 && id <= int.MaxValue ? (int)id : 0;
    }
}
