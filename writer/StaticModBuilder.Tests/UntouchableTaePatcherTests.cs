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

    [Fact]
    public void Patch_RetargetsFourEventsSpreadOverTheAnimation()
    {
        var tae = MakeTae();

        var count = UntouchableTaePatcher.Patch(tae, _ => { });

        Assert.Equal(4, count);
        var bullets = BulletsByTime(tae);
        var retargeted = bullets
            .Select((e, i) => (e, i))
            .Where(t => JudgeOf(t.e) == UntouchableTaePatcher.BEAM_JUDGE)
            .Select(t => t.i)
            .ToList();
        Assert.Equal(new List<int> { 0, 4, 8, 12 }, retargeted);
        Assert.All(bullets.Where(e => JudgeOf(e) != UntouchableTaePatcher.BEAM_JUDGE),
            e => Assert.Contains(JudgeOf(e), new[] { 101, 102 }));
        // Dummy poly untouched (BEAM_DUMMY is null by default), non-bullet event untouched.
        Assert.All(bullets, e => Assert.Equal(210, DummyOf(e)));
        Assert.Single(tae.Animations[0].Events, e => e.Type == 16);
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
}
