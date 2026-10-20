using System.Diagnostics;
using WolfMark.Core.Diagnostics;

namespace WolfMark.Core.Tests;

public sealed class EditorTraceTests
{
    private static long Ms(double ms) => (long)(ms * Stopwatch.Frequency / 1000);

    [Fact]
    public void Percentiles_use_nearest_rank()
    {
        var trace = new EditorTrace();
        for (var i = 1; i <= 100; i++)
        {
            trace.Record(EditorPhase.TextChanged, Ms(i), count: 2);
        }

        var s = trace.Stats(EditorPhase.TextChanged)!;
        Assert.Equal(100, s.Samples);
        Assert.Equal(50, s.P50, 3);
        Assert.Equal(95, s.P95, 3);
        Assert.Equal(99, s.P99, 3);
        Assert.Equal(100, s.Max, 3);
        Assert.Equal(50.5, s.Mean, 3);
        Assert.Equal(2, s.AverageCount);
    }

    [Fact]
    public void Report_lists_only_phases_with_samples_and_reset_clears()
    {
        var trace = new EditorTrace();
        using (trace.Measure(EditorPhase.StyleBatch))
        {
        }

        var report = trace.Report();
        Assert.Contains("StyleBatch", report, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumbers", report, StringComparison.Ordinal);

        trace.Reset();
        Assert.Null(trace.Stats(EditorPhase.StyleBatch));
    }

    [Fact]
    public void Slow_calls_are_listed_with_lazy_context()
    {
        var trace = new EditorTrace { SlowMs = 50 };
        var asked = 0;
        trace.Record(EditorPhase.StyleBatch, Ms(10), 3, () => { asked++; return "fast"; });
        trace.Record(EditorPhase.StyleBatch, Ms(80), 150, () => { asked++; return "mode switch"; });

        var slow = Assert.Single(trace.SlowEvents());
        Assert.Equal(1, asked);
        Assert.Equal(150, slow.Count);
        Assert.Equal("mode switch", slow.Context);
        Assert.Contains("mode switch", trace.Report(), StringComparison.Ordinal);
    }

    [Fact]
    public void Marks_are_listed_since_a_timestamp()
    {
        var trace = new EditorTrace();
        trace.Mark("before");
        var since = Stopwatch.GetTimestamp();
        trace.Mark("history reload");
        var mark = Assert.Single(trace.MarksSince(since));
        Assert.StartsWith("history reload +", mark, StringComparison.Ordinal);
    }
}
