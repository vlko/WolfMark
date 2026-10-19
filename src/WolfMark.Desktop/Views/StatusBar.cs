using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Status line along the bottom edge (src/WolfMark.App/Views/StatusBar.cs): index state, Claude Code import
/// (click = sync now), OneDrive conflicts – and the notes folder on the right.
/// </summary>
internal sealed class StatusBar : Border
{
    public StatusBar(MainViewModel vm)
    {
        var host = vm.Host;

        // Index: progress while scanning, otherwise the note count.
        var index = Item(Icons.Sync, out var indexText);
        Ui.Bind(vm, [nameof(MainViewModel.StatusText), nameof(MainViewModel.IsIndexing), nameof(MainViewModel.NoteCount)], () =>
            indexText.Text = (vm.IsIndexing ? vm.StatusText : Strings.Notes(vm.NoteCount)).ToUpper(CultureInfo.CurrentCulture));

        // Claude Code: click = sync now.
        var claudeLabel = Typo.Caps(string.Empty, 11.5, Palette.InkTertiary);
        var claudeIcon = new ContentControl { Content = Icons.Create(Icons.Code, 11, Palette.InkTertiary), VerticalAlignment = VerticalAlignment.Center };
        var claude = SmallButton(Ui.HStack(5, claudeIcon, claudeLabel));
        ToolTip.SetTip(claude, Strings.Get(R.Status_ClaudeSyncNow));
        claude.Click += (_, _) => host.TriggerClaudeSync();
        Ui.Bind(vm, [nameof(MainViewModel.ClaudeStatus), nameof(MainViewModel.ClaudeStatusIsError)], () =>
        {
            claudeLabel.Text = vm.ClaudeStatus.ToUpper(CultureInfo.CurrentCulture);
            var brush = vm.ClaudeStatusIsError ? Brushes.IndianRed : (IBrush)Palette.InkTertiary;
            claudeLabel.Foreground = brush;
            claudeIcon.Content = Icons.Create(Icons.Code, 11, brush);
        });

        // OneDrive conflicts: click opens the first one.
        var conflictLabel = Typo.Caps(string.Empty, 11.5, Palette.InkSecondary);
        var conflicts = SmallButton(Ui.HStack(5, Icons.Create(Icons.Warning, 11, Palette.InkSecondary), conflictLabel));
        Ui.Bind(vm, nameof(MainViewModel.ConflictCount), () =>
        {
            conflicts.IsVisible = vm.ConflictCount > 0;
            conflictLabel.Text = Strings.Format(R.Conflicts_Count, vm.ConflictCount).ToUpper(CultureInfo.CurrentCulture);
        });
        conflicts.Click += async (_, _) =>
        {
            var list = await host.History.GetConflictsAsync(host.ShutdownToken);
            if (list.Count > 0)
            {
                await vm.OpenNoteAsync(list[0].RelativePath);
            }
        };

        // Notes folder on the right – in its own case: unlike on Windows, paths here are case-sensitive.
        var folderText = Typo.Caps(host.Root, 11.5, Palette.InkTertiary);
        folderText.LetterSpacing = 0;
        folderText.TextTrimming = TextTrimming.CharacterEllipsis;
        var folder = Ui.HStack(5, Icons.Create(Icons.Folder, 11, Palette.InkTertiary), folderText);
        folder.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(folder, Strings.Get(R.Status_NotesFolder) + ": " + host.Root);

        var left = Ui.HStack(14, index, claude, conflicts);
        left.VerticalAlignment = VerticalAlignment.Center;
        var grid = Ui.Columns((Ui.Auto, left), (Ui.Star, null), (Ui.Auto, folder));
        grid.ColumnSpacing = 14;
        Child = grid;
        BorderBrush = Palette.Divider;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(18, 2, 14, 2);
        MinHeight = 28;
    }

    private static StackPanel Item(string icon, out TextBlock text)
    {
        text = Typo.Caps(string.Empty, 11.5, Palette.InkTertiary);
        text.VerticalAlignment = VerticalAlignment.Center;
        var panel = Ui.HStack(5, Icons.Create(icon, 11, Palette.InkTertiary), text);
        panel.VerticalAlignment = VerticalAlignment.Center;
        return panel;
    }

    private static Button SmallButton(Control content) => new()
    {
        Content = content,
        Padding = new Thickness(6, 0),
        Height = 24,
        Background = Palette.Transparent,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(3),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
}
