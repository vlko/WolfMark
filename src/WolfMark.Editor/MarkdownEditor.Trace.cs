using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using WolfMark.Core.Diagnostics;

namespace WolfMark.Editor;

/// <summary>
/// Hooks for <see cref="EditorTrace"/> (<c>--trace-editor</c>): key-to-frame latency and the count of RichEdit
/// layout queries (<c>GetRect</c>). Inert while tracing is off.
/// </summary>
public sealed partial class MarkdownEditor
{
    private long _keyDownStamp;
    private bool _frameArmed;
    private int _rectQueries; // GetRect calls since the last reset by a traced phase

    private void TraceKeyDown()
    {
        if (EditorTrace.Current is not null)
        {
            _keyDownStamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>The text changed: the next frame shows it – that ends the key's latency.</summary>
    private void TraceArmFrame()
    {
        if (EditorTrace.Current is null || _keyDownStamp == 0 || _frameArmed)
        {
            return;
        }

        _frameArmed = true;
        CompositionTarget.Rendering += OnTraceFrame;
    }

    private void OnTraceFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnTraceFrame;
        _frameArmed = false;
        EditorTrace.Current?.RecordSince(EditorPhase.KeyToFrame, _keyDownStamp, context: () => $"{_mode}, line {_activeLine + 1} of {LineOf(_text, _text.Length) + 1}");
        _keyDownStamp = 0;
    }

    private static string TraceDescribe(Snapshot s) => $"{(s.Load ? "one-go (open, mode, theme)" : s.Full ? "full restyle" : "edit")}, {s.Mode}";

    /// <summary>Records a phase with the number of <c>GetRect</c> calls it made.</summary>
    private void TraceLayout(EditorPhase phase, long start, int rectsBefore) =>
        EditorTrace.Current?.RecordSince(phase, start, _rectQueries - rectsBefore);
}
