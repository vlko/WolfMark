using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WolfMark.App.ViewModels;
using WolfMark.App.Views;
using WolfMark.Core.Settings;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// <c>--screenshots &lt;dir&gt;</c> on Windows: the stage of <see cref="ScreenshotScript"/> (which states are shown is
/// shared with the macOS/Linux app). A window's content is saved with RenderTargetBitmap – the system caption buttons
/// are not part of it.
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
        var scale = _window.Content.XamlRoot?.RasterizationScale ?? 1.0;
        _window.AppWindow.Resize(new SizeInt32((int)(ScreenshotScript.Width * scale), (int)(ScreenshotScript.Height * scale)));
        await _script.RunAsync();
    }

    public Task ShotAsync(string name, int settleMilliseconds = 900) =>
        ShotAsync(name, _window.RootElement, _window.Content.XamlRoot, settleMilliseconds);

    public async Task ShotExternalAsync(string fullPath, string name)
    {
        await _window.ViewModel.OpenFileAsync(fullPath);
        await Task.Delay(600);
        if (_window.ExternalWindowOf(fullPath) is { } externalWindow)
        {
            var scale = externalWindow.Content.XamlRoot?.RasterizationScale ?? 1.0;
            externalWindow.AppWindow.Resize(new SizeInt32((int)(ScreenshotScript.ExternalWidth * scale), (int)(ScreenshotScript.ExternalHeight * scale)));
            await ShotAsync(name, externalWindow.RootElement, externalWindow.Content.XamlRoot);
            await externalWindow.TryCloseAsync();
        }
    }

    public void SetTheme(AppTheme theme)
    {
        _host.Settings.Current.Theme = theme;
        _window.ApplyAppearance();
    }

    public void ScrollEditorToStart() => _window.ScrollEditorToStart();

    public Task ExitAsync() => _window.ExitAsync();

    /// <summary>Saves <paramref name="element"/> (a window's content) as a PNG after the UI settled.</summary>
    private async Task ShotAsync(string name, Microsoft.UI.Xaml.UIElement element, Microsoft.UI.Xaml.XamlRoot? root, int settle = 900)
    {
        await Task.Delay(settle);
        await NextFrameAsync();
        await NextFrameAsync();

        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var dpi = 96 * (root?.RasterizationScale ?? 1.0);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels);
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        var path = Path.Combine(_outputDirectory, name);
        await Task.Run(() => File.WriteAllBytes(path, bytes));
        _script.Saved(path);
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
}
