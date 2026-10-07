using FogModWrapper;
using SoulsFormats;
using Xunit;

namespace StaticModBuilder.Tests;

public class UntouchableTaePatcherTests
{
    // TAE type-2 (bullet) event parameters as observed on vanilla c5280:
    // four little-endian int32 [dummy, 0, judge, flags].
    private static byte[] BulletParams(int dummy, int judge, int flags = 0)
    {
        var p = new byte[16];
        BitConverter.GetBytes(dummy).CopyTo(p, 0);
        BitConverter.GetBytes(judge).CopyTo(p, 8);
        BitConverter.GetBytes(flags).CopyTo(p, 12);
        return p;
    }

    /// <summary>Animation 3004 as in vanilla: thirteen bullet events from
    /// dummy 210 alternating judges 101/102, plus one non-bullet event.</summary>
    private static TAE MakeTae(int bulletEvents = 13, long animId = 3004, int? oddJudge = null)
    {
        var tae = new TAE { BigEndian = false, Animations = new List<TAE.Animation>() };
        var anim = new TAE.Animation(animId, new TAE.Animation.AnimMiniHeader.Standard(), "a000_003004.hkt");
        anim.Events.Add(new TAE.Event(0f, 0.1f, 16, 0, new byte[16], false));
        for (int i = 0; i < bulletEvents; i++)
        {
            int judge = i % 2 == 0 ? 101 : 102;
            if (oddJudge is int odd && i == 5)
                judge = odd;
            anim.Events.Add(new TAE.Event(0.2f * i, 0.2f * i + 0.05f, 2, 0, BulletParams(210, judge), false));
        }
        tae.Animations.Add(anim);
        return tae;
    }

    private static List<TAE.Event> BulletsByTime(TAE tae) =>
        tae.Animations[0].Events.Where(e => e.Type == 2).OrderBy(e => e.StartTime).ToList();

    private static int JudgeOf(TAE.Event e) => BitConverter.ToInt32(e.GetParameterBytes(false), 8);
    private static int DummyOf(TAE.Event e) => BitConverter.ToInt32(e.GetParameterBytes(false), 0);

    private static List<int> IndicesWithJudge(List<TAE.Event> bullets, int judge) => bullets
        .Select((e, i) => (e, i))
        .Where(t => JudgeOf(t.e) == judge)
        .Select(t => t.i)
        .ToList();

    [Fact]
    public void Patch_RetargetsFourBeamEventsSpreadOverTheAnimationAndAFlameEventBetweenEachPair()
    {
        var tae = MakeTae();

        var count = UntouchableTaePatcher.Patch(tae, _ => { });

        Assert.Equal(7, count);
        var bullets = BulletsByTime(tae);
        Assert.Equal(new List<int> { 0, 4, 8, 12 }, IndicesWithJudge(bullets, SpeedFogIds.UntouchableBeamJudge));
        Assert.Equal(new List<int> { 2, 6, 10 }, IndicesWithJudge(bullets, SpeedFogIds.UntouchableFlameJudge));
        Assert.All(bullets.Where(e => JudgeOf(e) != SpeedFogIds.UntouchableBeamJudge
                                      && JudgeOf(e) != SpeedFogIds.UntouchableFlameJudge),
            e => Assert.Contains(JudgeOf(e), new[] { 101, 102 }));
        // Dummy poly and the non-bullet event untouched.
        Assert.All(bullets, e => Assert.Equal(210, DummyOf(e)));
        Assert.Single(tae.Animations[0].Events, e => e.Type == 16);
    }

    [Fact]
    public void Patch_LogsBothJudges()
    {
        var tae = MakeTae();
        var log = new List<string>();

        UntouchableTaePatcher.Patch(tae, log.Add);

        Assert.Contains(log, l => l.Contains(SpeedFogIds.UntouchableBeamJudge.ToString())
                                  && l.Contains(SpeedFogIds.UntouchableFlameJudge.ToString()));
    }

