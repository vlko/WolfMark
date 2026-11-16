using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>A question with one action and Cancel (the default, also Esc or closing the window) – true = the action.</summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string question, string detail, string action)
    {
        Title = Strings.Get(R.AppTitle);
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.WindowTint;

        var text = Typo.Body(question, 15, Palette.Ink);
        text.TextWrapping = TextWrapping.Wrap;
        text.MaxWidth = 380;
        var sub = Typo.Body(detail, 13, Palette.InkSecondary);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.MaxWidth = 380;
        sub.IsVisible = detail.Length > 0;

        var ok = Ui.FlatButton(Typo.Caps(action.ToUpper(CultureInfo.CurrentCulture), 13, Brushes.White), Palette.Accent);
        ok.Click += (_, _) => Close(true);
        var cancel = Ui.FlatButton(Strings.Get(R.Common_Cancel), Palette.Transparent);
        cancel.Click += (_, _) => Close(false);
        var buttons = Ui.HStack(8, ok, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;

        Content = new Border { Padding = new Thickness(20), Child = Ui.VStack(18, Ui.VStack(6, text, sub), buttons) };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
            }
        };
        Opened += (_, _) => cancel.Focus();
    }

    public static async Task<bool> AskAsync(Control owner, string question, string detail, string action) =>
        TopLevel.GetTopLevel(owner) is Window window
        && await new ConfirmDialog(question, detail, action).ShowDialog<bool>(window);
}
