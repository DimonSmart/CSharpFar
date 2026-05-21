namespace CSharpFar.App.Editor;

public sealed class EditorSyntaxHighlightCache
{
    private readonly Dictionary<int, EditorSyntaxCachedLine> _lines = [];
    private int _firstInvalidLine;

    public int FirstInvalidLine => _firstInvalidLine;
    public int TokenizedLineCount { get; private set; }
    public string? LanguageKey { get; private set; }
    public string? ThemeKey { get; private set; }

    public void Reset(string? languageKey = null, string? themeKey = null)
    {
        _lines.Clear();
        _firstInvalidLine = 0;
        TokenizedLineCount = 0;
        LanguageKey = languageKey;
        ThemeKey = themeKey;
    }

    public void ResetIfChanged(string languageKey, string themeKey)
    {
        if (string.Equals(LanguageKey, languageKey, StringComparison.Ordinal) &&
            string.Equals(ThemeKey, themeKey, StringComparison.Ordinal))
        {
            return;
        }

        Reset(languageKey, themeKey);
    }

    public void InvalidateFromLine(int lineIndex)
    {
        int firstLine = Math.Max(0, lineIndex);
        _firstInvalidLine = Math.Min(_firstInvalidLine, firstLine);

        foreach (int key in _lines.Keys.Where(key => key >= firstLine).ToArray())
            _lines.Remove(key);
    }

    public bool TryGetLineSpans(
        int lineIndex,
        string lineText,
        out IReadOnlyList<EditorColorSpan> spans)
    {
        if (lineIndex < _firstInvalidLine &&
            _lines.TryGetValue(lineIndex, out var cached) &&
            cached.TextHash == StringComparer.Ordinal.GetHashCode(lineText))
        {
            spans = cached.Spans;
            return true;
        }

        spans = [];
        return false;
    }

    public void StoreLineSpans(
        int lineIndex,
        string lineText,
        IReadOnlyList<EditorColorSpan> spans,
        bool tokenized)
    {
        _lines[lineIndex] = new EditorSyntaxCachedLine(
            StringComparer.Ordinal.GetHashCode(lineText),
            spans.ToArray());

        if (lineIndex >= _firstInvalidLine)
            _firstInvalidLine = lineIndex + 1;

        if (tokenized)
            TokenizedLineCount++;
    }

    private sealed record EditorSyntaxCachedLine(
        int TextHash,
        IReadOnlyList<EditorColorSpan> Spans);
}