    [Fact]
    public void Patch_SecondRunIsANoOp()
    {
        var tae = MakeTae();
        UntouchableTaePatcher.Patch(tae, _ => { });
        var before = BulletsByTime(tae).Select(JudgeOf).ToList();
        var log = new List<string>();

        var count = UntouchableTaePatcher.Patch(tae, log.Add);

        Assert.Equal(0, count);
        Assert.Equal(before, BulletsByTime(tae).Select(JudgeOf).ToList());
        Assert.Contains(log, l => l.Contains("already"));
    }

    [Fact]
    public void Patch_UnexpectedLayoutLeavesTaeUntouched()
    {
        // One event carries a judge the vanilla layout never has (a game
        // patch renumbered them): refuse to patch rather than guess.
        var tae = MakeTae(oddJudge: 103);
        var before = BulletsByTime(tae).Select(JudgeOf).ToList();
        var log = new List<string>();

        var count = UntouchableTaePatcher.Patch(tae, log.Add);

        Assert.Equal(0, count);
        Assert.Equal(before, BulletsByTime(tae).Select(JudgeOf).ToList());
        Assert.Contains(log, l => l.Contains("Warning"));
    }

    [Fact]
    public void Patch_MissingAnimationReturnsZero()
    {
        var tae = MakeTae(animId: 3000);
        var log = new List<string>();

        Assert.Equal(0, UntouchableTaePatcher.Patch(tae, log.Add));
        Assert.Contains(log, l => l.Contains("3004"));
    }

    // --- Near-warp marker on the burst (animation 3001) ---

    private static byte[] IntParams(params int[] values)
    {
        var p = new byte[16];
        for (int i = 0; i < values.Length; i++)
            BitConverter.GetBytes(values[i]).CopyTo(p, 4 * i);
        return p;
    }

    private static TAE.Event Grouped(TAE.Animation anim, TAE.Event ev, int groupType)
    {
        var group = anim.EventGroups.Find(g => g.GroupType == groupType);
        if (group == null)
        {
            group = new TAE.EventGroup(groupType);
            anim.EventGroups.Add(group);
        }
        ev.Group = group;
        anim.Events.Add(ev);
        return ev;
    }

    /// <summary>Animations 3000 and 3001 as in vanilla c5280: 3000 carries
    /// the warp marker 20011452 (type 66, one frame at 4.7667 s) next to
    /// other type-66 markers; 3001 lands its hit (type 1, judge 115) from
    /// 0.0333 s to 0.2667 s and carries no type-66 event.</summary>
    private static TAE MakeTeleportTae(bool withTeleportOut = true, int hitEvents = 1, int hitJudge = 115)
    {
        var tae = new TAE { BigEndian = false, Animations = new List<TAE.Animation>() };
        if (withTeleportOut)
        {
            var fade = new TAE.Animation(3000, new TAE.Animation.AnimMiniHeader.Standard(), "a000_003000.hkt");
            Grouped(fade, new TAE.Event(0f, 0.0333f, 66, 0, IntParams(20011453), false), 66);
            Grouped(fade, new TAE.Event(4.7667f, 4.8f, 66, 0, IntParams(UntouchableTaePatcher.VANILLA_WARP_MARKER), false), 66);
            tae.Animations.Add(fade);
        }
        var burst = new TAE.Animation(UntouchableTaePatcher.BURST_ANIMATION, new TAE.Animation.AnimMiniHeader.Standard(), "a000_003001.hkt");
        Grouped(burst, new TAE.Event(1f, 1.1667f, 0, 0, IntParams(23, 0, -1, 6), false), 0);
        for (int i = 0; i < hitEvents; i++)
            Grouped(burst, new TAE.Event(0.0333f, 0.2667f, 1, 0, IntParams(0, 0, hitJudge, 255), false), 1);
        tae.Animations.Add(burst);
        return tae;
    }

    private static TAE.Animation Anim(TAE tae, long id) => tae.Animations.Single(a => a.ID == id);

    private static List<TAE.Event> MarkersOf(TAE.Animation anim, int spEffect) => anim.Events
        .Where(e => e.Type == 66 && BitConverter.ToInt32(e.GetParameterBytes(false), 0) == spEffect)
        .ToList();

