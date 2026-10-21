using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Settings;
using WolfMark.Storage.Diagnostics;
using WolfMark.Storage.Settings;

namespace WolfMark.Storage.Tests;

public sealed class SettingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Default_root_prefers_onedrive()
    {
        var env = new Dictionary<string, string?> { ["OneDriveCommercial"] = "/od-business", ["OneDrive"] = "/od" };
        var root = RootDirectoryResolver.ResolveDefault(k => env.GetValueOrDefault(k), _ => true, "/docs");
        Assert.Equal(Path.Combine("/od", "WolfMark"), root);
    }

    [Fact]
    public void Default_root_falls_back_to_documents()
    {
        var env = new Dictionary<string, string?> { ["OneDrive"] = "/missing" };
        var root = RootDirectoryResolver.ResolveDefault(k => env.GetValueOrDefault(k), _ => false, "/docs");
        Assert.Equal(Path.Combine("/docs", "WolfMark"), root);
    }

    [Theory]
    [InlineData(15, 1, 16)]
    [InlineData(15, -1, 14)]
    [InlineData(14.5, 1, 15)] // a size typed in Settings rounds to a whole step
    [InlineData(28, 1, 28)]
    [InlineData(10, -1, 10)]
    [InlineData(12, 5, 17)] // a fast wheel spin: steps summed
    [InlineData(26, 5, 28)]
    public void Zoom_steps_whole_sizes_within_limits(double size, int steps, double expected)
    {
        Assert.Equal(expected, LocalSettings.ZoomFontSize(size, steps));
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        using var dir = new TempDirectory();
        var store = new LocalSettingsStore(new LocalPaths(dir.Path), NullLogger<LocalSettingsStore>.Instance);
        await store.LoadAsync(Ct);
        store.Current.LastNotePath = "2026/10/15/praca_1403.md";
        store.Current.Theme = AppTheme.Dark;
        store.Current.ClaudeCode.ExcludedProjects.Add("C:\\secret");
        await store.SaveAsync(Ct);

        var again = new LocalSettingsStore(new LocalPaths(dir.Path), NullLogger<LocalSettingsStore>.Instance);
        var loaded = await again.LoadAsync(Ct);
        Assert.Equal("2026/10/15/praca_1403.md", loaded.LastNotePath);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(["C:\\secret"], loaded.ClaudeCode.ExcludedProjects);
        Assert.Contains("\"theme\": \"Dark\"", await File.ReadAllTextAsync(Path.Combine(dir.Path, "settings.json"), Ct));
    }

    [Fact]
    public async Task Background_exception_is_logged_not_thrown()
    {
        var logger = new ListLogger<BackgroundRunner>();
        var runner = new BackgroundRunner(logger);
        await runner.Run("boom", _ => throw new InvalidOperationException("kaboom"), Ct);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task File_logger_writes_daily_file()
    {
        using var dir = new TempDirectory();
        var provider = new FileLoggerProvider(dir.Path);
        provider.CreateLogger("Test").LogWarning("hello {Name}", "svet");
        await provider.FlushAsync();

        var file = Assert.Single(Directory.GetFiles(dir.Path, "wolfmark-*.log"));
        Assert.Contains("[WRN] Test: hello svet", await File.ReadAllTextAsync(file, Ct));
    }
}
