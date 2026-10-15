namespace Zapisnik.Core.Settings;

/// <summary><c>&lt;root&gt;/.zapisnik/tags.json</c> – travels with the notes (SPEC §3.5).</summary>
public sealed class TagsDocument
{
    public int Version { get; set; } = 1;

    public List<TagDefinition> Tags { get; set; } = [];
}

public sealed class TagDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"#RRGGBB"; null = palette colour.</summary>
    public string? Color { get; set; }

    public int Order { get; set; }

    /// <summary>Show titles of notes with this tag as labels under calendar weeks (SPEC §5.2).</summary>
    public bool ShowInCalendarLabels { get; set; }
}
