using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Run = Microsoft.UI.Xaml.Documents.Run;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;

namespace WolfMark.App.Views;

/// <summary>Ctrl+K command palette (M5-03): hybrid search with highlighted matches and #tag / @date filters.</summary>
internal sealed partial class SearchPalette : UserControl
{
    private readonly MainViewModel _vm;
    private readonly TextBox _input;
    private readonly ScrollViewer _scroller;
    private readonly ItemsRepeater _results;

    public SearchPalette(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        var search = vm.Search;

        _input = new TextBox
        {
            PlaceholderText = Strings.Get(R.Search_Placeholder),
            FontSize = 18,
            BorderThickness = new Thickness(0),
            Background = Palette.Transparent,
            Padding = new Thickness(4, 6, 4, 6),
        };
        _input.Resources["TextControlBackgroundFocused"] = Palette.Transparent;
        _input.Resources["TextControlBackgroundPointerOver"] = Palette.Transparent;
        _input.Resources["TextControlBorderBrushFocused"] = Palette.Transparent;
        AutomationProperties.SetName(_input, Strings.Get(R.Toolbar_Search));
        _input.TextChanged += (_, _) => search.Query = _input.Text;
        _input.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Down:
                    search.Move(1);
                    BringSelectedIntoView();
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Up:
                    search.Move(-1);
                    BringSelectedIntoView();
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Enter:
                    search.InvokeSelected();
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Escape:
                    vm.IsSearchOpen = false;
                    e.Handled = true;
                    break;
            }
        };

        _results = new ItemsRepeater
        {
            ItemsSource = search.Results,
            ItemTemplate = new ElementFactory<SearchResultViewModel, SearchResultView>(() => new SearchResultView(search, brushes)),
            Layout = new StackLayout { Spacing = 2 },
        };
        _scroller = new ScrollViewer { Content = _results, MaxHeight = 460, Padding = new Thickness(0, 0, 6, 0) };

        var noResults = Typo.Body(Strings.Get(R.Search_NoResults), 13, Palette.InkTertiary);
        noResults.Margin = new Thickness(8, 10, 0, 10);
        noResults.Bind(search, nameof(SearchViewModel.ShowNoResults), () => noResults.Visibility = search.ShowNoResults ? Visibility.Visible : Visibility.Collapsed);

        var hint = Typo.Body(Strings.Get(R.Search_Hint), 11.5, Palette.InkTertiary);
        hint.Margin = new Thickness(8, 6, 0, 0);

        var inputRow = Ui.Ui.Columns((Ui.Ui.Auto, Typo.Icon(Glyphs.Search, 18, Palette.InkTertiary)), (Ui.Ui.Star, _input));
        inputRow.ColumnSpacing = 10;
        inputRow.Margin = new Thickness(10, 4, 10, 4);

        var card = Ui.Ui.Card(
            Ui.Ui.VStack(4, inputRow, new Border { Height = 1, Background = Palette.Divider }, noResults, _scroller, hint),
            radius: 16);
        card.Padding = new Thickness(10, 8, 10, 10);
        card.Width = 680;
        card.MaxWidth = 680;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(16, 64, 16, 16);
        card.Translation = new System.Numerics.Vector3(0, 0, 48);
        card.Shadow = new ThemeShadow();

        var dim = new Grid { Background = Palette.Overlay };
        dim.Tapped += (_, _) => vm.IsSearchOpen = false;

        var root = new Grid();
        root.Children.Add(dim);
        root.Children.Add(card);
        Content = root;
    }

    public void FocusInput()
    {
        _input.Text = string.Empty;
        _input.Focus(FocusState.Programmatic);
    }

    private void BringSelectedIntoView()
    {
        var index = _vm.Search.SelectedIndex;
        if (index >= 0 && _results.TryGetElement(index) is { } element)
        {
            element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
    }
}

internal sealed partial class SearchResultView : UserControl, IItemView<SearchResultViewModel>
{
    private readonly SearchViewModel _search;
    private readonly TagBrushes _brushes;
    private readonly Button _button;
    private readonly TextBlock _title = new() { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _snippet = new() { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _meta = Typo.Body(string.Empty, 11.5, Palette.InkTertiary);
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _bar = new() { Width = 4, RadiusX = 2, RadiusY = 2 };
    private SearchResultViewModel? _item;

    public SearchResultView(SearchViewModel search, TagBrushes brushes)
    {
        _search = search;
        _brushes = brushes;
        _title.Foreground = Palette.Ink;
        _snippet.Foreground = Palette.InkSecondary;
        var grid = Ui.Ui.Columns((Ui.Ui.Auto, _bar), (Ui.Ui.Star, Ui.Ui.VStack(2, _title, _snippet, _meta)));
        grid.ColumnSpacing = 10;
        _button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 6, 10, 6),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0),
            Background = Palette.Transparent,
        };
        _button.Click += (_, _) =>
        {
            if (_item is not null)
            {
                _search.Invoke(_item);
            }
        };
        Content = _button;
    }

    public void Bind(SearchResultViewModel item)
    {
        if (_item is not null)
        {
            _item.PropertyChanged -= OnChanged;
        }

        _item = item;
        item.PropertyChanged += OnChanged;
        Fill(_title, item.TitleSegments);
        Fill(_snippet, item.SnippetSegments);
        var note = item.Hit.Note;
        _meta.Text = item.DateText + (note.PrimaryTag is { } t ? "  ·  #" + t : string.Empty) + (item.Hit.FromSemantic ? "  ·  ≈ " + Strings.Get(R.Search_Semantic) : string.Empty);
        _bar.Fill = _brushes.BrushOf(note.PrimaryTag);
        AutomationProperties.SetName(_button, note.Title);
        OnChanged(null, null!);
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        _button.Background = _item?.IsSelected == true ? Palette.SubtleFill : Palette.Transparent;

    private static void Fill(TextBlock block, IReadOnlyList<(string Text, bool Highlight)> segments)
    {
        block.Inlines.Clear();
        foreach (var (text, highlight) in segments)
        {
            var run = new Run { Text = text };
            if (highlight)
            {
                run.FontWeight = FontWeights.Bold;
                run.Foreground = Palette.Accent;
            }

            block.Inlines.Add(run);
        }
    }
}
