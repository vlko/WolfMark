using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>Ctrl+K palette (M5-03): hybrid search with highlighted matches and #tag / @date filters.</summary>
internal sealed class SearchPalette : UserControl
{
    private readonly MainViewModel _vm;
    private readonly TextBox _input;
    private readonly ItemsControl _results;

    public SearchPalette(MainViewModel vm)
    {
        _vm = vm;
        var search = vm.Search;
        _input = new TextBox
        {
            PlaceholderText = Strings.Get(R.Search_Placeholder),
            FontSize = 18,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 6),
        };
        _input.TextChanged += (_, _) => search.Query = _input.Text ?? string.Empty;
        _input.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down:
                    search.Move(1);
                    BringSelectedIntoView();
                    e.Handled = true;
                    break;
                case Key.Up:
                    search.Move(-1);
                    BringSelectedIntoView();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    search.InvokeSelected();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.IsSearchOpen = false;
                    e.Handled = true;
                    break;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _results = new ItemsControl
        {
            ItemsSource = search.Results,
            ItemTemplate = new FuncDataTemplate<SearchResultViewModel>((item, _) => item is null ? null : new ResultRow(vm, item), supportsRecycling: false),
        };
        var scroller = new ScrollViewer { Content = _results, MaxHeight = 460 };

        var noResults = Typo.Body(Strings.Get(R.Search_NoResults), 13, Palette.InkTertiary);
        noResults.Margin = new Thickness(8, 10);
        Ui.Bind(search, nameof(SearchViewModel.ShowNoResults), () => noResults.IsVisible = search.ShowNoResults);
        var hint = Typo.Body(Strings.Get(R.Search_Hint), 11.5, Palette.InkTertiary);
        hint.Margin = new Thickness(8, 6, 0, 0);

        var card = new Border
        {
            Child = Ui.VStack(4, _input, new Border { Height = 1, Background = Palette.Divider }, noResults, scroller, hint),
            Background = Palette.WindowTint,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8, 14, 10),
            Width = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 64, 16, 16),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 40, Color = Color.FromArgb(0x40, 0, 0, 0) }),
        };
        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x55, 0x20, 0x1C, 0x16)) };
        dim.PointerPressed += (_, _) => vm.IsSearchOpen = false;
        var root = new Grid();
        root.Children.Add(dim);
        root.Children.Add(card);
        Content = root;
    }

    public void FocusInput()
    {
        _input.Text = string.Empty;
        // Just made visible: focus once it is laid out.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _input.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void BringSelectedIntoView()
    {
        if (_vm.Search.SelectedIndex is var index and >= 0 && _results.ContainerFromIndex(index) is { } container)
        {
            container.BringIntoView();
        }
    }

    private sealed class ResultRow : Button
    {
        private readonly SearchResultViewModel _item;

        public ResultRow(MainViewModel vm, SearchResultViewModel item)
        {
            _item = item;
            var note = item.Hit.Note;
            var title = new TextBlock { FontSize = 14.5, FontWeight = FontWeight.SemiBold, Foreground = Palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
            var snippet = new TextBlock { FontSize = 12.5, Foreground = Palette.InkSecondary, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            Fill(title, item.TitleSegments);
            Fill(snippet, item.SnippetSegments);
            var meta = Typo.Body(
                item.DateText + (note.PrimaryTag is { } t ? "  ·  #" + t : string.Empty) + (item.Hit.FromSemantic ? "  ·  ≈ " + Strings.Get(R.Search_Semantic) : string.Empty),
                11.5,
                Palette.InkTertiary);
            var bar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = note.PrimaryTag is { } tag ? new SolidColorBrush(Palette.ToColor(vm.Host.Tags.GetColor(tag))) : Palette.Divider,
            };
            var grid = Ui.Columns((Ui.Auto, bar), (Ui.Star, Ui.VStack(2, title, snippet, meta)));
            grid.ColumnSpacing = 10;
            Content = grid;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            BorderThickness = new Thickness(0);
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(6, 6, 10, 6);
            Click += (_, _) => vm.Search.Invoke(_item);
            Ui.Bind(item, nameof(SearchResultViewModel.IsSelected), () => Background = item.IsSelected ? Palette.SubtleFill : Palette.Transparent);
        }

        private static void Fill(TextBlock block, IReadOnlyList<(string Text, bool Highlight)> segments)
        {
            var inlines = new InlineCollection();
            foreach (var (text, highlight) in segments)
            {
                var run = new Run(text);
                if (highlight)
                {
                    run.FontWeight = FontWeight.Bold;
                    run.Foreground = Palette.Accent;
                }

                inlines.Add(run);
            }

            block.Inlines = inlines;
        }
    }
}
