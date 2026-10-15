namespace Zapisnik.Sync.ClaudeCode.Tests;

public sealed class TranscriptParserTests
{
    [Fact]
    public void Keeps_only_human_prompts()
    {
        var prompts = TranscriptParser.Parse(Samples.MixedTranscript).ToList();
        Assert.Equal(["Refaktoruj indexer", "Pridaj testy", "/review PR 42", "!git status", "Over AOT build"], prompts.Select(p => p.Text));
        Assert.All(prompts, p => Assert.Equal(Samples.Session, p.SessionId));
        Assert.Equal("main", prompts[0].GitBranch);
        Assert.Null(prompts[1].GitBranch);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 10, 0, TimeSpan.Zero), prompts[1].Timestamp);
    }

    [Fact]
    public void Strips_system_reminders()
    {
        var line = Samples.User("x", "2026-10-15T12:00:00Z", Samples.Text("<system-reminder>ignore</system-reminder>\nAhoj"));
        Assert.True(TranscriptParser.TryParseLine(line, out var prompt));
        Assert.Equal("Ahoj", prompt.Text);

        var onlyReminder = Samples.User("y", "2026-10-15T12:00:00Z", Samples.Text("<system-reminder>ignore</system-reminder>"));
        Assert.False(TranscriptParser.TryParseLine(onlyReminder, out _));
    }

    [Fact]
    public async Task Stream_reader_skips_bad_lines()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', Samples.MixedTranscript));
        using var stream = new MemoryStream(bytes);
        var count = 0;
        await foreach (var _ in TranscriptParser.ReadAsync(stream, TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(5, count);
    }
}
