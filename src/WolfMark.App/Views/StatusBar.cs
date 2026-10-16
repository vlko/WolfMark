using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;

namespace WolfMark.App.Views;

/// <summary>
/// Status line along the bottom edge: index state, Claude Code import, OneDrive conflicts – and the notes
/// folder on the right. Closable notices (InfoBars) sit right above it.
/// </summary>
internal sealed partial class StatusBar : UserControl
{
    public StatusBar(MainViewModel vm)
    {
        var host = vm.Host;

        // Index: progress while scanning, otherwise the note count.
        var index = Item(Glyphs.Sync, out var indexText);
        index.Bind(vm, [nameof(MainViewModel.StatusText), nameof(MainViewModel.IsIndexing), nameof(MainViewModel.NoteCount)], () =>
            indexText.Text = (vm.IsIndexing ? vm.StatusText : Strings.Notes(vm.NoteCount)).ToUpper(CultureInfo.CurrentCulture));

        // Claude Code: click = sync now.
        var claudeLabel = Typo.Caps(string.Empty, 11.5, Palette.InkTertiary);
        var claude = new Button { Content = Ui.Ui.HStack(5, Typo.Icon(Glyphs.Code, 11), claudeLabel), Padding = new Thickness(6, 0, 6, 0), Height = 24, CornerRadius = Radius.Control };
        claude.StyleButton(Palette.Transparent, Palette.InkTertiary);
        ToolTipService.SetToolTip(claude, Strings.Get(R.Status_ClaudeSyncNow));
        claude.Click += (_, _) => host.TriggerClaudeSync();
        claude.Bind(vm, [nameof(MainViewModel.ClaudeStatus), nameof(MainViewModel.ClaudeStatusIsError)], () =>
        {
            claudeLabel.Text = vm.ClaudeStatus.ToUpper(CultureInfo.CurrentCulture);
            claude.Foreground = vm.ClaudeStatusIsError ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed) : Palette.InkTertiary;
        });

        // OneDrive conflicts.
        var conflictLabel = Typo.Caps(string.Empty, 11.5, Palette.InkSecondary);
        var conflicts = new Button { Content = Ui.Ui.HStack(5, Typo.Icon(Glyphs.Warning, 11), conflictLabel), Padding = new Thickness(6, 0, 6, 0), Height = 24, CornerRadius = Radius.Control };
        conflicts.StyleButton(Palette.Transparent, Palette.InkSecondary);
        conflicts.Bind(vm, nameof(MainViewModel.ConflictCount), () =>
        {
            conflicts.Visibility = vm.ConflictCount > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // Notes folder on the right.
        var folderText = Typo.Caps(host.Root, 11.5, Palette.InkTertiary);
        folderText.TextTrimming = TextTrimming.CharacterEllipsis;
        folderText.CharacterSpacing = 0;
        var folder = Ui.Ui.HStack(5, Typo.Icon(Glyphs.Folder, 11, Palette.InkTertiary), folderText);
        folder.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(folder, Strings.Get(R.Status_NotesFolder) + ": " + host.Root);

        var left = Ui.Ui.HStack(14, index, claude, conflicts);
        left.VerticalAlignment = VerticalAlignment.Center;
        var grid = Ui.Ui.Columns((Ui.Ui.Auto, left), (Ui.Ui.Star, null), (Ui.Ui.Auto, folder));
        grid.ColumnSpacing = 14;

        Content = new Border
        {
            Child = grid,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 2, 14, 2),
            MinHeight = 28,
        };
    }

    private static StackPanel Item(string glyph, out TextBlock text)
    {
        text = Typo.Caps(string.Empty, 11.5, Palette.InkTertiary);
        var panel = Ui.Ui.HStack(5, Typo.Icon(glyph, 11, Palette.InkTertiary), text);
        panel.VerticalAlignment = VerticalAlignment.Center;
        return panel;
    }
}
