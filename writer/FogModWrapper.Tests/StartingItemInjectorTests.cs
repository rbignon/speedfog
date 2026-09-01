using System.Linq;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for StartingItemInjector, including the Tarnished Pack showcase
/// additions: regalia (Torrent cosmetic skin) delivery and the one-shot
/// default-skin flag.
///
/// StartingItemInjector.Inject builds its instructions via events.ParseAdd,
/// so exercising it requires a real SoulsIds.Events instance backed by the
/// gitignored data/er-common.emedf.json. Tests locate it the same way
/// RegionStabilityTests locates fog.txt, and skip (early return) if it is
/// not present rather than faking the parser.
/// </summary>
public class StartingItemInjectorTests
{
    // Directly Give Player Item (2003:43): ItemType(byte)@0, ItemID(i32)@4,
    // BaseFlag(i32)@8, NumFlagBits(i32)@12 - verified against the real emedf
    // (events.ParseAdd("DirectlyGivePlayerItem(ItemType.Goods, 2009600, 6001, 1)")).
    private const int BANK = 2003;
    private const int ID_GIVE_ITEM = 43;
    // Set Event Flag (2003:66): FlagType(byte)@0, FlagID(i32)@4, State(byte)@8
    // (matches StartupFlagInjector.MakeSetEventFlag's raw layout).
    private const int ID_SET_FLAG = 66;

    // Regalia Good IDs the injector must give when TorrentSkinsData.Unlock is
    // true: Tree Sentinel, Carian Silver, and Funereal Night Torrent skins.
    private static readonly int[] RegaliaGoods = { 2009600, 2009610, 2009620 };

    private const int ITEMS_GIVEN_FLAG = 1040299001; // SpeedFogIds.ItemsGivenFlag

    // Vanilla flag common.emevd event 780 sets ON (vanilla Torrent appearance)
    // when none of 6700-6703 is on. Must be cleared alongside setting a
    // default skin flag so exactly one of 6700-6703 stays ON.
    private const int TORRENT_VANILLA_SKIN_FLAG = 6700;

    // Grace-ESD "attire menu announced" flag: set whenever the regalia are
    // unlocked, so the one-time announce dialog never shows mid-run.
    private const int TORRENT_ATTIRE_ANNOUNCED_FLAG = 69560;

