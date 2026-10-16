using System.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownDocumentModelTests
{
    [Fact]
    public void Editing_one_line_retokenizes_only_that_line()
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"line {i} with **bold**").ToArray();
        var model = new MarkdownDocumentModel();
        var first = model.Update(string.Join('\n', lines));
        Assert.Equal(100, first.LineCount);

        lines[50] = "line 50 changed";
        var update = model.Update(string.Join('\n', lines));
        Assert.Equal(new MarkdownUpdate(50, 1), update);
        Assert.Equal(101, model.TokenizedLines);
    }

    [Fact]
    public void Opening_a_fence_retokenizes_until_state_converges()
    {
        var lines = new[] { "a", "b", "c", "```", "d", "e" };
        var model = new MarkdownDocumentModel();
        model.Update(string.Join('\n', lines));
        Assert.NotEqual(MarkdownTokenKind.CodeBlock, model.GetTokens(1) is [] ? default : model.GetTokens(1)[0].Kind);

        lines[0] = "```";
        var update = model.Update(string.Join('\n', lines));
        Assert.Equal(0, update.FirstLine);
        Assert.Equal(6, update.LineCount);
        Assert.Equal(MarkdownTokenKind.CodeBlock, model.GetTokens(1)[0].Kind);
        Assert.Equal(MarkdownTokenKind.CodeFence, model.GetTokens(3)[0].Kind);
    }

    [Fact]
    public void Inserting_lines_shifts_offsets()
    {
        var model = new MarkdownDocumentModel();
        model.Update("ab\ncd");
        model.Update("ab\nxx\ncd");
        Assert.Equal(3, model.LineCount);
        Assert.Equal(6, model.GetLineStart(2));
        Assert.Equal(2, model.GetLineAtOffset(7));
        Assert.Equal(1, model.GetLineAtOffset(3));
    }

    [Fact]
    public void Unchanged_text_does_nothing()
    {
        var model = new MarkdownDocumentModel();
        model.Update("a\nb");
        Assert.Equal(0, model.Update("a\nb").LineCount);
    }

    [Fact]
    public void Tokenizing_is_fast()
    {
        var text = string.Join('\n', Enumerable.Range(0, 5000).Select(i =>
            $"- [ ] task {i} with **bold**, *italic*, `code`, [link](https://example.com) and #tag"));
        var model = new MarkdownDocumentModel();
        model.Update(text); // warm-up / JIT
        var lines = text.Split('\n');
        lines[2500] = "# changed";
        var sw = Stopwatch.StartNew();
        model.Update(string.Join('\n', lines));
        sw.Stop();
        // Full split + compare + retokenize one line on 5 000 lines; generous bound for CI.
        Assert.True(sw.ElapsedMilliseconds < 50, $"took {sw.ElapsedMilliseconds} ms");
    }
}
