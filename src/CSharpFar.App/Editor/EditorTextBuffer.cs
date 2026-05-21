using System.Text;

namespace CSharpFar.App.Editor;

public sealed class EditorTextBuffer : IEditorTextBuffer
{
    private readonly List<EditorBufferLine> _lines;

    private EditorTextBuffer(List<EditorBufferLine> lines)
    {
        _lines = lines.Count == 0 ? [new EditorBufferLine(string.Empty, null)] : lines;
    }

    public int LineCount => _lines.Count;

    public EditorPosition End => new(_lines.Count - 1, _lines[^1].Text.Length);

    public static EditorTextBuffer FromText(string text) =>
        new(ParseLines(text));

    public static EditorTextBuffer FromLines(IEnumerable<string> lines) =>
        new(lines.Select(line => new EditorBufferLine(line, null)).ToList());

    public string GetLine(int lineIndex) => _lines[lineIndex].Text;

    public string? GetLineEnding(int lineIndex) => _lines[lineIndex].Ending;

    public EditorPosition NormalizePosition(EditorPosition position)
    {
        int line = Math.Clamp(position.Line, 0, _lines.Count - 1);
        int column = Math.Clamp(position.Column, 0, _lines[line].Text.Length);
        column = EditorUnicode.NormalizeScalarBoundary(_lines[line].Text, column);
        return new EditorPosition(line, column);
    }

    public string GetText(EditorDocumentFormat format)
    {
        var builder = new StringBuilder();
        string selectedSeparator = EditorDocumentFormat.Separator(format.LineEnding);
        for (int index = 0; index < _lines.Count; index++)
        {
            EditorBufferLine line = _lines[index];
            builder.Append(line.Text);
            string? ending = format.LineEnding == EditorLineEnding.Mixed
                ? line.Ending
                : index < _lines.Count - 1 || line.Ending is not null
                    ? selectedSeparator
                    : null;
            if (ending is not null)
                builder.Append(ending);
        }

        return builder.ToString();
    }

    public string GetTextInRange(EditorPosition start, EditorPosition end)
    {
        start = NormalizePosition(start);
        end = NormalizePosition(end);
        (start, end) = NormalizeRange(start, end);
        if (start == end)
            return string.Empty;

        if (start.Line == end.Line)
            return _lines[start.Line].Text[start.Column..end.Column];

        var builder = new StringBuilder();
        builder.Append(_lines[start.Line].Text[start.Column..]);
        for (int line = start.Line + 1; line < end.Line; line++)
        {
            builder.Append('\n');
            builder.Append(_lines[line].Text);
        }

        builder.Append('\n');
        builder.Append(_lines[end.Line].Text[..end.Column]);
        return builder.ToString();
    }

    public void Replace(
        EditorPosition start,
        EditorPosition end,
        string replacementText,
        EditorLineEnding insertedLineEnding)
    {
        start = NormalizePosition(start);
        end = NormalizePosition(end);
        (start, end) = NormalizeRange(start, end);
        List<EditorBufferLine> replacementLines = ParseReplacementLines(replacementText, insertedLineEnding);

        string prefix = _lines[start.Line].Text[..start.Column];
        string suffix = _lines[end.Line].Text[end.Column..];
        string? suffixEnding = _lines[end.Line].Ending;

        if (replacementLines.Count == 1)
        {
            replacementLines[0].Text = prefix + replacementLines[0].Text + suffix;
            replacementLines[0].Ending = suffixEnding;
        }
        else
        {
            replacementLines[0].Text = prefix + replacementLines[0].Text;
            replacementLines[^1].Text += suffix;
            replacementLines[^1].Ending = suffixEnding;
        }

        int removeCount = end.Line - start.Line + 1;
        _lines.RemoveRange(start.Line, removeCount);
        _lines.InsertRange(start.Line, replacementLines);
        if (_lines.Count == 0)
            _lines.Add(new EditorBufferLine(string.Empty, null));
    }

    private static (EditorPosition Start, EditorPosition End) NormalizeRange(
        EditorPosition start,
        EditorPosition end)
    {
        if (Compare(start, end) <= 0)
            return (start, end);
        return (end, start);
    }

    public static int Compare(EditorPosition left, EditorPosition right)
    {
        int line = left.Line.CompareTo(right.Line);
        return line != 0 ? line : left.Column.CompareTo(right.Column);
    }

    public static EditorPosition Advance(EditorPosition start, string text)
    {
        int line = start.Line;
        int column = start.Column;
        for (int index = 0; index < text.Length; index++)
        {
            char ch = text[index];
            if (ch == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                line++;
                column = 0;
            }
            else if (ch == '\n')
            {
                line++;
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return new EditorPosition(line, column);
    }

    private static List<EditorBufferLine> ParseReplacementLines(
        string text,
        EditorLineEnding insertedLineEnding)
    {
        var lines = ParseLines(text);
        if (lines.Count == 0)
            lines.Add(new EditorBufferLine(string.Empty, null));

        string separator = EditorDocumentFormat.Separator(insertedLineEnding);
        for (int index = 0; index < lines.Count - 1; index++)
            lines[index].Ending ??= separator;

        return lines;
    }

    private static List<EditorBufferLine> ParseLines(string text)
    {
        var result = new List<EditorBufferLine>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char ch = text[index];
            if (ch is not ('\r' or '\n'))
                continue;

            string ending;
            if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                ending = "\r\n";
                result.Add(new EditorBufferLine(text[start..index], ending));
                index++;
                start = index + 1;
            }
            else
            {
                ending = ch == '\r' ? "\r" : "\n";
                result.Add(new EditorBufferLine(text[start..index], ending));
                start = index + 1;
            }
        }

        result.Add(new EditorBufferLine(text[start..], null));
        return result;
    }
}
