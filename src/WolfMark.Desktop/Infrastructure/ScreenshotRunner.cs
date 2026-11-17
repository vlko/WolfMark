using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WolfMark.App;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Views;

namespace WolfMark.Desktop.Infrastructure;

/// <summary>
/// <c>--screenshots &lt;dir&gt;</c> on macOS and Linux: the stage of <see cref="ScreenshotScript"/> (which states are
/// shown is shared with the Windows app). A window is drawn into a bitmap, so it needs no screen capture and works
/// under Xvfb too.
/// </summary>
internal sealed class ScreenshotRunner : IScreenshotStage
{
    private readonly MainWindow _window;
    private readonly AppHost _host;
    private readonly string _outputDirectory;
    private readonly ScreenshotScript _script;

    public ScreenshotRunner(MainWindow window, AppHost host, string outputDirectory)
    {
        _window = window;
        _host = host;
        _outputDirectory = outputDirectory;
        _script = new ScreenshotScript(window.ViewModel, this);
    }

    public async Task RunAsync()
    {
        await Task.Run(() => Directory.CreateDirectory(_outputDirectory));
        _window.WindowState = WindowState.Normal;
        _window.Width = ScreenshotScript.Width;
        _window.Height = ScreenshotScript.Height;
        await _script.RunAsync();
    }

    public Task ShotAsync(string name, int settleMilliseconds = 900) => ShotAsync(name, _window, settleMilliseconds);

    public async Task ShotExternalAsync(string fullPath, string name)
    {
        await _window.ViewModel.OpenFileAsync(fullPath);
        await Task.Delay(600);
        if (_window.ExternalWindowOf(fullPath) is { } external)
        {
            external.Width = ScreenshotScript.ExternalWidth;
            external.Height = ScreenshotScript.ExternalHeight;
            await ShotAsync(name, external, 900);
            await external.TryCloseAsync();
        }
    }

    public void SetTheme(AppTheme theme)
    {
        _host.Settings.Current.Theme = theme;
        _window.ApplyAppearance();
    }

    public void ScrollEditorToStart() => _window.ScrollEditorToStart();

    public Task ExitAsync()
    {
        _window.Exit();
        return Task.CompletedTask;
    }

    /// <summary>Saves the window as a PNG after the UI settled and was laid out and drawn once more.</summary>
    private async Task ShotAsync(string name, Window window, int settle)
    {
        await Task.Delay(settle);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

        var scale = window.RenderScaling > 0 ? window.RenderScaling : 1;
        var size = new PixelSize(Math.Max(1, (int)(window.Bounds.Width * scale)), Math.Max(1, (int)(window.Bounds.Height * scale)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        var bytes = png.ToArray();
        var path = Path.Combine(_outputDirectory, name);
        await Task.Run(() => File.WriteAllBytes(path, bytes));
        _script.Saved(path);
    }
}
