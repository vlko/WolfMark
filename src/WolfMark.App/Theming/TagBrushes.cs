using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using WolfMark.Core.Calendar;
using WolfMark.Core.Tags;
using WolfMark.Storage.Tags;

namespace WolfMark.App.Theming;

/// <summary>Tag colours as brushes, cached per tag; diagonal multi-tag fills for calendar cells.</summary>
internal sealed class TagBrushes(TagStore store)
{
    private readonly Dictionary<string, SolidColorBrush> _cache = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public TagColor ColorOf(string? tag) => store.GetColor(tag);

    public SolidColorBrush BrushOf(string? tag)
    {
        var key = tag ?? string.Empty;
        if (!_cache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Palette.ToColor(store.GetColor(tag)));
            _cache[key] = brush;
        }

        return brush;
    }

    /// <summary>Re-reads colours (tags.json changed); existing brushes update in place.</summary>
    public void Refresh()
    {
        foreach (var (tag, brush) in _cache)
        {
            brush.Color = Palette.ToColor(store.GetColor(tag.Length == 0 ? null : tag));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fill for a day: one colour, or diagonal bands proportional to the note count per tag
    /// (SPEC §5.2). Untagged notes use the neutral colour.
    /// </summary>
    /// <param name="soft">The week band's softer colours (<see cref="Palette.CalendarTint"/>); the month grid shows the full ones.</param>
    public Brush DayFill(IReadOnlyList<TagCount> tags, Brush empty, bool soft)
    {
        if (tags.Count == 0)
        {
            return empty;
        }

        // The week band is softer than the tag colour elsewhere (Palette.CalendarTint): a week of full colours was loud.
        Color ColorOf(string tag) => soft ? Palette.CalendarTint(store.GetColor(tag.Length == 0 ? null : tag)) : Palette.ToColor(store.GetColor(tag.Length == 0 ? null : tag));
        if (tags.Count == 1)
        {
            return soft ? new SolidColorBrush(ColorOf(tags[0].Tag)) : BrushOf(tags[0].Tag.Length == 0 ? null : tags[0].Tag);
        }

        var total = (double)tags.Sum(t => t.Count);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var offset = 0.0;
        foreach (var t in tags.Take(4))
        {
            var color = ColorOf(t.Tag);
            var next = offset + (t.Count / total);
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = offset });
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = next });
            offset = next;
        }

        return brush;
    }

    /// <summary>Diagonal hatching used for "today" (reference design).</summary>
    public static Brush Hatch(Color background, Color stripe) => new LinearGradientBrush
    {
        MappingMode = BrushMappingMode.Absolute,
        StartPoint = new Point(0, 0),
        EndPoint = new Point(6, 6),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops =
        {
            new GradientStop { Color = background, Offset = 0 },
            new GradientStop { Color = background, Offset = 0.6 },
            new GradientStop { Color = stripe, Offset = 0.6 },
            new GradientStop { Color = stripe, Offset = 1 },
        },
    };

    public static Color DominantColor(TagStore store, IReadOnlyList<TagCount> tags) =>
        tags.Count == 0 ? default : Palette.ToColor(store.GetColor(tags[0].Tag.Length == 0 ? null : tags[0].Tag));
}
