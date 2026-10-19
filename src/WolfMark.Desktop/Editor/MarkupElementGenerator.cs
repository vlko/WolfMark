using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using WolfMark.Core.Markdown;

namespace WolfMark.Desktop.Editor;

/// <summary>
/// Formatted mode (ADR 0001: the document stays the plain Markdown text): delimiters and link targets become
/// zero-width elements, and an image link becomes its picture – one element, so the caret sits before or
/// after it, never inside the markup.
/// </summary>
internal sealed class MarkupElementGenerator(MarkdownTextEditor owner) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.Document.GetLineByOffset(startOffset);
        foreach (var segment in owner.SegmentsOf(line))
        {
            if (line.Offset + segment.Start >= startOffset)
            {
                return line.Offset + segment.Start;
            }
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.Document.GetLineByOffset(offset);
        foreach (var segment in owner.SegmentsOf(line))
        {
            if (line.Offset + segment.Start != offset)
            {
                continue;
            }

            if (segment.ImagePath is { } path && owner.PreviewOf(path) is { } preview)
            {
                return new InlineObjectElement(segment.Length, PreviewImage(path, preview));
            }

            return segment.ImagePath is null ? new InlineObjectElement(segment.Length, new Border { Width = 0, Height = 0 }) : null;
        }

        return null;
    }

    private Image PreviewImage(string path, ImagePreview preview)
    {
        var size = owner.PreviewSize(preview);
        var image = new Image
        {
            Source = preview.Bitmap,
            Width = size.Width,
            Height = size.Height,
            Stretch = Stretch.Uniform,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Avalonia.Thickness(0, 2, 0, 4),
        };
        ToolTip.SetTip(image, owner.OpenImageTip);
        image.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            owner.OpenFile(path);
        };
        return image;
    }
}

/// <summary>A hidden range (delimiters, link target) or an image link within a line.</summary>
internal readonly record struct MarkupSegment(int Start, int Length, string? ImagePath);

internal static class MarkupSegments
{
    /// <summary>
    /// Sorted, non-overlapping segments of a line in formatted mode. Images win over the delimiters inside them;
    /// touching hidden tokens are merged so the caret makes one step over them.
    /// </summary>
    public static List<MarkupSegment> Build(string text, MarkdownToken[] tokens, Func<string, string?> resolveImage)
    {
        var result = new List<MarkupSegment>();
        var images = new List<(int Start, int End, string Path)>();
        foreach (var t in tokens)
        {
            if (t.Kind == MarkdownTokenKind.Image && t.End <= text.Length
                && MarkdownImageLink.TryParse(text.AsSpan(t.Start, t.Length), out _, out var url)
                && resolveImage(url) is { } path)
            {
                images.Add((t.Start, t.End, path));
            }
        }

        var hidden = tokens
            .Where(t => t.Kind is MarkdownTokenKind.Syntax or MarkdownTokenKind.LinkUrl && t.Length > 0 && t.End <= text.Length)
            .Where(t => !images.Any(i => t.Start < i.End && t.End > i.Start))
            .Select(t => (t.Start, t.End))
            .OrderBy(t => t.Start);
        var merged = new List<(int Start, int End)>();
        foreach (var (start, end) in hidden)
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(end, merged[^1].End));
            }
            else
            {
                merged.Add((start, end));
            }
        }

        result.AddRange(merged.Select(m => new MarkupSegment(m.Start, m.End - m.Start, null)));
        result.AddRange(images.Select(i => new MarkupSegment(i.Start, i.End - i.Start, i.Path)));
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }
}
