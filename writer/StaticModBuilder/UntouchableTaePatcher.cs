using FogModWrapper;
using SoulsFormats;

namespace StaticModBuilder;

/// <summary>
/// Gives the Aging Untouchable's dormant lantern-ray animation (3004) two
/// bullet judge ids of its own so SpeedFog's boss can fire a beam and a
/// flame nova from it.
///
/// Animation 3004 carries thirteen bullet events (TAE type 2) from the
/// lantern dummy poly 210 with judge ids 101/102, the same ids the idle,
/// walk and teleport animations use for the lantern's ambient pulses. A
/// BehaviorParam remap of those ids would fire the beam at rest, so a
/// subset of 3004's events is retargeted instead: BEAM_EVENT_COUNT of them
/// to judge 150 (SpeedFogIds.UntouchableBeamJudge), spread evenly, and one
/// between each consecutive pair of those to judge 151
/// (SpeedFogIds.UntouchableFlameJudge). Both resolve to nothing under
/// vanilla c5280's behavior variation (52800) and to the beam and flame
/// bullets under the boss's own variation. Vanilla AI never selects 3004,
/// so the patched anibnd is inert for ambient untouchables. See
/// docs/untouchable-boss.md "Moveset".
///
/// The burst (3001) also gets a marker SpEffect event right after its hit
/// (<see cref="PatchNearWarpMarker"/>): the boss's near teleport warps when
/// the marker is applied, the way vanilla's far teleport answers 3000's
/// own marker, so the warp only ever follows a burst the boss actually
/// plays. Only the boss's battle script reads the marker.
/// </summary>
public static class UntouchableTaePatcher
{
    public const string ANIBND_PATH = "chr/c5280.anibnd.dcx";
    private const string TAE_NAME_SUFFIX = "c5280.tae";
    public const long BEAM_ANIMATION = 3004;
    private const int EVENT_TYPE_BULLET = 2;

    /// <summary>The lantern burst, whose hit (one type-1 event, judge 115)
    /// the near-warp marker follows.</summary>
    public const long BURST_ANIMATION = 3001;
    /// <summary>The far teleport's 5 s fade, which carries vanilla's warp
    /// marker: the template of the near one.</summary>
    public const long TELEPORT_OUT_ANIMATION = 3000;
    /// <summary>Vanilla's fieldless warp marker SpEffect, applied by a
    /// one-frame type-66 event of 3000 at 4.77 s (FogModWrapper's
    /// UntouchableBossInjector.VANILLA_WARP_MARKER_SPEFFECT).</summary>
    public const int VANILLA_WARP_MARKER = 20011452;
    private const int EVENT_TYPE_ATTACK = 1;
    private const int EVENT_TYPE_SPEFFECT = 66;
    private const int BURST_JUDGE = 115;
    // Parameter layout of type-1 and type-66 events, as observed on vanilla
    // c5280: the judge at byte 8 of the attack event, the SpEffect id at
    // byte 0 of the SpEffect event.
    private const int ATTACK_JUDGE_OFFSET = 8;
    private const int SPEFFECT_OFFSET = 0;

    /// <summary>How many of 3004's bullet events fire the beam, spread
    /// evenly over the animation; a flame event sits between each
    /// consecutive pair (BEAM_EVENT_COUNT - 1 of them) and the rest keep
    /// their harmless vanilla flash. Tuning knob (thirteen lasers at boss
    /// damage would be lethal).</summary>
    public const int BEAM_EVENT_COUNT = 4;

    private const int VANILLA_DUMMY = 210;
    private static readonly int[] VanillaJudges = { 101, 102 };

    // Parameter layout of a type-2 event, as observed on vanilla c5280:
    // four little-endian int32 [dummy, 0, judge, flags].
    private const int DUMMY_OFFSET = 0;
    private const int JUDGE_OFFSET = 8;
    private const int MIN_PARAM_BYTES = 16;

    /// <summary>
    /// Read c5280.anibnd.dcx from gameDir, retarget animation 3004's bullet
    /// events, add the near-warp marker to 3001, write to outputDir. Returns
    /// the number of events retargeted or added (0 when nothing was
    /// written). Nothing is written unless 3004 was retargeted: FogModWrapper
    /// reads the file's presence as "the payload judges exist" and enables
    /// the moveset on it (UntouchableBossInjector.MovesetStaticAssets), and
    /// the marker is part of that moveset. A marker refused alone only
    /// leaves the near teleport a burst without a warp (warning logged).
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
        patched += PatchNearWarpMarker(tae, Console.WriteLine);

