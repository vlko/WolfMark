namespace WolfMark.App.Infrastructure;

/// <summary>
/// A/B switch for startup optimisations (M4-09): <c>WOLFMARK_EXPERIMENT=name[,name]</c>. tools/smoke-windows.ps1
/// <c>-StartupExperiment name</c> alternates starts with and without it, so both see the same machine state.
/// Tried: "win2d-warmup" (Win2D device + DirectWrite on a pool thread from Main) – −35 ms in the editor, +22 ms in
/// the title bar and Activate (contention), net −17 ms = noise; removed 2026-11-16.
/// Open: "editor-defer-layout" (<c>MarkdownEditor.DeferInitialLayout</c>).
/// </summary>
internal static class StartupExperiment
{
    private static readonly string[] s_active =
        (Environment.GetEnvironmentVariable("WOLFMARK_EXPERIMENT") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Is(string name) => s_active.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static string Active => s_active.Length == 0 ? "none" : string.Join(",", s_active);
}
