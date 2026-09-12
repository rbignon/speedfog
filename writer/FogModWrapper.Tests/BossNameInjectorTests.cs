using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class BossNameInjectorTests
{
    private const uint WATCHDOG_ARENA = 30010800;
    private const uint SHADE_ARENA = 30010850;
    private const int WATCHDOG_NAME = 904260301;
    private const int CRUCIBLE_KNIGHT_NAME = 902500301;
    // Vanilla carries the same text at several ids, and variant names in their
    // own right: the randomizer's copied healthbar picks any of them.
    private const int CRUCIBLE_KNIGHT_NAME_ALT = 902500520;
    private const int PUMPKIN_HEAD_NAME = 904340540;
    private const int PUMPKIN_HEAD_HAMMER_NAME = 904340541;
    private const string MAP = "m30_01_00_00";
    // The bundle the game resolves text from (full base FMG copies inside).
    private const string ITEM_BND = "item_dlc02.msgbnd.dcx";

    private static BossNameEntry Entry(string name, string map = MAP) => new() { Name = name, Map = map };

    private static EMEVD.Instruction DisplayBossHp(bool on, uint entity, int nameId)
    {
        var args = new byte[16];
        args[0] = on ? (byte)1 : (byte)0;
        BitConverter.GetBytes(entity).CopyTo(args, 4);
        BitConverter.GetBytes(nameId).CopyTo(args, 12);
        return new EMEVD.Instruction(2003, 11, args);
    }

    private static (uint entity, int nameId) Decode(EMEVD.Instruction instr)
        => (BitConverter.ToUInt32(instr.ArgData, 4), BitConverter.ToInt32(instr.ArgData, 12));

    /// <summary>Watchdog arena (on + off healthbar) and the Shade arena of the same map.</summary>
    private static EMEVD MakeEmevd()
    {
        var emevd = new EMEVD();
        var fight = new EMEVD.Event(30012810);
        fight.Instructions.Add(DisplayBossHp(true, WATCHDOG_ARENA, WATCHDOG_NAME));
        fight.Instructions.Add(DisplayBossHp(false, WATCHDOG_ARENA, WATCHDOG_NAME));
        emevd.Events.Add(fight);
        var shade = new EMEVD.Event(30012860);
        shade.Instructions.Add(DisplayBossHp(true, SHADE_ARENA, CRUCIBLE_KNIGHT_NAME));
        emevd.Events.Add(shade);
        return emevd;
    }

    private static void WriteItemBnd(string msgDir, string lang, params (int id, string text)[] entries)
    {
        var fmg = new FMG();
        foreach (var (id, text) in entries)
            fmg.Entries.Add(new FMG.Entry(id, text));
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0,
            $@"N:\GR\data\INTERROOT_win64\msg\{lang}\NpcName.fmg", fmg.Write()));
        var langDir = Path.Combine(msgDir, lang);
        Directory.CreateDirectory(langDir);
        bnd.Write(Path.Combine(langDir, ITEM_BND));
    }

    private static FMG ReadNpcName(string bndPath)
    {
        var bnd = BND4.Read(bndPath);
        return FMG.Read(bnd.Files.Single(f => f.Name.EndsWith("NpcName.fmg")).Bytes);
    }

    private static string? NpcNameText(string bndPath, int id)
        => ReadNpcName(bndPath).Entries.Find(e => e.ID == id)?.Text;

    /// <summary>Vanilla msg tree with engus/frafr/deude, each carrying the
    /// Crucible Knight entry in its own language.</summary>
    private static string MakeGameDir(TempDir tmp)
    {
        var gameDir = Path.Combine(tmp.Path, "game");
        var msgDir = Path.Combine(gameDir, "msg");
        WriteItemBnd(msgDir, "engus",
            (CRUCIBLE_KNIGHT_NAME, "Crucible Knight"),
            (CRUCIBLE_KNIGHT_NAME_ALT, "Crucible Knight"),
            (PUMPKIN_HEAD_NAME, "Mad Pumpkin Head"),
            (PUMPKIN_HEAD_HAMMER_NAME, "Mad Pumpkin Head (Hammer)"));
        WriteItemBnd(msgDir, "frafr", (CRUCIBLE_KNIGHT_NAME, "Chevalier du Creuset"));
        WriteItemBnd(msgDir, "deude", (CRUCIBLE_KNIGHT_NAME, "Schmelztiegel-Ritter"));
        return gameDir;
    }

    private static string MakeModDir(TempDir tmp, EMEVD? emevd = null)
    {
        var modDir = Path.Combine(tmp.Path, "mod");
        Directory.CreateDirectory(Path.Combine(modDir, "event"));
        (emevd ?? MakeEmevd()).Write(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        return modDir;
    }

    // ---- ResolveNameIds ----

    [Fact]
    public void ResolveNameIds_ReusesVanillaEntryOnExactMatch()
    {
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Crucible Knight") };
        var vanilla = new Dictionary<string, int> { ["Crucible Knight"] = CRUCIBLE_KNIGHT_NAME };

        var r = Assert.Single(BossNameInjector.ResolveNameIds(bossNames, vanilla));
        Assert.Equal(CRUCIBLE_KNIGHT_NAME, r.NameId);
        Assert.False(r.IsNew);
    }

    [Fact]
    public void ResolveNameIds_AllocatesOneSpeedFogIdPerDistinctName()
    {
        var bossNames = new Dictionary<string, BossNameEntry>
        {
            ["31010800"] = Entry("Smith Golem"),
            ["30010800"] = Entry("Aging Untouchable"),
            ["30020800"] = Entry("Aging Untouchable"),
        };

        var resolved = BossNameInjector.ResolveNameIds(bossNames, new Dictionary<string, int>());

        var byArena = resolved.ToDictionary(r => r.ArenaId);
        int first = SpeedFogIds.BossNameFmgIds.Base;
        // Allocation follows ascending arena id, so the Untouchable (30010800) gets the first id.
        Assert.Equal(first, byArena[30010800].NameId);
        Assert.Equal(first, byArena[30020800].NameId);
        Assert.Equal(first + 1, byArena[31010800].NameId);
        Assert.All(resolved, r => Assert.True(r.IsNew));
    }

    [Fact]
    public void ResolveNameIds_ThrowsWhenBudgetExceeded()
    {
        var bossNames = new Dictionary<string, BossNameEntry>();
        for (int i = 0; i <= SpeedFogIds.BossNameFmgIds.Capacity; i++)
            bossNames[(30010800 + i).ToString()] = Entry($"Mob {i}");

        Assert.Throws<InvalidOperationException>(
            () => BossNameInjector.ResolveNameIds(bossNames, new Dictionary<string, int>()));
    }

    [Fact]
    public void ResolveNameIds_PrefersTheVanillaEntryOfTheVariantName()
    {
        // "Mad Pumpkin Head (Hammer)" is a vanilla name of its own, not a
        // boss_arena_tags disambiguation suffix: keep it.
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Mad Pumpkin Head (Hammer)") };
        var vanilla = new Dictionary<string, int>
        {
            ["Mad Pumpkin Head"] = PUMPKIN_HEAD_NAME,
            ["Mad Pumpkin Head (Hammer)"] = PUMPKIN_HEAD_HAMMER_NAME,
        };

        var r = Assert.Single(BossNameInjector.ResolveNameIds(bossNames, vanilla));
        Assert.Equal(PUMPKIN_HEAD_HAMMER_NAME, r.NameId);
        Assert.Equal("Mad Pumpkin Head (Hammer)", r.Name);
        Assert.False(r.IsNew);
    }

    [Fact]
    public void ResolveNameIds_DropsADisambiguationSuffixVanillaDoesNotKnow()
    {
        // "(Leda Fight)" only exists in boss_arena_tags.json.
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Hornsent (Leda Fight)") };
        var vanilla = new Dictionary<string, int> { ["Hornsent"] = 141403 };

        var r = Assert.Single(BossNameInjector.ResolveNameIds(bossNames, vanilla));
        Assert.Equal(141403, r.NameId);
        Assert.Equal("Hornsent", r.Name);
    }

    [Fact]
    public void ResolveNameIds_AllocatesForTheStrippedNameWhenVanillaKnowsNeither()
    {
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Divine Bird Warrior (Frost)") };

        var r = Assert.Single(BossNameInjector.ResolveNameIds(bossNames, new Dictionary<string, int>()));
        Assert.Equal(SpeedFogIds.BossNameFmgIds.Base, r.NameId);
        Assert.Equal("Divine Bird Warrior", r.Name);
        Assert.True(r.IsNew);
    }

    // ---- PatchEmevd ----

    [Fact]
    public void PatchEmevd_RewritesEveryHealthbarInstructionOfTheArena()
    {
        var emevd = MakeEmevd();

        var (n, _) = BossNameInjector.PatchEmevd(emevd, WATCHDOG_ARENA, 755890000, "Aging Untouchable", new Dictionary<int, string>());

        Assert.Equal(2, n);
        var fight = emevd.Events.Single(e => e.ID == 30012810);
        Assert.Equal((WATCHDOG_ARENA, 755890000), Decode(fight.Instructions[0]));
        Assert.Equal((WATCHDOG_ARENA, 755890000), Decode(fight.Instructions[1]));
        var shade = emevd.Events.Single(e => e.ID == 30012860);
        Assert.Equal((SHADE_ARENA, CRUCIBLE_KNIGHT_NAME), Decode(shade.Instructions[0]));
    }

    [Fact]
    public void PatchEmevd_SkipsTruncatedInstructions()
    {
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(30012810);
        var args = new byte[8];
        BitConverter.GetBytes(WATCHDOG_ARENA).CopyTo(args, 4);
        evt.Instructions.Add(new EMEVD.Instruction(2003, 11, args));
        emevd.Events.Add(evt);

        var (n, _) = BossNameInjector.PatchEmevd(emevd, WATCHDOG_ARENA, 755890000, "Aging Untouchable", new Dictionary<int, string>());

        Assert.Equal(0, n);
        Assert.Equal(8, evt.Instructions[0].ArgData.Length);
    }

    [Fact]
    public void PatchEmevd_SkipsInstructionsWhoseNameIdIsAParameter()
    {
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(30012810);
        evt.Instructions.Add(DisplayBossHp(true, WATCHDOG_ARENA, 0));
        // Name id bound from the event's argument slot 0 (target byte 12).
        evt.Parameters.Add(new EMEVD.Parameter(0, 12, 0, 4));
        emevd.Events.Add(evt);

        var (n, _) = BossNameInjector.PatchEmevd(emevd, WATCHDOG_ARENA, 755890000, "Aging Untouchable", new Dictionary<int, string>());

        Assert.Equal(0, n);
        Assert.Equal((WATCHDOG_ARENA, 0), Decode(evt.Instructions[0]));
    }

    // ---- Inject ----

    [Fact]
    public void Inject_PatchesEmevdAndWritesNewEntriesToEngusAndFrafrOnly()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        var bossNames = new Dictionary<string, BossNameEntry>
        {
            ["30010800"] = Entry("Aging Untouchable"),
            ["30010850"] = Entry("Crucible Knight"),
        };

        BossNameInjector.Inject(modDir, gameDir, bossNames, _ => { });

        int newId = SpeedFogIds.BossNameFmgIds.Base;
        var engus = Path.Combine(modDir, "msg", "engus", ITEM_BND);
        var frafr = Path.Combine(modDir, "msg", "frafr", ITEM_BND);
        Assert.Equal("Aging Untouchable", NpcNameText(engus, newId));
        Assert.Equal("Aging Untouchable", NpcNameText(frafr, newId));
        // Vanilla entries survive the edit; other languages are not touched;
        // the base item.msgbnd.dcx is left alone (the game reads the dlc02 one).
        Assert.Equal("Chevalier du Creuset", NpcNameText(frafr, CRUCIBLE_KNIGHT_NAME));
        Assert.False(File.Exists(Path.Combine(modDir, "msg", "deude", ITEM_BND)));
        Assert.False(File.Exists(Path.Combine(modDir, "msg", "engus", "item.msgbnd.dcx")));

        var emevd = EMEVD.Read(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        var fight = emevd.Events.Single(e => e.ID == 30012810);
        Assert.Equal((WATCHDOG_ARENA, newId), Decode(fight.Instructions[0]));
        Assert.Equal((WATCHDOG_ARENA, newId), Decode(fight.Instructions[1]));
        var shade = emevd.Events.Single(e => e.ID == 30012860);
        Assert.Equal((SHADE_ARENA, CRUCIBLE_KNIGHT_NAME), Decode(shade.Instructions[0]));
    }

    [Fact]
    public void Inject_LayersOnTheModCopyOfTheMsgBnd()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        // FogMod already wrote its engus item_dlc02 copy, carrying an Item Randomizer entry.
        WriteItemBnd(Path.Combine(modDir, "msg"), "engus",
            (CRUCIBLE_KNIGHT_NAME, "Crucible Knight"), (907770000, "Randomized Boss"));
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Aging Untouchable") };

        BossNameInjector.Inject(modDir, gameDir, bossNames, _ => { });

        var engus = Path.Combine(modDir, "msg", "engus", ITEM_BND);
        Assert.Equal("Randomized Boss", NpcNameText(engus, 907770000));
        Assert.Equal("Aging Untouchable", NpcNameText(engus, SpeedFogIds.BossNameFmgIds.Base));
    }

    [Fact]
    public void Inject_VanillaMatchWritesNoFmgEntry()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010850"] = Entry("Crucible Knight") };

        BossNameInjector.Inject(modDir, gameDir, bossNames, _ => { });

        Assert.False(Directory.Exists(Path.Combine(modDir, "msg")));
        var emevd = EMEVD.Read(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        var shade = emevd.Events.Single(e => e.ID == 30012860);
        Assert.Equal((SHADE_ARENA, CRUCIBLE_KNIGHT_NAME), Decode(shade.Instructions[0]));
    }

    [Fact]
    public void Inject_LeavesTheHealthbarAloneWhenAnotherVanillaIdShowsTheSameName()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        // The randomizer named the Shade arena with the second vanilla id of
        // "Crucible Knight"; both display the same text, so nothing to do.
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(30012860);
        evt.Instructions.Add(DisplayBossHp(true, SHADE_ARENA, CRUCIBLE_KNIGHT_NAME_ALT));
        emevd.Events.Add(evt);
        var modDir = MakeModDir(tmp, emevd);
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010850"] = Entry("Crucible Knight") };
        var log = new List<string>();

        BossNameInjector.Inject(modDir, gameDir, bossNames, log.Add);

        var after = EMEVD.Read(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        Assert.Equal((SHADE_ARENA, CRUCIBLE_KNIGHT_NAME_ALT), Decode(after.Events.Single().Instructions[0]));
        Assert.Contains(log, l => l.Contains("30010850") && l.Contains("already"));
    }

    [Fact]
    public void Inject_RepointsEveryEmevdThatHoldsTheArenaHealthbar()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        // A second EMEVD (a common one) drives the same arena's healthbar.
        var common = new EMEVD();
        var evt = new EMEVD.Event(9005845);
        evt.Instructions.Add(DisplayBossHp(true, WATCHDOG_ARENA, WATCHDOG_NAME));
        common.Events.Add(evt);
        common.Write(Path.Combine(modDir, "event", "common_func.emevd.dcx"));
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Crucible Knight") };

        BossNameInjector.Inject(modDir, gameDir, bossNames, _ => { });

        var map = EMEVD.Read(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        Assert.Equal((WATCHDOG_ARENA, CRUCIBLE_KNIGHT_NAME), Decode(map.Events.Single(e => e.ID == 30012810).Instructions[0]));
        var reread = EMEVD.Read(Path.Combine(modDir, "event", "common_func.emevd.dcx"));
        Assert.Equal((WATCHDOG_ARENA, CRUCIBLE_KNIGHT_NAME), Decode(reread.Events.Single().Instructions[0]));
    }

    [Fact]
    public void Inject_LeavesTheHealthbarAloneWhenItAlreadyNamesThePlacedEnemy()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        // The randomizer already repointed the Shade arena at the placed
        // Crucible Knight's vanilla name; nothing left to do.
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010850"] = Entry("Crucible Knight") };
        var before = File.GetLastWriteTimeUtc(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        var log = new List<string>();

        BossNameInjector.Inject(modDir, gameDir, bossNames, log.Add);

        Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx")));
        Assert.DoesNotContain(log, l => l.Contains("Warning"));
        Assert.Contains(log, l => l.Contains("30010850") && l.Contains("already"));
    }

    [Fact]
    public void Inject_WritesNoFmgEntryForAnArenaItCouldNotPatch()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        var bossNames = new Dictionary<string, BossNameEntry> { ["30019999"] = Entry("Aging Untouchable") };

        BossNameInjector.Inject(modDir, gameDir, bossNames, _ => { });

        Assert.False(Directory.Exists(Path.Combine(modDir, "msg")));
    }

    [Fact]
    public void Inject_WarnsWhenTheArenaHasNoHealthbarInstruction()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        var bossNames = new Dictionary<string, BossNameEntry> { ["30019999"] = Entry("Aging Untouchable") };
        var log = new List<string>();

        BossNameInjector.Inject(modDir, gameDir, bossNames, log.Add);

        Assert.Contains(log, l => l.Contains("Warning") && l.Contains("30019999"));
    }

    [Fact]
    public void Inject_FallsBackToScanningOtherEmevdsWhenTheDeclaredMapHasNoInstruction()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        // Declared map absent (a large overworld tile), instruction in another EMEVD of the mod dir.
        var bossNames = new Dictionary<string, BossNameEntry> { ["30010800"] = Entry("Aging Untouchable", "m60_13_09_02") };
        var log = new List<string>();

        BossNameInjector.Inject(modDir, gameDir, bossNames, log.Add);

        var emevd = EMEVD.Read(Path.Combine(modDir, "event", $"{MAP}.emevd.dcx"));
        var fight = emevd.Events.Single(e => e.ID == 30012810);
        Assert.Equal((WATCHDOG_ARENA, SpeedFogIds.BossNameFmgIds.Base), Decode(fight.Instructions[0]));
        Assert.Contains(log, l => l.Contains("30010800") && l.Contains(MAP) && !l.Contains("Warning"));
    }

    [Fact]
    public void Inject_WarnsWhenTheMapEmevdIsMissing()
    {
        using var tmp = new TempDir();
        var gameDir = MakeGameDir(tmp);
        var modDir = MakeModDir(tmp);
        var bossNames = new Dictionary<string, BossNameEntry> { ["31010800"] = Entry("Aging Untouchable", "m31_01_00_00") };
        var log = new List<string>();

        BossNameInjector.Inject(modDir, gameDir, bossNames, log.Add);

        Assert.Contains(log, l => l.Contains("Warning") && l.Contains("m31_01_00_00"));
    }
}
