using System.Threading.Channels;
using Avalonia.Threading;
using WolfMark.Core.Markdown;

namespace WolfMark.Desktop.Editor;

/// <summary>Tokens of one line, valid while the line still has <see cref="Text"/>.</summary>
internal readonly record struct LineTokens(string Text, MarkdownToken[] Tokens);

/// <summary>
/// Lexes the note off the UI thread (CLAUDE.md rule 3) with the incremental <see cref="MarkdownDocumentModel"/>
/// and hands the UI a snapshot of every line's tokens; only the newest text is lexed when edits pile up.
/// </summary>
internal sealed class MarkdownHighlighter : IDisposable
{
    private readonly Channel<string> _texts = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly MarkdownDocumentModel _model = new(); // owned by the loop
    private readonly CancellationTokenSource _cts = new();

    public MarkdownHighlighter() => _ = Task.Run(LoopAsync);

    /// <summary>Raised on the UI thread: the snapshot and the first/last changed line (0-based).</summary>
    public event Action<LineTokens[], int, int>? Updated;

    public void Request(string text) => _texts.Writer.TryWrite(text);

    public void Dispose() => _cts.Cancel();

    private async Task LoopAsync()
    {
        try
        {
            while (await _texts.Reader.WaitToReadAsync(_cts.Token))
            {
                if (!_texts.Reader.TryRead(out var text))
                {
                    continue;
                }

                var update = _model.Update(text);
                var lines = new LineTokens[_model.LineCount];
                for (var i = 0; i < lines.Length; i++)
                {
                    var tokens = _model.GetTokens(i);
                    lines[i] = new LineTokens(_model.GetLineText(i), tokens as MarkdownToken[] ?? [.. tokens]);
                }

                var first = update.FirstLine;
                var last = update.FirstLine + update.LineCount - 1;
                Dispatcher.UIThread.Post(() => Updated?.Invoke(lines, first, last), DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
