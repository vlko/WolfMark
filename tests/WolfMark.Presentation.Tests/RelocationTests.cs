using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;

namespace WolfMark.Presentation.Tests;

/// <summary>Settings → notes folder, shared by both apps: move the notes, or just switch to another folder.</summary>
public sealed class RelocationTests : IAsyncLifetime
{
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-move-").FullName;
    private AppHost _host = null!;
    private MainViewModel _vm = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _vm = new MainViewModel(_host);
    }

    [Fact]
    public async Task Moving_copies_the_notes_trashes_the_old_folder_and_saves_the_new_root()
    {
        var notes = Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length;
        var target = Path.Combine(_demo, "moved");

        var outcome = await _vm.MoveNotesAsync(target);

        Assert.True(outcome.RestartNow);
        Assert.False(outcome.Failed);
        Assert.Equal(notes, Directory.GetFiles(target, "*.md", SearchOption.AllDirectories).Length);
        Assert.False(Directory.Exists(_host.Root));
        Assert.Equal(target, _host.Settings.Current.RootDirectory);
        Assert.Contains("moved", await File.ReadAllTextAsync(_host.Paths.SettingsFile, Ct));
    }

    [Fact]
    public async Task A_move_that_cannot_work_keeps_the_notes_and_the_root_and_says_why()
    {
        var root = _host.Settings.Current.RootDirectory;
        var notes = Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length;

        var outcome = await _vm.MoveNotesAsync(Path.Combine(_host.Root, "inside")); // a folder inside itself

        Assert.False(outcome.RestartNow);
        Assert.True(outcome.Failed);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
        Assert.Equal(root, _host.Settings.Current.RootDirectory);
        Assert.Equal(notes, Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Switching_only_saves_the_new_root()
    {
        var notes = Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length;
        var target = Path.Combine(_demo, "elsewhere");

        await _vm.SwitchNotesRootAsync(target);

        Assert.Equal(target, _host.Settings.Current.RootDirectory);
        Assert.Equal(notes, Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length);
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData("/opt/wolfmark/WolfMark", null, "/opt/wolfmark/WolfMark --quick-note")]
    [InlineData("/opt/my apps/WolfMark", "/tmp/demo", "\"/opt/my apps/WolfMark\" --demo /tmp/demo --quick-note")]
    [InlineData(null, "/tmp/my demo", "wolfmark --demo \"/tmp/my demo\" --quick-note")]
    public void Quick_note_command_quotes_only_what_has_a_space(string? exe, string? demo, string expected)
    {
        Assert.Equal(expected, LaunchCommand.QuickNote(exe, demo));
    }

    [Fact]
    public void Registered_command_quotes_everything_and_keeps_the_demo_folder()
    {
        Assert.Equal("\"C:\\Apps\\WolfMark.exe\" \"%1\"", LaunchCommand.Quoted(@"C:\Apps\WolfMark.exe", null, "%1"));
        Assert.Equal("\"C:\\Apps\\WolfMark.exe\" --demo \"C:\\my demo\" \"%1\"", LaunchCommand.Quoted(@"C:\Apps\WolfMark.exe", @"C:\my demo", "%1"));
    }

    [Fact]
    public void Autostart_entry_starts_this_exe_with_its_demo_folder()
    {
        var entry = LaunchCommand.AutostartEntry("/opt/my apps/WolfMark", null);
        Assert.StartsWith("[Desktop Entry]\nType=Application\n", entry);
        Assert.Contains("\nExec=\"/opt/my apps/WolfMark\" --autostart\n", entry);

        Assert.Contains("\nExec=\"/opt/WolfMark\" --demo \"/tmp/my demo\" --autostart\n", LaunchCommand.AutostartEntry("/opt/WolfMark", "/tmp/my demo"));
    }

    [Fact]
    public void Launch_agent_lists_the_arguments_one_by_one_and_escapes_them()
    {
        var plist = LaunchCommand.LaunchAgentPlist("app.wolfmark.1a2b", "/Applications/Tom & Jerry/WolfMark.app/Contents/MacOS/WolfMark", "/tmp/my demo");

        Assert.Contains("<key>Label</key>\n  <string>app.wolfmark.1a2b</string>", plist);
        Assert.Contains("    <string>/Applications/Tom &amp; Jerry/WolfMark.app/Contents/MacOS/WolfMark</string>\n    <string>--demo</string>\n    <string>/tmp/my demo</string>\n    <string>--autostart</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>\n  <true/>", plist);
        Assert.DoesNotContain("--demo", LaunchCommand.LaunchAgentPlist("l", "/a", null));
    }

    [Theory]
    [InlineData("Search (Ctrl+K)", "Search (⌘K)")]
    [InlineData("Calendar (Ctrl+Shift+C)", "Calendar (⇧⌘C)")]
    [InlineData("Esc or Ctrl+Enter closes", "Esc or ⌘Enter closes")]
    [InlineData("No shortcut here", "No shortcut here")]
    public void Shortcuts_read_the_mac_way_on_a_mac(string text, string expected)
    {
        Assert.Equal(expected, Strings.MacKeys(text));
    }

    [Fact]
    public void Crash_entry_names_the_source_and_the_detail()
    {
        var entry = CrashFile.Format(new DateTime(2026, 11, 17, 9, 5, 7, 123), new InvalidOperationException("boom"), "XAML", "binding");

        Assert.StartsWith("2026-11-17 09:05:07.123 [XAML] (binding) System.InvalidOperationException: boom", entry);
        Assert.Contains("(no exception object)", CrashFile.Format(DateTime.Now, null, "TaskScheduler", null));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_demo, recursive: true);
    }
}
