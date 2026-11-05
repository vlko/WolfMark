using WolfMark.App.Infrastructure;

namespace WolfMark.Presentation.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Demo_directory_is_made_absolute()
    {
        var options = LaunchOptions.Parse(["WolfMark", "--demo", "demo"]);

        Assert.True(options.IsDemo);
        Assert.Equal(Path.GetFullPath("demo"), options.DemoDirectory);
        Assert.Null(options.ScreenshotDirectory);
    }

    [Fact]
    public void Screenshots_always_run_on_demo_data()
    {
        var options = LaunchOptions.Parse(["WolfMark", "--screenshots", "shots"]);

        Assert.True(options.IsDemo);
        Assert.Equal(Path.GetFullPath("shots"), options.ScreenshotDirectory);
    }

    [Fact]
    public void No_switches_means_the_real_notes()
    {
        var options = LaunchOptions.Parse(["WolfMark"]);

        Assert.False(options.IsDemo);
        Assert.False(options.TraceEditor);
    }

    [Fact]
    public void Editor_trace_is_a_flag_anywhere_on_the_line()
    {
        Assert.True(LaunchOptions.Parse(["WolfMark", "--trace-editor"]).TraceEditor);
        var both = LaunchOptions.Parse(["WolfMark", "--trace-editor", "--demo", "demo"]);
        Assert.True(both.TraceEditor);
        Assert.Equal(Path.GetFullPath("demo"), both.DemoDirectory);
    }
}

public sealed class OpenFileArgumentTests
{
    [Fact]
    public void A_file_argument_is_made_absolute()
    {
        var options = LaunchOptions.Parse(["WolfMark", "notes/readme.md"]);

        Assert.Equal(Path.GetFullPath("notes/readme.md"), options.OpenFile);
        Assert.False(options.IsDemo);
    }

    [Fact]
    public void Switch_values_are_not_the_file()
    {
        var options = LaunchOptions.Parse(["WolfMark", "--demo", "demo", "--trace-editor", "plan.md", "--autostart"]);

        Assert.Equal(Path.GetFullPath("demo"), options.DemoDirectory);
        Assert.True(options.TraceEditor);
        Assert.Equal(Path.GetFullPath("plan.md"), options.OpenFile);
    }

    [Fact]
    public void The_program_and_macos_process_serial_numbers_are_no_files()
    {
        Assert.Null(LaunchOptions.Parse(["/Applications/WolfMark.app/Contents/MacOS/WolfMark"]).OpenFile);
        Assert.Null(LaunchOptions.Parse(["WolfMark", "-psn_0_12345"]).OpenFile);
    }
}

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("WolfMark.exe C:\\notes\\a.md", new[] { "WolfMark.exe", "C:\\notes\\a.md" })]
    [InlineData("\"C:\\Program Files\\WolfMark\\WolfMark.exe\" \"C:\\My notes\\a b.md\"", new[] { "C:\\Program Files\\WolfMark\\WolfMark.exe", "C:\\My notes\\a b.md" })]
    [InlineData("  a   b  ", new[] { "a", "b" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("a\\\\\"b c\"", new[] { "a\\b c" })] // 2n backslashes + quote: n backslashes, the quote groups
    [InlineData("a\\\"b", new[] { "a\"b" })] // 2n+1: n backslashes and a literal quote
    [InlineData("C:\\dir\\", new[] { "C:\\dir\\" })] // backslashes not before a quote stay
    public void Splits_like_windows(string commandLine, string[] expected) =>
        Assert.Equal(expected, CommandLine.Split(commandLine));
}

public sealed class InstanceIdentityTests
{
    [Fact]
    public void The_same_folder_gives_the_same_key_however_it_is_written()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wolfmark-data");

        Assert.Equal(InstanceIdentity.Key(folder), InstanceIdentity.Key(folder + Path.DirectorySeparatorChar));
        Assert.NotEqual(InstanceIdentity.Key(folder), InstanceIdentity.Key(folder + "2"));
        Assert.StartsWith("WolfMark.", InstanceIdentity.Key(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void Demo_data_is_its_own_instance()
    {
        var demo = LaunchOptions.Parse(["WolfMark", "--demo", "demo"]);

        Assert.Equal(Path.Combine(Path.GetFullPath("demo"), "local"), InstanceIdentity.DataDirectory(demo));
    }
}

public sealed class StringsTests
{
    [Fact]
    public void Host_lookup_wins_and_missing_values_fall_back_to_english()
    {
        try
        {
            Strings.Source = key => key == R.AppTitle ? "Vlčia značka" : key == R.Search_NoResults ? string.Empty : null;

            Assert.Equal("Vlčia značka", Strings.Get(R.AppTitle));
            Assert.Equal("No results", Strings.Get(R.Search_NoResults)); // empty → fallback
            Assert.Equal("Unknown_Key", Strings.Get("Unknown_Key"));

            Strings.Source = _ => throw new InvalidOperationException("resources gone");
            Assert.Equal("WolfMark", Strings.Get(R.AppTitle));
        }
        finally
        {
            Strings.Source = null;
        }
    }
}

public sealed class UiThreadTests
{
    private sealed class QueueDispatcher : IUiDispatcher
    {
        public Queue<Action> Queue { get; } = new();

        public bool HasThreadAccess => true;

        public bool TryEnqueue(Action action)
        {
            Queue.Enqueue(action);
            return true;
        }
    }

    [Fact]
    public async Task Work_runs_on_the_dispatcher_and_RunAsync_completes_after_it()
    {
        var dispatcher = new QueueDispatcher();
        UiThread.Initialize(dispatcher);
        var ran = 0;

        UiThread.Post(() => ran++);
        var task = UiThread.RunAsync(() =>
        {
            ran++;
            return Task.CompletedTask;
        });

        Assert.Equal(0, ran); // nothing runs until the UI thread takes it
        while (dispatcher.Queue.TryDequeue(out var action))
        {
            action();
        }

        await task;
        Assert.Equal(2, ran);
        Assert.True(UiThread.HasAccess);
    }
}