    private static string? FindDataDir()
    {
        var envDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "er-common.emedf.json")))
            return envDir;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../..", "data"));
        if (File.Exists(Path.Combine(candidate, "er-common.emedf.json")))
            return candidate;

        return null;
    }

    private static Events? BuildEvents()
    {
        var dataDir = FindDataDir();
        return dataDir == null
            ? null
            : new Events(Path.Combine(dataDir, "er-common.emedf.json"), darkScriptMode: true, paramAwareMode: true);
    }

    /// <summary>
    /// Runs Inject with one plain key item (Good ID 100, no auxiliary flags of
    /// its own) plus the given torrentSkins, and returns the single generated
    /// event.
    /// </summary>
    private static EMEVD.Event RunInject(Events events, TorrentSkinsData? torrentSkins)
    {
        var commonEmevd = new EMEVD();
        commonEmevd.Events.Add(new EMEVD.Event(0));

        StartingItemInjector.Inject(commonEmevd, new List<int> { 100 }, new List<CarePackageItem>(), events, torrentSkins);

        return commonEmevd.Events.Single(e => e.ID == SpeedFogIds.StartingItemEvents.Base);
    }

    private static int DecodeArgInt(EMEVD.Instruction instr) => BitConverter.ToInt32(instr.ArgData, 4);

    // State(byte)@8 of SetEventFlag: 1 = ON, 0 = OFF.
    private static byte DecodeState(EMEVD.Instruction instr) => instr.ArgData[8];

    private static List<int> GivenGoodIds(EMEVD.Event evt) =>
        evt.Instructions.Where(i => i.Bank == BANK && i.ID == ID_GIVE_ITEM).Select(DecodeArgInt).ToList();

    private static List<EMEVD.Instruction> FlagSets(EMEVD.Event evt) =>
        evt.Instructions.Where(i => i.Bank == BANK && i.ID == ID_SET_FLAG).ToList();

    private static List<EMEVD.Instruction> FlagSetsFor(EMEVD.Event evt, int flagId) =>
        FlagSets(evt).Where(i => DecodeArgInt(i) == flagId).ToList();

    [Fact]
    public void Inject_UnlockWithDefaultFlag_GivesRegaliaAndSetsFlagBeforeGuard()
    {
        var events = BuildEvents();
        if (events == null) return;

        var evt = RunInject(events, new TorrentSkinsData { Unlock = true, DefaultFlag = 6702 });

        var givenIds = GivenGoodIds(evt);
        foreach (var regaliaId in RegaliaGoods)
            Assert.Contains(regaliaId, givenIds);

        int clearIndex = evt.Instructions.FindIndex(i =>
            i.Bank == BANK && i.ID == ID_SET_FLAG && DecodeArgInt(i) == TORRENT_VANILLA_SKIN_FLAG && DecodeState(i) == 0);
        int flagIndex = evt.Instructions.FindIndex(i =>
            i.Bank == BANK && i.ID == ID_SET_FLAG && DecodeArgInt(i) == 6702 && DecodeState(i) == 1);
        int guardIndex = evt.Instructions.FindIndex(i =>
            i.Bank == BANK && i.ID == ID_SET_FLAG && DecodeArgInt(i) == ITEMS_GIVEN_FLAG);

        Assert.True(clearIndex >= 0, "expected a SetEventFlag(6700, OFF) instruction");
        Assert.True(flagIndex >= 0, "expected a SetEventFlag(6702, ON) instruction");
        Assert.True(guardIndex >= 0, "expected the ITEMS_GIVEN_FLAG guard-set");
        Assert.True(clearIndex < flagIndex, "the vanilla-skin flag must be cleared before the default-skin flag is set");
        Assert.True(flagIndex < guardIndex, "the default-skin flag must be set before the one-shot guard flag");

        int announcedIndex = evt.Instructions.FindIndex(i =>
            i.Bank == BANK && i.ID == ID_SET_FLAG && DecodeArgInt(i) == TORRENT_ATTIRE_ANNOUNCED_FLAG && DecodeState(i) == 1);
        Assert.True(announcedIndex >= 0 && announcedIndex < guardIndex,
            "the attire-announced flag must be set (before the one-shot guard) whenever regalia are unlocked");
    }

    [Fact]
    public void Inject_UnlockWithoutDefaultFlag_GivesRegaliaButNoExtraFlag()
    {
        var events = BuildEvents();
        if (events == null) return;

        var evt = RunInject(events, new TorrentSkinsData { Unlock = true, DefaultFlag = 0 });

        var givenIds = GivenGoodIds(evt);
        foreach (var regaliaId in RegaliaGoods)
            Assert.Contains(regaliaId, givenIds);

        // Exactly two SetEventFlags: the attire-announced flag (regalia are
        // unlocked, so the grace popup is suppressed) and the mandatory
        // one-shot guard. No default flag means no 6700 clear either.
        var flagIds = FlagSets(evt).Select(DecodeArgInt).ToList();
        Assert.Equal(2, flagIds.Count);
        Assert.Contains(TORRENT_ATTIRE_ANNOUNCED_FLAG, flagIds);
        Assert.Contains(ITEMS_GIVEN_FLAG, flagIds);
        Assert.Empty(FlagSetsFor(evt, TORRENT_VANILLA_SKIN_FLAG));
    }

    [Fact]
    public void Inject_UnlockOnly_StillFiresEventWithNoOtherStartingItems()
    {
        var events = BuildEvents();
        if (events == null) return;

        var commonEmevd = new EMEVD();
        commonEmevd.Events.Add(new EMEVD.Event(0));

        // No key items, no care package: the regalia alone must still be enough
        // to create the event (it must not be skipped as "nothing to give").
        StartingItemInjector.Inject(
            commonEmevd, new List<int>(), new List<CarePackageItem>(), events,
            new TorrentSkinsData { Unlock = true, DefaultFlag = 0 });

        var evt = Assert.Single(commonEmevd.Events, e => e.ID == SpeedFogIds.StartingItemEvents.Base);
        var givenIds = GivenGoodIds(evt);
        Assert.Equal(RegaliaGoods.OrderBy(x => x), givenIds.OrderBy(x => x));
        Assert.Empty(FlagSetsFor(evt, TORRENT_VANILLA_SKIN_FLAG));
        Assert.Single(FlagSetsFor(evt, TORRENT_ATTIRE_ANNOUNCED_FLAG));
    }

    [Fact]
    public void Inject_NullTorrentSkins_EventUnchangedFromBaseline()
    {
        var events = BuildEvents();
        if (events == null) return;

        var evt = RunInject(events, torrentSkins: null);

        // Regression: no regalia given, and the event's shape is exactly what
        // it was before this feature existed (IfEventFlag guard-check,
        // EndIfEventFlag guard-bail, one DirectlyGivePlayerItem for Good ID
        // 100, ITEMS_GIVEN_FLAG guard-set - nothing else).
        Assert.Empty(GivenGoodIds(evt).Intersect(RegaliaGoods));
        Assert.Equal(4, evt.Instructions.Count);
        var flagSet = Assert.Single(FlagSets(evt));
        Assert.Equal(ITEMS_GIVEN_FLAG, DecodeArgInt(flagSet));
        Assert.Empty(FlagSetsFor(evt, TORRENT_VANILLA_SKIN_FLAG));
    }
}
