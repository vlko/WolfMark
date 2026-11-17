namespace WolfMark.Sync.Share.Tests;

public sealed class ShareReceiverTests : IDisposable
{
    private readonly ShareFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task A_share_becomes_a_note_with_the_Share_tag_at_the_phones_time()
    {
        var response = await _f.Receiver.ReceiveAsync(_f.Message("# Example\nhttps://example.com/x\n"), CancellationToken.None);

        Assert.Equal(new ShareResponse(ShareStatus.Created, "2026/11/17/Share_1403.md"), response);
        Assert.Equal(["2026/11/17/Share_1403.md"], _f.Notes());
        Assert.Equal("# Example\nhttps://example.com/x\n", _f.Read("2026/11/17/Share_1403.md"));
    }

    [Fact]
    public async Task A_long_text_is_stored_whole()
    {
        var text = "# Article\n\n" + string.Join("\n\n", Enumerable.Range(0, 4000).Select(i => $"Odsek {i} – žltá ruža."));

        var response = await _f.Receiver.ReceiveAsync(_f.Message(text), CancellationToken.None);

        Assert.Equal(ShareStatus.Created, response.Status);
        Assert.Equal(text, _f.Read(response.NotePath));
    }

    [Fact]
    public async Task The_same_share_sent_again_is_not_written_twice()
    {
        var id = Guid.NewGuid();
        var first = await _f.Receiver.ReceiveAsync(_f.Message("one", id), CancellationToken.None);
        var again = await _f.Receiver.ReceiveAsync(_f.Message("one", id), CancellationToken.None);

        Assert.Equal(ShareStatus.Created, first.Status);
        Assert.Equal(new ShareResponse(ShareStatus.Duplicate, first.NotePath), again);
        Assert.Single(_f.Notes());
    }

    [Fact]
    public async Task Shares_in_the_same_minute_get_their_own_names()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => _f.Receiver.ReceiveAsync(_f.Message("note " + i), CancellationToken.None)));

        Assert.All(results, r => Assert.Equal(ShareStatus.Created, r.Status));
        Assert.Equal(["2026/11/17/Share_1403.md", "2026/11/17/Share_140327-2.md", "2026/11/17/Share_140327.md"], _f.Notes());
    }

    [Fact]
    public async Task Images_are_stored_in_the_days_assets_and_linked_from_the_note()
    {
        var images = Enumerable.Range(0, 11).Select(i => new ShareAttachment(i == 0 ? "jpg" : "png", new byte[] { (byte)i })).ToArray();
        // 10 and 1 share a prefix; the last image is not mentioned in the text at all.
        var message = _f.Message("# Photos\n\n![](wolfmark-attachment:10)\n![](wolfmark-attachment:1)\n![a](wolfmark-attachment:0)\n", attachments: images[..11]);
        message = message with { Markdown = message.Markdown.Replace("![](wolfmark-attachment:1)\n", "![](wolfmark-attachment:1)\n" + string.Concat(Enumerable.Range(2, 7).Select(i => $"![](wolfmark-attachment:{i})\n")), StringComparison.Ordinal) };

        var response = await _f.Receiver.ReceiveAsync(message, CancellationToken.None);

        Assert.Equal(ShareStatus.Created, response.Status);
        var text = _f.Read(response.NotePath);
        Assert.DoesNotContain("wolfmark-attachment", text, StringComparison.Ordinal);
        var links = System.Text.RegularExpressions.Regex.Matches(text, @"\((assets/[^)]+)\)").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(11, links.Distinct().Count());
        // Each link leads to the bytes of the image it stood for: 10 first, then 1 … 8, then 0, and 9 appended.
        int[] expected = [10, 1, 2, 3, 4, 5, 6, 7, 8, 0, 9];
        Assert.Equal(expected.Select(i => (byte)i), links.Select(l => File.ReadAllBytes(Path.Combine(_f.Root, "2026/11/17", l))[0]));
        Assert.EndsWith(".jpg", links[9], StringComparison.Ordinal);
        Assert.StartsWith("assets/1403-", links[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-11)]
    [InlineData(11)]
    public async Task A_frame_sent_long_ago_or_in_the_future_is_refused(int minutes)
    {
        var message = _f.Message("old") with { SentAt = _f.Time.GetUtcNow().AddMinutes(minutes) };

        var response = await _f.Receiver.ReceiveAsync(message, CancellationToken.None);

        Assert.Equal(ShareStatus.Refused, response.Status);
        Assert.Empty(_f.Notes());
    }

    [Fact]
    public async Task A_share_made_days_ago_and_delivered_now_keeps_its_day()
    {
        // The offline queue: shared on the 14th, delivered on the 17th.
        var response = await _f.Receiver.ReceiveAsync(_f.Message("late", created: new DateTime(2026, 11, 14, 8, 15, 0)), CancellationToken.None);

        Assert.Equal(new ShareResponse(ShareStatus.Created, "2026/11/14/Share_0815.md"), response);
    }

    [Theory]
    [InlineData(1999, 12, 31)]
    [InlineData(2026, 11, 20)]
    public async Task An_impossible_note_time_is_refused(int year, int month, int day)
    {
        var response = await _f.Receiver.ReceiveAsync(_f.Message("x", created: new DateTime(year, month, day)), CancellationToken.None);

        Assert.Equal(ShareStatus.Refused, response.Status);
    }
}
