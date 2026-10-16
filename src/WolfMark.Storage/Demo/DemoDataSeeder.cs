using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Notes;
using WolfMark.Core.Tags;
using WolfMark.Storage.Tags;

namespace WolfMark.Storage.Demo;

/// <summary>
/// Demo content for <c>--demo</c> (README screenshots): about five weeks of English notes with a few tags and
/// Claude Code transcripts in the <c>~/.claude/projects</c> format, all relative to "now" so the calendar always
/// looks alive. Writes only into the given folders and only when the notes folder is still empty.
/// </summary>
public static class DemoDataSeeder
{
    private const int Weeks = 5;

    public static async Task<bool> SeedAsync(string notesRoot, string claudeDirectory, DateTime now, CancellationToken ct)
    {
        if (Directory.Exists(notesRoot) && Directory.EnumerateFileSystemEntries(notesRoot).Any())
        {
            return false;
        }

        Directory.CreateDirectory(notesRoot);
        var today = DateOnly.FromDateTime(now);
        var random = new Random(42);
        var start = today.AddDays(-7 * Weeks);
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            foreach (var (tag, time, text) in NotesFor(day, today, now, random))
            {
                await WriteNoteAsync(notesRoot, day, tag, time, text, ct).ConfigureAwait(false);
            }
        }

