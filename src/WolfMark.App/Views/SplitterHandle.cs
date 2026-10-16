using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using WolfMark.App.Theming;

namespace WolfMark.App.Views;

/// <summary>Draggable divider between editor and history (SPEC §4.5).</summary>
internal sealed partial class SplitterHandle : Grid
{
    private double _startY;
    private double _startHeight;
    private bool _dragging;

    public SplitterHandle(Func<double> getHeight, Action<double> setHeight, Action committed)
    {
        Height = 10;
        Background = Palette.Transparent;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        Children.Add(new Rectangle { Height = 1, Fill = Palette.Divider, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 24, 0) });
        Children.Add(new Rectangle { Width = 36, Height = 4, RadiusX = 2, RadiusY = 2, Fill = Palette.SubtleFillHover, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        PointerPressed += (_, e) =>
        {
            _dragging = true;
            _startY = e.GetCurrentPoint(XamlRoot.Content).Position.Y;
            _startHeight = getHeight();
            CapturePointer(e.Pointer);
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (_dragging)
            {
                var y = e.GetCurrentPoint(XamlRoot.Content).Position.Y;
                setHeight(Math.Clamp(_startHeight - (y - _startY), 80, 900));
            }
        };
        PointerReleased += (_, e) => End(e);
        PointerCaptureLost += (_, e) => End(e);

        void End(PointerRoutedEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            ReleasePointerCapture(e.Pointer);
            committed();
        }
    }
}
