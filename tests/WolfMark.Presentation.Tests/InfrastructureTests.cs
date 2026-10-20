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
