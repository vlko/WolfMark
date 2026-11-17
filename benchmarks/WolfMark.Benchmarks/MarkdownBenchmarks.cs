using BenchmarkDotNet.Attributes;
using WolfMark.Core.Markdown;

namespace WolfMark.Benchmarks;

/// <summary>
/// The editor's hot paths (SPEC §6, M4-02): lexing one line must stay far below 1 ms, and a keystroke in a long
/// note must re-lex only the lines it touches.
/// </summary>
[MemoryDiagnoser]
public class MarkdownBenchmarks
{
    private const string Line = "- [ ] task with **bold**, *italic*, `code`, [link](https://example.com) and #tag";

    private readonly List<MarkdownToken> _tokens = [];
    private string _note = string.Empty;
    private MarkdownDocumentModel _model = new();
    private int _middle;

    /// <summary>Lines in the note: a long day and a very long one.</summary>
    [Params(500, 5000)]
    public int Lines { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _note = string.Join('\n', Enumerable.Range(0, Lines).Select(i => i % 25 == 0 ? $"## Section {i}" : Line));
        _model = new MarkdownDocumentModel();
        _model.Update(_note);
        _middle = _model.GetLineStart(Lines / 2);
    }

    /// <summary>M4-02: "benchmark &lt; 1 ms per line".</summary>
    [Benchmark]
    public LineState LexOneLine()
    {
        _tokens.Clear();
        return MarkdownLineLexer.Tokenize(Line, default, _tokens);
    }

    /// <summary>Opening a note: the whole text into a new model.</summary>
    [Benchmark]
    public int OpenNote()
    {
        var model = new MarkdownDocumentModel();
        return model.Update(_note).LineCount;
    }

    /// <summary>A keystroke in the middle of the note: one character in, the same one out.</summary>
    [Benchmark]
    public int TypeOneCharacter()
    {
        var typed = _model.Replace(_middle, 0, "x");
        var removed = _model.Replace(_middle, 1, string.Empty);
        return typed.LineCount + removed.LineCount;
    }
}
