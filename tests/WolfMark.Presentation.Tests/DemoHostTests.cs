using WolfMark.App;

namespace WolfMark.Presentation.Tests;

public sealed class DemoHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fresh_demo_starts_in_a_folder_that_does_not_exist_yet()
    {
        // --screenshots into a new folder (the CI smoke test): the reset must not need local/ to exist.
        var parent = Directory.CreateTempSubdirectory("wolfmark-demo-").FullName;
        var demo = Path.Combine(parent, "new-demo");
        try
        {
            await using var host = await AppHost.CreateAsync(demo, freshDemo: true);

            Assert.True(Directory.EnumerateFiles(Path.Combine(demo, "notes"), "*.md", SearchOption.AllDirectories).Any());
            // It starts on today's work note, like a user on their last one (the startup measurement opens it).
            Assert.Matches(@"^\d{4}/\d{2}/\d{2}/work_\d{4}\.md$", host.Settings.Current.LastNotePath);
            Assert.True(File.Exists(host.Engine.GetFullPath(host.Settings.Current.LastNotePath!)));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task Fresh_demo_replaces_the_previous_demo_notes()
    {
        var demo = Directory.CreateTempSubdirectory("wolfmark-demo-").FullName;
        try
        {
            await (await AppHost.CreateAsync(demo)).DisposeAsync();
            var stale = Path.Combine(demo, "notes", "stale.md");
            await File.WriteAllTextAsync(stale, "old", Ct);

            await using var host = await AppHost.CreateAsync(demo, freshDemo: true);

            Assert.False(File.Exists(stale));
            Assert.True(Directory.EnumerateFiles(Path.Combine(demo, "notes"), "*.md", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(demo, recursive: true);
        }
    }

    /// <summary>A start that cannot read the tags or open the index says so instead of leaving empty lists without a word.</summary>
    [Fact]
    public async Task A_failed_start_is_reported()
    {
        var demo = Directory.CreateTempSubdirectory("wolfmark-demo-").FullName;
        try
        {
            await using var host = await AppHost.CreateAsync(demo);
            // A folder with content where the index file belongs: it can be neither opened nor replaced.
            Directory.CreateDirectory(Path.Combine(host.Paths.IndexDatabase, "x"));
            var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.StartupFailed += (_, ex) => failed.TrySetResult(ex);

            host.StartBackground();

            var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.Same(error, host.StartupError);
            Assert.False(host.DatabaseReady.IsCompleted);
        }
        finally
        {
            Directory.Delete(demo, recursive: true);
        }
    }

    /// <summary>The screenshot run waits for the demo's transcripts: it must not depend on listening early enough.</summary>
    [Fact]
    public async Task The_first_claude_code_import_can_be_awaited_after_it_happened()
    {
        var demo = Directory.CreateTempSubdirectory("wolfmark-demo-").FullName;
        try
        {
            await using var host = await AppHost.CreateAsync(demo);
            var synced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ClaudeSynced += (_, _) => synced.TrySetResult();
            host.StartBackground();
            await synced.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);

            await host.FirstClaudeSync.WaitAsync(TimeSpan.FromSeconds(1), Ct); // a late listener still gets it

            Assert.True(Directory.EnumerateFiles(Path.Combine(demo, "notes"), "ClaudeCode_*.md", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(demo, recursive: true);
        }
    }
}
