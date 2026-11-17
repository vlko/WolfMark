using System.Globalization;
using WolfMark.App.Infrastructure;

namespace WolfMark.App.ViewModels;

/// <summary>Texts around the open note that both apps show the same way: the day heading, the save state, the times.</summary>
internal static class NoteTexts
{
    /// <summary>
    /// A saved window position back on a screen: never larger than the work area and never (partly) outside it –
    /// the monitor it was on may be gone or smaller now. All values in the screen's physical pixels.
    /// </summary>
    public static (int X, int Y, int Width, int Height) FitWindow(int x, int y, int width, int height, int areaX, int areaY, int areaWidth, int areaHeight)
    {
        width = Math.Min(width, areaWidth);
        height = Math.Min(height, areaHeight);
        return (
            Math.Clamp(x, areaX, areaX + areaWidth - width),
            Math.Clamp(y, areaY, areaY + areaHeight - height),
            width,
            height);
    }

    /// <summary>The day heading: "17", "NOVEMBER 2026", "UTOROK" in the formats culture.</summary>
    public static (string Number, string Month, string Weekday) DayHeading(DateOnly day)
    {
        var c = CultureInfo.CurrentCulture;
        return (
            day.Day.ToString(c),
            (c.DateTimeFormat.GetMonthName(day.Month) + " " + day.Year).ToUpper(c),
            c.DateTimeFormat.GetDayName(day.DayOfWeek).ToUpper(c));
    }

    /// <summary>The save state next to the tag, in capitals.</summary>
    public static string SaveState(SaveState state) => Strings.Get(state switch
    {
        ViewModels.SaveState.Saved => R.Save_Saved,
        ViewModels.SaveState.Saving => R.Save_Saving,
        ViewModels.SaveState.Pending => R.Save_Pending,
        ViewModels.SaveState.Error => R.Save_Error,
        ViewModels.SaveState.ReadOnly => R.Save_ReadOnly,
        _ => R.Save_New,
    }).ToUpper(CultureInfo.CurrentCulture);

    /// <summary>
    /// The note's times, in capitals: under the creation time in the gutter ("UPRAVENÉ 14:32", empty for a note not
    /// saved yet) and the one line of the full view ("VYTVORENÉ 09:05 · UPRAVENÉ 14:32"). A change on another day
    /// carries its date.
    /// </summary>
    public static (string Modified, string FullView) Times(DateTime createdAt, DateTime? modifiedAt)
    {
        var c = CultureInfo.CurrentCulture;
        var created = Strings.Format(R.FullView_Created, createdAt.ToString("t", c));
        if (modifiedAt is not { } modified)
        {
            return (string.Empty, created.ToUpper(c));
        }

        var when = DateOnly.FromDateTime(modified) == DateOnly.FromDateTime(createdAt)
            ? modified.ToString("t", c)
            : modified.ToString("d", c) + " " + modified.ToString("t", c);
        var edited = Strings.Format(R.Editor_Modified, when);
        return (edited.ToUpper(c), (created + " · " + edited).ToUpper(c));
    }
}
