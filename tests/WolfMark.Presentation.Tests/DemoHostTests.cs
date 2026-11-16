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
}
