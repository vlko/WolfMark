using System.Text.Json;
using WolfMark.Core.Tags;

namespace WolfMark.Core.Tests;

/// <summary>
/// <c>testdata/tag-colors.json</c> is also what the Android app's tag colours are tested against: a tag must have
/// the same colour on the phone as on the computer, also for tags nobody gave a colour to.
/// </summary>
public sealed class SharedTagColorVectorsTests
{
    private static readonly JsonElement Vectors = Load();

    private static JsonElement Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "tag-colors.json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Tags_without_a_chosen_colour_get_the_listed_one()
    {
        Assert.Equal(TagColor.FromHex(Vectors.GetProperty("untagged").GetString()!), TagPalette.ForTag(null));
        var count = 0;
        foreach (var v in Vectors.GetProperty("tags").EnumerateArray())
        {
            var tag = v.GetProperty("tag").GetString()!;
            Assert.Equal(v.GetProperty("hash").GetUInt32(), TagPalette.StableHash(TagName.ToKey(tag)));
            Assert.Equal(TagColor.FromHex(v.GetProperty("color").GetString()!), TagPalette.ForTag(tag));
            count++;
        }

        Assert.True(count > 10);
    }

    [Fact]
    public void Dark_or_white_text_on_a_colour_is_chosen_as_listed()
    {
        foreach (var v in Vectors.GetProperty("foreground").EnumerateArray())
        {
            var color = v.GetProperty("color").GetString()!;
            Assert.True(v.GetProperty("darkText").GetBoolean() == TagColor.FromHex(color).PrefersDarkForeground, color);
        }
    }
}
