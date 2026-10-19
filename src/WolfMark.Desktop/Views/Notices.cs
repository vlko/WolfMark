using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Closable notices above the status bar (the WinUI InfoBars): changed on disk, deleted, sync conflict,
/// non-standard file name, Claude Code clean-up, errors. The area takes no room while none is open.
/// </summary>
internal sealed class Notices : StackPanel
{
    public Notices(MainViewModel vm)
    {
        Spacing = 4;
        var editor = vm.Editor;

        var external = Notice(Strings.Get(R.External_Title), Strings.Get(R.External_Message), warning: true,
            (Strings.Get(R.External_Reload), () => _ = editor.ReloadFromDiskAsync()),
            (Strings.Get(R.External_Keep), () => _ = editor.KeepMineAsync()));
        Bind(external, editor, nameof(NoteEditorViewModel.ExternalChangePending), () => editor.ExternalChangePending);

        var deleted = Notice(Strings.Get(R.Deleted_Title), Strings.Get(R.Deleted_Message), warning: true);
        Bind(deleted, editor, nameof(NoteEditorViewModel.IsDeletedExternally), () => editor.IsDeletedExternally);

        var conflict = Notice(Strings.Get(R.Conflict_Title), string.Empty, warning: true,
            (Strings.Get(R.Conflict_OpenOriginal), () =>
            {
                if (editor.ConflictOriginal is { } original)
                {
                    _ = vm.OpenNoteAsync(original.RelativePath);
                }
            }),
            (Strings.Get(R.Conflict_Discard), () => _ = editor.DeleteAsync()));
        Bind(conflict, editor, nameof(NoteEditorViewModel.ConflictOriginal), () =>
        {
            conflict.Message.Text = editor.ConflictOriginal is { } o ? Strings.Format(R.Conflict_Message, o.FileName) : string.Empty;
            return editor.ConflictOriginal is not null;
        });

        var nonStandard = Notice(Strings.Get(R.NonStandard_Title), Strings.Get(R.NonStandard_Message), warning: false);
        Bind(nonStandard, editor, nameof(NoteEditorViewModel.IsNonStandard), () => editor.IsNonStandard);

        var cleanup = Notice(Strings.Get(R.Claude_CleanupTitle), string.Empty, warning: true,
            (Strings.Get(R.Claude_CleanupFix), () => _ = vm.FixClaudeCleanupAsync()));
        Bind(cleanup, vm, nameof(MainViewModel.CleanupWarning), () =>
        {
            cleanup.Message.Text = vm.CleanupWarning is { } w ? Strings.Format(R.Claude_CleanupMessage, w.Days) : string.Empty;
            return vm.CleanupWarning is not null;
        });

        var error = Notice(Strings.Get(R.Error_Title), string.Empty, warning: true);
        error.Closed = () => vm.ErrorNotice = null;
        Bind(error, vm, nameof(MainViewModel.ErrorNotice), () =>
        {
            error.Message.Text = vm.ErrorNotice ?? string.Empty;
            return vm.ErrorNotice is not null;
        });

        Children.AddRange([error.Root, external.Root, deleted.Root, conflict.Root, nonStandard.Root, cleanup.Root]);
        Margin = new Thickness(24, 0, 24, 6);
    }

    private static void Bind(Item item, System.ComponentModel.INotifyPropertyChanged source, string property, Func<bool> isOpen) =>
        Ui.Bind(source, property, () => item.Root.IsVisible = isOpen());

    private static Item Notice(string title, string message, bool warning, params (string Label, Action Action)[] actions)
    {
        var item = new Item();
        var text = Ui.VStack(2, new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Palette.Ink }, item.Message);
        item.Message.Text = message;
        item.Message.Foreground = Palette.InkSecondary;
        item.Message.TextWrapping = TextWrapping.Wrap;
        var buttons = Ui.HStack(6);
        foreach (var (label, action) in actions)
        {
            var b = Ui.FlatButton(label);
            b.Click += (_, _) => action();
            buttons.Children.Add(b);
        }

        var close = Ui.FlatButton("✕", Palette.Transparent);
        close.Click += (_, _) =>
        {
            item.Root.IsVisible = false;
            item.Closed?.Invoke();
        };
        buttons.Children.Add(close);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        var stripe = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = warning ? new SolidColorBrush(Color.FromRgb(0xE0, 0xA1, 0x2B)) : Palette.Accent };
        var grid = Ui.Columns((Ui.Auto, stripe), (Ui.Star, text), (Ui.Auto, buttons));
        grid.ColumnSpacing = 12;
        item.Root.Child = grid;
        item.Root.Background = Palette.SubtleFill;
        item.Root.CornerRadius = new CornerRadius(4);
        item.Root.Padding = new Thickness(8, 8, 8, 8);
        item.Root.IsVisible = false;
        return item;
    }

    private sealed class Item
    {
        public Border Root { get; } = new();

        public TextBlock Message { get; } = new();

        public Action? Closed { get; set; }
    }
}