        await WriteTagsAsync(notesRoot, ct).ConfigureAwait(false);
        await WriteClaudeAsync(claudeDirectory, today, now, random, ct).ConfigureAwait(false);
        return true;
    }

    // ------------------------------------------------------------------ notes

    private static IEnumerable<(string? Tag, TimeOnly Time, string Text)> NotesFor(DateOnly day, DateOnly today, DateTime now, Random random)
    {
        var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var index = day.DayNumber - today.DayNumber; // 0 = today, negative = past
        if (index == 0)
        {
            // Today: a stand-up and the note that is open in the screenshots, both already in the past.
            yield return ("meeting", Earlier(now, 150), Standup(day, random));
            yield return ("work", Earlier(now, 40), MainNote());
            yield break;
        }

        if (index == -3)
        {
            yield return ("release", new TimeOnly(16, 30), Release());
        }

        if (weekend)
        {
            if (random.Next(3) > 0)
            {
                yield return (random.Next(2) == 0 ? "family" : "reading", new TimeOnly(10 + random.Next(6), 5 * random.Next(12)), Weekend(random));
            }

            yield break;
        }

        yield return ("meeting", new TimeOnly(9, 15 + random.Next(10)), Standup(day, random));
        if (random.Next(4) > 0)
        {
            yield return ("work", new TimeOnly(11 + random.Next(4), 5 * random.Next(12)), Work(random));
        }

        if (random.Next(3) == 0)
        {
            yield return ("ideas", new TimeOnly(17, 5 * random.Next(12)), Idea(random));
        }

        if (random.Next(4) == 0)
        {
            yield return (null, new TimeOnly(13, 5 * random.Next(12)), Untagged(random));
        }

        if (random.Next(5) == 0)
        {
            yield return ("reading", new TimeOnly(20, 5 * random.Next(10)), Reading(random));
        }
    }

    private static TimeOnly Earlier(DateTime now, int minutes)
    {
        var t = now.AddMinutes(-minutes);
        return t.Date == now.Date ? new TimeOnly(t.Hour, t.Minute) : new TimeOnly(0, 5);
    }

    private static readonly string[] StandupItems =
    [
        "Review open pull requests",
        "Sync with design about the calendar labels",
        "Follow up on the OneDrive conflict report",
        "Pair on the search ranking",
        "Triage new issues from the beta",
        "Check the nightly AOT publish",
        "Prepare the demo for Thursday",
        "Update the release checklist",
        "Write down the decision as an ADR",
        "Measure start-up time on the old laptop",
    ];

    private static string Standup(DateOnly day, Random random)
    {
        var items = StandupItems.OrderBy(_ => random.Next()).Take(3).ToArray();
        return $"""
            # Stand-up {day.ToString("ddd d MMM", CultureInfo.InvariantCulture)}

            - [x] {items[0]}
            - [{(random.Next(2) == 0 ? "x" : " ")}] {items[1]}
            - [ ] {items[2]}
            """;
    }

    private static string MainNote() => """
        # Calendar polish before 1.1

        Goal for this week: the calendar should explain the week *at a glance*.

        ## Done
        - [x] Week strip shows counts per tag and per Claude Code project
        - [x] Hatched past days, dashed frame for today
        - [x] Labels between weeks in the month view

        ## Next
        - [ ] Keyboard focus marker on calendar cells
        - [ ] Tray icon and **start with Windows**
        - [ ] README screenshots from the `--demo` data

        > Notes stay plain Markdown files – the index can always be rebuilt.

        Related: [release checklist](https://example.com/release) · #ideas
        """;

    private static string Release() => """
        ---
        tags: [release, work]
        ---
        # v1.0 release

        Shipped the first public build. Native AOT, starts in well under a second.

        - [x] Signed the installer
        - [x] Published release notes
        - [x] Announced in the team channel
        """;

    private static readonly string[] WorkTopics =
    [
        "Index rebuild after a crash",
        "Faster history paging",
        "Spell check in the editor",
        "Atomic writes on OneDrive",
        "Search ranking with embeddings",
        "Tag colours in dark mode",
        "Conflict copies from a second PC",
    ];

    private static string Work(Random random)
    {
        var topic = WorkTopics[random.Next(WorkTopics.Length)];
        return $"""
            # {topic}

            Investigated the current behaviour and wrote down the options.

            - [x] Reproduce locally
            - [{(random.Next(2) == 0 ? "x" : " ")}] Write a unit test
            - [ ] Measure before/after

            `dotnet test --solution WolfMark.CrossPlatform.slnf` is green.
            """;
    }

    private static readonly string[] Ideas =
    [
        "Quick capture window on a global shortcut",
        "Weekly review note generated every Friday",
        "Jump list with the last five notes",
        "Export a month as one PDF",
    ];

    private static string Idea(Random random) => $"""
        # Idea: {Ideas[random.Next(Ideas.Length)]}

        Small, but would save a few clicks every day. Worth a spike.
        """;

    private static readonly string[] Books = ["A Philosophy of Software Design", "The Pragmatic Programmer", "Designing Data-Intensive Applications", "Thinking, Fast and Slow"];

    private static string Reading(Random random) => $"""
        # Reading: {Books[random.Next(Books.Length)]}

        > Complexity is anything that makes software hard to understand or to modify.

        Two chapters tonight, notes in the margin.
        """;

    private static string Weekend(Random random) => random.Next(2) == 0
        ? """
          # Saturday

          - [x] Farmers market
          - [ ] Fix the bike light
          """
        : """
          # Hike plan

          Ridge trail, start 8:00, back before the rain.
          """;

    private static string Untagged(Random random) => random.Next(2) == 0
        ? "Call the dentist back, Thursday after 3 pm works."
        : "Parking spot moved to level -2 from next month.";

    private static async Task WriteNoteAsync(string root, DateOnly day, string? tag, TimeOnly time, string text, CancellationToken ct)
    {
        var path = NotePath.Create(day, NoteName.Create(tag, time));
        var full = Path.Combine(root, path.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
        {
            return; // same minute twice – the demo does not need it
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text.ReplaceLineEndings("\n") + "\n", ct).ConfigureAwait(false);
        var created = day.ToDateTime(time);
        File.SetLastWriteTime(full, created.AddMinutes(20));
    }

    private static async Task WriteTagsAsync(string root, CancellationToken ct)
    {
        var tags = new TagStore(root, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await tags.LoadAsync(ct).ConfigureAwait(false);
        (string Tag, string Color)[] colours =
        [
            ("work", "#2F6FEB"),
            ("meeting", "#F2B705"),
            ("ideas", "#8E44AD"),
            ("reading", "#16A085"),
            ("family", "#E84A5F"),
            ("release", "#00A884"),
        ];
        var order = 0;
        foreach (var (tag, color) in colours)
        {
            await tags.SetColorAsync(tag, TagColor.FromHex(color), ct).ConfigureAwait(false);
            await tags.SetOrderAsync(tag, order++, ct).ConfigureAwait(false);
        }

        await tags.SetShowInCalendarLabelsAsync("release", true, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Claude Code

    private static readonly (string Cwd, string[] Prompts)[] Projects =
    [
        (@"C:\src\WolfMark",
        [
            "Add per-tag counts under the week strip in the calendar panel",
            "Claude Code notes should be counted per project, not under one tag",
            "Write a unit test for GetWeekTopicsAsync with untagged notes",
            "The strikethrough icon in the menu looks odd, the bar does not cross the S",
            "Implement a tray icon with Show / Settings / Exit",
        ]),
        (@"C:\src\billing-api",
        [
            "Why does the invoice export time out for large customers?",
            "Add pagination to GET /invoices and keep the old contract working",
            "Use this key for the staging test: sk-ant-api03-DEMO0000000000000000000000000000",
            "Generate an OpenAPI example for the new endpoint",
        ]),
        (@"C:\src\infra-terraform",
        [
            "Plan the move of the storage account to a private endpoint",
            "Explain the diff in this terraform plan output",
            "Add a budget alert at 80% for the dev subscription",
        ]),
    ];

    private static async Task WriteClaudeAsync(string claudeDirectory, DateOnly today, DateTime now, Random random, CancellationToken ct)
    {
        var projects = Path.Combine(claudeDirectory, "projects");
        Directory.CreateDirectory(projects);
        await File.WriteAllTextAsync(Path.Combine(claudeDirectory, "settings.json"), "{ \"cleanupPeriodDays\": 365 }\n", ct).ConfigureAwait(false);

        var sessionNo = 0;
        for (var back = 7 * Weeks; back >= 0; back--)
        {
            var day = today.AddDays(-back);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || (back > 0 && random.Next(3) == 0))
            {
                continue;
            }

            // Busier this week: the week strip shows several projects.
            var sessions = back <= 4 ? 2 : 1;
            for (var s = 0; s < sessions; s++)
            {
                var (cwd, prompts) = Projects[(sessionNo + s) % Projects.Length];
                var startHour = s == 0 ? 10 : 15;
                var startLocal = day.ToDateTime(new TimeOnly(startHour, 5 * random.Next(10)));
                if (back == 0 && startLocal > now.AddMinutes(-60))
                {
                    startLocal = now.AddMinutes(-60 - (30 * s));
                    if (startLocal.Date != now.Date)
                    {
                        continue;
                    }
                }

                await WriteSessionAsync(projects, cwd, prompts, startLocal, sessionNo + s, random, ct).ConfigureAwait(false);
            }

            sessionNo += sessions;
        }
    }

    private static async Task WriteSessionAsync(string projectsDir, string cwd, string[] prompts, DateTime startLocal, int number, Random random, CancellationToken ct)
    {
        // Claude Code folder name: the cwd with ':', '\' and '/' replaced by '-'.
        var folder = Path.Combine(projectsDir, cwd.Replace(':', '-').Replace('\\', '-').Replace('/', '-'));
        Directory.CreateDirectory(folder);
        var session = $"4f1c2d3e-0000-4000-8000-{number:D12}";
        var buffer = new MemoryStream();
        var time = startLocal.ToUniversalTime();
        var count = 2 + random.Next(Math.Min(3, prompts.Length - 1));
        var first = random.Next(prompts.Length);
        for (var i = 0; i < count; i++)
        {
            var prompt = prompts[(first + i) % prompts.Length];
            WriteLine(buffer, "user", session, cwd, $"u{number}-{i}", time, w => w.WriteString("content", prompt));
            if (i == 0)
            {
                // Noise the importer must skip: a local-command caveat and a tool round-trip.
                WriteLine(buffer, "user", session, cwd, $"m{number}", time.AddSeconds(1), w => w.WriteString("content", "Caveat: The messages below were generated by the user while running local commands."), isMeta: true);
            }

            WriteLine(buffer, "assistant", session, cwd, $"a{number}-{i}", time.AddSeconds(5), w =>
            {
                w.WriteStartArray("content");
                w.WriteStartObject();
                w.WriteString("type", "tool_use");
                w.WriteString("id", $"t{number}-{i}");
                w.WriteString("name", "Read");
                w.WriteStartObject("input");
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndArray();
            });
            WriteLine(buffer, "user", session, cwd, $"r{number}-{i}", time.AddSeconds(6), w =>
            {
                w.WriteStartArray("content");
                w.WriteStartObject();
                w.WriteString("type", "tool_result");
                w.WriteString("tool_use_id", $"t{number}-{i}");
                w.WriteString("content", "file contents");
                w.WriteEndObject();
                w.WriteEndArray();
            });
            time = time.AddMinutes(6 + random.Next(20));
        }

        await File.WriteAllBytesAsync(Path.Combine(folder, session + ".jsonl"), buffer.ToArray(), ct).ConfigureAwait(false);
    }

    private static void WriteLine(MemoryStream buffer, string type, string session, string cwd, string uuid, DateTime utc, Action<Utf8JsonWriter> content, bool isMeta = false)
    {
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNull("parentUuid");
            w.WriteBoolean("isSidechain", false);
            w.WriteString("userType", "external");
            w.WriteString("cwd", cwd);
            w.WriteString("sessionId", session);
            w.WriteString("version", "2.1.0");
            w.WriteString("gitBranch", "main");
            w.WriteString("type", type);
            if (isMeta)
            {
                w.WriteBoolean("isMeta", true);
            }

            w.WriteStartObject("message");
            w.WriteString("role", type);
            content(w);
            w.WriteEndObject();
            w.WriteString("uuid", uuid);
            w.WriteString("timestamp", utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }

        buffer.Write(Encoding.UTF8.GetBytes("\n"));
    }
}
