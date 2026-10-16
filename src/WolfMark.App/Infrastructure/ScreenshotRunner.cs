using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WolfMark.App.Views;
using WolfMark.Core.Settings;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// <c>--screenshots &lt;dir&gt;</c>: puts the demo window into a few states and saves each as a PNG of the window
/// content (RenderTargetBitmap – the system caption buttons are not part of it), then exits. Used for the
/// README images in docs/screenshots.
/// </summary>
internal sealed partial class ScreenshotRunner(MainWindow window, AppHost host, string outputDirectory)
{
    private const int Width = 1440, Height = 960;
    private readonly ILogger _logger = host.Loggers.CreateLogger<ScreenshotRunner>();

    public async Task RunAsync()
    {
        try
        {
            await Task.Run(() => Directory.CreateDirectory(outputDirectory));
            var scale = window.Content.XamlRoot?.RasterizationScale ?? 1.0;
            window.AppWindow.Resize(new SizeInt32((int)(Width * scale), (int)(Height * scale)));
            await WaitForDataAsync();

            var vm = window.ViewModel;
            var root = host.Root;
            var today = DateOnly.FromDateTime(DateTime.Now);
            var main = await Task.Run(() => FindNote(root, today, "work_"));
            var claude = await Task.Run(() => FindNote(root, today, "ClaudeCode_")
                                              ?? FindNote(root, today.AddDays(-1), "ClaudeCode_"));

            SetTheme(AppTheme.Light);
            await vm.GoToTodayAsync();
            vm.Editor.Mode = EditorMode.Formatted;
            await OpenAsync(main);
            await ShotAsync("main-light.png");

            vm.Editor.Mode = EditorMode.Raw;
            await ShotAsync("raw-mode.png");
            vm.Editor.Mode = EditorMode.Formatted;

            await vm.ToggleCalendarAsync();
            await ShotAsync("calendar-month.png", settle: 2000); // the toolbar fades back in after the expand
            await vm.ToggleCalendarAsync();

            if (claude is not null)
            {
                await OpenAsync(claude);
                await ShotAsync("claude-session.png");
            }

            await OpenAsync(main);
            vm.OpenSearch();
            vm.Search.Query = "calendar";
            await ShotAsync("search.png", settle: 1500);
            vm.IsSearchOpen = false;

            SetTheme(AppTheme.Dark);
            await ShotAsync("main-dark.png");
            SetTheme(AppTheme.Light);
        }
        catch (Exception ex)
        {
            LogFailed(_logger, ex);
        }
        finally
        {
            await window.ExitAsync();
        }
    }

    /// <summary>Opens a note and shows it from its title (opening puts the caret at the end).</summary>
    private async Task OpenAsync(string? relativePath)
    {
        if (relativePath is null)
        {
            return;
        }

        await window.ViewModel.OpenNoteAsync(relativePath);
        await Task.Delay(400);
        window.ScrollEditorToStart();
    }

    private void SetTheme(AppTheme theme)
    {
        host.Settings.Current.Theme = theme;
        window.ApplyAppearance();
    }

    /// <summary>Index scan done and the Claude Code transcripts imported (with a timeout – never hang).</summary>
    private async Task WaitForDataAsync()
    {
        var synced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ClaudeSynced += (_, _) => synced.TrySetResult();
        host.ClaudeSyncFailed += (_, _) => synced.TrySetResult();
        await host.DatabaseReady;
        try
        {
            await synced.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
        }

        // The watcher indexes the imported notes after its debounce.
        await Task.Delay(TimeSpan.FromSeconds(4));
    }

    private static string? FindNote(string root, DateOnly day, string prefix)
    {
        var dir = Path.Combine(root, day.Year.ToString("D4"), day.Month.ToString("D2"), day.Day.ToString("D2"));
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var file = Directory.GetFiles(dir, prefix + "*.md").Order(StringComparer.Ordinal).LastOrDefault();
        return file is null ? null : Path.GetRelativePath(root, file).Replace('\\', '/');
    }

    private async Task ShotAsync(string name, int settle = 900)
    {
        await Task.Delay(settle);
        await NextFrameAsync();
        await NextFrameAsync();

        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(window.RootElement);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var dpi = 96 * (window.Content.XamlRoot?.RasterizationScale ?? 1.0);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels);
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        var path = Path.Combine(outputDirectory, name);
        await Task.Run(() => File.WriteAllBytes(path, bytes));
        LogSaved(_logger, path);
    }

    private static Task NextFrameAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            tcs.TrySetResult();
        }

        CompositionTarget.Rendering += OnRendering;
        return tcs.Task;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Screenshot saved: {Path}")]
    private static partial void LogSaved(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Screenshots failed")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
