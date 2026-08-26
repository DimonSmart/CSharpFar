using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal static class EditorUnicode
{
    private static readonly ConditionalWeakTable<string, EditorGraphemeMap> GraphemeMaps = new();

    private static EditorGraphemeMap GetGraphemeMap(string text) =>
        GraphemeMaps.GetValue(text, static value => new EditorGraphemeMap(value));

    public static int NextScalarColumn(string text, int column) => NextGraphemeColumn(text, column);

    public static int NextGraphemeColumn(string text, int column)
    {
        return GetGraphemeMap(text).NextBoundary(column);
    }

    public static int PreviousScalarColumn(string text, int column) => PreviousGraphemeColumn(text, column);

    public static int PreviousGraphemeColumn(string text, int column)
    {
        return GetGraphemeMap(text).PreviousBoundary(column);
    }

    public static int NormalizeScalarBoundary(string text, int column) => NormalizeGraphemeBoundary(text, column);

    public static int NormalizeGraphemeBoundary(string text, int column)
    {
        return GetGraphemeMap(text).NormalizeBoundary(column);
    }

    public static int ScalarColumnFromUtf16Column(string text, int column)
    {
        return GetGraphemeMap(text).LogicalColumnFromUtf16Column(column);
    }

    public static int DisplayCellWidthAt(string text, int column)
    {
        if (!TryGetScalarAt(text, column, out Rune scalar))
            return 1;

        return ConsoleTextMetrics.GetCellWidth(scalar);
    }

    public static bool TryGetScalarAt(string text, int column, out Rune scalar)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column > 0 &&
            column < text.Length &&
            char.IsLowSurrogate(text[column]) &&
            char.IsHighSurrogate(text[column - 1]))
        {
            column--;
        }

        if (column >= text.Length)
        {
            scalar = default;
            return false;
        }

        char ch = text[column];
        if (char.IsHighSurrogate(ch) &&
            column + 1 < text.Length &&
            char.IsLowSurrogate(text[column + 1]))
        {
            scalar = new Rune(char.ConvertToUtf32(ch, text[column + 1]));
            return true;
        }

        if (!char.IsSurrogate(ch))
        {
            scalar = new Rune(ch);
            return true;
        }

        scalar = default;
        return false;
    }

}

internal sealed class EditorGraphemeMap
{
    private readonly string _text;
    private readonly int[] _starts;

    public EditorGraphemeMap(string text)
    {
        _text = text;
        _starts = StringInfo.ParseCombiningCharacters(text);
    }

    public int NextBoundary(int column)
    {
        column = Math.Clamp(column, 0, _text.Length);
        if (column >= _text.Length)
            return column;

        int index = Array.BinarySearch(_starts, column);
        index = index < 0 ? ~index : index + 1;
        return index < _starts.Length ? _starts[index] : _text.Length;
    }

    public int PreviousBoundary(int column)
    {
        column = Math.Clamp(column, 0, _text.Length);
        if (column <= 0)
            return column;

        int index = Array.BinarySearch(_starts, column);
        index = index >= 0 ? index - 1 : ~index - 1;
        return index >= 0 ? _starts[index] : 0;
    }

    public int NormalizeBoundary(int column)
    {
        column = Math.Clamp(column, 0, _text.Length);
        return Array.BinarySearch(_starts, column) >= 0 || column == _text.Length
            ? column
            : PreviousBoundary(column);
    }

    public int LogicalColumnFromUtf16Column(int column)
    {
        column = Math.Clamp(column, 0, _text.Length);
        int index = Array.BinarySearch(_starts, column);
        return index >= 0 ? index : ~index;
    }
}
