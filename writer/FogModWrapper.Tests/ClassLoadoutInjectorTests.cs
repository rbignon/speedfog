using FogModWrapper.Models;
using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests;

public class ClassLoadoutInjectorTests
{
    // Sentinel values pre-set on every row so "untouched" assertions are real
    // (rather than coincidentally matching a zero default).
    private const int SentinelRight = 111;
    private const int SentinelLeft = 222;
    private const int SentinelHelm = 333;
    private const int SentinelArmer = 444;
    private const int SentinelGaunt = 555;
    private const int SentinelLeg = 666;

    // wepParamType_Right1/Left1 (u8, CHARA_INIT_WEP_TYPE enum: 0 =
    // EquipParamWeapon, 1 = EquipParamCustomWeapon) are set to 1 by
    // CharacterWriter (merged item-randomizer output) when it drew an
    // ash-of-war weapon into the slot. Pre-set to 1 here so the
    // "zeroed on every row of the written slot's group" assertions are real.
    private const byte SentinelWepTypeSet = 1;

    /// <summary>
    /// CharaInitParam-shaped PARAM with the six equip fields plus the two
    /// wepParamType companion fields the injector writes, all int fields as
    /// sentinel-carrying stand-ins for the vanilla def's real types (mirrors
    /// StartingClassRowsTests/StartingRuneInjectorTests), except the type
    /// fields which are u8 to mirror the real CharaInitParam def.
    /// </summary>
    private static PARAM MakeCharaInit(params int[] rowIds)
    {
        var def = new PARAMDEF { ParamType = "CHARA_INIT_PARAM_ST" };
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "equip_Wep_Right"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "equip_Wep_Left"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "equip_Helm"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "equip_Armer"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "equip_Gaunt"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.s32, "equip_Leg"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.u8, "wepParamType_Right1"));
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.u8, "wepParamType_Left1"));
        var param = new PARAM { Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var id in rowIds)
        {
            var row = new PARAM.Row(id, "", def);
            row["equip_Wep_Right"].Value = SentinelRight;
            row["equip_Wep_Left"].Value = SentinelLeft;
            row["equip_Helm"].Value = SentinelHelm;
            row["equip_Armer"].Value = SentinelArmer;
            row["equip_Gaunt"].Value = SentinelGaunt;
            row["equip_Leg"].Value = SentinelLeg;
            row["wepParamType_Right1"].Value = SentinelWepTypeSet;
            row["wepParamType_Left1"].Value = SentinelWepTypeSet;
            param.Rows.Add(row);
        }
        return param;
    }

    private static PARAM.Row Row(PARAM param, int id) => param.Rows.Find(r => r.ID == id)!;

    [Fact]
    public void Apply_RightSlotItem_WritesRightAndLeavesLeftAtSentinel()
    {
        var chara = MakeCharaInit(3000);
        var groups = new List<List<int>> { new() { 3000 } };
        var loadout = new ClassLoadoutData
        {
            HandItems = new List<HandItemData> { new() { Id = 9001, Slot = "right", Name = "Longsword" } },
        };

        ClassLoadoutInjector.Apply(chara, groups, loadout);

        Assert.Equal(9001, (int)Row(chara, 3000)["equip_Wep_Right"].Value);
        Assert.Equal(SentinelLeft, (int)Row(chara, 3000)["equip_Wep_Left"].Value);
        Assert.Equal((byte)0, (byte)Row(chara, 3000)["wepParamType_Right1"].Value);
        Assert.Equal(SentinelWepTypeSet, (byte)Row(chara, 3000)["wepParamType_Left1"].Value);
    }

    [Fact]
    public void Apply_LeftSlotItem_WritesLeftAndLeavesRightAtSentinel()
    {
        var chara = MakeCharaInit(3000);
        var groups = new List<List<int>> { new() { 3000 } };
        var loadout = new ClassLoadoutData
        {
            HandItems = new List<HandItemData> { new() { Id = 9002, Slot = "left", Name = "Buckler" } },
        };

        ClassLoadoutInjector.Apply(chara, groups, loadout);

        Assert.Equal(9002, (int)Row(chara, 3000)["equip_Wep_Left"].Value);
        Assert.Equal(SentinelRight, (int)Row(chara, 3000)["equip_Wep_Right"].Value);
        Assert.Equal((byte)0, (byte)Row(chara, 3000)["wepParamType_Left1"].Value);
        Assert.Equal(SentinelWepTypeSet, (byte)Row(chara, 3000)["wepParamType_Right1"].Value);
    }

    [Fact]
    public void Apply_EveryRowOfGroupGetsSameValues()
    {
        // origin (3000) and chrInit twins (3100, 3101) for one class.
        var chara = MakeCharaInit(3000, 3100, 3101);
        var groups = new List<List<int>> { new() { 3000, 3100, 3101 } };
        var loadout = new ClassLoadoutData
        {
            HandItems = new List<HandItemData> { new() { Id = 9001, Slot = "right", Name = "Longsword" } },
            ArmorSets = new List<List<int>> { new() { 1, 2, 3, 4 } },
        };

        ClassLoadoutInjector.Apply(chara, groups, loadout);

        foreach (var id in new[] { 3000, 3100, 3101 })
        {
            Assert.Equal(9001, (int)Row(chara, id)["equip_Wep_Right"].Value);
            Assert.Equal(1, (int)Row(chara, id)["equip_Helm"].Value);
            Assert.Equal(2, (int)Row(chara, id)["equip_Armer"].Value);
            Assert.Equal(3, (int)Row(chara, id)["equip_Gaunt"].Value);
            Assert.Equal(4, (int)Row(chara, id)["equip_Leg"].Value);
            Assert.Equal((byte)0, (byte)Row(chara, id)["wepParamType_Right1"].Value);
            Assert.Equal(SentinelWepTypeSet, (byte)Row(chara, id)["wepParamType_Left1"].Value);
        }
    }

    [Fact]
    public void Apply_RightSlotItem_ZeroesWepParamTypeOnEveryRowOfWrittenSlotGroup()
    {
        // Dedicated test for the finding: a stale wepParamType left over from
        // CharacterWriter (set to 1 when it drew an ash-of-war weapon into
        // the slot) must be zeroed on EVERY row of the group for the written
        // slot, not just the first row, and the other slot's type field must
        // stay untouched.
        var chara = MakeCharaInit(3000, 3100, 3101);
        var groups = new List<List<int>> { new() { 3000, 3100, 3101 } };
        var loadout = new ClassLoadoutData
        {
            HandItems = new List<HandItemData> { new() { Id = 9001, Slot = "right", Name = "Longsword" } },
        };

        ClassLoadoutInjector.Apply(chara, groups, loadout);

        foreach (var id in new[] { 3000, 3100, 3101 })
        {
            Assert.Equal((byte)0, (byte)Row(chara, id)["wepParamType_Right1"].Value);
            Assert.Equal(SentinelWepTypeSet, (byte)Row(chara, id)["wepParamType_Left1"].Value);
        }
    }

    [Fact]
    public void Apply_WrapsHandItemsAndArmorSetsIndependentlyByGroupIndexModulo()
    {
        // 3 class groups, 2 hand items, 3 armor sets: group index 2 (third
        // group) wraps hand items back to index 0 (2 % 2 == 0) while armor
        // sets have enough entries to use index 2 directly (2 % 3 == 2).
        var chara = MakeCharaInit(3000, 3001, 3002);
        var groups = new List<List<int>>
        {
            new() { 3000 },
            new() { 3001 },
            new() { 3002 },
        };
        var loadout = new ClassLoadoutData
        {
            HandItems = new List<HandItemData>
            {
                new() { Id = 100, Slot = "right", Name = "Sword A" },
                new() { Id = 200, Slot = "right", Name = "Sword B" },
            },
            ArmorSets = new List<List<int>>
            {
                new() { 11, 12, 13, 14 },
                new() { 21, 22, 23, 24 },
                new() { 31, 32, 33, 34 },
            },
        };

        ClassLoadoutInjector.Apply(chara, groups, loadout);

        // Group 3 (index 2) wraps hand items to index 0 (Sword A) but uses
        // its own armor set at index 2 (no wrap needed, 3 sets for 3 groups).
        Assert.Equal(100, (int)Row(chara, 3002)["equip_Wep_Right"].Value);
        Assert.Equal(31, (int)Row(chara, 3002)["equip_Helm"].Value);
    }

    [Fact]
    public void Apply_ArmorQuadrupleMapsInOrderToHelmArmerGauntLeg()
    {
        var chara = MakeCharaInit(3000);
        var groups = new List<List<int>> { new() { 3000 } };
        var loadout = new ClassLoadoutData
        {
            ArmorSets = new List<List<int>> { new() { 501, 502, 503, 504 } },
        };

        ClassLoadoutInjector.Apply(chara, groups, loadout);

        var row = Row(chara, 3000);
        Assert.Equal(501, (int)row["equip_Helm"].Value);
        Assert.Equal(502, (int)row["equip_Armer"].Value);
        Assert.Equal(503, (int)row["equip_Gaunt"].Value);
        Assert.Equal(504, (int)row["equip_Leg"].Value);
    }

    [Fact]
    public void ApplyTo_NullLoadout_LeavesEveryFieldUntouched()
    {
        // Real CharaInitParam + BaseChrSelectMenuParam defs wired into an
        // actual BND4 (ParamTestHelper/UntouchableBossInjectorTests
        // pattern), so the assertion is genuinely contingent on the
        // loadout==null guard: if that guard were removed, ApplyTo would
        // resolve the class group below (row 3000, via its origin
        // reference) and mutate these fields, instead of returning before
        // ever calling GetParam. A fixture with no params registered at all
        // (as in ApplyTo_NullLoadout's earlier version) cannot distinguish
        // "the null check fired" from "GetParam warn-returned null anyway".
        var chara = BuildParamFromDef("CharaInitParam", templateId: 3000);
        var row = chara.Rows.Single(r => r.ID == 3000);
        row["equip_Wep_Right"].Value = SentinelRight;
        row["equip_Wep_Left"].Value = SentinelLeft;
        row["equip_Helm"].Value = SentinelHelm;
        row["equip_Armer"].Value = SentinelArmer;
        row["equip_Gaunt"].Value = SentinelGaunt;
        row["equip_Leg"].Value = SentinelLeg;

        var menu = BuildParamFromDef("BaseChrSelectMenuParam", templateId: 2000);
        menu.Rows.Single(r => r.ID == 2000)["originChrInitParam"].Value = (uint)3000;

        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/CharaInitParam.param", chara.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/BaseChrSelectMenuParam.param", menu.Write()));
        var reg = new RegulationEditor(bnd, DefsDir());

        ClassLoadoutInjector.ApplyTo(reg, null);

        var updatedRow = reg.GetParam("CharaInitParam")!.Rows.Single(r => r.ID == 3000);
        Assert.Equal(SentinelRight, (int)updatedRow["equip_Wep_Right"].Value);
        Assert.Equal(SentinelLeft, (int)updatedRow["equip_Wep_Left"].Value);
        Assert.Equal(SentinelHelm, (int)updatedRow["equip_Helm"].Value);
        Assert.Equal(SentinelArmer, (int)updatedRow["equip_Armer"].Value);
        Assert.Equal(SentinelGaunt, (int)updatedRow["equip_Gaunt"].Value);
        Assert.Equal(SentinelLeg, (int)updatedRow["equip_Leg"].Value);
    }

    [Fact]
    public void ApplyTo_NonNullLoadoutButCharaInitParamMissing_Throws()
    {
        // A non-empty loadout with no CharaInitParam in the regulation is a
        // packaging problem (both are vanilla params shipped with the exe),
        // not a soft "nothing to do" case: it must throw rather than
        // silently no-op, matching StartingClassRows.Resolve's style.
        var loadout = new ClassLoadoutData
        {
            HandItems = new List<HandItemData> { new() { Id = 9001, Slot = "right", Name = "Longsword" } },
        };
        var bnd = new BND4();
        var reg = new RegulationEditor(bnd, DefsDir());

        var ex = Assert.Throws<InvalidOperationException>(() => ClassLoadoutInjector.ApplyTo(reg, loadout));
        Assert.Contains("CharaInitParam", ex.Message);
    }

    [Fact]
    public void ApplyTo_EmptyLoadout_StaysASilentNoOpEvenWithoutCharaInitParam()
    {
        // Null/empty loadout keeps its silent no-op: nothing to apply means
        // the missing param is irrelevant, unlike the non-empty case above.
        var loadout = new ClassLoadoutData();
        var bnd = new BND4();
        var reg = new RegulationEditor(bnd, DefsDir());

        var exception = Record.Exception(() => ClassLoadoutInjector.ApplyTo(reg, loadout));

        Assert.Null(exception);
    }
}
