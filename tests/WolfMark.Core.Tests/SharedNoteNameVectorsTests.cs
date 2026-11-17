using System.Text.Json;
using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

/// <summary>
/// <c>testdata/note-names.json</c> is also what the Android app's name parser is tested against: a phone that names
/// a note differently would write files the desktop reads as something else (a conflict copy, a non-standard note).
/// </summary>
public sealed class SharedNoteNameVectorsTests
{
    private static readonly JsonElement Vectors = Load();

    private static JsonElement Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "note-names.json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Valid_names_parse_as_listed_and_round_trip()
    {
        var count = 0;
        foreach (var v in Vectors.GetProperty("valid").EnumerateArray())
        {
            var file = v.GetProperty("file").GetString()!;
            Assert.True(NoteName.TryParse(file, out var name), file);
            Assert.Equal(v.GetProperty("tag").GetString(), name.Tag);
            Assert.Equal(new TimeOnly(v.GetProperty("hour").GetInt32(), v.GetProperty("minute").GetInt32(), v.GetProperty("second").GetInt32()), name.Time);
            Assert.Equal(v.GetProperty("hasSeconds").GetBoolean(), name.HasSeconds);
            Assert.Equal(v.GetProperty("suffix").GetInt32(), name.Suffix);
            Assert.Equal(v.GetProperty("conflict").GetString(), name.ConflictMarker);
            Assert.Equal(file, name.FileName, ignoreCase: true);
            count++;
        }

        Assert.True(count > 5);
    }

    [Fact]
    public void Invalid_names_are_rejected()
    {
        foreach (var v in Vectors.GetProperty("invalid").EnumerateArray())
        {
            Assert.False(NoteName.TryParse(v.GetString()!, out _), v.GetString());
        }
    }

    [Fact]
    public void A_taken_name_gets_seconds_and_then_a_number()
    {
        foreach (var v in Vectors.GetProperty("collisions").EnumerateArray())
        {
            var second = v.GetProperty("second").GetInt32();
            var name = NoteName.Create(v.GetProperty("tag").GetString(), new TimeOnly(v.GetProperty("hour").GetInt32(), v.GetProperty("minute").GetInt32(), second));
            foreach (var expected in v.GetProperty("candidates").EnumerateArray())
            {
                Assert.Equal(expected.GetString(), name.FileName);
                name = name.NextCollisionCandidate(second);
            }
        }
    }
}
