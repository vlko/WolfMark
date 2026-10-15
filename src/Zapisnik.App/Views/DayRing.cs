using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Zapisnik.App.Theming;
using Zapisnik.Core.Calendar;

namespace Zapisnik.App.Views;

/// <summary>
/// Day circle of the week strip: tag colours as ring segments proportional to note counts,
/// today filled (reference: Finalist week bar).
/// </summary>
internal sealed partial class DayRing : Grid
{
    private const double StrokeWidth = 3.5;
    private readonly Ellipse _fill = new();
    private readonly Canvas _arcs = new();
    private readonly TextBlock _number;
    private readonly double _size;

    public DayRing(double size = 40)
    {
        _size = size;
        Width = size;
        Height = size;
        _number = new TextBlock
        {
            FontFamily = Typo.Display,
            FontSize = size * 0.4,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_fill);
        Children.Add(_arcs);
        Children.Add(_number);
    }

    public void Update(int dayNumber, CalendarDay day, bool isToday, bool isSelected, TagBrushes brushes)
    {
        _number.Text = dayNumber.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _fill.Fill = isToday ? Palette.TodayFill : isSelected ? Palette.SubtleFillHover : Palette.Transparent;
        _number.Foreground = isToday ? Palette.TodayInk : day.HasNotes ? Palette.Ink : Palette.InkTertiary;
        _arcs.Children.Clear();

        var radius = (_size - StrokeWidth) / 2;
        var center = new Point(_size / 2, _size / 2);
        if (day.Tags.Count == 0)
        {
            _arcs.Children.Add(new Ellipse
            {
                Width = _size - StrokeWidth,
                Height = _size - StrokeWidth,
                Stroke = Palette.Divider,
                StrokeThickness = 1,
                Margin = new Thickness(StrokeWidth / 2),
            });
            return;
        }

        if (day.Tags.Count == 1)
        {
            _arcs.Children.Add(new Ellipse
            {
                Width = _size - StrokeWidth,
                Height = _size - StrokeWidth,
                Stroke = brushes.BrushOf(day.Tags[0].Tag.Length == 0 ? null : day.Tags[0].Tag),
                StrokeThickness = StrokeWidth,
                Margin = new Thickness(StrokeWidth / 2),
            });
            return;
        }

        var total = (double)day.Tags.Sum(t => t.Count);
        var start = -90.0;
        const double Gap = 6;
        foreach (var t in day.Tags)
        {
            var sweep = (t.Count / total * 360) - Gap;
            if (sweep > 0.5)
            {
                _arcs.Children.Add(Arc(center, radius, start + (Gap / 2), sweep, brushes.BrushOf(t.Tag.Length == 0 ? null : t.Tag)));
            }

            start += t.Count / total * 360;
        }
    }

    private static Microsoft.UI.Xaml.Shapes.Path Arc(Point center, double radius, double startDeg, double sweepDeg, Brush stroke)
    {
        static Point On(Point c, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(c.X + (r * Math.Cos(rad)), c.Y + (r * Math.Sin(rad)));
        }

        var figure = new PathFigure { StartPoint = On(center, radius, startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = On(center, radius, startDeg + sweepDeg),
            Size = new Size(radius, radius),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }
}
