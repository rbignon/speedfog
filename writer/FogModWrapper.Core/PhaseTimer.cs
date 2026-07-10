using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FogModWrapper;

/// <summary>
/// Tracks elapsed time per named phase, mirroring the Python StepTimer
/// (speedfog/main.py). Standard way to time pipeline phases; do not add
/// ad-hoc Stopwatches in individual injectors.
/// </summary>
public class PhaseTimer
{
    private readonly Func<TimeSpan> clock;
    private readonly TimeSpan start;
    private readonly List<(string Name, double Seconds)> phases = new();
    private string? currentName;
    private TimeSpan currentStart;

    /// <param name="clock">
    /// Monotonic time source; defaults to a real Stopwatch. Injectable for tests.
    /// </param>
    public PhaseTimer(Func<TimeSpan>? clock = null)
    {
        if (clock == null)
        {
            var sw = Stopwatch.StartNew();
            clock = () => sw.Elapsed;
        }
        this.clock = clock;
        start = clock();
    }

    public IReadOnlyList<(string Name, double Seconds)> Phases => phases;

    /// <summary>Start a new phase, closing the previous one if any.</summary>
    public void Phase(string name)
    {
        var now = clock();
        CloseCurrent(now);
        currentName = name;
        currentStart = now;
    }

    /// <summary>Stop the current phase and return total elapsed seconds.</summary>
    public double Stop()
    {
        var now = clock();
        CloseCurrent(now);
        return (now - start).TotalSeconds;
    }

    private void CloseCurrent(TimeSpan now)
    {
        if (currentName != null)
        {
            phases.Add((currentName, (now - currentStart).TotalSeconds));
            currentName = null;
        }
    }

    /// <summary>Format a per-phase timing summary (invariant culture).</summary>
    public string FormatSummary()
    {
        double total = 0;
        foreach (var (_, seconds) in phases) total += seconds;

        var sb = new StringBuilder();
        foreach (var (name, seconds) in phases)
        {
            var pct = total > 0 ? seconds / total * 100 : 0;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-25} {1,6:F2}s  ({2,4:F1}%)", name, seconds, pct));
        }
        return sb.ToString().TrimEnd('\n', '\r');
    }
}
