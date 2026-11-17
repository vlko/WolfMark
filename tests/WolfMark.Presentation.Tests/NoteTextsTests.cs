using System.Globalization;
using WolfMark.App.ViewModels;

namespace WolfMark.Presentation.Tests;

/// <summary>Texts around the open note, shared by both apps (formats follow the current culture).</summary>
public sealed class NoteTextsTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public NoteTextsTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    [Fact]
    public void Day_heading_is_number_month_with_year_and_weekday()
    {
        Assert.Equal(("17", "NOVEMBER 2026", "TUESDAY"), NoteTexts.DayHeading(new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public void A_note_not_saved_yet_has_only_its_creation_time()
    {
        var (modified, fullView) = NoteTexts.Times(new DateTime(2026, 11, 17, 9, 5, 0), null);

        Assert.Equal(string.Empty, modified);
        Assert.Contains("09:05", fullView);
        Assert.Equal(fullView.ToUpper(CultureInfo.CurrentCulture), fullView);
    }

    [Fact]
    public void A_change_on_the_same_day_shows_the_time_only()
    {
        var (modified, fullView) = NoteTexts.Times(new DateTime(2026, 11, 17, 9, 5, 0), new DateTime(2026, 11, 17, 14, 32, 0));

        Assert.Contains("14:32", modified);
        Assert.DoesNotContain("2026", modified);
        Assert.Contains("09:05", fullView);
        Assert.EndsWith(modified, fullView);
    }

    [Fact]
    public void A_change_on_another_day_carries_its_date()
    {
        var (modified, _) = NoteTexts.Times(new DateTime(2026, 11, 17, 9, 5, 0), new DateTime(2026, 11, 19, 14, 32, 0));

        Assert.Contains("19/11/2026 14:32", modified);
    }

    [Fact]
    public void Every_save_state_has_a_text_of_its_own()
    {
        var texts = Enum.GetValues<SaveState>().Select(NoteTexts.SaveState).ToList();

        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Theory]
    [InlineData(100, 80, 1180, 900, 100, 80, 1180, 900)] // fits: untouched
    [InlineData(2500, 80, 1180, 900, 740, 80, 1180, 900)] // was on a monitor to the right that is gone
    [InlineData(-300, -50, 1180, 900, 0, 0, 1180, 900)] // off the top left corner
    [InlineData(100, 80, 3000, 2000, 0, 0, 1920, 1040)] // saved on a larger screen
    public void A_saved_window_position_is_brought_back_onto_the_screen(int x, int y, int w, int h, int ex, int ey, int ew, int eh)
    {
        Assert.Equal((ex, ey, ew, eh), NoteTexts.FitWindow(x, y, w, h, 0, 0, 1920, 1040));
    }
}
