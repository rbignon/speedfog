using System.Numerics;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for ChapelGraceInjector: the in-memory MSB transformation (grace asset,
/// NPC, player warp target, spawn region), the BonfireWarpParam row allocation,
/// the "Game start" event spawn redirect, and the position math.
///
/// The EMEVD warp event construction itself (RegisterBonfire + one-shot WarpPlayer)
/// is not covered here: it requires a SoulsIds.Events instance backed by the
/// gitignored er-common.emedf.json.
/// </summary>
public class ChapelGraceInjectorTests
{
    // FogRando chapel conventions (see ChapelGraceInjector constants)
    // Deliberate duplicates of the injector's constants: they pin the
    // external FogRando entity convention. If the production values change,
    // these tests MUST fail; do not deduplicate them against the internals.
    private const uint BONFIRE_ENTITY_BASE = 10011952;
    private const uint GRACE_SPAWN_REGION = 10012021;
    private const long GAME_START_EVENT_ID = 10010020;
    private const uint VANILLA_SPAWN_REGION = 10012020;

    // --- MSB fixture ---

    /// <summary>
    /// Build a minimal chapel-shaped MSB with the preferred source parts that
    /// the injector clones from (fog.txt CustomBonfires entries).
    /// </summary>
    private static MSBE MakeChapelMsb()
    {
        var msb = new MSBE();

        var asset = new MSBE.Part.Asset
        {
            Name = "AEG217_237_0501",
            ModelName = "AEG217_237",
            EntityID = 0,
            Position = new Vector3(1f, 2f, 3f),
        };
        msb.Parts.Assets.Add(asset);

        var enemy = new MSBE.Part.Enemy
        {
            Name = "c4690_9000",
            ModelName = "c4690",
            EntityID = 0,
            NPCParamID = 46900010,
            ThinkParamID = 46900000,
            TalkID = 0,
        };
        msb.Parts.Enemies.Add(enemy);

        var player = new MSBE.Part.Player
        {
            Name = "c0000_0000",
            ModelName = "c0000",
            EntityID = 10011950,
        };
        msb.Parts.Players.Add(player);

        return msb;
    }

    // --- ApplyToMsb: fresh MSB ---

    [Fact]
    public void ApplyToMsb_FreshMsb_AllocatesFogRandoEntityConvention()
    {
        var msb = MakeChapelMsb();

        var result = ChapelGraceInjector.ApplyToMsb(msb);

        Assert.NotNull(result);
        Assert.Equal(BONFIRE_ENTITY_BASE, result.Value.BonfireEntityId);
        // chrEntity = bonfire - 1000, playerEntity = bonfire - 970
        Assert.Equal(result.Value.BonfireEntityId - 970, result.Value.PlayerEntityId);
        Assert.Equal(GRACE_SPAWN_REGION, result.Value.SpawnRegionEntityId);
    }

    [Fact]
    public void ApplyToMsb_FreshMsb_AddsGraceAssetNpcAndPlayer()
    {
        var msb = MakeChapelMsb();

        var result = ChapelGraceInjector.ApplyToMsb(msb);
        Assert.NotNull(result);

        // Grace asset: the visual flame, at the bonfire entity ID
        var grace = Assert.Single(msb.Parts.Assets, a => a.ModelName == "AEG099_060");
        Assert.Equal(result.Value.BonfireEntityId, grace.EntityID);

        // Grace NPC: invisible bonfire controller c1000 with grace talk script
        var npc = Assert.Single(msb.Parts.Enemies, e => e.ModelName == "c1000");
        Assert.Equal(result.Value.BonfireEntityId - 1000, npc.EntityID);
        Assert.Equal(1000, npc.TalkID);
        Assert.Equal(1, npc.ThinkParamID);
        Assert.Equal(10000000, npc.NPCParamID);
        Assert.Equal(-1, npc.CharaInitID);
        Assert.Equal("h002000", npc.CollisionPartName);
        Assert.All(npc.EntityGroupIDs, id => Assert.Equal(0u, id));
        // NPC sits exactly at the grace asset
        Assert.Equal(grace.Position, npc.Position);

        // Player warp target: 2m in front of the grace, same height
        var player = Assert.Single(msb.Parts.Players, p => p.EntityID == result.Value.PlayerEntityId);
        Assert.Equal(grace.Position.Y, player.Position.Y);
        var dx = player.Position.X - grace.Position.X;
        var dz = player.Position.Z - grace.Position.Z;
        Assert.Equal(2f, MathF.Sqrt(dx * dx + dz * dz), 3);
    }

    [Fact]
    public void ApplyToMsb_FreshMsb_CreatesSpawnRegionAtPlayerPosition()
    {
        var msb = MakeChapelMsb();

        var result = ChapelGraceInjector.ApplyToMsb(msb);
        Assert.NotNull(result);

        var region = Assert.Single(msb.Regions.SpawnPoints);
        Assert.Equal(GRACE_SPAWN_REGION, region.EntityID);

        // The spawn region and the player warp target use the same "2m forward" spot
        var player = Assert.Single(msb.Parts.Players, p => p.EntityID == result.Value.PlayerEntityId);
        Assert.Equal(player.Position, region.Position);
        Assert.Equal(player.Rotation, region.Rotation);
    }

    [Fact]
    public void ApplyToMsb_FreshMsb_EnsuresModelDefinitions()
    {
        var msb = MakeChapelMsb();

        ChapelGraceInjector.ApplyToMsb(msb);

        Assert.Contains(msb.Models.Assets, m => m.Name == "AEG099_060");
        Assert.Contains(msb.Models.Enemies, m => m.Name == "c1000");
    }

    [Fact]
    public void ApplyToMsb_ClearsInheritedPartReferencesOnGraceAsset()
    {
        var msb = MakeChapelMsb();
        var source = msb.Parts.Assets[0];
        source.UnkPartNames[0] = "h002000";
        source.UnkT54PartName = "h002000";

        ChapelGraceInjector.ApplyToMsb(msb);

        var grace = Assert.Single(msb.Parts.Assets, a => a.ModelName == "AEG099_060");
        Assert.All(grace.UnkPartNames, n => Assert.Null(n));
        Assert.Null(grace.UnkT54PartName);
    }

    [Fact]
    public void ApplyToMsb_PrefersNamedSourceParts()
    {
        var msb = MakeChapelMsb();
        // Put decoy parts FIRST so FirstOrDefault would pick them; the injector
        // must still clone from the preferred fog.txt source parts. Scale is
        // copied by DeepCopy and never overwritten, so it identifies the source.
        msb.Parts.Assets.Insert(0, new MSBE.Part.Asset { Name = "AEG007_010_1000", ModelName = "AEG007_010" });
        msb.Parts.Enemies.Insert(0, new MSBE.Part.Enemy { Name = "c3100_9001", ModelName = "c3100" });
        msb.Parts.Players.Insert(0, new MSBE.Part.Player { Name = "c0000_0009", ModelName = "c0000" });
        var marker = new Vector3(7f, 7f, 7f);
        msb.Parts.Assets.Find(a => a.Name == "AEG217_237_0501")!.Scale = marker;
        msb.Parts.Enemies.Find(e => e.Name == "c4690_9000")!.Scale = marker;
        msb.Parts.Players.Find(p => p.Name == "c0000_0000")!.Scale = marker;

        var result = ChapelGraceInjector.ApplyToMsb(msb);
        Assert.NotNull(result);

        var grace = Assert.Single(msb.Parts.Assets, a => a.ModelName == "AEG099_060");
        var npc = Assert.Single(msb.Parts.Enemies, e => e.ModelName == "c1000");
        var player = Assert.Single(msb.Parts.Players, p => p.EntityID == result.Value.PlayerEntityId);
        Assert.Equal(marker, grace.Scale);
        Assert.Equal(marker, npc.Scale);
        Assert.Equal(marker, player.Scale);
    }

    [Fact]
    public void ApplyToMsb_EntityIdConflict_AllocatesNextFreeId()
    {
        var msb = MakeChapelMsb();
        // Occupy the base bonfire entity ID with an unrelated part
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c9999_0000",
            ModelName = "c9999",
            EntityID = BONFIRE_ENTITY_BASE,
        });

        var result = ChapelGraceInjector.ApplyToMsb(msb);

        Assert.NotNull(result);
        Assert.Equal(BONFIRE_ENTITY_BASE + 1, result.Value.BonfireEntityId);
        Assert.Equal(BONFIRE_ENTITY_BASE + 1 - 970, result.Value.PlayerEntityId);
    }

    // --- ApplyToMsb: existing grace ---

    [Fact]
    public void ApplyToMsb_ExistingGrace_SkipsCreationButAddsSpawnRegion()
    {
        var msb = MakeChapelMsb();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_060_9900",
            ModelName = "AEG099_060",
            EntityID = BONFIRE_ENTITY_BASE + 8,
        });
        int assetCount = msb.Parts.Assets.Count;
        int enemyCount = msb.Parts.Enemies.Count;
        int playerCount = msb.Parts.Players.Count;

        var result = ChapelGraceInjector.ApplyToMsb(msb);

        Assert.NotNull(result);
        Assert.Equal(BONFIRE_ENTITY_BASE + 8, result.Value.BonfireEntityId);
        Assert.Equal(BONFIRE_ENTITY_BASE + 8 - 970, result.Value.PlayerEntityId);
        // No new parts created
        Assert.Equal(assetCount, msb.Parts.Assets.Count);
        Assert.Equal(enemyCount, msb.Parts.Enemies.Count);
        Assert.Equal(playerCount, msb.Parts.Players.Count);
        // The spawn region is still created
        var region = Assert.Single(msb.Regions.SpawnPoints);
        Assert.Equal(GRACE_SPAWN_REGION, region.EntityID);
        Assert.Equal(GRACE_SPAWN_REGION, result.Value.SpawnRegionEntityId);
    }

    [Fact]
    public void ApplyToMsb_GraceModelOutsideEntityRange_IsNotTreatedAsExisting()
    {
        var msb = MakeChapelMsb();
        // Same model but a vanilla entity ID far outside the chapel bonfire range
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_060_1000",
            ModelName = "AEG099_060",
            EntityID = 10001950,
        });

        var result = ChapelGraceInjector.ApplyToMsb(msb);

        Assert.NotNull(result);
        Assert.Equal(BONFIRE_ENTITY_BASE, result.Value.BonfireEntityId);
        // A new grace asset was created in the chapel range
        Assert.Contains(msb.Parts.Assets, a =>
            a.ModelName == "AEG099_060" && a.EntityID == BONFIRE_ENTITY_BASE);
    }

    // --- ApplyToMsb: missing source parts ---

    [Fact]
    public void ApplyToMsb_NoAssetParts_ReturnsNull()
    {
        var msb = MakeChapelMsb();
        msb.Parts.Assets.Clear();

        Assert.Null(ChapelGraceInjector.ApplyToMsb(msb));
    }

    [Fact]
    public void ApplyToMsb_NoEnemyParts_ReturnsNull()
    {
        var msb = MakeChapelMsb();
        msb.Parts.Enemies.Clear();

        Assert.Null(ChapelGraceInjector.ApplyToMsb(msb));
    }

    [Fact]
    public void ApplyToMsb_NoPlayerParts_ReturnsNull()
    {
        var msb = MakeChapelMsb();
        msb.Parts.Players.Clear();

        Assert.Null(ChapelGraceInjector.ApplyToMsb(msb));
    }

    // --- CreateGraceSpawnRegion ---

    [Fact]
    public void CreateGraceSpawnRegion_CalledTwice_IsIdempotent()
    {
        var msb = new MSBE();

        var first = ChapelGraceInjector.CreateGraceSpawnRegion(msb);
        var second = ChapelGraceInjector.CreateGraceSpawnRegion(msb);

        Assert.Equal(first, second);
        Assert.Single(msb.Regions.SpawnPoints);
    }

    // --- AddBonfireWarpRow ---

    private const uint TEMPLATE_BONFIRE_ENTITY = 10001950;
    private const uint TEMPLATE_FLAG = 71001;
    private const int TEMPLATE_ROW_ID = 100101;
    private const int BONFIRE_ROW_BASE = 100102;
    private const uint NEW_BONFIRE_ENTITY = 10011952;

    /// <summary>
    /// Build an in-memory BonfireWarpParam with only the fields the injector touches,
    /// containing the template chapel bonfire row (entity 10001950).
    /// </summary>
    private static PARAM MakeBonfireParam()
    {
        var def = new PARAMDEF { ParamType = "BONFIRE_WARP_PARAM_ST" };
        void AddField(PARAMDEF.DefType type, string name) =>
            def.Fields.Add(new PARAMDEF.Field(def, type, name));

        AddField(PARAMDEF.DefType.u32, "eventflagId");
        AddField(PARAMDEF.DefType.u32, "bonfireEntityId");
        AddField(PARAMDEF.DefType.u8, "areaNo");
        AddField(PARAMDEF.DefType.u8, "gridXNo");
        AddField(PARAMDEF.DefType.u8, "gridZNo");
        AddField(PARAMDEF.DefType.f32, "posX");
        AddField(PARAMDEF.DefType.f32, "posY");
        AddField(PARAMDEF.DefType.f32, "posZ");
        AddField(PARAMDEF.DefType.s32, "textId1");
        AddField(PARAMDEF.DefType.u16, "bonfireSubCategorySortId");
        AddField(PARAMDEF.DefType.s32, "forbiddenIconId");
        AddField(PARAMDEF.DefType.u8, "bonfireSubCategoryId");
        AddField(PARAMDEF.DefType.s32, "iconId");
        AddField(PARAMDEF.DefType.u8, "dispMask00");
        AddField(PARAMDEF.DefType.u8, "dispMask01");
        AddField(PARAMDEF.DefType.u8, "dispMask02");
        AddField(PARAMDEF.DefType.s32, "noIgnitionSfxDmypolyId_0");
        AddField(PARAMDEF.DefType.s32, "noIgnitionSfxId_0");

        var param = new PARAM { Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);

        var template = new PARAM.Row(TEMPLATE_ROW_ID, "chapel template", def);
        template["eventflagId"].Value = TEMPLATE_FLAG;
        template["bonfireEntityId"].Value = TEMPLATE_BONFIRE_ENTITY;
        template["iconId"].Value = 34;
        template["forbiddenIconId"].Value = 35;
        template["bonfireSubCategoryId"].Value = (byte)3;
        template["dispMask00"].Value = (byte)1;
        template["noIgnitionSfxId_0"].Value = 808000;
        param.Rows.Add(template);

        return param;
    }

    [Fact]
    public void AddBonfireWarpRow_CreatesRowWithAllocatedFlagAndCoordinates()
    {
        var param = MakeBonfireParam();

        var flag = ChapelGraceInjector.AddBonfireWarpRow(param, NEW_BONFIRE_ENTITY);

        // The template's flag is taken, so the next free one is allocated
        Assert.NotNull(flag);
        Assert.Equal(TEMPLATE_FLAG + 1, flag.Value);

        var row = param.Rows.Find(r => (uint)r["bonfireEntityId"].Value == NEW_BONFIRE_ENTITY);
        Assert.NotNull(row);
        Assert.Equal(BONFIRE_ROW_BASE, row.ID);
        Assert.Equal(flag.Value, (uint)row["eventflagId"].Value);
        // Map coordinates parsed from m10_01_00_00
        Assert.Equal((byte)10, (byte)row["areaNo"].Value);
        Assert.Equal((byte)1, (byte)row["gridXNo"].Value);
        Assert.Equal((byte)0, (byte)row["gridZNo"].Value);
        Assert.Equal(10010, (int)row["textId1"].Value);
        Assert.Equal((ushort)9999, (ushort)row["bonfireSubCategorySortId"].Value);
    }

    [Fact]
    public void AddBonfireWarpRow_CopiesCosmeticFieldsFromTemplate()
    {
        var param = MakeBonfireParam();

        ChapelGraceInjector.AddBonfireWarpRow(param, NEW_BONFIRE_ENTITY);

        var row = param.Rows.Find(r => (uint)r["bonfireEntityId"].Value == NEW_BONFIRE_ENTITY);
        Assert.NotNull(row);
        Assert.Equal(34, (int)row["iconId"].Value);
        Assert.Equal(35, (int)row["forbiddenIconId"].Value);
        Assert.Equal((byte)3, (byte)row["bonfireSubCategoryId"].Value);
        Assert.Equal((byte)1, (byte)row["dispMask00"].Value);
        Assert.Equal(808000, (int)row["noIgnitionSfxId_0"].Value);
    }

    [Fact]
    public void AddBonfireWarpRow_ExistingRowForEntity_ReturnsItsFlagWithoutAdding()
    {
        var param = MakeBonfireParam();
        var existing = new PARAM.Row(200000, "already there", param.AppliedParamdef);
        existing["eventflagId"].Value = 71050u;
        existing["bonfireEntityId"].Value = NEW_BONFIRE_ENTITY;
        param.Rows.Add(existing);
        int rowCount = param.Rows.Count;

        var flag = ChapelGraceInjector.AddBonfireWarpRow(param, NEW_BONFIRE_ENTITY);

        Assert.NotNull(flag);
        Assert.Equal(71050u, flag.Value);
        Assert.Equal(rowCount, param.Rows.Count);
    }

    [Fact]
    public void AddBonfireWarpRow_MissingTemplateRow_ReturnsNull()
    {
        var param = MakeBonfireParam();
        param.Rows.RemoveAll(r => (uint)r["bonfireEntityId"].Value == TEMPLATE_BONFIRE_ENTITY);
        int rowCount = param.Rows.Count;

        var flag = ChapelGraceInjector.AddBonfireWarpRow(param, NEW_BONFIRE_ENTITY);

        Assert.Null(flag);
        Assert.Equal(rowCount, param.Rows.Count);
    }

    [Fact]
    public void AddBonfireWarpRow_RowIdAndFlagConflicts_IncrementPastTakenValues()
    {
        var param = MakeBonfireParam();
        // Occupy the base row ID and the first candidate flag
        var blocker = new PARAM.Row(BONFIRE_ROW_BASE, "blocker", param.AppliedParamdef);
        blocker["eventflagId"].Value = TEMPLATE_FLAG + 1;
        blocker["bonfireEntityId"].Value = 10009999u;
        param.Rows.Add(blocker);

        var flag = ChapelGraceInjector.AddBonfireWarpRow(param, NEW_BONFIRE_ENTITY);

        Assert.NotNull(flag);
        Assert.Equal(TEMPLATE_FLAG + 2, flag.Value);
        var row = param.Rows.Find(r => (uint)r["bonfireEntityId"].Value == NEW_BONFIRE_ENTITY);
        Assert.NotNull(row);
        Assert.Equal(BONFIRE_ROW_BASE + 1, row.ID);
    }

    [Fact]
    public void AddBonfireWarpRow_KeepsRowsSortedById()
    {
        var param = MakeBonfireParam();
        var late = new PARAM.Row(999999, "late", param.AppliedParamdef);
        late["eventflagId"].Value = 90000u;
        late["bonfireEntityId"].Value = 10008888u;
        param.Rows.Add(late);

        ChapelGraceInjector.AddBonfireWarpRow(param, NEW_BONFIRE_ENTITY);

        var ids = param.Rows.Select(r => r.ID).ToList();
        Assert.Equal(ids.OrderBy(id => id).ToList(), ids);
    }

    // --- PatchGameStartEvent ---

    /// <summary>
    /// SetPlayerRespawnPoint (bank 2003, id 23): single uint32 arg (region entity ID).
    /// </summary>
    private static EMEVD.Instruction MakeSetPlayerRespawnPoint(uint region)
    {
        var args = new byte[4];
        BitConverter.GetBytes(region).CopyTo(args, 0);
        return new EMEVD.Instruction(2003, 23, args);
    }

    private static EMEVD MakeGameStartEmevd(params EMEVD.Instruction[] instructions)
    {
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(GAME_START_EVENT_ID);
        evt.Instructions.AddRange(instructions);
        emevd.Events.Add(evt);
        return emevd;
    }

    [Fact]
    public void PatchGameStartEvent_VanillaRegion_ReplacesWithGraceRegion()
    {
        var respawn = MakeSetPlayerRespawnPoint(VANILLA_SPAWN_REGION);
        var emevd = MakeGameStartEmevd(respawn);

        ChapelGraceInjector.PatchGameStartEvent(emevd, GRACE_SPAWN_REGION);

        Assert.Equal(GRACE_SPAWN_REGION, BitConverter.ToUInt32(respawn.ArgData, 0));
    }

    [Fact]
    public void PatchGameStartEvent_OtherRegion_Untouched()
    {
        // A SetPlayerRespawnPoint targeting a different region (not the vanilla
        // Grafted Scion spawn) must not be redirected.
        var respawn = MakeSetPlayerRespawnPoint(10012099);
        var emevd = MakeGameStartEmevd(respawn);

        ChapelGraceInjector.PatchGameStartEvent(emevd, GRACE_SPAWN_REGION);

        Assert.Equal(10012099u, BitConverter.ToUInt32(respawn.ArgData, 0));
    }

    [Fact]
    public void PatchGameStartEvent_OtherInstructions_Untouched()
    {
        // An unrelated instruction whose first 4 bytes happen to match the vanilla
        // region must not be rewritten (only bank 2003 id 23 qualifies).
        var lookalike = new EMEVD.Instruction(2003, 66,
            BitConverter.GetBytes(VANILLA_SPAWN_REGION));
        var respawn = MakeSetPlayerRespawnPoint(VANILLA_SPAWN_REGION);
        var emevd = MakeGameStartEmevd(lookalike, respawn);

        ChapelGraceInjector.PatchGameStartEvent(emevd, GRACE_SPAWN_REGION);

        Assert.Equal(VANILLA_SPAWN_REGION, BitConverter.ToUInt32(lookalike.ArgData, 0));
        Assert.Equal(GRACE_SPAWN_REGION, BitConverter.ToUInt32(respawn.ArgData, 0));
    }

    [Fact]
    public void PatchGameStartEvent_MultipleRespawnPoints_PatchesAllVanillaOnes()
    {
        var respawn1 = MakeSetPlayerRespawnPoint(VANILLA_SPAWN_REGION);
        var other = MakeSetPlayerRespawnPoint(10012099);
        var respawn2 = MakeSetPlayerRespawnPoint(VANILLA_SPAWN_REGION);
        var emevd = MakeGameStartEmevd(respawn1, other, respawn2);

        ChapelGraceInjector.PatchGameStartEvent(emevd, GRACE_SPAWN_REGION);

        Assert.Equal(GRACE_SPAWN_REGION, BitConverter.ToUInt32(respawn1.ArgData, 0));
        Assert.Equal(10012099u, BitConverter.ToUInt32(other.ArgData, 0));
        Assert.Equal(GRACE_SPAWN_REGION, BitConverter.ToUInt32(respawn2.ArgData, 0));
    }

    [Fact]
    public void PatchGameStartEvent_MissingEvent_DoesNotThrowOrModify()
    {
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(42);
        var respawn = MakeSetPlayerRespawnPoint(VANILLA_SPAWN_REGION);
        evt.Instructions.Add(respawn);
        emevd.Events.Add(evt);

        var exception = Record.Exception(() =>
            ChapelGraceInjector.PatchGameStartEvent(emevd, GRACE_SPAWN_REGION));

        Assert.Null(exception);
        // The respawn point lives in another event and must not be touched
        Assert.Equal(VANILLA_SPAWN_REGION, BitConverter.ToUInt32(respawn.ArgData, 0));
    }

    // --- MoveInDirection ---

    [Fact]
    public void MoveInDirection_ZeroRotation_MovesAlongPositiveZ()
    {
        var moved = ChapelGraceInjector.MoveInDirection(1f, 2f, 3f, 0f, 2f);

        Assert.Equal(1f, moved.X, 4);
        Assert.Equal(2f, moved.Y, 4);
        Assert.Equal(5f, moved.Z, 4);
    }

    [Fact]
    public void MoveInDirection_NinetyDegrees_MovesAlongPositiveX()
    {
        var moved = ChapelGraceInjector.MoveInDirection(1f, 2f, 3f, 90f, 2f);

        Assert.Equal(3f, moved.X, 4);
        Assert.Equal(2f, moved.Y, 4);
        Assert.Equal(3f, moved.Z, 4);
    }

    [Fact]
    public void MoveInDirection_ArbitraryRotation_PreservesDistanceAndHeight()
    {
        var moved = ChapelGraceInjector.MoveInDirection(-32.5f, 21.3f, -91.5f, 166.234589f, 2f);

        Assert.Equal(21.3f, moved.Y, 4);
        var dx = moved.X - -32.5f;
        var dz = moved.Z - -91.5f;
        Assert.Equal(2f, MathF.Sqrt(dx * dx + dz * dz), 3);
    }
}
