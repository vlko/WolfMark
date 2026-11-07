using System.Security;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// What this copy of the app registers in the system (registry). Portable mode (ADR 0009) keeps nothing there:
/// switching it on removes this copy's entries, and the settings do not offer them.
/// </summary>
internal static class SystemIntegration
{
    /// <summary>Removes start at sign-in and the .md association of this exe; best effort, the mode switch already happened.</summary>
    public static void Remove()
    {
        try
        {
            if (StartupRegistration.PointsToThisApp)
            {
                StartupRegistration.Set(false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }

        try
        {
            if (FileAssociation.IsEnabled)
            {
                FileAssociation.Set(false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }
}
