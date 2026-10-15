using System.Text.Json.Serialization;
using Zapisnik.Core.Calendar;

namespace Zapisnik.Core.Settings;

/// <summary>Source-generated JSON metadata – no reflection serialisation anywhere (CLAUDE.md rule 1).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(LocalSettings))]
[JsonSerializable(typeof(TagsDocument))]
[JsonSerializable(typeof(List<TagCount>))]
public sealed partial class ZapisnikJsonContext : JsonSerializerContext;
