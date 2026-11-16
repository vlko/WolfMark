namespace WolfMark.App.Infrastructure;

/// <summary>
/// A/B switch for startup optimisations (M4-09): <c>WOLFMARK_EXPERIMENT=name[,name]</c>. tools/smoke-windows.ps1
/// <c>-StartupExperiment name</c> alternates starts with and without it, so both see the same machine state.
/// </summary>
internal static class StartupExperiment
{
    private static readonly string[] s_active =
        (Environment.GetEnvironmentVariable("WOLFMARK_EXPERIMENT") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Is(string name) => s_active.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static string Active => s_active.Length == 0 ? "none" : string.Join(",", s_active);
}
