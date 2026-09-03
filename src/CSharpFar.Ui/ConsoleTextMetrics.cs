using System.Globalization;
using System.Text;

namespace CSharpFar.Ui;

/// <summary>Terminal-cell measurements for text rendered by the console UI.</summary>
public static class ConsoleTextMetrics
{
    /// <summary>Returns a terminal-cell viewport of <paramref name="text"/>.</summary>
    /// <remarks>
    /// The requested offset is clamped to the last viewport and then advanced to
    /// a Unicode-scalar boundary when it intersects a wide scalar. Consequently
    /// the returned value is always valid UTF-16 and contains only whole glyphs.
    /// </remarks>
    public static ConsoleTextViewport GetViewport(string text, int cellOffset, int cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        cells = Math.Max(0, cells);
        int totalWidth = GetCellWidth(text);
        int requested = Math.Clamp(cellOffset, 0, Math.Max(0, totalWidth - cells));

        int currentCell = 0;
        int start = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int nextCell = currentCell + GetCellWidth(rune);
            if (nextCell > requested)
                break;
            currentCell = nextCell;
            start += rune.Utf16SequenceLength;
        }

        // A viewport starting in the second cell of a wide rune omits that rune.
        if (currentCell < requested && start < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(start), out Rune crossed, out int consumed);
            currentCell += GetCellWidth(crossed);
            start += consumed;
        }

        string slice = TruncateToCells(text[start..], cells);
        return new ConsoleTextViewport(slice, requested, currentCell, cells, totalWidth);
    }

    /// <summary>Returns only the text from <see cref="GetViewport"/>.</summary>
    public static string SliceToCells(string text, int cellOffset, int cells) =>
        GetViewport(text, cellOffset, cells).Text;

    public static int GetCellWidth(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int width = 0;
        foreach (Rune rune in text.EnumerateRunes())
            width += GetCellWidth(rune);
        return width;
    }

    public static string TruncateToCells(string text, int cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cells <= 0)
            return string.Empty;

        int width = 0;
        int end = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int runeWidth = GetCellWidth(rune);
            if (width + runeWidth > cells)
                break;

            width += runeWidth;
            end += rune.Utf16SequenceLength;
        }

        return text[..end];
    }

    public static string FitToCells(string text, int cells)
    {
        if (cells <= 0)
            return string.Empty;

        string truncated = TruncateToCells(text, cells);
        return truncated + new string(' ', cells - GetCellWidth(truncated));
    }

    public static string TruncateEndToCells(string text, int cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cells <= 0)
            return string.Empty;
        if (GetCellWidth(text) <= cells)
            return text;
        if (cells == 1)
            return "…";

        int start = text.Length;
        int width = 0;
        foreach (Rune rune in text.EnumerateRunes().Reverse())
        {
            int runeWidth = GetCellWidth(rune);
            if (width + runeWidth > cells - 1)
                break;

            width += runeWidth;
            start -= rune.Utf16SequenceLength;
        }

        return "…" + text[start..];
    }

    public static int Utf16IndexFromCellOffset(string text, int cellOffset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cellOffset <= 0)
            return 0;

        int width = 0;
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int runeWidth = GetCellWidth(rune);
            if (width + runeWidth > cellOffset)
                break;

            width += runeWidth;
            index += rune.Utf16SequenceLength;
        }
        return index;
    }

    public static int CellOffsetFromUtf16Index(string text, int utf16Index)
    {
        ArgumentNullException.ThrowIfNull(text);
        utf16Index = Math.Clamp(utf16Index, 0, text.Length);

        int width = 0;
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (index + rune.Utf16SequenceLength > utf16Index)
                break;

            width += GetCellWidth(rune);
            index += rune.Utf16SequenceLength;
        }
        return width;
    }

    public static int GetCellWidth(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return 0;

        int value = rune.Value;
        return value is >= 0x1100 and <= 0x115F or
            0x2329 or 0x232A or
            >= 0x2E80 and <= 0xA4CF or
            >= 0xAC00 and <= 0xD7A3 or
            >= 0xF900 and <= 0xFAFF or
            >= 0xFE10 and <= 0xFE19 or
            >= 0xFE30 and <= 0xFE6F or
            >= 0xFF00 and <= 0xFF60 or
            >= 0xFFE0 and <= 0xFFE6 or
            >= 0x1F300 and <= 0x1FAFF or
            >= 0x20000 and <= 0x3FFFD ? 2 : 1;
    }
}

/// <summary>A clamped, whole-scalar terminal-cell viewport.</summary>
public readonly record struct ConsoleTextViewport(
    string Text,
    int CellOffset,
    int TextStartCell,
    int Width,
    int TotalWidth)
{
    public int MaximumOffset => Math.Max(0, TotalWidth - Width);
}
