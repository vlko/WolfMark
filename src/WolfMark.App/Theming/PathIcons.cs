using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WolfMark.App.Theming;

/// <summary>Builds <see cref="PathIcon"/>s from the generated M/L/Q/Z path data (no XAML parser needed).</summary>
internal static class PathIcons
{
    public static PathIcon Create(string data) => new()
    {
        Data = Parse(data),
        Width = 16,
        Height = 16,
    };

    internal static PathGeometry Parse(string data)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        PathFigure? figure = null;
        var i = 0;
        while (i < data.Length)
        {
            var command = data[i++];
            switch (command)
            {
                case 'M':
                    figure = new PathFigure { StartPoint = ReadPoint(data, ref i), IsClosed = true, IsFilled = true };
                    geometry.Figures.Add(figure);
                    break;
                case 'L':
                    figure!.Segments.Add(new LineSegment { Point = ReadPoint(data, ref i) });
                    break;
                case 'Q':
                    var control = ReadPoint(data, ref i);
                    figure!.Segments.Add(new QuadraticBezierSegment { Point1 = control, Point2 = ReadPoint(data, ref i) });
                    break;
                case 'Z':
                    figure = null;
                    break;
            }
        }

        return geometry;
    }

    private static Point ReadPoint(string data, ref int i)
    {
        var x = ReadNumber(data, ref i);
        i++; // ','
        var y = ReadNumber(data, ref i);
        if (i < data.Length && data[i] == ' ')
        {
            i++;
        }

        return new Point(x, y);
    }

    private static double ReadNumber(string data, ref int i)
    {
        var start = i;
        while (i < data.Length && (char.IsAsciiDigit(data[i]) || data[i] is '.' or '-'))
        {
            i++;
        }

        return double.Parse(data.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
