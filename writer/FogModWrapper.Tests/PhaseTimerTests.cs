using System;
using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class PhaseTimerTests
{
    static (PhaseTimer Timer, Action<double> Advance) MakeTimer()
    {
        var now = TimeSpan.Zero;
        var timer = new PhaseTimer(() => now);
        return (timer, seconds => now += TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void RecordsPhaseDurationsInOrder()
    {
        var (timer, advance) = MakeTimer();

        timer.Phase("Load inputs");
        advance(2.0);
        timer.Phase("Write fogmod");
        advance(3.0);
        var total = timer.Stop();

        Assert.Equal(5.0, total, precision: 3);
        Assert.Collection(timer.Phases,
            p => { Assert.Equal("Load inputs", p.Name); Assert.Equal(2.0, p.Seconds, precision: 3); },
            p => { Assert.Equal("Write fogmod", p.Name); Assert.Equal(3.0, p.Seconds, precision: 3); });
    }

    [Fact]
    public void StopWithoutPhasesReturnsTotalAndKeepsPhasesEmpty()
    {
        var (timer, advance) = MakeTimer();
        advance(1.5);

        var total = timer.Stop();

        Assert.Equal(1.5, total, precision: 3);
        Assert.Empty(timer.Phases);
    }

    [Fact]
    public void FormatSummaryShowsSecondsAndPercentages()
    {
        var (timer, advance) = MakeTimer();
        timer.Phase("Load inputs");
        advance(2.0);
        timer.Phase("Write fogmod");
        advance(8.0);
        timer.Stop();

        var summary = timer.FormatSummary();

        Assert.Contains("Load inputs", summary);
        Assert.Contains("2.00s", summary);
        Assert.Contains("(20.0%)", summary);
        Assert.Contains("8.00s", summary);
        Assert.Contains("(80.0%)", summary);
    }

    [Fact]
    public void FormatSummaryUsesInvariantDecimalSeparator()
    {
        // The wrappers run under Wine where the locale may be French;
        // the summary must not switch to "2,00s".
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("fr-FR");
            var (timer, advance) = MakeTimer();
            timer.Phase("Load inputs");
            advance(2.0);
            timer.Stop();

            Assert.Contains("2.00s", timer.FormatSummary());
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void DefaultClockMeasuresRealTime()
    {
        var timer = new PhaseTimer();
        timer.Phase("only");
        var total = timer.Stop();

        Assert.True(total >= 0);
        Assert.Single(timer.Phases);
        Assert.True(timer.Phases[0].Seconds >= 0);
    }
}