    [Fact]
    public void PatchNearWarpMarker_AddsTheMarkerOnTheFrameAfterTheBurstsHit()
    {
        var tae = MakeTeleportTae();
        var burstEventsBefore = Anim(tae, 3001).Events.Count;

        var count = UntouchableTaePatcher.PatchNearWarpMarker(tae, _ => { });

        Assert.Equal(1, count);
        var burst = Anim(tae, 3001);
        var marker = Assert.Single(MarkersOf(burst, SpeedFogIds.UntouchableNearWarpMarkerSpEffect));
        Assert.Equal(burstEventsBefore + 1, burst.Events.Count);
        // Right where the hit ends, one frame long like vanilla's own marker.
        Assert.Equal(0.2667f, marker.StartTime, 4);
        Assert.Equal(0.2667f + (4.8f - 4.7667f), marker.EndTime, 4);
        // Grouped like vanilla's type-66 events, the group in the animation.
        Assert.NotNull(marker.Group);
        Assert.Equal(66, marker.Group.GroupType);
        Assert.Contains(marker.Group, burst.EventGroups);
        // The rest of the parameter block is vanilla's marker's.
        Assert.Equal(IntParams(SpeedFogIds.UntouchableNearWarpMarkerSpEffect), marker.GetParameterBytes(false));
        // The far teleport keeps its own marker, and no near one.
        Assert.Single(MarkersOf(Anim(tae, 3000), UntouchableTaePatcher.VANILLA_WARP_MARKER));
        Assert.Empty(MarkersOf(Anim(tae, 3000), SpeedFogIds.UntouchableNearWarpMarkerSpEffect));
    }

    [Fact]
    public void PatchNearWarpMarker_JoinsAnExistingGroupOfItsType()
    {
        var tae = MakeTeleportTae();
        var burst = Anim(tae, 3001);
        var existing = Grouped(burst, new TAE.Event(0f, 0.0333f, 66, 0, IntParams(20011450), false), 66);
        var groupsBefore = burst.EventGroups.Count;

        Assert.Equal(1, UntouchableTaePatcher.PatchNearWarpMarker(tae, _ => { }));

        var marker = Assert.Single(MarkersOf(burst, SpeedFogIds.UntouchableNearWarpMarkerSpEffect));
        Assert.Same(existing.Group, marker.Group);
        Assert.Equal(groupsBefore, burst.EventGroups.Count);
    }

    [Fact]
    public void PatchNearWarpMarker_SecondRunIsANoOp()
    {
        var tae = MakeTeleportTae();
        UntouchableTaePatcher.PatchNearWarpMarker(tae, _ => { });
        var log = new List<string>();

        Assert.Equal(0, UntouchableTaePatcher.PatchNearWarpMarker(tae, log.Add));

        Assert.Single(MarkersOf(Anim(tae, 3001), SpeedFogIds.UntouchableNearWarpMarkerSpEffect));
        Assert.Contains(log, l => l.Contains("already"));
    }

    [Theory]
    [InlineData(false, 1, 115)] // no vanilla marker to clone
    [InlineData(true, 0, 115)]  // no hit to anchor on
    [InlineData(true, 2, 115)]  // two hits: not the layout this was written for
    [InlineData(true, 1, 116)]  // another judge: same
    public void PatchNearWarpMarker_UnexpectedLayoutLeavesTaeUntouched(bool withTeleportOut, int hitEvents, int hitJudge)
    {
        var tae = MakeTeleportTae(withTeleportOut, hitEvents, hitJudge);
        var before = Anim(tae, 3001).Events.Count;
        var log = new List<string>();

        Assert.Equal(0, UntouchableTaePatcher.PatchNearWarpMarker(tae, log.Add));

        Assert.Equal(before, Anim(tae, 3001).Events.Count);
        Assert.Contains(log, l => l.Contains("Warning"));
    }

    [Fact]
    public void PatchNearWarpMarker_MissingBurstReturnsZero()
    {
        var tae = MakeTae(); // 3004 only
        var log = new List<string>();

        Assert.Equal(0, UntouchableTaePatcher.PatchNearWarpMarker(tae, log.Add));
        Assert.Contains(log, l => l.Contains("3001"));
    }
}
