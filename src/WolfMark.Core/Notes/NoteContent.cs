using System.Text;
using WolfMark.Core.Markdown;
using WolfMark.Core.Tags;
using WolfMark.Core.Text;

namespace WolfMark.Core.Notes;

/// <summary>Metadata extracted from note content for the index.</summary>
public sealed record NoteContentInfo(
    string Title,
    string Preview,
    IReadOnlyList<string> Tags,
    int WordCount,
    int OpenTasks,
    int DoneTasks,
    string? Source,
    string? SessionId,
    string Body);

/// <summary>Extracts title, preview, tags (#tag + front matter), word and task counts (SPEC §3.4, §4.2).</summary>
public static class NoteContentAnalyzer
{
    public const int PreviewLength = 200;
    public const int TitleLength = 120;

    public static NoteContentInfo Analyze(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var frontMatter = FrontMatter.Parse(lines);

        var tags = new List<string>();
        var seenTags = new HashSet<string>(TagName.Comparer);
        foreach (var t in frontMatter.GetList("tags"))
        {
            AddTag(t);
        }

        if (frontMatter.Get("tag") is { } single)
        {
            AddTag(single);
        }

        string? title = null;
        var preview = new StringBuilder();
        var body = new StringBuilder(content.Length);
        var words = 0;
        var open = 0;
        var done = 0;

        var tokens = new List<MarkdownToken>();
        var state = frontMatter.IsPresent ? LineState.Normal : LineState.DocumentStart;
        for (var i = frontMatter.LineCount; i < lines.Length; i++)
        {
            var line = lines[i];
            tokens.Clear();
            var stateIn = state;
            state = MarkdownLineLexer.Tokenize(line, stateIn, tokens);

            var isCode = stateIn.Kind == LineStateKind.FencedCode || tokens is [{ Kind: MarkdownTokenKind.CodeFence }, ..];
            var plain = isCode ? line.Trim() : StripMarkdown(line, tokens);

            body.Append(line).Append('\n');
            words += CountWords(plain);

            if (isCode)
            {
                continue;
            }

            foreach (var token in tokens)
            {
                switch (token.Kind)
                {
                    case MarkdownTokenKind.TaskOpen:
                        open++;
                        break;
                    case MarkdownTokenKind.TaskDone:
                        done++;
                        break;
                    case MarkdownTokenKind.Hashtag:
                        AddTag(line.Substring(token.Start + 1, token.Length - 1));
                        break;
                }
            }

            if (plain.Length == 0)
            {
                continue;
            }

            if (title is null)
            {
                title = Truncate(plain, TitleLength);
                continue;
            }

            if (preview.Length < PreviewLength)
            {
                if (preview.Length > 0)
                {
                    preview.Append(' ');
                }

                preview.Append(plain);
            }
        }

        return new NoteContentInfo(
            title ?? string.Empty,
            Truncate(preview.ToString(), PreviewLength),
            tags,
            words,
            open,
            done,
            frontMatter.Get("source"),
            frontMatter.Get("session"),
            body.ToString());

        void AddTag(string raw)
        {
            if (TagName.TryNormalize(raw, out var tag) && seenTags.Add(tag))
            {
                tags.Add(tag);
            }
        }
    }

    /// <summary>Removes syntax tokens and collapses whitespace.</summary>
    public static string StripMarkdown(string line, IReadOnlyList<MarkdownToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return line.Trim();
        }

        Span<bool> hidden = line.Length <= 512 ? stackalloc bool[line.Length] : new bool[line.Length];
        foreach (var t in tokens)
        {
            if (t.Kind is MarkdownTokenKind.Syntax or MarkdownTokenKind.ListMarker or MarkdownTokenKind.TaskOpen
                or MarkdownTokenKind.TaskDone or MarkdownTokenKind.LinkUrl or MarkdownTokenKind.HorizontalRule
                or MarkdownTokenKind.FrontMatter)
            {
                hidden.Slice(t.Start, Math.Min(t.Length, line.Length - t.Start)).Fill(true);
            }
        }

        var sb = new StringBuilder(line.Length);
        var lastWasSpace = true;
        for (var i = 0; i < line.Length; i++)
        {
            if (hidden[i])
            {
                continue;
            }

            var c = line[i];
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
        }

        return count;
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
        {
            return s;
        }

        var cut = s.LastIndexOf(' ', max - 1, Math.Min(max, 40));
        return (cut > 0 ? s[..cut] : s[..(max - 1)]) + "…";
    }
}
