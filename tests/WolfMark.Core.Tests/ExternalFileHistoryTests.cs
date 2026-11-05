using WolfMark.Core.Settings;

namespace WolfMark.Core.Tests;

public sealed class ExternalFileHistoryTests
{
    private static readonly DateTime Noon = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Touch_puts_the_file_on_top_once()
    {
        var entries = new List<ExternalFileEntry>();
        ExternalFileHistory.Touch(entries, "/a.md", Noon);
        ExternalFileHistory.Touch(entries, "/b.md", Noon.AddMinutes(1));
        ExternalFileHistory.Touch(entries, "/a.md", Noon.AddMinutes(2));

        Assert.Equal(["/a.md", "/b.md"], entries.Select(e => e.Path));
        Assert.Equal(Noon.AddMinutes(2), entries[0].TouchedUtc);
    }

    [Fact]
    public void The_oldest_fall_off_past_the_limit()
    {
        var entries = new List<ExternalFileEntry>();
        for (var i = 0; i <= ExternalFileHistory.Limit; i++)
        {
            ExternalFileHistory.Touch(entries, $"/f{i}.md", Noon.AddMinutes(i));
        }

        Assert.Equal(ExternalFileHistory.Limit, entries.Count);
        Assert.Equal($"/f{ExternalFileHistory.Limit}.md", entries[0].Path);
        Assert.DoesNotContain(entries, e => e.Path == "/f0.md");
    }

    [Fact]
    public void Remove_drops_only_that_file()
    {
        var entries = new List<ExternalFileEntry>();
        ExternalFileHistory.Touch(entries, "/a.md", Noon);
        ExternalFileHistory.Touch(entries, "/b.md", Noon);

        Assert.True(ExternalFileHistory.Remove(entries, "/a.md"));
        Assert.False(ExternalFileHistory.Remove(entries, "/a.md"));
        Assert.Equal(["/b.md"], entries.Select(e => e.Path));
    }
}
