using BenchmarkDotNet.Attributes;
using WolfMark.Core.Notes;
using WolfMark.Core.Text;

namespace WolfMark.Benchmarks;

/// <summary>Run once per file by the directory scan and the indexer: with 10 000 notes every microsecond is 10 ms.</summary>
[MemoryDiagnoser]
public class NoteNameBenchmarks
{
    private readonly byte[] _content = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("A paragraph of an ordinary note. ", 120)));

    [Benchmark]
    public bool ParsePath() => NotePath.TryParse("2026/11/17/praca_140312-2.md", out _);

    [Benchmark]
    public bool ParseConflictCopy() => NoteName.TryParse("praca_1403-MYLAPTOP.md", out _);

    /// <summary>The content hash of a 4 kB note (change detection, echo suppression, conditional save).</summary>
    [Benchmark]
    public string HashNote() => ContentHash.Compute(_content);
}
