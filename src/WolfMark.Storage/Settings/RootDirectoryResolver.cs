namespace WolfMark.Storage.Settings;

/// <summary>Default notes root (SPEC §3.1): OneDrive → OneDriveConsumer → OneDriveCommercial → Documents.</summary>
public static class RootDirectoryResolver
{
    public const string FolderName = "WolfMark";

    private static readonly string[] OneDriveVariables = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public static string ResolveDefault() =>
        ResolveDefault(
            Environment.GetEnvironmentVariable,
            Directory.Exists,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    public static string ResolveDefault(Func<string, string?> getEnvironmentVariable, Func<string, bool> directoryExists, string documentsFolder)
    {
        foreach (var variable in OneDriveVariables)
        {
            var value = getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && directoryExists(value))
            {
                return Path.Combine(value, FolderName);
            }
        }

        return Path.Combine(documentsFolder, FolderName);
    }

    public static bool IsInOneDrive(string path, Func<string, string?> getEnvironmentVariable)
    {
        foreach (var variable in OneDriveVariables)
        {
            var value = getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)
                && Path.GetFullPath(path).StartsWith(Path.GetFullPath(value), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
