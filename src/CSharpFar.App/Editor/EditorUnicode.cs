using System.Globalization;
using System.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal static class EditorUnicode
{
    public static int NextScalarColumn(string text, int column) => NextGraphemeColumn(text, column);

    public static int NextGraphemeColumn(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column >= text.Length)
            return column;

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int index = Array.BinarySearch(starts, column);
        if (index < 0)
            index = ~index;
        else
            index++;
        return index < starts.Length ? starts[index] : text.Length;
    }

    public static int PreviousScalarColumn(string text, int column) => PreviousGraphemeColumn(text, column);

    public static int PreviousGraphemeColumn(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column <= 0)
            return column;

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int index = Array.BinarySearch(starts, column);
        if (index >= 0)
            return index == 0 ? 0 : starts[index - 1];
        index = ~index;
        return index == 0 ? 0 : starts[index - 1];
    }

    public static int NormalizeScalarBoundary(string text, int column) => NormalizeGraphemeBoundary(text, column);

    public static int NormalizeGraphemeBoundary(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column == 0 || column == text.Length)
            return column;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return Array.BinarySearch(starts, column) >= 0 ? column : PreviousGraphemeColumn(text, column);
    }

    public static int ScalarColumnFromUtf16Column(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        int scalarColumn = 0;
        int index = 0;
        while (index < column)
        {
            int next = NextScalarColumn(text, index);
            index = next > index ? next : index + 1;
            scalarColumn++;
        }

        return scalarColumn;
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
