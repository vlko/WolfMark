namespace WolfMark.Core.Text;

/// <summary>
/// Minimal YAML front matter reader: <c>key: value</c>, inline lists <c>[a, b]</c> and block lists
/// (<c>- a</c>). Enough for <c>tags</c>, <c>source</c>, <c>session</c>; anything else is ignored.
/// </summary>
public sealed class FrontMatter
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public static FrontMatter Empty { get; } = new();

    /// <summary>Number of lines (including both '---' fences) occupied by front matter.</summary>
    public int LineCount { get; private set; }

    public bool IsPresent => LineCount > 0;

    public string? Get(string key) => _values.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

    public IReadOnlyList<string> GetList(string key) => _values.TryGetValue(key, out var v) ? v : [];

    public static FrontMatter Parse(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].TrimEnd() != "---")
        {
            return Empty;
        }

        var fm = new FrontMatter();
        string? listKey = null;
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i].TrimEnd();
            if (line is "---" or "...")
            {
                fm.LineCount = i + 1;
                return fm;
            }

            var trimmed = line.TrimStart();
            if (listKey is not null && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                fm.Add(listKey, Unquote(trimmed[2..]));
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            listKey = null;

            if (value.Length == 0)
            {
                listKey = key;
                fm._values.TryAdd(key, []);
            }
            else if (value.StartsWith('[') && value.EndsWith(']'))
            {
                fm._values.TryAdd(key, []);
                foreach (var item in value[1..^1].Split(','))
                {
                    var v = Unquote(item.Trim());
                    if (v.Length > 0)
                    {
                        fm.Add(key, v);
                    }
                }
            }
            else
            {
                fm.Add(key, Unquote(value));
            }
        }

        // Unterminated front matter is not front matter.
        return Empty;
    }

    private void Add(string key, string value)
    {
        if (!_values.TryGetValue(key, out var list))
        {
            list = [];
            _values[key] = list;
        }

        list.Add(value);
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
