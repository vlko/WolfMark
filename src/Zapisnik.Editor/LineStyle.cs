using Zapisnik.Core.Markdown;

namespace Zapisnik.Editor;

/// <summary>Tokens of one line with its absolute document offset, computed off the UI thread.</summary>
internal readonly record struct LineStyle(int Line, int Start, int Length, string Text, MarkdownToken[] Tokens);
