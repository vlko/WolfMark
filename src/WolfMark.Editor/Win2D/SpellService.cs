using System.Collections.Concurrent;
using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Globalization;
using Windows.Win32.System.Com;

namespace WolfMark.Editor.Win2D;

internal readonly record struct SpellError(int Start, int Length);

/// <summary>
/// The Windows spell checker (ISpellChecker, COM) on its own MTA thread – the UI thread never waits for it.
/// Several languages at once: a word is wrong only when every language says so, so a note mixing Slovak and
/// English is not covered in squiggles. Results go back through <c>post</c> (the UI dispatcher).
/// </summary>
internal sealed unsafe class SpellService : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Action<Action> _post;
    private readonly List<nint> _checkers = []; // ISpellChecker*, touched only on the worker thread
    private readonly Thread _thread;

    public SpellService(IReadOnlyList<string> languages, Action<Action> post)
    {
        _post = post;
        _thread = new Thread(() => Run(languages)) { IsBackground = true, Name = "WolfMark spell check" };
        _thread.Start();
    }

    /// <summary>The languages that have a dictionary on this machine (known once the worker started).</summary>
    public IReadOnlyList<string> Languages { get; private set; } = [];

    /// <summary>The input language, the UI language, Slovak and English – each once.</summary>
    public static IReadOnlyList<string> DefaultLanguages()
    {
        var list = new List<string>();
        try
        {
            var layout = (int)((nint)PInvoke.GetKeyboardLayout(0).Value & 0xFFFF);
            list.Add(CultureInfo.GetCultureInfo(layout).Name);
        }
        catch (CultureNotFoundException)
        {
        }

        list.Add(CultureInfo.CurrentUICulture.Name);
        list.Add("sk-SK");
        list.Add("en-US");
        return list.Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Checks <paramref name="text"/>; <paramref name="done"/> runs on the UI thread with the errors.</summary>
    public void Check(string text, Action<string, SpellError[]> done) =>
        Enqueue(() =>
        {
            var errors = CheckOnWorker(text);
            _post(() => done(text, errors));
        });

    public Task<IReadOnlyList<string>> SuggestAsync(string word)
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() => result.TrySetResult(SuggestOnWorker(word)));
        return result.Task;
    }

    /// <summary>Adds the word to the user dictionary of every language (permanent).</summary>
    public void Add(string word) => Enqueue(() => ForEachChecker(word, ignore: false));

    /// <summary>Ignores the word until the app ends.</summary>
    public void Ignore(string word) => Enqueue(() => ForEachChecker(word, ignore: true));

    public void Dispose() => _work.CompleteAdding();

    private void Enqueue(Action action)
    {
        if (!_work.IsAddingCompleted)
        {
            _work.Add(action);
        }
    }

    private void Run(IReadOnlyList<string> languages)
    {
        PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
        ISpellCheckerFactory* factory = null;
        try
        {
            if (SpellCheckerFactory.CreateInstance(out factory).Failed)
            {
                return;
            }

            var supported = new List<string>();
            foreach (var language in languages)
            {
                fixed (char* tag = language)
                {
                    if (factory->IsSupported(new PCWSTR(tag)))
                    {
                        _checkers.Add((nint)factory->CreateSpellChecker(new PCWSTR(tag)));
                        supported.Add(language);
                    }
                }
            }

            Languages = supported;
            foreach (var action in _work.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception)
                {
                    // one failed request must not stop the checker; the line simply shows no squiggles
                }
            }
        }
        catch (Exception)
        {
            // no spell checking on this machine
        }
        finally
        {
            foreach (var checker in _checkers)
            {
                ((ISpellChecker*)checker)->Release();
            }

            if (factory is not null)
            {
                factory->Release();
            }

            PInvoke.CoUninitialize();
        }
    }

    private SpellError[] CheckOnWorker(string text)
    {
        if (_checkers.Count == 0 || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<SpellError>? common = null;
        foreach (var checker in _checkers)
        {
            var errors = Errors((ISpellChecker*)checker, text);
            // Wrong in this language too? Only words every language flags stay.
            common = common is null ? errors : [.. common.Where(e => errors.Any(o => o.Start < e.Start + e.Length && e.Start < o.Start + o.Length))];
            if (common.Count == 0)
            {
                break;
            }
        }

        return common is null ? [] : [.. common];
    }

    private static List<SpellError> Errors(ISpellChecker* checker, string text)
    {
        var list = new List<SpellError>();
        IEnumSpellingError* errors;
        fixed (char* p = text)
        {
            errors = checker->Check(new PCWSTR(p));
        }

        try
        {
            ISpellingError* error;
            while (errors->Next(&error).Value == 0)
            {
                if (error->CorrectiveAction != CORRECTIVE_ACTION.CORRECTIVE_ACTION_NONE)
                {
                    list.Add(new SpellError((int)error->StartIndex, (int)error->Length));
                }

                error->Release();
            }
        }
        finally
        {
            errors->Release();
        }

        return list;
    }

    private IReadOnlyList<string> SuggestOnWorker(string word)
    {
        var suggestions = new List<string>();
        foreach (var checker in _checkers)
        {
            IEnumString* list;
            fixed (char* p = word)
            {
                list = ((ISpellChecker*)checker)->Suggest(new PCWSTR(p));
            }

            try
            {
                PWSTR item;
                uint fetched;
                while (suggestions.Count < 12 && list->Next(1, &item, &fetched).Value == 0 && fetched == 1)
                {
                    suggestions.Add(new string(item.Value));
                    PInvoke.CoTaskMemFree(item.Value);
                }
            }
            finally
            {
                list->Release();
            }
        }

        return suggestions.Distinct(StringComparer.Ordinal).Take(8).ToArray();
    }

    private void ForEachChecker(string word, bool ignore)
    {
        fixed (char* p = word)
        {
            foreach (var checker in _checkers)
            {
                if (ignore)
                {
                    ((ISpellChecker*)checker)->Ignore(new PCWSTR(p));
                }
                else
                {
                    ((ISpellChecker*)checker)->Add(new PCWSTR(p));
                }
            }
        }
    }
}
