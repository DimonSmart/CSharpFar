using System.Globalization;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

internal sealed class LargeFileViewer
{
    private const int BinaryBytesPerRow = 16;
    private const int FollowPollMs = 250;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public LargeFileViewer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show(string filePath)
    {
        try
        {
            using var reader = new RandomAccessFileByteReader(filePath);
            var cache = new BlockCache(reader);
            var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
            var state = new LargeFileViewerState(cache, scanner);

            var size = _screen.GetSize();
            var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
            try
            {
                RunLoop(filePath, reader, state);
            }
            finally
            {
                _screen.Restore(saved);
            }
        }
        catch (Exception ex)
        {
            new MessageDialog(_screen, _palette).Show("Viewer", ex.Message);
        }
    }

    private void RunLoop(string filePath, IFileByteReader reader, LargeFileViewerState state)
    {
        while (true)
        {
            var size = _screen.GetSize();
            int contentHeight = Math.Max(0, size.Height - 2);
            var view = Draw(filePath, reader, state, contentHeight, size);

            var key = ReadViewerKey(reader, state, contentHeight);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    MoveUp(state);
                    break;

                case ConsoleKey.DownArrow:
                    MoveDown(reader, state, view);
                    break;

                case ConsoleKey.LeftArrow:
                    state.HorizontalOffset = Math.Max(0, state.HorizontalOffset - 1);
                    break;

                case ConsoleKey.RightArrow:
                    state.HorizontalOffset++;
                    break;

                case ConsoleKey.PageUp:
                    MovePageUp(state, contentHeight);
                    break;

                case ConsoleKey.PageDown:
                    MovePageDown(reader, state, view, contentHeight);
                    break;

                case ConsoleKey.Home:
                    state.TopByteOffset = state.LineScanner.ContentStartOffset;
                    state.HorizontalOffset = 0;
                    state.FollowMode = false;
                    break;

                case ConsoleKey.End:
                    MoveToEnd(reader, state, contentHeight);
                    state.HorizontalOffset = 0;
                    state.FollowMode = true;
                    break;

                case ConsoleKey.F:
                    state.FollowMode = !state.FollowMode;
                    if (state.FollowMode)
                        MoveToEnd(reader, state, contentHeight);
                    break;

                case ConsoleKey.G:
                    JumpToPosition(reader, state, contentHeight);
                    break;

                case ConsoleKey.H:
                    ToggleViewMode(state);
                    break;

                case ConsoleKey.F8:
                    ChangeEncoding(filePath, reader, state, contentHeight, size);
                    break;

                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    return;
            }
        }
    }

    private ConsoleKeyInfo ReadViewerKey(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight)
    {
        if (!state.FollowMode)
            return _screen.ReadKey();

        long knownLength = reader.Length;
        while (true)
        {
            if (_screen.TryReadInput(out var inputEvent) && inputEvent is CSharpFar.Console.Input.KeyConsoleInputEvent keyEvent)
                return keyEvent.Key;

            long currentLength = reader.Length;
            if (currentLength != knownLength)
            {
                MoveToEnd(reader, state, contentHeight);
                return new ConsoleKeyInfo('\0', ConsoleKey.NoName, false, false, false);
            }

            Thread.Sleep(FollowPollMs);
        }
    }

    private LargeFileRenderView Draw(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        ConsoleSize size)
    {
        _screen.SetCursorVisible(false);
        DrawHeader(filePath, reader, state, size);

        var view = state.IsHexMode
            ? DrawBinaryContent(reader, state, contentHeight, size.Width)
            : DrawTextContent(reader, state, contentHeight, size.Width);

        DrawFooter(size);
        return view;
    }

    private void DrawHeader(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        ConsoleSize size)
    {
        string nameSection = $" {Path.GetFileName(filePath)} ";
        string mode = state.IsHexMode ? " HEX" : $" TEXT {state.LineScanner.EncodingDisplayName}";
        string follow = state.FollowMode ? " F" : string.Empty;
        string posSection = reader.Length == 0
            ? $" 0%{mode}{follow} "
            : $" {FormatPercent(state.TopByteOffset, reader.Length)}%{mode}{follow} ";

        int nameWidth = Math.Max(0, size.Width - posSection.Length);
        if (nameSection.Length > nameWidth)
            nameSection = nameSection[..nameWidth];

        string header = nameSection.PadRight(nameWidth) + posSection;
        _screen.Write(0, 0, header, PaletteStyles.PathHeaderActive(_palette));
    }

    private LargeFileRenderView DrawTextContent(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        int bytesPerLine = Math.Max(256, (state.HorizontalOffset + width + 32) * 4);
        var scanned = state.LineScanner
            .ReadLinesAsync(state.TopByteOffset, contentHeight, bytesPerLine)
            .GetAwaiter()
            .GetResult();

        for (int row = 0; row < contentHeight; row++)
        {
            string text = row < scanned.Lines.Count
                ? FormatLine(scanned.Lines[row].Text, state.HorizontalOffset, width)
                : new string(' ', width);
            _screen.Write(0, row + 1, text, PaletteStyles.CommandLine(_palette));
        }

        return new LargeFileRenderView(scanned.Lines, scanned.NextOffset);
    }

    private LargeFileRenderView DrawBinaryContent(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        var rows = new List<ScannedLine>(contentHeight);
        long offset = Math.Clamp(state.TopByteOffset, 0, reader.Length);
        long nextOffset = offset;

        for (int row = 0; row < contentHeight; row++)
        {
            long rowOffset = offset + row * BinaryBytesPerRow;
            string text = rowOffset < reader.Length
                ? ReadHexRow(state.BlockCache, rowOffset)
                : string.Empty;
            rows.Add(new ScannedLine(rowOffset, Math.Min(reader.Length, rowOffset + BinaryBytesPerRow), text));
            nextOffset = Math.Min(reader.Length, rowOffset + BinaryBytesPerRow);
            _screen.Write(0, row + 1, FormatLine(text, state.HorizontalOffset, width), PaletteStyles.CommandLine(_palette));
        }

        return new LargeFileRenderView(rows, nextOffset);
    }

    private void DrawFooter(ConsoleSize size)
    {
        _screen.FillRegion(new Rect(0, size.Height - 1, size.Width, 1), PaletteStyles.KeyBarLabel(_palette));
        int y = size.Height - 1;
        WriteFooterItem(0, y, "F", "Follow", size.Width);
        WriteFooterItem(10, y, "G", "Go", size.Width);
        WriteFooterItem(16, y, "H", "Hex/Text", size.Width);
        WriteFooterItem(28, y, "8", "Encoding", size.Width);
        WriteFooterItem(39, y, "10", "Close", size.Width);
    }

    private void WriteFooterItem(int x, int y, string key, string text, int width)
    {
        if (x >= width)
            return;

        _screen.Write(x, y, sizeLimited(key, width - x), PaletteStyles.KeyBarNum(_palette));
        int textX = x + key.Length;
        if (textX < width)
            _screen.Write(textX, y, sizeLimited(text, width - textX), PaletteStyles.KeyBarLabel(_palette));

        static string sizeLimited(string text, int maxLength) =>
            text.Length <= maxLength ? text : text[..Math.Max(0, maxLength)];
    }

    private void MoveUp(LargeFileViewerState state)
    {
        state.TopByteOffset = state.IsHexMode
            ? Math.Max(0, state.TopByteOffset - BinaryBytesPerRow)
            : state.LineScanner
                .FindPreviousLineStartAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();
        state.FollowMode = false;
    }

    private static void MoveDown(
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view)
    {
        state.TopByteOffset = state.IsHexMode
            ? Math.Min(reader.Length, state.TopByteOffset + BinaryBytesPerRow)
            : view.Lines.Count > 1
                ? view.Lines[1].StartOffset
                : view.NextOffset;
        state.FollowMode = false;
    }

    private void MovePageUp(LargeFileViewerState state, int contentHeight)
    {
        if (state.IsHexMode)
        {
            long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
            state.TopByteOffset = Math.Max(0, state.TopByteOffset - delta);
        }
        else
        {
            for (int i = 0; i < Math.Max(1, contentHeight); i++)
                state.TopByteOffset = state.LineScanner
                    .FindPreviousLineStartAsync(state.TopByteOffset)
                    .GetAwaiter()
                    .GetResult();
        }

        state.FollowMode = false;
    }

    private static void MovePageDown(
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int contentHeight)
    {
        if (state.IsHexMode)
        {
            long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
            state.TopByteOffset = Math.Min(reader.Length, state.TopByteOffset + delta);
        }
        else
        {
            state.TopByteOffset = view.NextOffset;
        }

        state.FollowMode = false;
    }

    private void MoveToEnd(IFileByteReader reader, LargeFileViewerState state, int contentHeight)
    {
        state.TopByteOffset = state.IsHexMode
            ? Math.Max(0, reader.Length - (long)Math.Max(1, contentHeight) * BinaryBytesPerRow)
            : state.LineScanner
                .FindTailTopOffsetAsync(Math.Max(1, contentHeight))
                .GetAwaiter()
                .GetResult();
    }

    private void JumpToPosition(IFileByteReader reader, LargeFileViewerState state, int contentHeight)
    {
        string? input = new InputDialog(_screen, _palette).Show(
            "Viewer",
            state.IsHexMode ? "Percent or byte offset:" : "Line number or percent:",
            validate: text => ValidateJump(text, state.IsHexMode));
        if (input is null)
            return;

        if (input.EndsWith('%'))
        {
            int percent = int.Parse(input[..^1], CultureInfo.InvariantCulture);
            long target = reader.Length * percent / 100;
            state.TopByteOffset = state.IsHexMode
                ? Math.Clamp(target, 0, reader.Length)
                : state.LineScanner
                    .FindLineStartAtOrBeforeAsync(target)
                    .GetAwaiter()
                    .GetResult();
        }
        else if (state.IsHexMode)
        {
            long offset = long.Parse(input, CultureInfo.InvariantCulture);
            state.TopByteOffset = Math.Clamp(offset, 0, reader.Length);
        }
        else
        {
            long lineNumber = long.Parse(input, CultureInfo.InvariantCulture);
            state.TopByteOffset = state.LineScanner
                .FindLineOffsetAsync(lineNumber, state.LineIndex)
                .GetAwaiter()
                .GetResult();
        }

        if (state.TopByteOffset >= reader.Length)
            MoveToEnd(reader, state, contentHeight);

        state.FollowMode = false;
    }

    private static void ToggleViewMode(LargeFileViewerState state)
    {
        if (state.IsHexMode)
        {
            state.ViewMode = LargeFileViewMode.Text;
            state.TopByteOffset = state.LineScanner
                .FindLineStartAtOrBeforeAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();
        }
        else
        {
            state.ViewMode = LargeFileViewMode.Hex;
        }

        state.HorizontalOffset = 0;
    }

    private void ChangeEncoding(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        ConsoleSize size)
    {
        var items = TextEncodingCatalog.CreateViewerCatalog(state.LineScanner.Detection);
        long anchorByteOffset = state.TopByteOffset;
        var originalSelection = state.EncodingSelection;
        var originalViewMode = state.ViewMode;
        int originalHorizontalOffset = state.HorizontalOffset;
        bool originalFollowMode = state.FollowMode;

        var selected = new EncodingSelectionDialog(_screen, _palette).Show(
            items,
            state.EncodingSelection,
            previewSelection: item => ApplyEncodingSelection(
                reader,
                state,
                item.Selection,
                anchorByteOffset,
                originalViewMode),
            renderUnderlay: () => Draw(filePath, reader, state, contentHeight, size));

        if (selected is null)
        {
            ApplyEncodingSelection(reader, state, originalSelection, anchorByteOffset, originalViewMode);
            state.HorizontalOffset = originalHorizontalOffset;
            state.FollowMode = originalFollowMode;
            return;
        }

        ApplyEncodingSelection(reader, state, selected.Selection, anchorByteOffset, originalViewMode);
    }

    private static void ApplyEncodingSelection(
        IFileByteReader reader,
        LargeFileViewerState state,
        TextEncodingSelection selection,
        long anchorByteOffset,
        LargeFileViewMode baseViewMode)
    {
        var scanner = LineScanner
            .CreateAsync(state.BlockCache, reader, selection)
            .GetAwaiter()
            .GetResult();

        var targetViewMode = baseViewMode == LargeFileViewMode.Hex &&
                             selection.Kind == TextEncodingSelectionKind.Explicit
            ? LargeFileViewMode.Text
            : baseViewMode;

        state.ResetScanner(scanner, selection);
        state.ViewMode = targetViewMode;

        state.TopByteOffset = state.IsHexMode
            ? Math.Clamp(anchorByteOffset, 0, reader.Length)
            : scanner.FindLineStartAtOrBeforeAsync(anchorByteOffset).GetAwaiter().GetResult();
        state.HorizontalOffset = 0;
    }

    private static string? ValidateJump(string text, bool binary)
    {
        if (text.EndsWith('%'))
        {
            return int.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int percent) &&
                   percent is >= 0 and <= 100
                ? null
                : "Enter a percent from 0% to 100%.";
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) &&
               (binary ? value >= 0 : value > 0)
            ? null
            : binary
                ? "Enter a non-negative byte offset."
                : "Enter a positive line number.";
    }

    private static string ReadHexRow(BlockCache cache, long offset)
    {
        var bytes = new byte[BinaryBytesPerRow];
        int read = cache.ReadAsync(offset, bytes).GetAwaiter().GetResult();
        string hex = string.Join(' ', bytes.Take(read).Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        string ascii = new(bytes.Take(read).Select(ToPrintableAscii).ToArray());
        return $"{offset:X8}  {hex.PadRight(47)}  {ascii}";
    }

    private static char ToPrintableAscii(byte value) =>
        value is >= 0x20 and <= 0x7E ? (char)value : '.';

    private static string FormatLine(string line, int scrollLeft, int width)
    {
        if (width <= 0)
            return string.Empty;

        line = SanitizeTextForConsole(line);
        if (scrollLeft >= line.Length)
            return new string(' ', width);

        string visible = line[scrollLeft..];
        return visible.Length <= width ? visible.PadRight(width) : visible[..width];
    }

    internal static string SanitizeTextForConsole(string line)
    {
        line = line.Replace("\t", "    ");
        Span<char> chars = line.Length <= 1024 ? stackalloc char[line.Length] : new char[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            chars[i] = char.IsControl(ch) ? ' ' : ch;
        }

        return new string(chars);
    }

    private static long FormatPercent(long offset, long length)
    {
        if (length <= 0)
            return 0;

        return Math.Clamp(offset * 100 / length, 0, 100);
    }

    private sealed record LargeFileRenderView(IReadOnlyList<ScannedLine> Lines, long NextOffset);
}
