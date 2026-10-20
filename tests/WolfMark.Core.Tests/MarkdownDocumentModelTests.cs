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
        Assert.Equal((50, 1), (update.FirstLine, update.LineCount));
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
    public void Map_line_follows_inserted_lines()
    {
        var model = new MarkdownDocumentModel();
        model.Update("a\nb\nc\nd");
        var update = model.Update("a\nx\ny\nb\nc\nd");
        Assert.Equal(0, update.MapLine(0));
        Assert.Equal(3, update.MapLine(1));
        Assert.Equal(5, update.MapLine(3));

        var replace = model.Update("a\nZ\nb\nc\nd");
        Assert.Equal(-1, replace.MapLine(1));
        Assert.Equal(2, replace.MapLine(3));
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

        // Best of five: a shared CI runner can stall any single run; the minimum measures the code.
        var best = long.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            lines[2500] = $"# changed {run}";
            var changed = string.Join('\n', lines);
            var sw = Stopwatch.StartNew();
            model.Update(changed);
            sw.Stop();
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }

        // Full split + compare + retokenize one line on 5 000 lines.
        Assert.True(best < 50, $"took {best} ms");
    }

    [Fact]
    public void Random_edits_match_a_fresh_model()
    {
        var rng = new Random(7);
        string[] pieces = ["# h", "text **b**", "```", "code", "", "- [ ] task", "> quote", "\r", "\n", "\n\n", "x"];
        var text = string.Join('\n', Enumerable.Range(0, 40).Select(_ => pieces[rng.Next(pieces.Length)]));
        var model = new MarkdownDocumentModel();
        model.Update(text);
        for (var round = 0; round < 500; round++)
        {
            var start = rng.Next(text.Length + 1);
            var length = rng.Next(Math.Min(12, text.Length - start) + 1);
            text = text.Remove(start, length).Insert(start, pieces[rng.Next(pieces.Length)]);
            model.Update(text);

            var fresh = new MarkdownDocumentModel();
            fresh.Update(text);
            Assert.Equal(fresh.LineCount, model.LineCount);
            for (var i = 0; i < fresh.LineCount; i++)
            {
                Assert.Equal(fresh.GetLineText(i), model.GetLineText(i));
                Assert.Equal(fresh.GetLineStart(i), model.GetLineStart(i));
                Assert.Equal(fresh.GetTokens(i), model.GetTokens(i));
            }
        }
    }

    [Fact]
    public void Replace_edits_match_a_fresh_model()
    {
        var rng = new Random(11);
        string[] pieces = ["# h", "text **b**", "```", "code", "", "- [ ] task", "> q", "\n", "\n\n", "x", "\r\n"];
        var text = string.Join('\n', Enumerable.Range(0, 30).Select(_ => pieces[rng.Next(pieces.Length)])).Replace("\r", string.Empty);
        var model = new MarkdownDocumentModel();
        model.Update(text);
        for (var round = 0; round < 500; round++)
        {
            var start = rng.Next(text.Length + 1);
            var length = rng.Next(Math.Min(15, text.Length - start) + 1);
            var insert = pieces[rng.Next(pieces.Length)];
            text = text.Remove(start, length).Insert(start, insert.Replace("\r", string.Empty));
            model.Replace(start, length, insert);

            Assert.Equal(text, model.GetText());
            Assert.Equal(text.Length, model.Length);
            var fresh = new MarkdownDocumentModel();
            fresh.Update(text);
            Assert.Equal(fresh.LineCount, model.LineCount);
            for (var i = 0; i < fresh.LineCount; i++)
            {
                Assert.Equal(fresh.GetTokens(i), model.GetTokens(i));
                Assert.Equal(fresh.GetStateAtLineStart(i), model.GetStateAtLineStart(i));
            }
        }
    }

    [Fact]
    public void Replace_on_an_empty_model_sets_the_text()
    {
        var model = new MarkdownDocumentModel();
        model.Replace(0, 0, "a\nb");
        Assert.Equal(2, model.LineCount);
        Assert.Equal("a\nb", model.GetText());
    }
}
