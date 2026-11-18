using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;

namespace WolfMark.Presentation.Tests;

/// <summary>M12: the desktop says so when a note shared from the phone arrives.</summary>
public sealed class ShareArrivalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("2026/11/18/Share_1403.md", ChangeOrigin.External, true)]
    [InlineData("2026/11/18/share_140327-2.md", ChangeOrigin.External, true)] // tags compare without case
    [InlineData("2026/11/18/Share_1403.md", ChangeOrigin.Local, false)] // tagged by hand here, or received by this app (reported by the receiver)
    [InlineData("2026/11/18/praca_1403.md", ChangeOrigin.External, false)]
    [InlineData("2026/11/18/1403.md", ChangeOrigin.External, false)]
    [InlineData("2026/11/18/Share_1403-LAPTOP.md", ChangeOrigin.External, false)] // a OneDrive conflict copy
    [InlineData("Share_1403.md", ChangeOrigin.External, false)] // not in a day folder
    public void Only_a_Share_note_put_there_by_someone_else_is_an_arrival(string path, ChangeOrigin origin, bool expected)
    {
        Assert.Equal(expected, ShareArrivals.IsArrival(new NoteCreated(NotePath.Parse(path), origin)));
    }

    [Fact]
    public void A_change_or_removal_of_a_Share_note_is_not_an_arrival()
    {
        var path = NotePath.Parse("2026/11/18/Share_1403.md");
        Assert.False(ShareArrivals.IsArrival(new NoteChanged(path, ChangeOrigin.External)));
        Assert.False(ShareArrivals.IsArrival(new NoteDeleted(path, ChangeOrigin.External)));
    }

    [Fact]
    public void The_notice_names_the_latest_share_and_counts_the_ones_before_it()
    {
        var first = ShareNoticeInfo.After(null, new ShareArrival(NotePath.Parse("2026/11/18/Share_1403.md"), "example.com"));
        Assert.Equal(new ShareNoticeInfo("2026/11/18/Share_1403.md", "example.com", 1), first);
        Assert.Equal("example.com", first.Message);

        var second = ShareNoticeInfo.After(first, new ShareArrival(NotePath.Parse("2026/11/18/Share_1404.md"), "Článok"));
        Assert.Equal("2026/11/18/Share_1404.md", second.RelativePath);
        Assert.Equal(2, second.Count);
        Assert.Contains("2", second.Message, StringComparison.Ordinal);
        Assert.Contains("Článok", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_share_file_brought_by_OneDrive_while_the_app_runs_is_announced_with_its_title()
    {
        var demo = Directory.CreateTempSubdirectory("wolfmark-share-").FullName;
        var host = await AppHost.CreateAsync(demo);
        try
        {
            var arrived = new TaskCompletionSource<ShareArrival>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ShareArrived += (_, a) => arrived.TrySetResult(a);
            host.StartBackground();
            await host.DatabaseReady.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            // The watcher starts right after the database opens; give it a moment, then do what OneDrive does.
            await Task.Delay(1500, Ct);
            var day = Path.Combine(host.Root, "2026", "11", "18");
            Directory.CreateDirectory(day);
            // A note of the user's own, written by another program, is not a share.
            await File.WriteAllTextAsync(Path.Combine(day, "praca_0900.md"), "# Nie zdieľanie\n", Ct);
            await File.WriteAllTextAsync(Path.Combine(day, "Share_1403.md"), "# Example Domain\nhttps://example.com/x\n", Ct);

            var arrival = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.Equal("2026/11/18/Share_1403.md", arrival.Path.RelativePath);
            Assert.Equal("Example Domain", arrival.Title);
        }
        finally
        {
            await host.DisposeAsync();
            Directory.Delete(demo, recursive: true);
        }
    }
}
