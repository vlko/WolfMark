using System.Diagnostics;
using WolfMark.App;
using WolfMark.App.ViewModels;

namespace WolfMark.Presentation.Tests;

/// <summary>
/// M4-05: killing the process after 1 s without typing loses no text. The test project is an executable
/// (Microsoft.Testing.Platform), so the test starts itself as a child process running only
/// <see cref="Child_types_then_idles"/>, which types into the real <see cref="NoteEditorViewModel"/> (shared by the
/// Windows and the Avalonia app) and then waits; the parent kills it without any shutdown or flush.
/// </summary>
public sealed class AutosaveKillTests
{
    private const string ChildDirectoryVariable = "WOLFMARK_KILL_TEST_DIR";
    private const string TypedMarker = "typed";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Killing_the_process_after_one_second_idle_keeps_the_text()
    {
        var dir = Directory.CreateTempSubdirectory("wolfmark-kill-").FullName;
        try
        {
            var text = $"# Rozpísaná poznámka\n\nTýchto pár viet nesmie zmiznúť {Guid.NewGuid():N}.";
            using var child = StartChild(dir, text);
            var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
            child.OutputDataReceived += (_, e) => output.Enqueue(e.Data ?? "");
            child.ErrorDataReceived += (_, e) => output.Enqueue(e.Data ?? "");
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();

            try
            {
                // Startup includes seeding the demo notes – generous; the measured part starts at the marker.
                var marker = Path.Combine(dir, TypedMarker);
                var wait = Stopwatch.StartNew();
                while (!File.Exists(marker))
                {
                    Assert.False(child.HasExited, "child exited before typing:\n" + string.Join('\n', output));
                    Assert.True(wait.Elapsed < TimeSpan.FromSeconds(60), "child did not type in time:\n" + string.Join('\n', output));
                    await Task.Delay(20, Ct);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), Ct);
                Assert.False(child.HasExited, "child must still be running when killed:\n" + string.Join('\n', output));
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                }

                await child.WaitForExitAsync(Ct);
            }

            var saved = Directory.EnumerateFiles(Path.Combine(dir, "notes"), "*.md", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f) == text)
                .ToList();
            Assert.Single(saved);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The child half; explicit, so it runs only when the parent asks for it.</summary>
    [Fact(Explicit = true)]
    public async Task Child_types_then_idles()
    {
        var dir = Environment.GetEnvironmentVariable(ChildDirectoryVariable);
        Assert.False(string.IsNullOrEmpty(dir), $"run only by {nameof(Killing_the_process_after_one_second_idle_keeps_the_text)}");
        var text = await File.ReadAllTextAsync(Path.Combine(dir, "text"), Ct);

        var host = await AppHost.CreateAsync(dir);
        var editor = new NoteEditorViewModel(host);

        // Warm up the save path first (JIT, first file write) – as in an app that has been running for a while.
        // Otherwise a slow CI runner could spend the 200 ms between the 800 ms debounce and the kill compiling.
        await editor.NewAsync("praca");
        editor.OnTextEdited("rozohriatie");
        await editor.FlushAsync();
        editor.OnTextEdited("rozohriatie 2");
        await editor.FlushAsync();

        await editor.NewAsync("praca");
        foreach (var length in new[] { 1, text.Length / 2, text.Length })
        {
            editor.OnTextEdited(text[..length]); // a few keystrokes, the last one completes the text
        }

        await File.WriteAllTextAsync(Path.Combine(dir, TypedMarker), "", Ct);
        await Task.Delay(Timeout.Infinite, Ct); // idle until killed – no flush, no dispose
    }

    private static Process StartChild(string dir, string text)
    {
        File.WriteAllText(Path.Combine(dir, "text"), text);
        // Started directly (not through dotnet test), the test executable runs the xunit console runner: -method etc.
        var assembly = typeof(AutosaveKillTests).Assembly.Location;
        var appHost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
        var start = File.Exists(appHost)
            ? new ProcessStartInfo(appHost)
            : new ProcessStartInfo("dotnet") { ArgumentList = { "exec", assembly } };
        foreach (var arg in new[] { "-method", $"{typeof(AutosaveKillTests).FullName}.{nameof(Child_types_then_idles)}", "-explicit", "only", "-noLogo", "-noColor" })
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment[ChildDirectoryVariable] = dir;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        return Process.Start(start) ?? throw new InvalidOperationException("child process did not start");
    }
}