        taeFile.Bytes = tae.Write();
        var destPath = Path.Combine(outputDir, ANIBND_PATH);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        bnd.Write(destPath);
        Console.WriteLine($"Untouchable TAE patch: wrote {ANIBND_PATH}");
        return patched;
    }

    /// <summary>
    /// Adds to animation 3001 one copy of 3000's warp-marker event, applying
    /// SpeedFogIds.UntouchableNearWarpMarkerSpEffect instead, starting when
    /// the burst's hit ends and lasting as long as vanilla's marker (one
    /// frame), in an event group of the template's type. Returns 1 when
    /// added; 0 and no change when an animation or the template is missing,
    /// the marker is already there, or 3001 does not land exactly one hit
    /// with judge 115 (never guess on a layout this was not written for).
    /// </summary>
    public static int PatchNearWarpMarker(TAE tae, Action<string> log)
    {
        if (tae.BigEndian)
        {
            log("Warning: big-endian TAE, skipping untouchable near-warp marker");
            return 0;
        }

        var burst = tae.Animations.Find(a => a.ID == BURST_ANIMATION);
        if (burst == null)
        {
            log($"Warning: animation {BURST_ANIMATION} not found in {TAE_NAME_SUFFIX}, skipping untouchable near-warp marker");
            return 0;
        }
        if (burst.Events.Any(e => e.Type == EVENT_TYPE_SPEFFECT && IntAt(e, SPEFFECT_OFFSET) == SpeedFogIds.UntouchableNearWarpMarkerSpEffect))
        {
            log($"  Animation {BURST_ANIMATION}: already carries the near-warp marker {SpeedFogIds.UntouchableNearWarpMarkerSpEffect}, skipping");
            return 0;
        }

        var hits = burst.Events.Where(e => e.Type == EVENT_TYPE_ATTACK).ToList();
        if (hits.Count != 1 || IntAt(hits[0], ATTACK_JUDGE_OFFSET) != BURST_JUDGE)
        {
            log($"Warning: animation {BURST_ANIMATION} has {hits.Count} attack event(s), expected one with judge {BURST_JUDGE}; skipping untouchable near-warp marker");
            return 0;
        }

        var template = tae.Animations.Find(a => a.ID == TELEPORT_OUT_ANIMATION)?.Events
            .FirstOrDefault(e => e.Type == EVENT_TYPE_SPEFFECT && IntAt(e, SPEFFECT_OFFSET) == VANILLA_WARP_MARKER);
        if (template == null)
        {
            log($"Warning: animation {TELEPORT_OUT_ANIMATION} has no type-{EVENT_TYPE_SPEFFECT} event applying {VANILLA_WARP_MARKER}; skipping untouchable near-warp marker");
            return 0;
        }

        var marker = template.GetClone(tae.BigEndian);
        var bytes = (byte[])template.GetParameterBytes(tae.BigEndian).Clone();
        BitConverter.GetBytes(SpeedFogIds.UntouchableNearWarpMarkerSpEffect).CopyTo(bytes, SPEFFECT_OFFSET);
        marker.SetParameterBytes(tae.BigEndian, bytes);
        marker.StartTime = hits[0].EndTime;
        marker.EndTime = hits[0].EndTime + (template.EndTime - template.StartTime);
        if (template.Group != null)
        {
            var group = burst.EventGroups.Find(g => g.GroupType == template.Group.GroupType
                                                    && g.GroupData.DataType == template.Group.GroupData.DataType);
            if (group == null)
            {
                group = template.Group.GetClone();
                burst.EventGroups.Add(group);
            }
            marker.Group = group;
        }
        burst.Events.Add(marker);
        log($"Untouchable TAE patch: near-warp marker {SpeedFogIds.UntouchableNearWarpMarkerSpEffect} on animation {BURST_ANIMATION} at {marker.StartTime:F4} s (after its hit)");
        return 1;
    }

    /// <summary>The little-endian int32 at <paramref name="offset"/> of an
    /// event's parameters, or -1 when they are shorter.</summary>
    private static int IntAt(TAE.Event e, int offset)
    {
        var p = e.GetParameterBytes(false);
        return p.Length >= offset + 4 ? BitConverter.ToInt32(p, offset) : -1;
    }

    /// <summary>
    /// Retargets BEAM_EVENT_COUNT of animation 3004's bullet events to
    /// SpeedFogIds.UntouchableBeamJudge, spread evenly by start time, and
    /// the event halfway between each consecutive pair of those to
    /// SpeedFogIds.UntouchableFlameJudge. Returns the number of events
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
        if (layouts.Any(p => BitConverter.ToInt32(p, JUDGE_OFFSET) is var j
                             && (j == SpeedFogIds.UntouchableBeamJudge || j == SpeedFogIds.UntouchableFlameJudge)))
        {
            log($"  Animation {BEAM_ANIMATION}: already carries judge {SpeedFogIds.UntouchableBeamJudge} or {SpeedFogIds.UntouchableFlameJudge}, skipping");
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
        var beamIndices = new SortedSet<int>();
        for (int i = 0; i < BEAM_EVENT_COUNT; i++)
        {
            int index = BEAM_EVENT_COUNT == 1
                ? 0
                : (int)Math.Round((double)i * (n - 1) / (BEAM_EVENT_COUNT - 1), MidpointRounding.AwayFromZero);
            beamIndices.Add(index);
        }
        // A flame event halfway between consecutive beam events, when one
        // separates them.
        var flameIndices = new SortedSet<int>();
        var beams = beamIndices.ToList();
        for (int i = 1; i < beams.Count; i++)
        {
            int index = (beams[i - 1] + beams[i]) / 2;
            if (!beamIndices.Contains(index))
                flameIndices.Add(index);
        }
        foreach (var (indices, judge) in new[] { (beamIndices, SpeedFogIds.UntouchableBeamJudge), (flameIndices, SpeedFogIds.UntouchableFlameJudge) })
        {
            foreach (var index in indices)
            {
                var bytes = (byte[])layouts[index].Clone();
                BitConverter.GetBytes(judge).CopyTo(bytes, JUDGE_OFFSET);
                bullets[index].SetParameterBytes(tae.BigEndian, bytes);
            }
        }
        log($"Untouchable TAE patch: retargeted {beamIndices.Count} bullet event(s) of animation {BEAM_ANIMATION} to judge {SpeedFogIds.UntouchableBeamJudge} (beam) and {flameIndices.Count} to judge {SpeedFogIds.UntouchableFlameJudge} (flame nova)");
        return beamIndices.Count + flameIndices.Count;
    }
}
