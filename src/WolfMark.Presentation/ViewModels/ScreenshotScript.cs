using Microsoft.Extensions.Logging;
using WolfMark.Core.Settings;

namespace WolfMark.App.ViewModels;

/// <summary>What an app does for <see cref="ScreenshotScript"/>: its window, rendered to image files.</summary>
internal interface IScreenshotStage
{
    /// <summary>Saves the main window's content as <paramref name="name"/> once the UI has settled.</summary>
    Task ShotAsync(string name, int settleMilliseconds = 900);

    /// <summary>Opens <paramref name="fullPath"/> in its own window, saves that window as <paramref name="name"/> and closes it.</summary>
    Task ShotExternalAsync(string fullPath, string name);

    void SetTheme(AppTheme theme);

    /// <summary>The note from its title – opening one puts the caret at the end.</summary>
    void ScrollEditorToStart();

    Task ExitAsync();
}

/// <summary>
/// <c>--screenshots &lt;dir&gt;</c> in both apps: puts the demo window into a few states, has each saved as a PNG and
/// exits. The README images in docs/screenshots come from it, and the smoke tests use it to prove the published
/// binary starts, shows and draws its window.
/// </summary>
internal sealed partial class ScreenshotScript(MainViewModel vm, IScreenshotStage stage)
{
    /// <summary>The window's size in device-independent pixels.</summary>
    public const int Width = 1440, Height = 960, ExternalWidth = 1100, ExternalHeight = 820;

    private readonly AppHost _host = vm.Host;
    private readonly ILogger _logger = vm.Host.Loggers.CreateLogger<ScreenshotScript>();

    public async Task RunAsync()
    {
        try
        {
            await WaitForDataAsync();

            var root = _host.Root;
            var today = DateOnly.FromDateTime(DateTime.Now);
            var main = await Task.Run(() => FindNote(root, today, "work_"));
            var claude = await Task.Run(() => FindNote(root, today, "ClaudeCode_")
                                              ?? FindNote(root, today.AddDays(-1), "ClaudeCode_"));

            stage.SetTheme(AppTheme.Light);
            await vm.GoToTodayAsync();
            vm.Editor.Mode = EditorMode.Formatted;
            await OpenAsync(main);
            await stage.ShotAsync("main-light.png");

            vm.Editor.Mode = EditorMode.Raw;
            await stage.ShotAsync("raw-mode.png");
            vm.Editor.Mode = EditorMode.Formatted;

            await vm.ToggleCalendarAsync();
            await stage.ShotAsync("calendar-month.png", 2000); // the toolbar fades back in after the expand
            await vm.ToggleCalendarAsync();

            if (claude is not null)
            {
                await OpenAsync(claude);
                await stage.ShotAsync("claude-session.png");
            }

            await OpenAsync(main);
            vm.OpenSearch();
            vm.Search.Query = "calendar";
            await stage.ShotAsync("search.png", 1500);
            vm.IsSearchOpen = false;

            // Full view: only the day, the formatting and the note (the table at the top).
            vm.IsFullView = true;
            await stage.ShotAsync("full-view.png");
            vm.IsFullView = false;

            // A README outside the notes in its own window – the demo has it in the history.
            if (_host.Settings.Current.ExternalFiles.FirstOrDefault()?.Path is { } external)
            {
                await stage.ShotExternalAsync(external, "external-file.png");
            }

            stage.SetTheme(AppTheme.Dark);
            await stage.ShotAsync("main-dark.png");
            stage.SetTheme(AppTheme.Light);
        }
        catch (Exception ex)
        {
            LogFailed(ex);
        }
        finally
        {
            await stage.ExitAsync();
        }
    }

    /// <summary>Called by the stage for each image written (the smoke tests read it in the log).</summary>
    public void Saved(string path) => LogSaved(path);

    private async Task OpenAsync(string? relativePath)
    {
        if (relativePath is null)
        {
            return;
        }

        await vm.OpenNoteAsync(relativePath);
        await Task.Delay(400);
        stage.ScrollEditorToStart();
    }

    /// <summary>Index scan done and the Claude Code transcripts imported (with a timeout – never hang).</summary>
    private async Task WaitForDataAsync()
    {
        await _host.DatabaseReady;
        try
        {
            // Not the Synced event: the import of the demo is often over before this runs, and waiting for an event
            // that has already passed cost the full 20 seconds on every run.
            await _host.FirstClaudeSync.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
        }

        // The watcher indexes the imported notes after its debounce.
        await Task.Delay(TimeSpan.FromSeconds(4));
    }

    internal static string? FindNote(string root, DateOnly day, string prefix)
    {
        var dir = Path.Combine(root, day.Year.ToString("D4"), day.Month.ToString("D2"), day.Day.ToString("D2"));
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var file = Directory.GetFiles(dir, prefix + "*.md").Order(StringComparer.Ordinal).LastOrDefault();
        return file is null ? null : Path.GetRelativePath(root, file).Replace('\\', '/');
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Screenshot saved: {Path}")]
    private partial void LogSaved(string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Screenshots failed")]
    private partial void LogFailed(Exception ex);
}
