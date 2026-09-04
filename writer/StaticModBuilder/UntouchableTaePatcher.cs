using SoulsFormats;

namespace StaticModBuilder;

/// <summary>
/// Gives the Aging Untouchable's dormant lantern-ray animation (3004) a
/// bullet judge id of its own so SpeedFog's boss can fire a beam from it.
///
/// Animation 3004 carries thirteen bullet events (TAE type 2) from the
/// lantern dummy poly 210 with judge ids 101/102, the same ids the idle,
/// walk and teleport animations use for the lantern's ambient pulses. A
/// BehaviorParam remap of those ids would fire the beam at rest, so a
/// subset of 3004's events is retargeted to judge 150 instead: it resolves
/// to nothing under vanilla c5280's behavior variation (52800) and to the
/// beam bullet under the boss's own variation (FogModWrapper's
/// SpeedFogIds.UntouchableBeamJudge, duplicated here because this project
/// does not reference FogModWrapper). Vanilla AI never selects 3004, so
/// the patched anibnd is inert for ambient untouchables. See
/// docs/untouchable-boss.md "Moveset".
/// </summary>
public static class UntouchableTaePatcher
{
    public const string ANIBND_PATH = "chr/c5280.anibnd.dcx";
    private const string TAE_NAME_SUFFIX = "c5280.tae";
    public const long BEAM_ANIMATION = 3004;
    private const int EVENT_TYPE_BULLET = 2;

    /// <summary>Same value as FogModWrapper SpeedFogIds.UntouchableBeamJudge.</summary>
    public const int BEAM_JUDGE = 150;

    /// <summary>How many of 3004's bullet events fire the beam, spread
    /// evenly over the animation; the rest keep their harmless vanilla
    /// flash. Tuning knob (thirteen lasers at boss damage would be lethal).</summary>
    public const int BEAM_EVENT_COUNT = 4;

    /// <summary>Dummy poly override for the retargeted events; null keeps
    /// vanilla 210 (the lantern, designed for FromSoft's cut ray). Tuning
    /// knob for the in-game session if the lantern does not aim at the
    /// player (candidates: 10, the 3002/3003 flash origin; 906, the swing).</summary>
    public static readonly int? BEAM_DUMMY = null;

    private const int VANILLA_DUMMY = 210;
    private static readonly int[] VanillaJudges = { 101, 102 };

    // Parameter layout of a type-2 event, as observed on vanilla c5280:
    // four little-endian int32 [dummy, 0, judge, flags].
    private const int DUMMY_OFFSET = 0;
    private const int JUDGE_OFFSET = 8;
    private const int MIN_PARAM_BYTES = 16;

    /// <summary>
    /// Read c5280.anibnd.dcx from gameDir, retarget animation 3004's bullet
    /// events, write to outputDir. Returns the number of events retargeted
    /// (0 when nothing was written).
    /// </summary>
    public static int Patch(string gameDir, string outputDir)
    {
        var srcPath = Path.Combine(gameDir, ANIBND_PATH);
        if (!File.Exists(srcPath))
        {
            Console.WriteLine($"Warning: {ANIBND_PATH} not found in game dir, skipping untouchable TAE patch");
            return 0;
        }

        var bnd = BND4.Read(srcPath);
        var taeFile = bnd.Files.Find(f => f.Name.EndsWith(TAE_NAME_SUFFIX, StringComparison.OrdinalIgnoreCase));
        if (taeFile == null)
        {
            Console.WriteLine($"Warning: {TAE_NAME_SUFFIX} not found in {ANIBND_PATH}, skipping untouchable TAE patch");
            return 0;
        }

        var tae = TAE.Read(taeFile.Bytes);
        int patched = Patch(tae, Console.WriteLine);
        if (patched == 0)
            return 0;

        taeFile.Bytes = tae.Write();
        var destPath = Path.Combine(outputDir, ANIBND_PATH);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        bnd.Write(destPath);
        Console.WriteLine(
            $"Untouchable TAE patch: retargeted {patched} bullet event(s) of animation {BEAM_ANIMATION} to judge {BEAM_JUDGE} in {ANIBND_PATH}");
        return patched;
    }

    /// <summary>
    /// Retargets BEAM_EVENT_COUNT of animation 3004's bullet events to
    /// BEAM_JUDGE, spread evenly by start time. Returns the number of events
    /// retargeted; 0 and no change when the animation is missing, already
    /// patched, or does not have the expected vanilla layout (never corrupt
    /// the TAE on a layout this code was not written for).
    /// </summary>
    public static int Patch(TAE tae, Action<string> log)
    {
        if (tae.BigEndian)
        {
            log("Warning: big-endian TAE, skipping untouchable TAE patch");
            return 0;
        }

        var anim = tae.Animations.Find(a => a.ID == BEAM_ANIMATION);
        if (anim == null)
        {
            log($"Warning: animation {BEAM_ANIMATION} not found in {TAE_NAME_SUFFIX}, skipping untouchable TAE patch");
            return 0;
        }

        var bullets = anim.Events
            .Where(e => e.Type == EVENT_TYPE_BULLET)
            .OrderBy(e => e.StartTime)
            .ToList();
        if (bullets.Count < BEAM_EVENT_COUNT)
        {
            log($"Warning: animation {BEAM_ANIMATION} has {bullets.Count} bullet event(s), expected at least {BEAM_EVENT_COUNT}; skipping untouchable TAE patch");
            return 0;
        }

        var layouts = bullets.Select(e => e.GetParameterBytes(tae.BigEndian)).ToList();
        if (layouts.Any(p => p.Length < MIN_PARAM_BYTES))
        {
            log($"Warning: animation {BEAM_ANIMATION} bullet event parameters shorter than {MIN_PARAM_BYTES} bytes; skipping untouchable TAE patch");
            return 0;
        }
        if (layouts.Any(p => BitConverter.ToInt32(p, JUDGE_OFFSET) == BEAM_JUDGE))
        {
            log($"  Animation {BEAM_ANIMATION}: already carries judge {BEAM_JUDGE}, skipping");
            return 0;
        }
        foreach (var p in layouts)
        {
            int dummy = BitConverter.ToInt32(p, DUMMY_OFFSET);
            int judge = BitConverter.ToInt32(p, JUDGE_OFFSET);
            if (dummy != VANILLA_DUMMY || Array.IndexOf(VanillaJudges, judge) < 0)
            {
                log($"Warning: animation {BEAM_ANIMATION} bullet event has dummy {dummy} judge {judge}, not the vanilla layout (dummy {VANILLA_DUMMY}, judges 101/102); skipping untouchable TAE patch");
                return 0;
            }
        }

        int n = bullets.Count;
        for (int i = 0; i < BEAM_EVENT_COUNT; i++)
        {
            int index = BEAM_EVENT_COUNT == 1
                ? 0
                : (int)Math.Round((double)i * (n - 1) / (BEAM_EVENT_COUNT - 1), MidpointRounding.AwayFromZero);
            var bytes = (byte[])layouts[index].Clone();
            BitConverter.GetBytes(BEAM_JUDGE).CopyTo(bytes, JUDGE_OFFSET);
            if (BEAM_DUMMY is int dummyOverride)
                BitConverter.GetBytes(dummyOverride).CopyTo(bytes, DUMMY_OFFSET);
            bullets[index].SetParameterBytes(tae.BigEndian, bytes);
        }
        return BEAM_EVENT_COUNT;
    }
}
