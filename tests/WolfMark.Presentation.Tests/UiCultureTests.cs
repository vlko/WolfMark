using System.Globalization;
using WolfMark.App.Infrastructure;

namespace WolfMark.Presentation.Tests;

[CollectionDefinition(nameof(ProcessCulture), DisableParallelization = true)]
public sealed class ProcessCulture;

/// <summary>Settings → language and "dates and times in the app's language" – the same in both apps.</summary>
[Collection(nameof(ProcessCulture))]
public sealed class UiCultureTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public UiCultureTests()
    {
        // The "system region" for these tests: English (US) with its 12-hour clock.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public void Language_with_its_formats_gives_slovak_day_names_and_24_hours()
    {
        Assert.True(UiCulture.Apply("sk-SK", formatsFromLanguage: true));

        Assert.Equal("sk", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Assert.Equal("pondelok", new DateTime(2026, 11, 16).ToString("dddd", CultureInfo.CurrentCulture));
        Assert.DoesNotContain('h', CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern);
        Assert.Equal("sk-SK", CultureInfo.DefaultThreadCurrentCulture?.Name); // background threads too
    }

    [Fact]
    public void Language_without_its_formats_keeps_the_region()
    {
        Assert.True(UiCulture.Apply("sk-SK", formatsFromLanguage: false));

        Assert.Equal("sk", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName); // texts Slovak
        Assert.Equal("Monday", new DateTime(2026, 11, 16).ToString("dddd", CultureInfo.CurrentCulture));
        Assert.Contains('h', CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a culture!")] // malformed; ICU accepts any well-formed tag as a custom culture
    public void System_language_or_an_unknown_one_changes_nothing(string? language)
    {
        Assert.False(UiCulture.Apply(language, formatsFromLanguage: true));

        Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
        Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
    }
}
