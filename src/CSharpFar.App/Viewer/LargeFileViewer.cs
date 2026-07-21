using System.Globalization;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class LargeFileViewer
{
    private const int BinaryBytesPerRow = 16;
    private const int FollowPollMs = 250;
    private const int FastHorizontalTextScrollChars = 20;
    private const int FastPageMultiplier = 5;

    private readonly UiCompositionHost _composition;
    private readonly ScreenRenderer _screen;
    private readonly ModalDialogHost _modalDialogs;
    private readonly ConsolePalette _palette;
    private readonly InteractiveSurfaceHost _surfaces;

    public LargeFileViewer(UiCompositionHost composition, ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _composition = composition;
        _screen = composition.Screen;
        _modalDialogs = modalDialogs;
        _palette = palette ?? PaletteRegistry.Default;
        _surfaces = new InteractiveSurfaceHost(composition);
    }

    public void Show(string filePath) => Show(filePath, null);

    internal void Show(string filePath, LargeFileViewerOptions? options)
    {
        options ??= new LargeFileViewerOptions();
        RandomAccessFileByteReader? reader = null;

        try
        {
            var opened = OpenViewerFile(filePath);
            reader = opened.Reader;
            var state = opened.State;

            while (true)
            {
                var layer = new LargeFileViewerLayer(this, filePath, reader, state);
                long knownFollowLength = reader.Length;
                var action = _surfaces.Run(
                    layer,
                    (routed, input) => HandleViewerInput(filePath, reader, state, options, routed.Frame, input),
                    getNextWakeUtc: () => state.FollowMode ? DateTimeOffset.UtcNow.AddMilliseconds(FollowPollMs) : null,
                    handleWake: frame =>
                    {
                        long currentLength = reader.Length;
                        if (currentLength == knownFollowLength)
                            return InteractiveSurfaceWakeResult.NoChange;

                        knownFollowLength = currentLength;
                        MoveToEnd(reader, state, frame.ContentHeight);
                        return InteractiveSurfaceWakeResult.Changed;
                    });
                if (action == ViewerLoopAction.Close)
                    return;

                if (!TryMoveToSibling(options, action == ViewerLoopAction.NextFile ? 1 : -1, out string nextPath))
                {
                    ShowUnsupported(action == ViewerLoopAction.NextFile ? "Next file" : "Previous file");
                    continue;
                }

                reader.Dispose();
                reader = null;

                filePath = nextPath;
                opened = OpenViewerFile(filePath);
                reader = opened.Reader;
                state = opened.State;
                options.CurrentFileChanged?.Invoke(filePath);
            }
        }
        catch (Exception ex)
        {
            new MessageDialog(_modalDialogs).Show("Viewer", ex.Message);
        }
        finally
        {
            reader?.Dispose();
        }
    }

    private static (RandomAccessFileByteReader Reader, LargeFileViewerState State) OpenViewerFile(string filePath)
    {
        var reader = new RandomAccessFileByteReader(filePath);
        try
        {
            var cache = new BlockCache(reader);
            var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
            return (reader, new LargeFileViewerState(cache, scanner));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private ModalDialogLoopResult<ViewerLoopAction> HandleViewerInput(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileViewerOptions options,
        LargeFileViewerFrame frame,
        ViewerInput input)
    {
        var size = frame.Size;
        int contentHeight = frame.ContentHeight;
        var view = frame.View;

        if (input.ScrollLines is { } lines)
        {
            ApplyScrollLines(reader, state, view, lines);
            return ModalDialogLoopResult<ViewerLoopAction>.Continue;
        }

        if (input.Key is not { } key || key.Key == ConsoleKey.NoName)
            return ModalDialogLoopResult<ViewerLoopAction>.Continue;

        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;

        if (TryHandleUnsupportedNumberedBookmark(key, control, shift, alt))
            return ModalDialogLoopResult<ViewerLoopAction>.Continue;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                MoveUp(state);
                break;

            case ConsoleKey.DownArrow:
                MoveDown(reader, state, view);
                break;

            case ConsoleKey.LeftArrow when control && shift:
                state.HorizontalOffset = 0;
                break;

            case ConsoleKey.RightArrow when control && shift:
                MoveHorizontalToCurrentLineEnd(state, view, size.Width);
                break;

            case ConsoleKey.LeftArrow when control:
                MoveHorizontal(state, -(state.IsHexMode ? 1 : FastHorizontalTextScrollChars));
                break;

            case ConsoleKey.RightArrow when control:
                MoveHorizontal(state, state.IsHexMode ? 1 : FastHorizontalTextScrollChars);
                break;

            case ConsoleKey.LeftArrow when shift:
            case ConsoleKey.RightArrow when shift:
                ShowUnsupported("Viewer text selection");
                break;

            case ConsoleKey.LeftArrow:
                MoveHorizontal(state, -1);
                break;

            case ConsoleKey.RightArrow:
                MoveHorizontal(state, 1);
                break;

            case ConsoleKey.PageUp when alt:
                MovePageUp(state, contentHeight, FastPageMultiplier);
                break;

            case ConsoleKey.PageDown when alt:
                MovePageDown(reader, state, contentHeight, FastPageMultiplier);
                break;

            case ConsoleKey.PageUp:
                MovePageUp(state, contentHeight, pages: 1);
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

            case ConsoleKey.F1:
                new HelpViewer(_composition, _palette).Show();
                break;

            case ConsoleKey.F2 when shift && !alt && !control:
                state.WordWrap = !state.WordWrap;
                state.WrapLines = true;
                state.HorizontalOffset = 0;
                break;

            case ConsoleKey.F2 when !shift && !alt && !control:
                state.WrapLines = !state.WrapLines;
                if (state.WrapLines)
                    state.HorizontalOffset = 0;
                break;

            case ConsoleKey.F3 when !shift && !alt && !control:
            case ConsoleKey.NumPad5 when !shift && !alt && !control:
                return ModalDialogLoopResult<ViewerLoopAction>.Complete(ViewerLoopAction.Close);

            case ConsoleKey.F4 when !shift && !alt && !control:
                ToggleViewMode(state);
                break;

            case ConsoleKey.F5 when alt:
                ShowUnsupported("Print");
                break;

            case ConsoleKey.F5:
                ShowUnsupported("Raw/processed viewer mode");
                break;

            case ConsoleKey.F6 when !shift && !alt && !control:
                EditCurrentFile(filePath, reader, state, options);
                break;

            case ConsoleKey.F7 when control:
                ShowUnsupported("Viewer grep filter");
                break;

            case ConsoleKey.F7 when alt:
                RepeatSearch(reader, state, searchBackward: true, size.Width);
                break;

            case ConsoleKey.F7 when shift && !alt:
                RepeatSearch(reader, state, searchBackward: false, size.Width);
                break;

            case ConsoleKey.F7 when !shift && !alt && !control:
                ShowFindDialog(reader, state, size.Width);
                break;

            case ConsoleKey.F8 when alt:
                JumpToPosition(reader, state, contentHeight);
                break;

            case ConsoleKey.F8 when control:
                ShowUnsupported("Ctrl+F8");
                break;

            case ConsoleKey.F8 when shift:
                ChangeEncoding(filePath, reader, state, contentHeight, size);
                break;

            case ConsoleKey.F8 when !shift && !alt && !control:
                CycleCommonEncoding(reader, state);
                break;

            case ConsoleKey.F9:
                ShowUnsupported("Viewer settings");
                break;

            case ConsoleKey.F10 when control:
                ShowUnsupported("Show current file in panel");
                break;

            case ConsoleKey.F10 when !shift && !alt && !control:
            case ConsoleKey.Escape:
                return ModalDialogLoopResult<ViewerLoopAction>.Complete(ViewerLoopAction.Close);

            case ConsoleKey.F11 when alt:
                ShowUnsupported("Viewer history");
                break;

            case ConsoleKey.F11:
                ShowUnsupported("Plugin menu");
                break;

            case ConsoleKey.F when !shift && !alt && !control:
                state.FollowMode = !state.FollowMode;
                if (state.FollowMode)
                    MoveToEnd(reader, state, contentHeight);
                break;

            case ConsoleKey.G when !shift && !alt && !control:
                JumpToPosition(reader, state, contentHeight);
                break;

            case ConsoleKey.H when !shift && !alt && !control:
                ToggleViewMode(state);
                break;

            case ConsoleKey.Spacebar when !shift && !alt && !control:
                RepeatSearch(reader, state, searchBackward: false, size.Width);
                break;

            case ConsoleKey.U when control:
                state.SearchMatch = null;
                break;

            case ConsoleKey.C when control:
            case ConsoleKey.Insert when control:
                CopySearchMatch(state, options);
                break;

            case ConsoleKey.O when control:
                ShowUnsupported("Show work screen");
                break;

            case ConsoleKey.B when control:
                ShowUnsupported(shift ? "Status line toggle" : "Function key bar toggle");
                break;

            case ConsoleKey.S when control:
                ShowUnsupported("Scrollbar toggle");
                break;

            case ConsoleKey.Z when control:
            case ConsoleKey.Backspace when alt:
                ShowUnsupported("Undo viewer position");
                break;

            case ConsoleKey.Add when !shift && !alt && !control:
            case ConsoleKey.OemPlus when !shift && !alt && !control:
                return ModalDialogLoopResult<ViewerLoopAction>.Complete(ViewerLoopAction.NextFile);

            case ConsoleKey.Subtract when !shift && !alt && !control:
            case ConsoleKey.OemMinus when !shift && !alt && !control:
                return ModalDialogLoopResult<ViewerLoopAction>.Complete(ViewerLoopAction.PreviousFile);
        }

        return ModalDialogLoopResult<ViewerLoopAction>.Continue;
    }

    private bool TryHandleUnsupportedNumberedBookmark(
        ConsoleKeyInfo key,
        bool control,
        bool shift,
        bool alt)
    {
        _ = shift;
        if (!control || alt || !TryGetNumberKey(key.Key, out _))
            return false;

        ShowUnsupported("Viewer bookmarks");
        return true;
    }

    private static bool TryGetNumberKey(ConsoleKey key, out int number)
    {
        number = key switch
        {
            ConsoleKey.D0 or ConsoleKey.NumPad0 => 0,
            ConsoleKey.D1 or ConsoleKey.NumPad1 => 1,
            ConsoleKey.D2 or ConsoleKey.NumPad2 => 2,
            ConsoleKey.D3 or ConsoleKey.NumPad3 => 3,
            ConsoleKey.D4 or ConsoleKey.NumPad4 => 4,
            ConsoleKey.D5 or ConsoleKey.NumPad5 => 5,
            ConsoleKey.D6 or ConsoleKey.NumPad6 => 6,
            ConsoleKey.D7 or ConsoleKey.NumPad7 => 7,
            ConsoleKey.D8 or ConsoleKey.NumPad8 => 8,
            ConsoleKey.D9 or ConsoleKey.NumPad9 => 9,
            _ => -1,
        };
        return number >= 0;
    }

    private void ApplyScrollLines(
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int lines)
    {
        if (lines < 0)
        {
            for (int i = 0; i < -lines; i++)
                MoveUp(state);
            return;
        }

        for (int i = 0; i < lines; i++)
            MoveDown(reader, state, view);
    }

    private LargeFileRenderView Draw(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        ConsoleSize size)
    {
        DrawHeader(filePath, reader, state, size);

        var view = state.IsHexMode
            ? DrawBinaryContent(reader, state, contentHeight, size.Width)
            : DrawTextContent(reader, state, contentHeight, size.Width);

        DrawFooter(size, state);
        return view;
    }

    private void DrawHeader(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        ConsoleSize size)
    {
        string mode = state.IsHexMode ? " HEX" : $" TEXT {state.LineScanner.EncodingDisplayName}";
        string wrap = !state.IsHexMode && state.WrapLines
            ? state.WordWrap ? " WRAP-W" : " WRAP-C"
            : string.Empty;
        string follow = state.FollowMode ? " F " : string.Empty;
        string found = state.SearchMatch is not null ? " FIND" : string.Empty;
        string posSection = reader.Length == 0
            ? $" 0%{mode}{wrap}{follow}{found} "
            : $" {FormatPercent(state.TopByteOffset, reader.Length)}%{mode}{wrap}{follow}{found} ";

        int nameWidth = Math.Max(0, size.Width - posSection.Length);
        string nameSection = FormatHeaderPath(filePath, nameWidth);

        string header = nameSection.PadRight(nameWidth) + posSection;
        _screen.WriteForced(0, 0, header, PaletteStyles.PathHeaderActive(_palette));
    }

    private static string FormatHeaderPath(string filePath, int width)
    {
        if (width <= 0)
            return string.Empty;

        string text = $" {filePath} ";
        if (text.Length <= width)
            return text;

        if (width <= 3)
            return text[^width..];

        return "..." + text[^(width - 3)..];
    }

    private LargeFileRenderView DrawTextContent(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        return state.WrapLines
            ? DrawWrappedTextContent(reader, state, contentHeight, width)
            : DrawUnwrappedTextContent(state, contentHeight, width);
    }

    private LargeFileRenderView DrawUnwrappedTextContent(
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
            if (row < scanned.Lines.Count)
            {
                var line = scanned.Lines[row];
                WriteTextLine(line.Text, row + 1, state.HorizontalOffset, width, state.SearchMatch, line.StartOffset, segmentStartIndex: 0);
            }
            else
            {
                _screen.WriteForced(0, row + 1, new string(' ', width), PaletteStyles.CommandLine(_palette));
            }
        }

        return new LargeFileRenderView(scanned.Lines, scanned.NextOffset);
    }

    private LargeFileRenderView DrawWrappedTextContent(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        var lines = new List<ScannedLine>();
        int row = 0;
        long offset = Math.Clamp(state.TopByteOffset, state.LineScanner.ContentStartOffset, reader.Length);
        long nextOffset = offset;
        int bytesPerLine = Math.Max(256, Math.Max(1, width) * Math.Max(1, contentHeight) * 4 + 1024);

        while (row < contentHeight && offset < reader.Length)
        {
            var scanned = state.LineScanner
                .ReadLinesAsync(offset, 1, bytesPerLine)
                .GetAwaiter()
                .GetResult();
            if (scanned.Lines.Count == 0)
                break;

            var line = scanned.Lines[0];
            lines.Add(line);
            nextOffset = line.NextOffset;

            foreach (var segment in SplitWrappedLine(line.Text, Math.Max(1, width), state.WordWrap))
            {
                if (row >= contentHeight)
                    break;

                WriteTextLine(segment.Text, row + 1, scrollLeft: 0, width, state.SearchMatch, line.StartOffset, segment.StartIndex);
                row++;
            }

            if (line.NextOffset <= offset)
                break;

            offset = line.NextOffset;
        }

        while (row < contentHeight)
        {
            _screen.WriteForced(0, row + 1, new string(' ', width), PaletteStyles.CommandLine(_palette));
            row++;
        }

        return new LargeFileRenderView(lines, nextOffset);
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
            var style = IsHexMatchOnRow(state.SearchMatch, rowOffset)
                ? PaletteStyles.InputHighlight(_palette)
                : PaletteStyles.CommandLine(_palette);
            _screen.WriteForced(0, row + 1, FormatLine(text, state.HorizontalOffset, width), style);
        }

        return new LargeFileRenderView(rows, nextOffset);
    }

    private static bool IsHexMatchOnRow(ViewerSearchMatch? match, long rowOffset) =>
        match is { IsHex: true } &&
        match.ByteOffset < rowOffset + BinaryBytesPerRow &&
        match.ByteOffset + match.ByteLength > rowOffset;

    private void WriteTextLine(
        string line,
        int y,
        int scrollLeft,
        int width,
        ViewerSearchMatch? match,
        long lineStartOffset,
        int segmentStartIndex)
    {
        if (width <= 0)
            return;

        string sanitized = SanitizeTextForConsole(line);
        if (scrollLeft >= sanitized.Length)
        {
            _screen.WriteForced(0, y, new string(' ', width), PaletteStyles.CommandLine(_palette));
            return;
        }

        string visible = sanitized[scrollLeft..];
        if (visible.Length > width)
            visible = visible[..width];

        _screen.WriteForced(0, y, visible.PadRight(width), PaletteStyles.CommandLine(_palette));
        if (match is not { IsHex: false } || match.LineStartOffset != lineStartOffset)
            return;

        int visibleStart = segmentStartIndex + scrollLeft;
        int visibleEnd = visibleStart + visible.Length;
        int matchStart = match.CharacterIndex;
        int matchEnd = match.CharacterIndex + match.CharacterLength;
        int highlightStart = Math.Max(visibleStart, matchStart);
        int highlightEnd = Math.Min(visibleEnd, matchEnd);
        if (highlightEnd <= highlightStart)
            return;

        int x = highlightStart - visibleStart;
        int length = highlightEnd - highlightStart;
        if (x >= 0 && x < visible.Length)
            _screen.Write(x, y, visible.Substring(x, Math.Min(length, visible.Length - x)), PaletteStyles.InputHighlight(_palette));
    }

    private void DrawFooter(ConsoleSize size, LargeFileViewerState state)
    {
        new FunctionKeyBarController<ConsoleKeyInfo>().Render(
            _screen,
            size.Height - 1,
            size.Width,
            ViewerFunctionKeyBarActions(state));
    }

    private static FunctionKeyBarAction<ConsoleKeyInfo>[] ViewerFunctionKeyBarActions(LargeFileViewerState state) =>
    [
        ViewerFunctionKeyAction(1, "Help", ConsoleKey.F1),
        ViewerFunctionKeyAction(2, state.WrapLines ? "Unwrap" : "Wrap", ConsoleKey.F2),
        ViewerFunctionKeyAction(3, "Close", ConsoleKey.F3),
        ViewerFunctionKeyAction(4, "Hex", ConsoleKey.F4),
        ViewerFunctionKeyAction(6, "Edit", ConsoleKey.F6),
        ViewerFunctionKeyAction(7, "Find", ConsoleKey.F7),
        ViewerFunctionKeyAction(8, "Enc", ConsoleKey.F8),
        ViewerFunctionKeyAction(10, "Close", ConsoleKey.F10),
    ];

    private static FunctionKeyBarAction<ConsoleKeyInfo> ViewerFunctionKeyAction(
        int keyNumber,
        string label,
        ConsoleKey key) =>
        new(keyNumber, label, new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

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

    private void MovePageUp(LargeFileViewerState state, int contentHeight, int pages)
    {
        int pageCount = Math.Max(1, pages);
        for (int page = 0; page < pageCount; page++)
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

    private void MovePageDown(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int pages)
    {
        int pageCount = Math.Max(1, pages);
        for (int page = 0; page < pageCount; page++)
        {
            if (state.IsHexMode)
            {
                long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
                state.TopByteOffset = Math.Min(reader.Length, state.TopByteOffset + delta);
            }
            else
            {
                var scanned = state.LineScanner
                    .ReadLinesAsync(state.TopByteOffset, Math.Max(1, contentHeight), maxBytesPerLine: 256)
                    .GetAwaiter()
                    .GetResult();
                state.TopByteOffset = scanned.NextOffset;
            }
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

    private void MoveHorizontal(LargeFileViewerState state, int delta)
    {
        if (state.WrapLines)
            return;

        state.HorizontalOffset = Math.Max(0, state.HorizontalOffset + delta);
    }

    private static void MoveHorizontalToCurrentLineEnd(
        LargeFileViewerState state,
        LargeFileRenderView view,
        int width)
    {
        if (state.WrapLines || view.Lines.Count == 0)
            return;

        int lineLength = SanitizeTextForConsole(view.Lines[0].Text).Length;
        state.HorizontalOffset = Math.Max(0, lineLength - Math.Max(1, width));
    }

    private void JumpToPosition(IFileByteReader reader, LargeFileViewerState state, int contentHeight)
    {
        string? input = new InputDialog(_modalDialogs).Show(
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
        state.SearchMatch = null;
    }

    private void ShowFindDialog(IFileByteReader reader, LargeFileViewerState state, int width)
    {
        var selected = new ViewerFindDialog(_modalDialogs, _palette).Show(state.LastSearch, state.IsHexMode);
        if (selected is null)
            return;

        var request = ViewerSearchRequest.FromDialog(selected);
        FindAndApply(reader, state, request, searchBackward: false, width);
    }

    private void RepeatSearch(
        IFileByteReader reader,
        LargeFileViewerState state,
        bool searchBackward,
        int width)
    {
        if (state.LastSearch is null)
        {
            ShowFindDialog(reader, state, width);
            return;
        }

        FindAndApply(reader, state, state.LastSearch, searchBackward, width);
    }

    private void FindAndApply(
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request,
        bool searchBackward,
        int width)
    {
        ViewerSearchMatch? match;
        try
        {
            match = ViewerSearchEngine.Find(reader, state, request, searchBackward);
        }
        catch (ArgumentException ex)
        {
            new MessageDialog(_modalDialogs).Show("Find", ex.Message);
            return;
        }

        if (match is null)
        {
            new MessageDialog(_modalDialogs).Show("Find", "Text not found.");
            return;
        }

        state.LastSearch = request;
        state.SearchMatch = match;
        state.TopByteOffset = match.TopByteOffset;
        state.FollowMode = false;

        if (!match.IsHex && !state.WrapLines)
        {
            int rightEdge = state.HorizontalOffset + Math.Max(1, width);
            if (match.CharacterIndex < state.HorizontalOffset || match.CharacterIndex >= rightEdge)
                state.HorizontalOffset = Math.Max(0, match.CharacterIndex - 4);
        }
        else if (match.IsHex)
        {
            state.ViewMode = LargeFileViewMode.Hex;
        }
    }

    private void CopySearchMatch(LargeFileViewerState state, LargeFileViewerOptions options)
    {
        if (state.SearchMatch is null)
        {
            new MessageDialog(_modalDialogs).Show("Viewer", "No active search match.");
            return;
        }

        if (options.Clipboard is null)
        {
            ShowUnsupported("Clipboard copy");
            return;
        }

        if (!options.Clipboard.TrySetText(state.SearchMatch.MatchedText))
            new MessageDialog(_modalDialogs).Show("Viewer", "Could not copy text to clipboard.");
    }

    private void EditCurrentFile(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileViewerOptions options)
    {
        if (options.EditFile is null)
        {
            ShowUnsupported("Edit from viewer");
            return;
        }

        options.EditFile(filePath);
        RefreshScanner(reader, state);
    }

    private static void RefreshScanner(IFileByteReader reader, LargeFileViewerState state)
    {
        long anchorByteOffset = state.TopByteOffset;
        var originalViewMode = state.ViewMode;
        state.BlockCache.Clear();
        var scanner = LineScanner
            .CreateAsync(state.BlockCache, reader, state.EncodingSelection)
            .GetAwaiter()
            .GetResult();
        state.ResetScanner(scanner, state.EncodingSelection);
        state.ViewMode = originalViewMode;
        state.TopByteOffset = state.IsHexMode
            ? Math.Clamp(anchorByteOffset, 0, reader.Length)
            : scanner.FindLineStartAtOrBeforeAsync(anchorByteOffset).GetAwaiter().GetResult();
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

        var selected = new EncodingSelectionDialog(_modalDialogs).Show(
            items,
            state.EncodingSelection,
            previewSelection: item => ApplyEncodingSelection(
                reader,
                state,
                item.Selection,
                anchorByteOffset,
                originalViewMode),
            previewRedraw: () => _composition.Render());

        if (selected is null)
        {
            ApplyEncodingSelection(reader, state, originalSelection, anchorByteOffset, originalViewMode);
            state.HorizontalOffset = originalHorizontalOffset;
            state.FollowMode = originalFollowMode;
            return;
        }

        ApplyEncodingSelection(reader, state, selected.Selection, anchorByteOffset, originalViewMode);
    }

    private static void CycleCommonEncoding(IFileByteReader reader, LargeFileViewerState state)
    {
        int[] commonCodePages = [65001, 866, 1251];
        int currentCodePage = state.EncodingSelection.Kind == TextEncodingSelectionKind.Explicit
            ? state.EncodingSelection.CodePage ?? -1
            : -1;
        int currentIndex = Array.IndexOf(commonCodePages, currentCodePage);
        int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % commonCodePages.Length;
        ApplyEncodingSelection(
            reader,
            state,
            TextEncodingSelection.Explicit(commonCodePages[nextIndex]),
            state.TopByteOffset,
            state.ViewMode);
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

    private static bool TryMoveToSibling(
        LargeFileViewerOptions options,
        int direction,
        out string filePath)
    {
        filePath = string.Empty;
        if (!options.HasSiblingFiles)
            return false;

        int index = options.CurrentFileIndex + direction;
        while (index >= 0 && index < options.FilePaths.Count)
        {
            string candidate = options.FilePaths[index];
            if (File.Exists(candidate))
            {
                options.CurrentFileIndex = index;
                filePath = candidate;
                return true;
            }

            index += direction;
        }

        return false;
    }

    private void ShowUnsupported(string command) =>
        new MessageDialog(_modalDialogs).Show("Viewer", $"{command} is not supported yet.");

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

    private static IEnumerable<WrappedTextSegment> SplitWrappedLine(string line, int width, bool wordWrap)
    {
        if (line.Length == 0)
        {
            yield return new WrappedTextSegment(0, string.Empty);
            yield break;
        }

        int index = 0;
        while (index < line.Length)
        {
            int take = Math.Min(width, line.Length - index);
            if (wordWrap && index + take < line.Length)
            {
                int breakAt = line.LastIndexOf(' ', index + take - 1, take);
                if (breakAt > index)
                    take = breakAt - index + 1;
            }

            yield return new WrappedTextSegment(index, line.Substring(index, take));
            index += take;
        }
    }

    private static long FormatPercent(long offset, long length)
    {
        if (length <= 0)
            return 0;

        return Math.Clamp(offset * 100 / length, 0, 100);
    }

    private sealed class LargeFileViewerLayer : InteractiveSurfaceLayer<LargeFileViewerFrame, ViewerInput>
    {
        internal static readonly UiTargetId Keyboard = new("viewer.keyboard");
        internal static readonly UiTargetId Content = new("viewer.content");
        internal static readonly UiTargetId FunctionKeys = new("viewer.function-key-bar");

        private readonly LargeFileViewer _viewer;
        private readonly string _filePath;
        private readonly IFileByteReader _reader;
        private readonly LargeFileViewerState _state;

        public LargeFileViewerLayer(
            LargeFileViewer viewer,
            string filePath,
            IFileByteReader reader,
            LargeFileViewerState state)
            : base(
                (_, _) => throw new InvalidOperationException("LargeFileViewerLayer uses overridden rendering."),
                _ => UiInteractionFrame.Empty,
                (_, _, _) => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None))
        {
            _viewer = viewer;
            _filePath = filePath;
            _reader = reader;
            _state = state;
        }

        protected override LargeFileViewerFrame RenderFrameCore(UiRenderContext context)
        {
            int contentHeight = Math.Max(0, context.Size.Height - 2);
            LargeFileRenderView view = _viewer.Draw(_filePath, _reader, _state, contentHeight, context.Size);
            IReadOnlyList<FunctionKeyHit> functionKeyHits = BuildFunctionKeyHits(
                context.Size.Height > 0 ? context.Size.Height - 1 : 0,
                context.Size.Width,
                ViewerFunctionKeyBarActions(_state));

            return new LargeFileViewerFrame(
                context.Viewport,
                context.Size,
                context.Size.Height > 0 ? new Rect(0, 0, context.Size.Width, 1) : new Rect(0, 0, 0, 0),
                contentHeight > 0 ? new Rect(0, 1, context.Size.Width, contentHeight) : new Rect(0, 0, 0, 0),
                context.Size.Height > 0 ? new Rect(0, context.Size.Height - 1, context.Size.Width, 1) : new Rect(0, 0, 0, 0),
                contentHeight,
                view,
                functionKeyHits);
        }

        protected override UiInteractionFrame BuildInteractionFrameCore(LargeFileViewerFrame frame)
        {
            var builder = new UiInteractionFrameBuilder()
                .AddFocusEntry(Keyboard, 0, cursor: new UiCursorPlacement(0, 0, false))
                .SetDefaultFocusTarget(Keyboard)
                .SetKeyboardTarget(Keyboard);
            if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
                builder.AddHitRegion(Content, frame.ContentBounds);
            foreach (FunctionKeyHit hit in frame.FunctionKeyHits)
                builder.AddHitRegion(FunctionKeys, hit.Bounds);

            return builder.Build();
        }

        protected override InteractiveSurfaceRouteResult<ViewerInput> RouteSemanticInput(
            ConsoleInputEvent input,
            LargeFileViewerFrame frame,
            UiInputRouteContext context)
        {
            if (input is KeyConsoleInputEvent key &&
                context is { RouteKind: UiInputRouteKind.KeyboardTarget, Target: not null } &&
                context.Target == Keyboard)
            {
                return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromKey(key.Key));
            }

            if (input is not MouseConsoleInputEvent mouse)
                return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None);

            if (context.Target == Content && mouse.Kind == MouseEventKind.Wheel)
            {
                const int wheelLines = 3;
                return mouse.Button switch
                {
                    MouseButton.WheelUp => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromScroll(-wheelLines)),
                    MouseButton.WheelDown => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromScroll(wheelLines)),
                    _ => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None),
                };
            }

            if (context.Target == FunctionKeys && mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            {
                FunctionKeyHit? hit = frame.FunctionKeyHits.FirstOrDefault(value => value.Bounds.Contains(mouse.X, mouse.Y));
                return new InteractiveSurfaceRouteResult<ViewerInput>(
                    hit is { } value ? ViewerInput.FromKey(value.Key) : ViewerInput.None);
            }

            return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None);
        }

        private static IReadOnlyList<FunctionKeyHit> BuildFunctionKeyHits(
            int y,
            int totalWidth,
            IReadOnlyList<FunctionKeyBarAction<ConsoleKeyInfo>> actions)
        {
            var enabled = actions.Where(action => action.Enabled).ToDictionary(action => action.KeyNumber, action => action.Action);
            return FunctionKeyBar.BuildSlots(y, totalWidth)
                .Where(slot => enabled.ContainsKey(slot.KeyNumber))
                .Select(slot => new FunctionKeyHit(slot.Bounds, enabled[slot.KeyNumber]))
                .ToArray();
        }
    }

    private sealed record LargeFileViewerFrame(
        ConsoleViewport Viewport,
        ConsoleSize Size,
        Rect HeaderBounds,
        Rect ContentBounds,
        Rect FunctionKeyBarBounds,
        int ContentHeight,
        LargeFileRenderView View,
        IReadOnlyList<FunctionKeyHit> FunctionKeyHits);

    private sealed record FunctionKeyHit(Rect Bounds, ConsoleKeyInfo Key);

    private readonly record struct ViewerInput(ConsoleKeyInfo? Key, int? ScrollLines)
    {
        public static ViewerInput None => new(null, null);

        public static ViewerInput FromKey(ConsoleKeyInfo key) => new(key, null);

        public static ViewerInput FromScroll(int lines) => new(null, lines);
    }

    private sealed record LargeFileRenderView(IReadOnlyList<ScannedLine> Lines, long NextOffset);

    private sealed record WrappedTextSegment(int StartIndex, string Text);

    private enum ViewerLoopAction
    {
        Close,
        NextFile,
        PreviousFile,
    }
}
