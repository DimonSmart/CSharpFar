namespace CSharpFar.App.Editor;

/// <summary>Provides the editor's single, <c>WordDiv</c>-based word-boundary policy.</summary>
internal sealed class EditorWordNavigator
{
    private readonly IEditorTextBuffer _buffer;
    private readonly string _separators;

    public EditorWordNavigator(IEditorTextBuffer buffer, string separators)
    {
        _buffer = buffer;
        _separators = separators;
    }

    public EditorPosition PreviousWordStart(EditorPosition position)
    {
        position = Normalize(position);
        if (position == EditorPosition.Start)
            return position;

        position = Previous(position);
        while (position != EditorPosition.Start && IsSeparatorAt(position))
            position = Previous(position);

        while (position.Column > 0 && !IsSeparatorAt(Previous(position)))
            position = Previous(position);

        return position;
    }

    public EditorPosition NextWordStart(EditorPosition position)
    {
        position = Normalize(position);
        while (position != _buffer.End && !IsSeparatorAt(position))
            position = Next(position);
        while (position != _buffer.End && IsSeparatorAt(position))
            position = Next(position);
        return position;
    }

    public (EditorPosition Start, EditorPosition End)? WordRangeAt(EditorPosition position)
    {
        position = Normalize(position);
        string line = _buffer.GetLine(position.Line);
        if (line.Length == 0 || position.Column >= line.Length)
            return null;

        bool separator = IsSeparatorAt(position);
        EditorPosition start = position;
        while (start.Column > 0 && IsSeparatorAt(Previous(start)) == separator)
            start = Previous(start);

        EditorPosition end = Next(position);
        while (end.Column < line.Length && IsSeparatorAt(end) == separator)
            end = Next(end);
        return (start, end);
    }

    public static bool IsWordSeparator(string separators, char character) =>
        character is '\r' or '\n' || separators.Contains(character, StringComparison.Ordinal);

    private EditorPosition Normalize(EditorPosition position)
    {
        position = _buffer.NormalizePosition(position);
        string line = _buffer.GetLine(position.Line);
        return position with { Column = EditorUnicode.NormalizeGraphemeBoundary(line, position.Column) };
    }

    private EditorPosition Next(EditorPosition position)
    {
        string line = _buffer.GetLine(position.Line);
        if (position.Column < line.Length)
            return position with { Column = EditorUnicode.NextGraphemeColumn(line, position.Column) };
        return position.Line < _buffer.LineCount - 1 ? new EditorPosition(position.Line + 1, 0) : _buffer.End;
    }

    private EditorPosition Previous(EditorPosition position)
    {
        if (position.Column > 0)
        {
            string line = _buffer.GetLine(position.Line);
            return position with { Column = EditorUnicode.PreviousGraphemeColumn(line, position.Column) };
        }

        return position.Line > 0
            ? new EditorPosition(position.Line - 1, _buffer.GetLine(position.Line - 1).Length)
            : EditorPosition.Start;
    }

    private bool IsSeparatorAt(EditorPosition position)
    {
        string line = _buffer.GetLine(position.Line);
        return position.Column >= line.Length || IsSeparatorTextElement(line, position.Column);
    }

    private bool IsSeparatorTextElement(string text, int column)
    {
        if (column < 0 || column >= text.Length)
            return true;
        int end = EditorUnicode.NextGraphemeColumn(text, column);
        return text[column..end].All(ch => _separators.Contains(ch, StringComparison.Ordinal));
    }
}
