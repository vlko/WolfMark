using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;

namespace WolfMark.App.Views;

/// <summary>
/// Button showing the chosen monospaced font; opens a small picker with the installed monospaced fonts, each
/// drawn in itself, a filter box and a preview. A flyout, not a dialog: it opens from inside the settings
/// ContentDialog, and WinUI shows only one ContentDialog at a time.
/// </summary>
internal sealed class MonoFontPicker
{
    private const string Sample = "0Oo 1lIi {}[]() => != ===\nThe quick brown fox jumps over the lazy dog";

    private readonly Button _button;
    private readonly TextBlock _buttonText = Typo.Body(string.Empty, 13.5);
    private readonly TextBox _filter = new() { PlaceholderText = Strings.Get(R.MonoFont_Filter) };
    private readonly ListView _list = new() { Height = 260, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock _preview = new() { FontSize = 14, TextWrapping = TextWrapping.NoWrap, Text = Sample };
    private readonly TextBlock _status = Typo.Body(Strings.Get(R.MonoFont_Loading), 12.5, Palette.InkTertiary);
    private IReadOnlyList<string> _fonts = [];
    private bool _filling;

    public MonoFontPicker(string current)
    {
        Selected = current;
        _button = new Button { Content = _buttonText, MinWidth = 260, HorizontalContentAlignment = HorizontalAlignment.Left };
        _button.StyleButton(Palette.SubtleFill, Palette.Ink);
        ShowSelected();

        var preview = new Border
        {
            Child = _preview,
            Background = Palette.SubtleFill,
            CornerRadius = Radius.Control,
            Padding = new Thickness(10, 8, 10, 8),
        };
        var panel = Ui.Ui.VStack(8, _filter, _status, _list, preview);
        panel.Width = 340;
        _button.Flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Bottom };
        _button.Flyout.Opened += async (_, _) => await LoadAsync();

        _filter.TextChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) =>
        {
            if (!_filling && _list.SelectedItem is ListViewItem { Tag: string name })
            {
                Selected = name;
                ShowSelected();
            }
        };
        _list.DoubleTapped += (_, _) => _button.Flyout.Hide();
    }

    /// <summary>Font family chosen in the picker.</summary>
    public string Selected { get; private set; }

    public FrameworkElement Element => _button;

    public bool IsEnabled
    {
        get => _button.IsEnabled;
        set => _button.IsEnabled = value;
    }

    private async Task LoadAsync()
    {
        if (_fonts.Count == 0)
        {
            _fonts = await InstalledFonts.MonospacedAsync(); // enumerated on the thread pool
        }

        _status.Visibility = Visibility.Collapsed;
        Fill();
        _filter.Focus(FocusState.Programmatic);
    }

    private void Fill()
    {
        var query = _filter.Text.Trim();
        _filling = true;
        try
        {
            _list.Items.Clear();
            foreach (var name in _fonts.Where(f => f.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            {
                var item = new ListViewItem
                {
                    Tag = name,
                    Content = new TextBlock { Text = name, FontFamily = new FontFamily(name), FontSize = 14 },
                };
                _list.Items.Add(item);
                if (string.Equals(name, Selected, StringComparison.OrdinalIgnoreCase))
                {
                    _list.SelectedItem = item;
                }
            }
        }
        finally
        {
            _filling = false;
        }

        if (_list.SelectedItem is { } selected)
        {
            _list.ScrollIntoView(selected);
        }
    }

    private void ShowSelected()
    {
        _buttonText.Text = Selected;
        _buttonText.FontFamily = new FontFamily(Selected);
        _preview.FontFamily = new FontFamily(Selected);
    }
}
