using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Markdown;
using WolfMark.Editor;

namespace WolfMark.App.Views;

/// <summary>
/// Find and replace in the open note (Ctrl+F, Ctrl+H), in the editor's top right corner – the twin of
/// src/WolfMark.Desktop/Views/FindBar.cs. What is searched and replaced is decided by <see cref="FindViewModel"/>;
/// the current match is the editor's selection.
/// </summary>
internal sealed class FindBar
{
    private readonly FindViewModel _find;
    private readonly MarkdownEditor _editor;
    private readonly TextBox _query = new() { Width = 220, PlaceholderText = Strings.Get(R.Find_Placeholder) };
    private readonly TextBox _replacement = new() { Width = 220, PlaceholderText = Strings.Get(R.Find_ReplacePlaceholder) };

    public FindBar(FindViewModel find, MarkdownEditor editor)
    {
        _find = find;
        _editor = editor;

        var status = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.MinWidth = 64;
        var previous = Ui.Ui.IconButton(Glyphs.ChevronUp, Strings.Get(R.Find_Previous), () => Go(backwards: true), 12);
        var next = Ui.Ui.IconButton(Glyphs.ChevronDown, Strings.Get(R.Find_Next), () => Go(backwards: false), 12);
        var matchCase = new ToggleButton { Content = "Aa", Padding = new Thickness(8, 4, 8, 4), IsChecked = find.MatchCase };
        ToolTipService.SetToolTip(matchCase, Strings.Get(R.Find_MatchCase));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(matchCase, Strings.Get(R.Find_MatchCase));
        void OnMatchCase()
        {
            find.MatchCase = matchCase.IsChecked == true;
            FindFromSelection();
        }

        matchCase.Checked += (_, _) => OnMatchCase();
        matchCase.Unchecked += (_, _) => OnMatchCase();
        var close = Ui.Ui.IconButton(Glyphs.Close, Strings.Get(R.Common_Close), Close, 11);
        var replace = Ui.Ui.TextButton(Strings.Get(R.Find_Replace), ReplaceCurrent);
        var replaceAll = Ui.Ui.TextButton(Strings.Get(R.Find_ReplaceAll), ReplaceAll);
        var replaceRow = Ui.Ui.HStack(6, _replacement, replace, replaceAll);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_query, Strings.Get(R.Find_Placeholder));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_replacement, Strings.Get(R.Find_ReplacePlaceholder));

        Element = new Border
        {
            Child = Ui.Ui.VStack(6, Ui.Ui.HStack(6, _query, status, previous, next, matchCase, close), replaceRow),
            Background = Palette.WindowTint,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };

        _query.TextChanged += (_, _) =>
        {
            find.Query = _query.Text;
            FindFromSelection();
        };
        _replacement.TextChanged += (_, _) => find.Replacement = _replacement.Text;
        _query.KeyDown += (_, e) => OnKey(e, replaceOnEnter: false);
        _replacement.KeyDown += (_, e) => OnKey(e, replaceOnEnter: true);

        status.Bind(find, nameof(FindViewModel.Status), () => status.Text = find.Status);
        replaceRow.Bind(find, nameof(FindViewModel.ShowReplace), () =>
            replaceRow.Visibility = find.ShowReplace && !_editor.IsReadOnly ? Visibility.Visible : Visibility.Collapsed);
        Element.Bind(find, nameof(FindViewModel.IsOpen), () =>
        {
            Element.Visibility = find.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (find.IsOpen)
            {
                Show();
            }
        });
    }

    public Border Element { get; }

    /// <summary>Ctrl+F / Ctrl+H: opens (or comes back to) the bar with the editor's selection as the query.</summary>
    public void Open(bool replace)
    {
        _find.Open(replace, _editor.SelectedText);
        Show(); // already open: the binding did not fire
    }

    /// <summary>F3 / Shift+F3 from anywhere in the window.</summary>
    public void Go(bool backwards)
    {
        var (start, length) = _editor.SelectedRange;
        Select(_find.FindNext(_editor.Text, start, length, backwards));
    }

    private void Show()
    {
        if (_query.Text != _find.Query)
        {
            _query.Text = _find.Query;
        }

        FindFromSelection();
        _query.DispatcherQueue.TryEnqueue(() =>
        {
            _query.Focus(FocusState.Programmatic);
            _query.SelectAll();
        });
    }

    private void Close()
    {
        _find.Close();
        _editor.FocusEditor();
    }

    private void FindFromSelection() => Select(_find.FindFrom(_editor.Text, _editor.SelectedRange.Start));

    private void Select(TextMatch? match)
    {
        if (match is { } m)
        {
            _editor.Select(m.Start, m.Length);
        }
    }

    private void ReplaceCurrent()
    {
        var (start, length) = _editor.SelectedRange;
        if (_find.ReplaceCurrent(_editor.Text, start, length) is { } edit)
        {
            _editor.ApplyEdit(edit);
        }

        Go(backwards: false);
    }

    private void ReplaceAll()
    {
        if (_find.ReplaceAll(_editor.Text) is { } edit)
        {
            _editor.ApplyEdit(edit);
        }
    }

    private void OnKey(KeyRoutedEventArgs e, bool replaceOnEnter)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                Close();
                break;
            case VirtualKey.Enter when replaceOnEnter && !shift:
                e.Handled = true;
                ReplaceCurrent();
                break;
            case VirtualKey.Enter:
            case VirtualKey.F3:
                e.Handled = true;
                Go(backwards: shift);
                break;
        }
    }
}
