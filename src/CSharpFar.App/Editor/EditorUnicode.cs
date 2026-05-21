using System.Text;

namespace CSharpFar.App.Editor;

internal static class EditorUnicode
{
    public static int NextScalarColumn(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column >= text.Length)
            return column;

        return char.IsHighSurrogate(text[column]) &&
            column + 1 < text.Length &&
            char.IsLowSurrogate(text[column + 1])
                ? column + 2
                : column + 1;
    }

    public static int PreviousScalarColumn(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column <= 0)
            return column;

        return column >= 2 &&
            char.IsLowSurrogate(text[column - 1]) &&
            char.IsHighSurrogate(text[column - 2])
                ? column - 2
                : column - 1;
    }

    public static int NormalizeScalarBoundary(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        return column > 0 &&
            column < text.Length &&
            char.IsLowSurrogate(text[column]) &&
            char.IsHighSurrogate(text[column - 1])
                ? column - 1
                : column;
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

        return DisplayCellWidth(scalar);
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

    private static int DisplayCellWidth(Rune scalar)
    {
        var category = Rune.GetUnicodeCategory(scalar);
        if (category is
            System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.EnclosingMark or
            System.Globalization.UnicodeCategory.Format)
        {
            return 0;
        }

        int value = scalar.Value;
        return IsWideDisplayScalar(value) ? 2 : 1;
    }

    private static bool IsWideDisplayScalar(int value) =>
        value is >= 0x1100 and <= 0x115F or
        0x2329 or
        0x232A or
        >= 0x2E80 and <= 0xA4CF or
        >= 0xAC00 and <= 0xD7A3 or
        >= 0xF900 and <= 0xFAFF or
        >= 0xFE10 and <= 0xFE19 or
        >= 0xFE30 and <= 0xFE6F or
        >= 0xFF00 and <= 0xFF60 or
        >= 0xFFE0 and <= 0xFFE6 or
        >= 0x1F300 and <= 0x1FAFF or
        >= 0x20000 and <= 0x3FFFD;
}
