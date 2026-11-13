using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace WolfMark.Sync.ClaudeCode.Tests;

/// <summary>M7-05: sync at start, every interval, on "Sync now" and (coalesced) on transcript writes.</summary>
public sealed class SyncSchedulerTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);
    private static readonly SyncResult Empty = new(0, 0, 0, 0);

    private readonly string _projects = Directory.CreateTempSubdirectory("wolfmark-sched-").FullName;
    private readonly Clock _clock = new();
    private readonly Channel<int> _runs = Channel.CreateUnbounded<int>();
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private int _runCount;
    private Func<int, Exception?> _failOn = _ => null;

    private ClaudeCodeSyncScheduler Start(bool watch, out Task loop, Action<ClaudeCodeSyncScheduler>? subscribe = null)
    {
        var scheduler = new ClaudeCodeSyncScheduler(
            _ =>
            {
                var n = Interlocked.Increment(ref _runCount);
                _runs.Writer.TryWrite(n);
                return _failOn(n) is { } ex ? Task.FromException<SyncResult>(ex) : Task.FromResult(Empty);
            },
            _projects,
            Interval,
            watch,
            _clock,
            NullLogger.Instance);
        subscribe?.Invoke(scheduler); // the first run happens synchronously inside RunAsync
        loop = scheduler.RunAsync(_stop.Token);
        return scheduler;
    }

    private async Task<int> NextRunAsync() =>
        await _runs.Reader.ReadAsync(_stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), _stop.Token);

    private async Task AssertNoRunAsync()
    {
        await Task.Delay(Quiet, _stop.Token);
        Assert.False(_runs.Reader.TryRead(out var n), $"unexpected run {n}");
    }

    /// <summary>Waits (in real time) until the loop has started its <paramref name="count"/>-th timer.</summary>
    private async Task WaitForTimersAsync(int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (_clock.Timers < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"timer {count} not started (have {_clock.Timers})");
            await Task.Delay(10, _stop.Token);
        }
    }

    [Fact]
    public async Task Runs_at_start_and_after_each_interval()
    {
        await using var scheduler = Start(watch: false, out _);
        Assert.Equal(1, await NextRunAsync());

        await WaitForTimersAsync(1);
        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        await AssertNoRunAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, await NextRunAsync());

        await WaitForTimersAsync(2);
        _clock.Advance(Interval);
        Assert.Equal(3, await NextRunAsync());
    }

    [Fact]
    public async Task Sync_now_runs_immediately_even_when_watching()
    {
        await using var scheduler = Start(watch: true, out _);
        Assert.Equal(1, await NextRunAsync());
        await WaitForTimersAsync(1);

        scheduler.Trigger();
        Assert.Equal(2, await NextRunAsync()); // no time advanced: no coalescing delay
    }

    [Fact]
    public async Task Burst_of_transcript_writes_is_one_run_after_the_window()
    {
        await using var scheduler = Start(watch: true, out _);
        Assert.Equal(1, await NextRunAsync());
        Assert.True(scheduler.IsWatching);
        await WaitForTimersAsync(1);

        var transcript = Path.Combine(_projects, "session.jsonl");
        await File.WriteAllTextAsync(transcript, "{}\n", _stop.Token);
        await WaitForTimersAsync(2); // the coalescing delay started
        await AssertNoRunAsync();

        // Claude Code keeps writing during the window.
        await File.AppendAllTextAsync(transcript, "{}\n", _stop.Token);
        await File.AppendAllTextAsync(transcript, "{}\n", _stop.Token);
        await Task.Delay(Quiet, _stop.Token); // let the watcher deliver them

        _clock.Advance(ClaudeCodeSyncScheduler.CoalesceDelay);
        Assert.Equal(2, await NextRunAsync());

        // The writes in the window were covered: back to waiting for the interval, no second coalescing round.
        await WaitForTimersAsync(3);
        await AssertNoRunAsync();
        Assert.Equal(3, _clock.Timers);
    }

    [Fact]
    public async Task Without_watching_transcript_writes_wait_for_the_interval()
    {
        await using var scheduler = Start(watch: false, out _);
        Assert.Equal(1, await NextRunAsync());
        Assert.False(scheduler.IsWatching);
        await WaitForTimersAsync(1);

        await File.WriteAllTextAsync(Path.Combine(_projects, "session.jsonl"), "{}\n", _stop.Token);
        await AssertNoRunAsync();
        _clock.Advance(Interval);
        Assert.Equal(2, await NextRunAsync());
    }

    [Fact]
    public async Task Failed_run_is_reported_and_the_schedule_goes_on()
    {
        _failOn = n => n == 1 ? new IOException("transcript locked") : null;
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var synced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = Start(watch: false, out var loop, s =>
        {
            s.Failed += (_, ex) => failed.TrySetResult(ex);
            s.Synced += (_, _) => synced.TrySetResult();
        });

        Assert.Equal(1, await NextRunAsync());
        Assert.IsType<IOException>(await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), _stop.Token));
        Assert.False(loop.IsCompleted);

        await WaitForTimersAsync(1);
        _clock.Advance(Interval);
        Assert.Equal(2, await NextRunAsync());
        await synced.Task.WaitAsync(TimeSpan.FromSeconds(10), _stop.Token);
    }

    [Fact]
    public async Task Cancellation_ends_the_loop()
    {
        await using var scheduler = Start(watch: true, out var loop);
        Assert.Equal(1, await NextRunAsync());
        await WaitForTimersAsync(1);

        await _stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        try
        {
            Directory.Delete(_projects, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Fake time that also tells how many timers (interval / coalescing delays) the loop started.</summary>
    private sealed class Clock : FakeTimeProvider
    {
        private int _timers;

        public int Timers => Volatile.Read(ref _timers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timers);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
