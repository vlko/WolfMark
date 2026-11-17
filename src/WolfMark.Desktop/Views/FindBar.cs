using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Markdown;
using WolfMark.Desktop.Editor;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Find and replace in the open note (Ctrl+F, Ctrl+H), in the editor's top right corner – the twin of
/// src/WolfMark.App/Views/FindBar.cs. What is searched and replaced is decided by <see cref="FindViewModel"/>; the
/// current match is the editor's selection.
/// </summary>
internal sealed class FindBar : Border
{
    private readonly FindViewModel _find;
    private readonly MarkdownTextEditor _editor;
    private readonly TextBox _query = new() { Width = 220, PlaceholderText = Strings.Get(R.Find_Placeholder) };
    private readonly TextBox _replacement = new() { Width = 220, PlaceholderText = Strings.Get(R.Find_ReplacePlaceholder) };

    public FindBar(FindViewModel find, MarkdownTextEditor editor)
    {
        _find = find;
        _editor = editor;
        Background = Palette.WindowTint;
        BorderBrush = Palette.Divider;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(8, 6);
        Margin = new Thickness(0, 0, 18, 0);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        IsVisible = false;

        var status = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.MinWidth = 64;
        var previous = Small("↑", R.Find_Previous, () => Go(backwards: true));
        var next = Small("↓", R.Find_Next, () => Go(backwards: false));
        var matchCase = new ToggleButton { Content = "Aa", Padding = new Thickness(8, 4), IsChecked = find.MatchCase };
        ToolTip.SetTip(matchCase, Strings.Get(R.Find_MatchCase));
        Avalonia.Automation.AutomationProperties.SetName(matchCase, Strings.Get(R.Find_MatchCase));
        matchCase.IsCheckedChanged += (_, _) =>
        {
            find.MatchCase = matchCase.IsChecked == true;
            FindFromSelection();
        };
        var close = Small("✕", R.Common_Close, Close);
        var replace = Ui.FlatButton(Strings.Get(R.Find_Replace));
        replace.Click += (_, _) => ReplaceCurrent();
        var replaceAll = Ui.FlatButton(Strings.Get(R.Find_ReplaceAll));
        replaceAll.Click += (_, _) => ReplaceAll();
        var replaceRow = Ui.HStack(6, _replacement, replace, replaceAll);
        Avalonia.Automation.AutomationProperties.SetName(_query, Strings.Get(R.Find_Placeholder));
        Avalonia.Automation.AutomationProperties.SetName(_replacement, Strings.Get(R.Find_ReplacePlaceholder));
        Child = Ui.VStack(6, Ui.HStack(6, _query, status, previous, next, matchCase, close), replaceRow);

        _query.TextChanged += (_, _) =>
        {
            find.Query = _query.Text ?? string.Empty;
            FindFromSelection();
        };
        _replacement.TextChanged += (_, _) => find.Replacement = _replacement.Text ?? string.Empty;
        _query.KeyDown += (_, e) => OnKey(e, replaceOnEnter: false);
        _replacement.KeyDown += (_, e) => OnKey(e, replaceOnEnter: true);

        Ui.Bind(find, nameof(FindViewModel.Status), () => status.Text = find.Status);
        Ui.Bind(find, nameof(FindViewModel.ShowReplace), () => replaceRow.IsVisible = find.ShowReplace && !_editor.IsReadOnly);
        Ui.Bind(find, nameof(FindViewModel.IsOpen), () =>
        {
            IsVisible = find.IsOpen;
            if (find.IsOpen)
            {
                Show();
            }
        });
    }

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
        Dispatcher.UIThread.Post(() =>
        {
            _query.Focus();
            _query.SelectAll();
        }, DispatcherPriority.Input);
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

    private void OnKey(KeyEventArgs e, bool replaceOnEnter)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Enter when replaceOnEnter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                ReplaceCurrent();
                break;
            case Key.Enter:
            case Key.F3:
                e.Handled = true;
                Go(backwards: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
        }
    }

    private static Button Small(string text, string tooltipKey, Action onClick)
    {
        var button = Ui.FlatButton(text, Palette.Transparent);
        button.Padding = new Thickness(8, 4);
        button.MinWidth = 30;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        var tip = Strings.Keys(Strings.Get(tooltipKey));
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }
}
