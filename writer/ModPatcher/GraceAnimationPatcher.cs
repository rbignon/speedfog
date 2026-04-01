using SoulsFormats;

namespace ModPatcher;

/// <summary>
/// Speeds up grace animations by injecting TAE event type 608 (AnimSpeedGradient)
/// into the player character's animation bundle (c0000.anibnd.dcx).
///
/// Patched animations:
///   - 63000: Grace sit (rest at grace). Also used as the HKX source for 60131 (kneeling arrival).
///   - 68000: Grace discovery (first activation of a grace).
///
/// The event type 608 has two float parameters (SpeedAtStart, SpeedAtEnd) followed
/// by 8 bytes of zero padding. A corresponding EventGroup is added for each new event.
/// </summary>
public static class GraceAnimationPatcher
{
    private const string ANIBND_PATH = "chr/c0000.anibnd.dcx";
    private const string TAE_NAME_SUFFIX = "a00.tae";
    private const int EVENT_TYPE_ANIM_SPEED = 608;

    private static readonly (long animId, float speed, float duration)[] Patches =
    {
        // Grace sit: 150% speed for 2 seconds
        (63000, 150f, 2.0f),
        // Grace discovery: ~467% speed for the full animation (~4.67 seconds)
        (68000, 4.667f, 4.6666665f),
    };

    /// <summary>
    /// Read c0000.anibnd.dcx from gameDir, patch grace animations, write to outputDir.
    /// Returns the number of animations patched, or 0 if the file wasn't found.
    /// </summary>
    public static int Patch(string gameDir, string outputDir)
    {
        var srcPath = Path.Combine(gameDir, ANIBND_PATH);
        if (!File.Exists(srcPath))
        {
            Console.WriteLine($"Warning: {ANIBND_PATH} not found in game dir, skipping grace animation patch");
            return 0;
        }

        var bnd = BND4.Read(srcPath);

        // Find a00.tae in the archive
        BinderFile? taeFile = null;
        foreach (var file in bnd.Files)
        {
            if (file.Name.EndsWith(TAE_NAME_SUFFIX, StringComparison.OrdinalIgnoreCase))
            {
                taeFile = file;
                break;
            }
        }

        if (taeFile == null)
        {
            Console.WriteLine($"Warning: {TAE_NAME_SUFFIX} not found in {ANIBND_PATH}, skipping grace animation patch");
            return 0;
        }

        var tae = TAE.Read(taeFile.Bytes);
        int patched = 0;

        foreach (var (animId, speed, duration) in Patches)
        {
            var anim = tae.Animations.Find(a => a.ID == animId);
            if (anim == null)
            {
                Console.WriteLine($"Warning: animation {animId} not found in {TAE_NAME_SUFFIX}");
                continue;
            }

            // Check if already patched (event type 608 present)
            if (anim.Events.Any(e => e.Type == EVENT_TYPE_ANIM_SPEED))
            {
                Console.WriteLine($"  Animation {animId}: already has speed event, skipping");
                continue;
            }

            // Create AnimSpeedGradient event (type 608)
            // Params: SpeedAtStart(f32), SpeedAtEnd(f32), assert0(s32), assert0(s32)
            var paramBytes = new byte[16];
            BitConverter.GetBytes(speed).CopyTo(paramBytes, 0);
            BitConverter.GetBytes(speed).CopyTo(paramBytes, 4);
            // bytes 8-15 stay zero (two s32 asserts)

            var speedEvent = new TAE.Event(0f, duration, EVENT_TYPE_ANIM_SPEED, 0, paramBytes, false);

            anim.Events.Add(speedEvent);

            // Associate event with a new EventGroup
            var group = new TAE.EventGroup(0);
            group.GroupData = new TAE.EventGroup.EventGroupDataStruct
            {
                DataType = TAE.EventGroup.EventGroupDataType.GroupData0,
            };
            speedEvent.Group = group;
            anim.EventGroups.Add(group);

            patched++;
        }

        if (patched > 0)
        {
            // Write modified TAE back into BND
            taeFile.Bytes = tae.Write();

            // Write patched BND to mod output
            var destPath = Path.Combine(outputDir, ANIBND_PATH);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            bnd.Write(destPath);

            Console.WriteLine($"Grace animation patch: sped up {patched} animation(s) in {ANIBND_PATH}");
        }

        return patched;
    }
}
