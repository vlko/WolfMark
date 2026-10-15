using System.Text;

namespace Zapisnik.Core.Text;

/// <summary>Splits note text into paragraph-based chunks for embeddings (SPEC §4.2).</summary>
public static class TextChunker
{
    public const int DefaultMaxChars = 1500;

    public static IReadOnlyList<string> Chunk(string text, int maxChars = DefaultMaxChars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 50);
        var result = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in SplitParagraphs(text))
        {
            if (paragraph.Length > maxChars)
            {
                Flush();
                var pos = 0;
                while (pos < paragraph.Length)
                {
                    var take = Math.Min(maxChars, paragraph.Length - pos);
                    if (pos + take < paragraph.Length)
                    {
                        // Try not to cut words.
                        var space = paragraph.LastIndexOf(' ', pos + take - 1, take);
                        if (space > pos + (maxChars / 2))
                        {
                            take = space - pos;
                        }
                    }

                    var piece = paragraph.Substring(pos, take).Trim();
                    if (piece.Length > 0)
                    {
                        result.Add(piece);
                    }

                    pos += take;
                }

                continue;
            }

            if (current.Length + paragraph.Length + 2 > maxChars)
            {
                Flush();
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        Flush();
        return result;

        void Flush()
        {
            if (current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
        }
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        var sb = new StringBuilder();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString().Trim();
                    sb.Clear();
                }

                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(line);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString().Trim();
        }
    }
}
