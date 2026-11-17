using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Calendar;
using WolfMark.Core.Notes;

namespace WolfMark.Presentation.Tests;

/// <summary>M9-01: the "Open tasks" list of both apps – the tasks under their notes, ticked off from the list.</summary>
public sealed class OpenTasksTests : IAsyncLifetime
{
    private static readonly DateTime At = DateTime.Today.AddHours(14).AddMinutes(3);
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-tasks-").FullName;
    private AppHost _host = null!;
    private MainViewModel _vm = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _vm = new MainViewModel(_host);
    }

    [Fact]
    public async Task The_list_shows_each_note_followed_by_its_open_tasks()
    {
        var first = await _host.Engine.CreateAsync(At, "praca", "# Plán\n- [ ] prvá\n- [x] hotová\n- [ ] druhá", Ct);
        await _host.Engine.CreateAsync(At.AddMinutes(1), null, "# Bez úloh", Ct);
        await IndexAsync();

        await _vm.History.ToggleOpenTasksAsync();

        // The demo brings notes of its own: the test looks at the one it wrote.
        var items = _vm.History.Items;
        var row = items.IndexOf(items.Single(i => i is HistoryRowViewModel r && r.Note.RelativePath == first.RelativePath));
        Assert.Equal((1, "prvá"), Task(items[row + 1]));
        Assert.Equal((3, "druhá"), Task(items[row + 2]));
        Assert.True(row + 3 == items.Count || items[row + 3] is not HistoryTaskViewModel);
        Assert.IsType<HistoryHeaderViewModel>(items[0]);
        Assert.DoesNotContain(items, i => i is HistoryRowViewModel { Note.OpenTasks: 0 });
        Assert.Equal(items.OfType<HistoryTaskViewModel>().Count(), _vm.History.OpenTaskCount);
        Assert.EndsWith(_vm.History.OpenTaskCount.ToString(System.Globalization.CultureInfo.CurrentCulture), _vm.History.OpenTasksLabel);

        static (int, string) Task(object item)
        {
            var task = Assert.IsType<HistoryTaskViewModel>(item);
            return (task.Line, task.Text);
        }
    }

    [Fact]
    public async Task With_nothing_to_do_under_a_tag_the_list_says_so()
    {
        await _host.Engine.CreateAsync(At, "hotovo", "# Bez úloh\n- [x] hotová", Ct);
        await IndexAsync();

        // The tag filter narrows the tasks too: the demo's own open tasks are under other tags.
        await _vm.History.SetTagFilterAsync("hotovo");
        Assert.False(_vm.History.IsEmpty);
        await _vm.History.ToggleOpenTasksAsync();

        Assert.True(_vm.History.IsEmpty);
        Assert.Equal(Strings.Get(R.History_NoOpenTasks), _vm.History.EmptyText);
    }

    [Fact]
    public async Task Ticking_a_task_of_another_note_changes_its_file()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Plán\r\n- [ ] prvá\r\n- [ ] druhá\r\n", Ct);
        var task = new HistoryTaskViewModel(Summary(path), 2, "druhá");

        await _vm.CompleteTaskAsync(task);

        Assert.True(task.IsDone);
        Assert.Null(_vm.ErrorNotice);
        Assert.Equal("# Plán\r\n- [ ] prvá\r\n- [x] druhá\r\n", await File.ReadAllTextAsync(_host.Engine.GetFullPath(path), Ct));
    }

    [Fact]
    public async Task Ticking_a_task_of_the_open_note_edits_the_editor_and_saves()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Plán\n- [ ] prvá", Ct);
        await _vm.OpenNoteAsync(path.RelativePath);
        _vm.Editor.OnTextEdited("# Plán\nrozpísané\n- [ ] prvá"); // not saved yet: the task is one line lower than in the index

        var task = new HistoryTaskViewModel(Summary(path), 1, "prvá");
        await _vm.CompleteTaskAsync(task);

        Assert.True(task.IsDone);
        Assert.Equal("# Plán\nrozpísané\n- [x] prvá", _vm.Editor.Text);
        Assert.Equal("# Plán\nrozpísané\n- [x] prvá", await File.ReadAllTextAsync(_host.Engine.GetFullPath(path), Ct));
    }

    /// <summary>The view applies the edit in the editor control (undo, caret); the view model only asks for it.</summary>
    [Fact]
    public async Task With_a_view_attached_the_edit_goes_through_it()
    {
        var path = await _host.Engine.CreateAsync(At, null, "- [ ] prvá", Ct);
        await _vm.OpenNoteAsync(path.RelativePath);
        var edits = new List<WolfMark.Core.Markdown.TextEdit>();
        _vm.Editor.EditRequested += (_, edit) => edits.Add(edit);

        Assert.True(_vm.Editor.CompleteTask(0, "prvá"));

        var edit = Assert.Single(edits);
        Assert.Equal((3, 1, "x"), (edit.Start, edit.Length, edit.Insert));
    }

    [Fact]
    public async Task A_task_that_is_gone_is_reported_and_the_row_comes_back()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Plán\n- [x] prvá", Ct);
        var task = new HistoryTaskViewModel(Summary(path), 1, "prvá");

        await _vm.CompleteTaskAsync(task);

        Assert.False(task.IsDone);
        Assert.Equal(Strings.Get(R.Task_CompleteFailed), _vm.ErrorNotice);
        Assert.Equal("# Plán\n- [x] prvá", await File.ReadAllTextAsync(_host.Engine.GetFullPath(path), Ct));
    }

    [Fact]
    public async Task Tasks_of_a_claude_code_note_cannot_be_ticked()
    {
        var path = await _host.Engine.CreateAsync(At, "claudecode", "- [ ] z promptu", Ct);
        var task = new HistoryTaskViewModel(Summary(path) with { Source = WolfMark.Index.Queries.NoteSources.ClaudeCode }, 0, "z promptu");

        await _vm.CompleteTaskAsync(task);

        Assert.False(task.CanComplete);
        Assert.False(task.IsDone);
        Assert.Equal("- [ ] z promptu", await File.ReadAllTextAsync(_host.Engine.GetFullPath(path), Ct));
    }

    [Fact]
    public async Task Opening_a_task_puts_the_caret_on_its_line()
    {
        var path = await _host.Engine.CreateAsync(At, null, "# Plán\n- [ ] prvá\n- [ ] druhá", Ct);
        LoadRequest? request = null;
        _vm.Editor.LoadRequested += (_, r) => request = r;

        await _vm.OpenNoteAsync(path.RelativePath, line: 2);

        Assert.Equal("# Plán\n- [ ] prvá\n".Length, request?.CaretOffset);
        Assert.False(request?.CaretToEnd);
    }

    [Fact]
    public void A_day_with_open_tasks_says_how_many()
    {
        var day = new DateOnly(2026, 11, 17);

        Assert.Contains(Strings.OpenTasks(3), DayFormatting.Describe(day, new CalendarDay(day, 1, [], 3, 0)));
        Assert.DoesNotContain(Strings.OpenTasks(1), DayFormatting.Describe(day, new CalendarDay(day, 1, [], 0, 2)));
    }

    private static WolfMark.Index.Queries.NoteSummary Summary(NotePath path) => new(
        1, path.RelativePath, At, DateOnly.FromDateTime(At), path.Tag, "Plán", string.Empty, 0, 1, 0,
        WolfMark.Index.Queries.NoteSources.User, IsStandard: true, IsConflict: false, IsPlaceholder: false, At.ToUniversalTime().Ticks);

    /// <summary>The index of the real start, read to the end before the test looks at it.</summary>
    private async Task IndexAsync()
    {
        _host.StartBackground();
        await _host.DatabaseReady.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        try
        {
            Directory.Delete(_demo, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
