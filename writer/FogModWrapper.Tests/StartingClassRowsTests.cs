using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class StartingClassRowsTests
{
    /// <summary>
    /// BaseChrSelectMenuParam with only the two fields the helper reads. Each
    /// tuple is (row id, chrInitParam, originChrInitParam).
    /// </summary>
    private static PARAM MakeBaseChrSelectMenu(params (int id, int chrInit, int origin)[] rows)
    {
        var def = new PARAMDEF { ParamType = "BASECHR_SELECT_MENU_PARAM_ST" };
        // u32 in the Paramdex def (BaseChrSelectMenuParam.xml), so the cells box uint values.
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.u32, "chrInitParam"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.u32, "originChrInitParam"));
        var param = new PARAM { Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var (id, chrInit, origin) in rows)
        {
            var row = new PARAM.Row(id, "", def);
            row["chrInitParam"].Value = unchecked((uint)chrInit);
            row["originChrInitParam"].Value = unchecked((uint)origin);
            param.Rows.Add(row);
        }
        return param;
    }

    [Fact]
    public void FromBaseChrSelectMenu_UnionsOriginAndChrInitRowsWithOddNeighbours()
    {
        // Vagabond (1.16 layout) and Idus Knight (added by 1.17). Each chrInitParam
        // row has an unreferenced odd twin (3101, 3121) that RandomizerCommon edits
        // alongside it, so both are returned.
        var menu = MakeBaseChrSelectMenu((2000, 3100, 3000), (2010, 3120, 3010));

        var rows = StartingClassRows.FromBaseChrSelectMenu(menu);

        Assert.Equal(new[] { 3000, 3010, 3100, 3101, 3120, 3121 }, rows);
    }

    [Fact]
    public void Resolve_ThrowsWhenMenuParamIsMissing()
    {
        // A regulation without BaseChrSelectMenuParam: the old 3000-3009 assumption
        // would silently leave the 1.17 classes out, so this must stop generation.
        var reg = new RegulationEditor(new BND4(), defsDir: Path.GetTempPath());

        var ex = Assert.Throws<InvalidOperationException>(() => StartingClassRows.Resolve(reg));

        Assert.Contains("BaseChrSelectMenuParam", ex.Message);
    }

    [Fact]
    public void FromBaseChrSelectMenu_SkipsUnsetReferences()
    {
        var menu = MakeBaseChrSelectMenu((2000, -1, 3000), (2001, 3102, 0));  // -1 boxes as 0xFFFFFFFF

        var rows = StartingClassRows.FromBaseChrSelectMenu(menu);

        Assert.Equal(new[] { 3000, 3102, 3103 }, rows);
    }

    private static PARAM MakeCharaInit(params int[] rowIds)
    {
        var def = new PARAMDEF { ParamType = "CHARA_INIT_PARAM_ST" };
        var param = new PARAM { Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var id in rowIds)
            param.Rows.Add(new PARAM.Row(id, "", def));
        return param;
    }

    [Fact]
    public void GroupsFromBaseChrSelectMenu_GroupsByMenuRowAndDropsMissingRows()
    {
        // Menu row 2000 (the lower id) references the higher origin/chrInit
        // pair (3001/3102), and menu row 2010 references the lower pair
        // (3000/3100/3101), so menu-row order and origin/chrInit order
        // actively disagree: sorting by origin or chrInit instead of
        // menu-row id would flip the two groups. CharaInitParam is also
        // missing 3103 (odd twin of 3102), so that group loses its third
        // entry.
        var chara = MakeCharaInit(3000, 3100, 3101, 3001, 3102);
        var menu = MakeBaseChrSelectMenu((2000, 3102, 3001), (2010, 3100, 3000));

        var groups = StartingClassRows.GroupsFromBaseChrSelectMenu(menu, chara);

        Assert.Equal(new List<List<int>>
        {
            new() { 3001, 3102 },
            new() { 3000, 3100, 3101 },
        }, groups);
    }

    [Fact]
    public void GroupsFromBaseChrSelectMenu_ThrowsWhenNoGroupSurvives()
    {
        var chara = MakeCharaInit(24700); // unrelated NPC row only
        var menu = MakeBaseChrSelectMenu((2000, 3100, 3000));

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartingClassRows.GroupsFromBaseChrSelectMenu(menu, chara));

        Assert.Contains("BaseChrSelectMenuParam", ex.Message);
    }

    [Fact]
    public void ResolveGroups_ThrowsWhenMenuParamIsMissing()
    {
        var reg = new RegulationEditor(new BND4(), defsDir: Path.GetTempPath());

        var ex = Assert.Throws<InvalidOperationException>(() => StartingClassRows.ResolveGroups(reg));

        Assert.Contains("BaseChrSelectMenuParam", ex.Message);
    }
}
