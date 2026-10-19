using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WolfMark.Core.Diagnostics;

public enum EditorPhase
{
    /// <summary>Key down until the first rendered frame after the text changed – the latency the user feels.</summary>
    KeyToFrame,
    TextChanged,
    ReadText,
    UndoRecord,
    TextEditedHandler,
    SelectionChanged,
    CaretOverlay,
    LexerUpdate,
    StyleQueueDelay,
    StyleBatch,
    /// <summary>Within <see cref="StyleBatch"/>: setting the formats.</summary>
    StyleFormats,
    /// <summary>Within <see cref="StyleBatch"/>: <c>ApplyDisplayUpdates</c> (RichEdit's layout of the batch).</summary>
    StyleDisplay,
    ImageLayout,
    LineNumbers,
    LoadFormatted,
}

/// <summary>
/// Opt-in timing of the editor's hot paths (<c>--trace-editor</c>, docs/adr/0001-editor.md "Merania").
/// Disabled while <see cref="Current"/> is null: call sites cost one null check. Thread-safe – the UI thread and
/// the background lexer record; <see cref="Report"/> runs on a background thread.
/// </summary>
public sealed class EditorTrace
{
    private const int MaxSamples = 20_000; // per phase; older samples go first
    private const int MaxSlow = 200;

    private static readonly int PhaseCount = Enum.GetValues<EditorPhase>().Length;

    private readonly Lock _lock = new();
    private readonly List<Sample>[] _samples;
    private readonly Queue<SlowEvent> _slow = new();

    public EditorTrace()
    {
        _samples = new List<Sample>[PhaseCount];
        for (var i = 0; i < PhaseCount; i++)
        {
            _samples[i] = [];
        }
    }

    public static EditorTrace? Current { get; set; }

    /// <summary>Calls at least this long are also listed one by one, with their context.</summary>
    public double SlowMs { get; init; } = 50;

    /// <summary>Times a block: <c>using var _ = EditorTrace.Current?.Measure(EditorPhase.X);</c></summary>
    /// <param name="context">What was going on – evaluated only for a slow call.</param>
    public Scope Measure(EditorPhase phase, Func<string>? context = null) => new(this, phase, Stopwatch.GetTimestamp(), context);

    /// <param name="count">Work done in the phase (lines styled, layout queries) – reported as an average.</param>
    /// <param name="context">What was going on – evaluated only for a slow call.</param>
    public void Record(EditorPhase phase, long elapsedTicks, int count = 0, Func<string>? context = null)
    {
        var ms = elapsedTicks * 1000.0 / Stopwatch.Frequency;
        var detail = ms >= SlowMs ? context?.Invoke() : null;
        lock (_lock)
        {
            var list = _samples[(int)phase];
            if (list.Count == MaxSamples)
            {
                list.RemoveRange(0, MaxSamples / 4);
            }

            list.Add(new Sample(elapsedTicks, count));
            if (ms >= SlowMs)
            {
                if (_slow.Count == MaxSlow)
                {
                    _slow.Dequeue();
                }

                _slow.Enqueue(new SlowEvent(DateTime.Now, phase, ms, count, detail));
            }
        }
    }

    public void RecordSince(EditorPhase phase, long startTimestamp, int count = 0, Func<string>? context = null) =>
        Record(phase, Stopwatch.GetTimestamp() - startTimestamp, count, context);

    /// <summary>The latest calls of at least <see cref="SlowMs"/>, oldest first.</summary>
    public IReadOnlyList<SlowEvent> SlowEvents()
    {
        lock (_lock)
        {
            return [.. _slow];
        }
    }

    public PhaseStats? Stats(EditorPhase phase)
    {
        Sample[] samples;
        lock (_lock)
        {
            samples = [.. _samples[(int)phase]];
        }

        if (samples.Length == 0)
        {
            return null;
        }

        var ms = samples.Select(s => s.Ticks * 1000.0 / Stopwatch.Frequency).Order().ToArray();
        return new PhaseStats(
            phase,
            ms.Length,
            ms.Average(),
            Percentile(ms, 0.50),
            Percentile(ms, 0.95),
            Percentile(ms, 0.99),
            ms[^1],
            samples.Average(s => s.Count));
    }

    public void Reset()
    {
        lock (_lock)
        {
            foreach (var list in _samples)
            {
                list.Clear();
            }

            _slow.Clear();
        }
    }

    /// <summary>A plain-text table of every phase with samples.</summary>
    public string Report()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"WolfMark editor trace {DateTime.Now:yyyy-MM-dd HH:mm:ss} (ms; count = lines/queries per call)");
        sb.AppendLine(inv, $"{"phase",-18}{"n",8}{"mean",9}{"p50",9}{"p95",9}{"p99",9}{"max",9}{"count",9}");
        foreach (var phase in Enum.GetValues<EditorPhase>())
        {
            if (Stats(phase) is { } s)
            {
                sb.AppendLine(inv, $"{phase,-18}{s.Samples,8}{s.Mean,9:0.00}{s.P50,9:0.00}{s.P95,9:0.00}{s.P99,9:0.00}{s.Max,9:0.00}{s.AverageCount,9:0.0}");
            }
        }

        var slow = SlowEvents();
        if (slow.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(inv, $"Slow calls (>= {SlowMs:0} ms), latest {slow.Count}:");
            foreach (var e in slow)
            {
                sb.AppendLine(inv, $"{e.At:HH:mm:ss.fff}  {e.Phase,-16}{e.Ms,9:0.0} ms  count {e.Count,-5} {e.Context}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Nearest-rank percentile of sorted values.</summary>
    internal static double Percentile(double[] sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private readonly record struct Sample(long Ticks, int Count);

    public readonly struct Scope(EditorTrace trace, EditorPhase phase, long start, Func<string>? context) : IDisposable
    {
        public void Dispose() => trace.RecordSince(phase, start, context: context);
    }
}

public sealed record SlowEvent(DateTime At, EditorPhase Phase, double Ms, int Count, string? Context);

public sealed record PhaseStats(EditorPhase Phase, int Samples, double Mean, double P50, double P95, double P99, double Max, double AverageCount);
