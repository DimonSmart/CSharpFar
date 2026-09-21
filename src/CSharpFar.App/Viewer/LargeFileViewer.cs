using System.Globalization;
using System.Text;
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
    private const int MaxWrappedLineCaptureBytes = 4 * 1024 * 1024;
    private const int LivePollMs = 250;
    private const int FastHorizontalTextScrollCells = 20;
    private const int FastPageMultiplier = 5;

    private static readonly UiTargetScope Targets = new("viewer");

    private static readonly FunctionKeyBarController<ConsoleKeyInfo> FunctionKeysController =
        new(Targets.Child("function-key-bar"));

    private readonly ModalDialogHost _modalDialogs;
    private readonly DialogService _dialogs;
    private readonly CSharpFarPalette _palette;
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly FormFieldFactory _fields;

    public LargeFileViewer(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        DialogService dialogs,
        FormFieldFactory fields,
        CSharpFarPalette? palette = null)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _modalDialogs = modalDialogs;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _palette = palette ?? CSharpFarPaletteRegistry.Default;
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public void Show(string filePath) => Show(filePath, null);

    internal void Show(string filePath, LargeFileViewerOptions? options)
    {
        options ??= new LargeFileViewerOptions();
        LocalViewerSession? session = null;

        try
        {
            session = OpenViewerFile(filePath);

            while (true)
            {
                var reader = session.Reader;
                var state = session.State;
                var layer = new LargeFileViewerLayer(this, filePath, reader, state);
                var action = _surfaces.Run(
                    layer,
                    (routed, input) => HandleViewerInput(
                        filePath,
                        hasPhysicalSourcePath: true,
                        reader,
                        state,
                        options,
                        routed.Frame,
                        input,
                        session),
                    getNextWakeUtc: () =>
                        state.LiveMode == ViewerLiveMode.Off
                            ? null
                            : DateTimeOffset.UtcNow.AddMilliseconds(LivePollMs),
                    handleWake: frame => HandleLocalWake(filePath, session, frame));
                if (action == ViewerLoopAction.Close)
                    return;

                if (!TryMoveToSibling(options, action == ViewerLoopAction.NextFile ? 1 : -1, out string nextPath))
                {
                    ShowUnsupported(action == ViewerLoopAction.NextFile ? "Next file" : "Previous file");
                    continue;
                }

                var presentationMode = state.PresentationMode;
                session.Dispose();
                session = null;

                filePath = nextPath;
                session = OpenViewerFile(filePath);
                session.State.PresentationMode = presentationMode;
                options.CurrentFileChanged?.Invoke(filePath);
            }
        }
        catch (Exception ex)
        {
            _dialogs.Message("Viewer", ex.Message);
        }
        finally
        {
            session?.Dispose();
        }
    }

    internal void ShowVirtual(string filePath, IFileByteReader reader, LargeFileViewerOptions? options)
    {
        options ??= new LargeFileViewerOptions();

        try
        {
            var cache = new BlockCache(reader);
            var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
            var state = new LargeFileViewerState(cache, scanner);
            if (state.IsHexMode)
                state.TopByteOffset = 0;
            var layer = new LargeFileViewerLayer(this, filePath, reader, state);
            _surfaces.Run(
                layer,
                (routed, input) => HandleViewerInput(
                    filePath,
                    hasPhysicalSourcePath: false,
                    reader,
                    state,
                    options,
                    routed.Frame,
                    input,
                    localSession: null));
        }
        catch (Exception ex)
        {
            _dialogs.Message("Viewer", ex.Message);
        }
    }

    private static LocalViewerSession OpenViewerFile(string filePath)
    {
        var reader = new RandomAccessFileByteReader(filePath);
        LocalFileMonitor? monitor = null;
        try
        {
            monitor = new LocalFileMonitor(filePath);
            if (!LocalFileMonitor.TryCaptureSnapshot(filePath, out LocalFileSnapshot before) || !before.Exists)
                throw new FileNotFoundException("File not found.", filePath);

            var cache = new BlockCache(reader);
            var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
            var state = new LargeFileViewerState(cache, scanner);
            if (state.IsHexMode)
                state.TopByteOffset = 0;

            LocalFileSnapshot applied = before;
            bool pending = monitor.TakeDirty();
            if (LocalFileMonitor.TryCaptureSnapshot(filePath, out LocalFileSnapshot after) && after.Exists)
            {
                applied = after;
                pending |= after != before;
            }
            else
            {
                pending = true;
            }

            return new LocalViewerSession(filePath, reader, state, monitor, applied, pending);
        }
        catch
        {
            monitor?.Dispose();
            reader.Dispose();
            throw;
        }
    }

    private InteractiveSurfaceWakeResult HandleLocalWake(
        string filePath,
        LocalViewerSession session,
        LargeFileViewerFrame frame)
    {
        session.DetectChanges();
        return TryRefreshLocalFile(session, filePath, frame.ContentHeight, frame.Size.Width)
            ? InteractiveSurfaceWakeResult.Changed
            : InteractiveSurfaceWakeResult.NoChange;
    }

    private bool TryRefreshLocalFile(
        LocalViewerSession session,
        string filePath,
        int contentHeight,
        int width)
    {
        if (!session.PendingRefresh)
            return false;

        if (!LocalFileMonitor.TryCaptureSnapshot(filePath, out LocalFileSnapshot before) || !before.Exists)
            return false;

        var reader = session.Reader;
        var state = session.State;
        int failureVersion = reader.TransientFailureVersion;
        long anchorByteOffset = state.TopByteOffset;
        int anchorWrappedSegment = state.TopWrappedSegmentIndex;
        var viewMode = state.ViewMode;
        int horizontalOffset = state.HorizontalOffset;
        var liveMode = state.LiveMode;
        var encodingSelection = state.EncodingSelection;

        try
        {
            var candidateCache = new BlockCache(reader);
            var candidateScanner = LineScanner
                .CreateAsync(candidateCache, reader, encodingSelection)
                .GetAwaiter()
                .GetResult();

            if (reader.TransientFailureVersion != failureVersion)
                return false;

            if (!LocalFileMonitor.TryCaptureSnapshot(filePath, out LocalFileSnapshot after) ||
                !after.Exists ||
                after != before)
            {
                return false;
            }

            state.ReplaceContent(candidateCache, candidateScanner, encodingSelection);
            state.ViewMode = viewMode;
            state.HorizontalOffset = horizontalOffset;
            state.LiveMode = liveMode;
            state.TopByteOffset = state.IsHexMode
                ? Math.Max(0, anchorByteOffset)
                : candidateScanner
                    .FindLineStartAtOrBeforeAsync(anchorByteOffset)
                    .GetAwaiter()
                    .GetResult();
            state.TopWrappedSegmentIndex = anchorWrappedSegment;

            NormalizeViewport(filePath, reader, state, contentHeight, width);
            session.Commit(after);
            return true;
        }
        catch (Exception ex) when (RandomAccessFileByteReader.IsTransientFileAccess(ex))
        {
            return false;
        }
    }

    private ModalDialogLoopResult<ViewerLoopAction> HandleViewerInput(
        string filePath,
        bool hasPhysicalSourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileViewerOptions options,
        LargeFileViewerFrame frame,
        ViewerInput input,
        LocalViewerSession? localSession)
    {
        var size = frame.Size;
        int contentHeight = frame.ContentHeight;
        var view = frame.View;

        if (input.ScrollLines is { } lines)
        {
            ApplyScrollLines(filePath, reader, state, view, lines, contentHeight, size.Width, localSession);
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueChanged;
        }

        if (input.LinkTarget is { } linkTarget)
        {
            OpenMarkdownLink(
                linkTarget,
                hasPhysicalSourcePath ? filePath : null,
                options);
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueNoChange;
        }

        if (input.Key is not { } key || key.Key == ConsoleKey.NoName)
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueNoChange;

        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;

        if (TryHandleUnsupportedNumberedBookmark(key, control, shift, alt))
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueNoChange;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                MoveUp(filePath, reader, state, contentHeight, size.Width);
                UpdateLiveModeAfterAwayNavigation(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.DownArrow:
                MoveDown(filePath, reader, state, view, contentHeight, size.Width);
                UpdateLiveModeAfterDownwardNavigation(localSession, filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.LeftArrow when control && shift:
                state.HorizontalOffset = 0;
                break;

            case ConsoleKey.RightArrow when control && shift:
                MoveHorizontalToCurrentLineEnd(state, view, size.Width);
                break;

            case ConsoleKey.LeftArrow when control:
                MoveHorizontal(state, -(state.IsHexMode ? 1 : FastHorizontalTextScrollCells));
                break;

            case ConsoleKey.RightArrow when control:
                MoveHorizontal(state, state.IsHexMode ? 1 : FastHorizontalTextScrollCells);
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
                MovePageUp(filePath, reader, state, contentHeight, size.Width, FastPageMultiplier);
                UpdateLiveModeAfterAwayNavigation(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.PageDown when alt:
                MovePageDown(filePath, reader, state, contentHeight, size.Width, FastPageMultiplier);
                UpdateLiveModeAfterDownwardNavigation(localSession, filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.PageUp:
                MovePageUp(filePath, reader, state, contentHeight, size.Width, pages: 1);
                UpdateLiveModeAfterAwayNavigation(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.PageDown:
                MovePageDown(filePath, reader, state, view, contentHeight, size.Width);
                UpdateLiveModeAfterDownwardNavigation(localSession, filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.Home:
                state.TopByteOffset = state.IsHexMode ? 0 : state.LineScanner.ContentStartOffset;
                state.TopWrappedSegmentIndex = 0;
                state.HorizontalOffset = 0;
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width, keepTail: false);
                UpdateLiveModeAfterAwayNavigation(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.End:
                MoveToEnd(filePath, reader, state, contentHeight, size.Width);
                state.HorizontalOffset = 0;
                if (localSession is not null)
                    state.LiveMode = ViewerLiveMode.Tail;
                break;

            case ConsoleKey.F1:
                new HelpViewer(_surfaces, _palette).Show();
                break;

            case ConsoleKey.F2 when shift && !alt && !control:
                state.WordWrap = !state.WordWrap;
                state.WrapLines = true;
                state.HorizontalOffset = 0;
                state.TopWrappedSegmentIndex = 0;
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.F2 when !shift && !alt && !control:
                state.WrapLines = !state.WrapLines;
                state.TopWrappedSegmentIndex = 0;
                if (state.WrapLines)
                    state.HorizontalOffset = 0;
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.F3 when !shift && !alt && !control:
            case ConsoleKey.NumPad5 when !shift && !alt && !control:
                return ModalDialogLoopResult<ViewerLoopAction>.Complete(ViewerLoopAction.Close);

            case ConsoleKey.F4 when !shift && !alt && !control:
                ToggleViewMode(state);
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.F5 when alt:
                ShowUnsupported("Print");
                break;

            case ConsoleKey.F5 when !shift && !alt && !control:
                state.PresentationMode = state.PresentationMode == ViewerPresentationMode.Auto
                    ? ViewerPresentationMode.Raw
                    : ViewerPresentationMode.Auto;
                break;

            case ConsoleKey.F6 when !shift && !alt && !control:
                EditCurrentFile(filePath, reader, state, options, localSession, contentHeight, size.Width);
                break;

            case ConsoleKey.F7 when control:
                ShowUnsupported("Viewer grep filter");
                break;

            case ConsoleKey.F7 when alt:
                RepeatSearch(filePath, reader, state, searchBackward: true, size.Width);
                break;

            case ConsoleKey.F7 when shift && !alt:
                RepeatSearch(filePath, reader, state, searchBackward: false, size.Width);
                break;

            case ConsoleKey.F7 when !shift && !alt && !control:
                ShowFindDialog(filePath, reader, state, size.Width);
                break;

            case ConsoleKey.F8 when alt:
                JumpToPosition(filePath, reader, state, contentHeight, size.Width);
                UpdateLiveModeAfterAwayNavigation(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.F8 when control:
                ShowUnsupported("Ctrl+F8");
                break;

            case ConsoleKey.F8 when shift:
                ChangeEncoding(filePath, reader, state, contentHeight, size);
                break;

            case ConsoleKey.F8 when !shift && !alt && !control:
                CycleCommonEncoding(reader, state);
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
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
                if (localSession is null)
                {
                    _dialogs.Message("Viewer", "Live refresh is not supported for this source.");
                    break;
                }

                state.LiveMode = state.LiveMode switch
                {
                    ViewerLiveMode.Off => ViewerLiveMode.Watch,
                    ViewerLiveMode.Watch => ViewerLiveMode.Tail,
                    _ => ViewerLiveMode.Off,
                };
                if (state.LiveMode != ViewerLiveMode.Off)
                {
                    localSession.DetectChanges();
                    TryRefreshLocalFile(localSession, filePath, contentHeight, size.Width);
                    if (state.LiveMode == ViewerLiveMode.Tail)
                        MoveToEnd(filePath, reader, state, contentHeight, size.Width);
                }
                break;

            case ConsoleKey.G when !shift && !alt && !control:
                JumpToPosition(filePath, reader, state, contentHeight, size.Width);
                UpdateLiveModeAfterAwayNavigation(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.H when !shift && !alt && !control:
                ToggleViewMode(state);
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
                break;

            case ConsoleKey.Spacebar when !shift && !alt && !control:
                RepeatSearch(filePath, reader, state, searchBackward: false, size.Width);
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

        return ModalDialogLoopResult<ViewerLoopAction>.ContinueChanged;
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
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int lines,
        int contentHeight,
        int width,
        LocalViewerSession? localSession)
    {
        if (lines < 0)
        {
            for (int i = 0; i < -lines; i++)
                MoveUp(sourcePath, reader, state, contentHeight, width);

            UpdateLiveModeAfterAwayNavigation(sourcePath, reader, state, contentHeight, width);
            return;
        }

        for (int i = 0; i < lines; i++)
            MoveDown(sourcePath, reader, state, view, contentHeight, width);

        UpdateLiveModeAfterDownwardNavigation(
            localSession,
            sourcePath,
            reader,
            state,
            contentHeight,
            width);
    }

    private LargeFileRenderView Draw(
        IUiCanvas canvas,
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        ConsoleSize size)
    {
        if (state.LastViewportWidth != size.Width || state.LastContentHeight != contentHeight)
        {
            NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
            state.LastViewportWidth = size.Width;
            state.LastContentHeight = contentHeight;
        }

        DrawHeader(canvas, filePath, reader, state, size);

        var view = state.IsHexMode
            ? DrawBinaryContent(canvas, reader, state, contentHeight, size.Width)
            : DrawTextContent(canvas, filePath, reader, state, contentHeight, size.Width);

        DrawFooter(canvas, size, state);
        return view;
    }

    private void DrawHeader(
        IUiCanvas canvas,
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        ConsoleSize size)
    {
        string mode = state.IsHexMode ? " HEX" : $" TEXT {state.LineScanner.EncodingDisplayName}";
        string wrap = !state.IsHexMode && state.WrapLines
            ? state.WordWrap ? " WRAP-W" : " WRAP-C"
            : string.Empty;
        string live = state.LiveMode switch
        {
            ViewerLiveMode.Watch => " WATCH",
            ViewerLiveMode.Tail => " TAIL",
            _ => string.Empty,
        };
        string found = state.SearchMatch is not null ? " FIND" : string.Empty;
        string posSection = reader.Length == 0
            ? $" 0%{mode}{wrap}{live}{found} "
            : $" {FormatPercent(state.TopByteOffset, reader.Length)}%{mode}{wrap}{live}{found} ";

        int nameWidth = Math.Max(0, size.Width - ConsoleTextMetrics.GetCellWidth(posSection));
        string nameSection = FormatHeaderPath(filePath, nameWidth);

        string header = ConsoleTextMetrics.FitToCells(nameSection, nameWidth) + posSection;
        canvas.WriteForced(0, 0, header, CSharpFarPaletteStyles.PathHeaderActive(_palette));
    }

    private static string FormatHeaderPath(string filePath, int width)
    {
        if (width <= 0)
            return string.Empty;

        string text = $" {filePath} ";
        if (ConsoleTextMetrics.GetCellWidth(text) <= width)
            return text;

        return ConsoleTextMetrics.TruncateEndToCells(text, width);
    }

    private LargeFileRenderView DrawTextContent(
        IUiCanvas canvas,
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        return state.WrapLines
            ? DrawWrappedTextContent(canvas, sourcePath, reader, state, contentHeight, width)
            : DrawUnwrappedTextContent(canvas, sourcePath, state, contentHeight, width);
    }

    private LargeFileRenderView DrawUnwrappedTextContent(
        IUiCanvas canvas,
        string sourcePath,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        int bytesPerLine = Math.Max(256, (state.HorizontalOffset + width + 32) * 4);
        var scanned = state.LineScanner
            .ReadLinesAsync(state.TopByteOffset, contentHeight, bytesPerLine)
            .GetAwaiter()
            .GetResult();
        var presented = state.Presentation.Present(
            state.PresentationMode,
            sourcePath,
            state.LineScanner,
            scanned.Lines,
            width);
        var linkHits = new List<ViewerLinkHit>();

        for (int row = 0; row < contentHeight; row++)
        {
            if (row < scanned.Lines.Count)
            {
                var line = ResolvePresentationForSearch(presented[row], state.SearchMatch);
                WriteTextLine(
                    canvas,
                    line,
                    line.Text,
                    row + 1,
                    state.HorizontalOffset,
                    width,
                    state.SearchMatch,
                    segmentStartIndex: 0,
                    linkHits);
            }
            else
            {
                canvas.WriteForced(0, row + 1, new string(' ', width), CSharpFarPaletteStyles.CommandLine(_palette));
            }
        }

        return new LargeFileRenderView(scanned.Lines, scanned.NextOffset, linkHits);
    }

    private LargeFileRenderView DrawWrappedTextContent(
        IUiCanvas canvas,
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        var lines = new List<ScannedLine>();
        var linkHits = new List<ViewerLinkHit>();
        int row = 0;
        long offset = Math.Clamp(state.TopByteOffset, state.LineScanner.ContentStartOffset, reader.Length);
        long nextOffset = offset;
        bool firstPhysicalLine = true;

        while (row < contentHeight && offset < reader.Length)
        {
            var scanned = state.LineScanner
                .ReadLinesAsync(offset, 1, MaxWrappedLineCaptureBytes)
                .GetAwaiter()
                .GetResult();
            if (scanned.Lines.Count == 0)
                break;

            var line = scanned.Lines[0];
            lines.Add(line);
            nextOffset = line.NextOffset;
            var presented = state.Presentation.Present(
                state.PresentationMode,
                sourcePath,
                state.LineScanner,
                [line],
                width)[0];
            presented = ResolvePresentationForSearch(presented, state.SearchMatch);

            WrappedTextSegment[] segments = SplitWrappedLine(
                    presented.Text,
                    Math.Max(1, width),
                    state.WordWrap)
                .ToArray();
            int firstSegment = firstPhysicalLine
                ? Math.Clamp(state.TopWrappedSegmentIndex, 0, Math.Max(0, segments.Length - 1))
                : 0;

            for (int segmentIndex = firstSegment;
                 segmentIndex < segments.Length && row < contentHeight;
                 segmentIndex++)
            {
                WrappedTextSegment segment = segments[segmentIndex];
                WriteTextLine(
                    canvas,
                    presented,
                    segment.Text,
                    row + 1,
                    scrollLeft: 0,
                    width,
                    state.SearchMatch,
                    segment.StartIndex,
                    linkHits);
                row++;
            }

            firstPhysicalLine = false;
            if (line.NextOffset <= offset)
                break;

            offset = line.NextOffset;
        }

        while (row < contentHeight)
        {
            canvas.WriteForced(0, row + 1, new string(' ', width), CSharpFarPaletteStyles.CommandLine(_palette));
            row++;
        }

        return new LargeFileRenderView(lines, nextOffset, linkHits);
    }

    private LargeFileRenderView DrawBinaryContent(
        IUiCanvas canvas,
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
                ? CSharpFarPaletteStyles.InputHighlight(_palette)
                : CSharpFarPaletteStyles.CommandLine(_palette);
            canvas.WriteForced(0, row + 1, FormatLine(text, state.HorizontalOffset, width), style);
        }

        return new LargeFileRenderView(rows, nextOffset, []);
    }

    private static bool IsHexMatchOnRow(ViewerSearchMatch? match, long rowOffset) =>
        match is { IsHex: true } &&
        match.ByteOffset < rowOffset + BinaryBytesPerRow &&
        match.ByteOffset + match.ByteLength > rowOffset;

    private void WriteTextLine(
        IUiCanvas canvas,
        PresentedLine presented,
        string line,
        int y,
        int scrollLeft,
        int width,
        ViewerSearchMatch? match,
        int segmentStartIndex,
        List<ViewerLinkHit> linkHits)
    {
        if (width <= 0)
            return;

        var layout = new ViewerTextLayout(line);
        string visible = layout.Slice(scrollLeft, width);
        canvas.WriteForced(0, y, visible, CSharpFarPaletteStyles.CommandLine(_palette));
        ApplyMarkdownStyles(canvas, presented, line, y, scrollLeft, width, segmentStartIndex, layout);
        CollectLinkHits(presented, line, y, scrollLeft, width, segmentStartIndex, layout, linkHits);
        if (match is not { IsHex: false } ||
            match.LineStartOffset != presented.Source.StartOffset ||
            !presented.TryMapSourceRange(
                match.CharacterIndex,
                match.CharacterLength,
                out int presentedMatchStart,
                out int presentedMatchLength))
        {
            return;
        }

        int segmentEndIndex = segmentStartIndex + line.Length;
        int presentedMatchEnd = presentedMatchStart + presentedMatchLength;
        if (presentedMatchEnd <= segmentStartIndex || presentedMatchStart >= segmentEndIndex)
            return;

        int localMatchStart = Math.Max(presentedMatchStart, segmentStartIndex) - segmentStartIndex;
        int localMatchEnd = Math.Min(presentedMatchEnd, segmentEndIndex) - segmentStartIndex;
        int visibleStart = scrollLeft;
        int visibleEnd = scrollLeft + width;
        int matchStart = layout.CellOffsetFromSourceIndex(localMatchStart);
        int matchEnd = layout.CellOffsetFromSourceIndex(localMatchEnd);
        int highlightStart = Math.Max(visibleStart, matchStart);
        int highlightEnd = Math.Min(visibleEnd, matchEnd);
        if (highlightEnd <= highlightStart)
            return;

        string highlight = layout.Slice(highlightStart, highlightEnd - highlightStart);
        if (ConsoleTextMetrics.GetCellWidth(highlight) > 0)
            canvas.Write(highlightStart - visibleStart, y, highlight, CSharpFarPaletteStyles.InputHighlight(_palette));
    }

    private static void CollectLinkHits(
        PresentedLine presented,
        string line,
        int y,
        int scrollLeft,
        int width,
        int segmentStartIndex,
        ViewerTextLayout layout,
        List<ViewerLinkHit> linkHits)
    {
        if (presented.LinkSpans.Count == 0)
            return;

        int segmentEndIndex = segmentStartIndex + line.Length;
        int visibleStart = scrollLeft;
        int visibleEnd = scrollLeft + width;

        foreach (PresentedLinkSpan span in presented.LinkSpans)
        {
            int spanEnd = span.Start + span.Length;
            if (spanEnd <= segmentStartIndex || span.Start >= segmentEndIndex)
                continue;

            int localStart = Math.Max(span.Start, segmentStartIndex) - segmentStartIndex;
            int localEnd = Math.Min(spanEnd, segmentEndIndex) - segmentStartIndex;
            int linkStartCell = layout.CellOffsetFromSourceIndex(localStart);
            int linkEndCell = layout.CellOffsetFromSourceIndex(localEnd);
            int hitStart = Math.Max(linkStartCell, visibleStart);
            int hitEnd = Math.Min(linkEndCell, visibleEnd);
            if (hitEnd <= hitStart)
                continue;

            linkHits.Add(new ViewerLinkHit(
                new Rect(hitStart - visibleStart, y, hitEnd - hitStart, 1),
                span.Target));
        }
    }

    private void ApplyMarkdownStyles(
        IUiCanvas canvas,
        PresentedLine presented,
        string line,
        int y,
        int scrollLeft,
        int width,
        int segmentStartIndex,
        ViewerTextLayout layout)
    {
        if (presented.StyleSpans.Count == 0)
            return;

        int segmentEndIndex = segmentStartIndex + line.Length;
        int visibleStart = scrollLeft;
        int visibleEnd = scrollLeft + width;

        foreach (PresentedStyleSpan span in presented.StyleSpans)
        {
            int spanEnd = span.Start + span.Length;
            if (spanEnd <= segmentStartIndex || span.Start >= segmentEndIndex)
                continue;

            int localStart = Math.Max(span.Start, segmentStartIndex) - segmentStartIndex;
            int localEnd = Math.Min(spanEnd, segmentEndIndex) - segmentStartIndex;
            int styleStartCell = layout.CellOffsetFromSourceIndex(localStart);
            int styleEndCell = layout.CellOffsetFromSourceIndex(localEnd);
            int drawStart = Math.Max(styleStartCell, visibleStart);
            int drawEnd = Math.Min(styleEndCell, visibleEnd);
            if (drawEnd <= drawStart)
                continue;

            string styled = layout.Slice(drawStart, drawEnd - drawStart);
            if (ConsoleTextMetrics.GetCellWidth(styled) > 0)
                canvas.Write(drawStart - visibleStart, y, styled, ResolveMarkdownStyle(span.Style));
        }
    }

    private CellStyle ResolveMarkdownStyle(ViewerTextStyle style) =>
        style switch
        {
            ViewerTextStyle.Link => CSharpFarPaletteStyles.MarkdownLink(_palette),
            ViewerTextStyle.Bold => CSharpFarPaletteStyles.MarkdownBold(_palette),
            ViewerTextStyle.Italic => CSharpFarPaletteStyles.MarkdownItalic(_palette),
            ViewerTextStyle.InlineCode => CSharpFarPaletteStyles.MarkdownInlineCode(_palette),
            ViewerTextStyle.Heading1 => CSharpFarPaletteStyles.MarkdownHeading1(_palette),
            ViewerTextStyle.Heading2 => CSharpFarPaletteStyles.MarkdownHeading2(_palette),
            ViewerTextStyle.Heading3 => CSharpFarPaletteStyles.MarkdownHeading3(_palette),
            ViewerTextStyle.Heading4 => CSharpFarPaletteStyles.MarkdownHeading4(_palette),
            ViewerTextStyle.Heading5 => CSharpFarPaletteStyles.MarkdownHeading5(_palette),
            ViewerTextStyle.Heading6 => CSharpFarPaletteStyles.MarkdownHeading6(_palette),
            _ => CSharpFarPaletteStyles.CommandLine(_palette),
        };

    private static PresentedLine ResolvePresentationForSearch(
        PresentedLine presented,
        ViewerSearchMatch? match)
    {
        if (match is not { IsHex: false } || match.LineStartOffset != presented.Source.StartOffset)
            return presented;

        return presented.TryMapSourceRange(match.CharacterIndex, match.CharacterLength, out _, out _)
            ? presented
            : PresentedLine.Raw(presented.Source);
    }

    private void DrawFooter(IUiCanvas canvas, ConsoleSize size, LargeFileViewerState state)
    {
        FunctionKeysController.Render(
            canvas,
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
        ViewerFunctionKeyAction(5, state.PresentationMode == ViewerPresentationMode.Auto ? "Raw" : "Auto", ConsoleKey.F5),
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

    private void MoveUp(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(0, state.TopByteOffset - BinaryBytesPerRow);
        }
        else if (state.WrapLines)
        {
            MoveWrappedUpOne(sourcePath, state, width);
        }
        else
        {
            state.TopByteOffset = state.LineScanner
                .FindPreviousLineStartAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MoveDown(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int contentHeight,
        int width)
    {
        if (state.IsHexMode)
        {
            state.TopByteOffset += BinaryBytesPerRow;
        }
        else if (state.WrapLines)
        {
            MoveWrappedDownOne(sourcePath, reader, state, width);
        }
        else
        {
            state.TopByteOffset = view.Lines.Count > 1
                ? view.Lines[1].StartOffset
                : view.NextOffset;
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MovePageUp(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        int pages)
    {
        int pageCount = Math.Max(1, pages);
        if (state.WrapLines && !state.IsHexMode)
        {
            int steps = Math.Max(1, contentHeight) * pageCount;
            for (int i = 0; i < steps; i++)
            {
                if (!MoveWrappedUpOne(sourcePath, state, width))
                    break;
            }
        }
        else
        {
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
                    {
                        state.TopByteOffset = state.LineScanner
                            .FindPreviousLineStartAsync(state.TopByteOffset)
                            .GetAwaiter()
                            .GetResult();
                    }
                }
            }
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MovePageDown(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int contentHeight,
        int width)
    {
        if (state.WrapLines && !state.IsHexMode)
        {
            MovePageDown(sourcePath, reader, state, contentHeight, width, pages: 1);
            return;
        }

        if (state.IsHexMode)
        {
            long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
            state.TopByteOffset += delta;
        }
        else
        {
            state.TopByteOffset = view.NextOffset;
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MovePageDown(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        int pages)
    {
        int pageCount = Math.Max(1, pages);
        if (state.WrapLines && !state.IsHexMode)
        {
            int steps = Math.Max(1, contentHeight) * pageCount;
            for (int i = 0; i < steps; i++)
            {
                if (!MoveWrappedDownOne(sourcePath, reader, state, width))
                    break;
            }
        }
        else
        {
            for (int page = 0; page < pageCount; page++)
            {
                if (state.IsHexMode)
                {
                    long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
                    state.TopByteOffset += delta;
                }
                else
                {
                    var scanned = state.LineScanner
                        .ReadLinesAsync(
                            state.TopByteOffset,
                            Math.Max(1, contentHeight),
                            maxBytesPerLine: 256)
                        .GetAwaiter()
                        .GetResult();
                    state.TopByteOffset = scanned.NextOffset;
                }
            }
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MoveToEnd(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width) =>
        ApplyViewportPosition(
            state,
            GetMaximumViewportPosition(sourcePath, reader, state, contentHeight, width));

    private bool MoveWrappedUpOne(
        string sourcePath,
        LargeFileViewerState state,
        int width)
    {
        if (state.TopWrappedSegmentIndex > 0)
        {
            state.TopWrappedSegmentIndex--;
            return true;
        }

        long previous = state.LineScanner
            .FindPreviousLineStartAsync(state.TopByteOffset)
            .GetAwaiter()
            .GetResult();
        if (previous == state.TopByteOffset)
            return false;

        state.TopByteOffset = previous;
        state.TopWrappedSegmentIndex = Math.Max(
            0,
            GetWrappedSegmentCount(sourcePath, state, previous, width) - 1);
        return true;
    }

    private bool MoveWrappedDownOne(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int width)
    {
        var scanned = state.LineScanner
            .ReadLinesAsync(state.TopByteOffset, 1, MaxWrappedLineCaptureBytes)
            .GetAwaiter()
            .GetResult();
        if (scanned.Lines.Count == 0)
            return false;

        ScannedLine line = scanned.Lines[0];
        int segmentCount = GetWrappedSegmentCount(sourcePath, state, line, width);
        if (state.TopWrappedSegmentIndex + 1 < segmentCount)
        {
            state.TopWrappedSegmentIndex++;
            return true;
        }

        if (line.NextOffset <= state.TopByteOffset || line.NextOffset > reader.Length)
            return false;

        state.TopByteOffset = line.NextOffset;
        state.TopWrappedSegmentIndex = 0;
        return true;
    }

    private ViewerViewportPosition GetMaximumViewportPosition(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        int visibleRows = Math.Max(1, contentHeight);
        long length = reader.Length;

        if (state.IsHexMode)
        {
            long rowCount = (length + BinaryBytesPerRow - 1) / BinaryBytesPerRow;
            long maxTopRow = Math.Max(0, rowCount - visibleRows);
            return new ViewerViewportPosition(maxTopRow * BinaryBytesPerRow, 0);
        }

        if (length <= state.LineScanner.ContentStartOffset)
            return new ViewerViewportPosition(state.LineScanner.ContentStartOffset, 0);

        if (!state.WrapLines)
        {
            long offset = state.LineScanner
                .FindTailTopOffsetAsync(visibleRows)
                .GetAwaiter()
                .GetResult();
            return new ViewerViewportPosition(offset, 0);
        }

        long lineStart = state.LineScanner
            .FindTailTopOffsetAsync(1)
            .GetAwaiter()
            .GetResult();
        int remainingRows = visibleRows;

        while (true)
        {
            int segmentCount = GetWrappedSegmentCount(sourcePath, state, lineStart, width);
            if (segmentCount >= remainingRows)
            {
                return new ViewerViewportPosition(
                    lineStart,
                    Math.Max(0, segmentCount - remainingRows));
            }

            remainingRows -= segmentCount;
            long previous = state.LineScanner
                .FindPreviousLineStartAsync(lineStart)
                .GetAwaiter()
                .GetResult();
            if (previous == lineStart)
                return new ViewerViewportPosition(lineStart, 0);

            lineStart = previous;
        }
    }

    private int GetWrappedSegmentCount(
        string sourcePath,
        LargeFileViewerState state,
        long lineStart,
        int width)
    {
        var scanned = state.LineScanner
            .ReadLinesAsync(lineStart, 1, MaxWrappedLineCaptureBytes)
            .GetAwaiter()
            .GetResult();
        return scanned.Lines.Count == 0
            ? 1
            : GetWrappedSegmentCount(sourcePath, state, scanned.Lines[0], width);
    }

    private int GetWrappedSegmentCount(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width)
    {
        var presented = state.Presentation.Present(
            state.PresentationMode,
            sourcePath,
            state.LineScanner,
            [line],
            Math.Max(1, width))[0];
        presented = ResolvePresentationForSearch(presented, state.SearchMatch);
        return Math.Max(
            1,
            SplitWrappedLine(
                    presented.Text,
                    Math.Max(1, width),
                    state.WordWrap)
                .Count());
    }

    private void NormalizeViewport(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        bool keepTail = true)
    {
        ViewerViewportPosition maximum =
            GetMaximumViewportPosition(sourcePath, reader, state, contentHeight, width);

        if (keepTail && state.LiveMode == ViewerLiveMode.Tail)
        {
            ApplyViewportPosition(state, maximum);
            return;
        }

        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(0, state.TopByteOffset / BinaryBytesPerRow * BinaryBytesPerRow);
            state.TopWrappedSegmentIndex = 0;
        }
        else
        {
            state.TopByteOffset = state.LineScanner
                .FindLineStartAtOrBeforeAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();

            if (state.WrapLines)
            {
                int segmentCount =
                    GetWrappedSegmentCount(sourcePath, state, state.TopByteOffset, width);
                state.TopWrappedSegmentIndex = Math.Clamp(
                    state.TopWrappedSegmentIndex,
                    0,
                    Math.Max(0, segmentCount - 1));
            }
            else
            {
                state.TopWrappedSegmentIndex = 0;
            }
        }

        var current = new ViewerViewportPosition(
            state.TopByteOffset,
            state.TopWrappedSegmentIndex);
        if (CompareViewportPositions(current, maximum) > 0)
            ApplyViewportPosition(state, maximum);
    }

    private bool IsAtEnd(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        ViewerViewportPosition maximum =
            GetMaximumViewportPosition(sourcePath, reader, state, contentHeight, width);
        return state.TopByteOffset == maximum.ByteOffset &&
               state.TopWrappedSegmentIndex == maximum.WrappedSegmentIndex;
    }

    private void UpdateLiveModeAfterAwayNavigation(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        if (state.LiveMode == ViewerLiveMode.Tail &&
            !IsAtEnd(sourcePath, reader, state, contentHeight, width))
        {
            state.LiveMode = ViewerLiveMode.Watch;
        }
    }

    private void UpdateLiveModeAfterDownwardNavigation(
        LocalViewerSession? localSession,
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        if (localSession is null)
            return;

        if (IsAtEnd(sourcePath, reader, state, contentHeight, width))
        {
            state.LiveMode = ViewerLiveMode.Tail;
        }
        else if (state.LiveMode == ViewerLiveMode.Tail)
        {
            state.LiveMode = ViewerLiveMode.Watch;
        }
    }

    private static int CompareViewportPositions(
        ViewerViewportPosition left,
        ViewerViewportPosition right)
    {
        int byteComparison = left.ByteOffset.CompareTo(right.ByteOffset);
        return byteComparison != 0
            ? byteComparison
            : left.WrappedSegmentIndex.CompareTo(right.WrappedSegmentIndex);
    }

    private static void ApplyViewportPosition(
        LargeFileViewerState state,
        ViewerViewportPosition position)
    {
        state.TopByteOffset = position.ByteOffset;
        state.TopWrappedSegmentIndex = position.WrappedSegmentIndex;
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

        int lineLength = new ViewerTextLayout(view.Lines[0].Text).CellWidth;
        state.HorizontalOffset = Math.Max(0, lineLength - Math.Max(1, width));
    }

    private void JumpToPosition(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        string? input = _dialogs.Input(new SingleLineInputDialogOptions
        {
            Title = "Viewer",
            Prompt = state.IsHexMode ? "Percent or byte offset:" : "Line number or percent:",
            Validate = text => ValidateJump(text, state.IsHexMode),
        });
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

        state.TopWrappedSegmentIndex = 0;
        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
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
            state.TopByteOffset = 0;
        }

        state.TopWrappedSegmentIndex = 0;
        state.HorizontalOffset = 0;
        state.SearchMatch = null;
    }

    private void ShowFindDialog(string filePath, IFileByteReader reader, LargeFileViewerState state, int width)
    {
        var selected = new ViewerFindDialog(_dialogs).Show(state.LastSearch, state.IsHexMode);
        if (selected is null)
            return;

        var request = ViewerSearchRequest.FromDialog(selected);
        FindAndApply(filePath, reader, state, request, searchBackward: false, width);
    }

    private void RepeatSearch(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        bool searchBackward,
        int width)
    {
        if (state.LastSearch is null)
        {
            ShowFindDialog(filePath, reader, state, width);
            return;
        }

        FindAndApply(filePath, reader, state, state.LastSearch, searchBackward, width);
    }

    private void FindAndApply(
        string filePath,
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
            _dialogs.Message("Find", ex.Message);
            return;
        }

        if (match is null)
        {
            _dialogs.Message("Find", "Text not found.");
            return;
        }

        state.LastSearch = request;
        state.SearchMatch = match;
        state.TopByteOffset = match.TopByteOffset;
        state.TopWrappedSegmentIndex = 0;
        if (state.LiveMode == ViewerLiveMode.Tail)
            state.LiveMode = ViewerLiveMode.Watch;

        if (!match.IsHex && !state.WrapLines)
        {
            int captureCharacters = Math.Min(1_000_000, match.CharacterIndex + match.CharacterLength + 32);
            var scanned = state.LineScanner
                .ReadLinesAsync(match.LineStartOffset, 1, Math.Max(256, captureCharacters * 4))
                .GetAwaiter()
                .GetResult();
            int matchCell = match.CharacterIndex;
            if (scanned.Lines.Count > 0)
            {
                var presented = state.Presentation.Present(
                    state.PresentationMode,
                    filePath,
                    state.LineScanner,
                    scanned.Lines,
                    width)[0];
                presented = ResolvePresentationForSearch(presented, match);
                if (presented.TryMapSourceRange(
                        match.CharacterIndex,
                        match.CharacterLength,
                        out int presentedMatchStart,
                        out _))
                {
                    matchCell = new ViewerTextLayout(presented.Text)
                        .CellOffsetFromSourceIndex(presentedMatchStart);
                }
            }

            int rightEdge = state.HorizontalOffset + Math.Max(1, width);
            if (matchCell < state.HorizontalOffset || matchCell >= rightEdge)
                state.HorizontalOffset = Math.Max(0, matchCell - 4);
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
            _dialogs.Message("Viewer", "No active search match.");
            return;
        }

        if (options.Clipboard is null)
        {
            ShowUnsupported("Clipboard copy");
            return;
        }

        if (!options.Clipboard.TrySetText(state.SearchMatch.MatchedText))
            _dialogs.Message("Viewer", "Could not copy text to clipboard.");
    }

    private void EditCurrentFile(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileViewerOptions options,
        LocalViewerSession? localSession,
        int contentHeight,
        int width)
    {
        if (options.EditCurrentFile is not null)
        {
            options.EditCurrentFile();
        }
        else if (options.EditFile is not null)
        {
            options.EditFile(filePath);
        }
        else
        {
            ShowUnsupported("Edit from viewer");
            return;
        }

        if (localSession is not null)
        {
            localSession.MarkDirty();
            localSession.DetectChanges();
            TryRefreshLocalFile(localSession, filePath, contentHeight, width);
            return;
        }

        RefreshScanner(reader, state);
    }

    private static void RefreshScanner(IFileByteReader reader, LargeFileViewerState state)
    {
        long anchorByteOffset = state.TopByteOffset;
        int anchorWrappedSegment = state.TopWrappedSegmentIndex;
        var originalViewMode = state.ViewMode;
        var cache = new BlockCache(reader);
        var scanner = LineScanner
            .CreateAsync(cache, reader, state.EncodingSelection)
            .GetAwaiter()
            .GetResult();
        state.ReplaceContent(cache, scanner, state.EncodingSelection);
        state.ViewMode = originalViewMode;
        state.TopByteOffset = state.IsHexMode
            ? Math.Clamp(anchorByteOffset, 0, reader.Length)
            : scanner.FindLineStartAtOrBeforeAsync(anchorByteOffset).GetAwaiter().GetResult();
        state.TopWrappedSegmentIndex = anchorWrappedSegment;
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
        var originalLiveMode = state.LiveMode;

        var result = _dialogs.Select(new SelectionDialogOptions<TextEncodingCatalogItem>
        {
            Title = "Encoding",
            Items = items,
            ItemText = static item => item.Label,
            DoubleBorder = true,
            MaxWidth = 44,
            MaxVisibleRows = items.Count,
            SelectedIndex = FindEncodingSelection(items, state.EncodingSelection),
            SelectionChanged = (item, _) =>
            {
                ApplyEncodingSelection(
                    reader,
                    state,
                    item.Selection,
                    anchorByteOffset,
                    originalViewMode);
                NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
                _surfaces.RequestRedraw();
            },
        });
        TextEncodingCatalogItem? selected = result.IsConfirmed ? result.SelectedItem : null;

        if (selected is null)
        {
            ApplyEncodingSelection(reader, state, originalSelection, anchorByteOffset, originalViewMode);
            state.HorizontalOffset = originalHorizontalOffset;
            state.LiveMode = originalLiveMode;
            NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
            return;
        }

        ApplyEncodingSelection(reader, state, selected.Selection, anchorByteOffset, originalViewMode);
        state.LiveMode = originalLiveMode;
        NormalizeViewport(filePath, reader, state, contentHeight, size.Width);
    }

    private static int FindEncodingSelection(
        IReadOnlyList<TextEncodingCatalogItem> items,
        TextEncodingSelection selection)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Selection == selection)
                return i;
        }

        return 0;
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
        state.TopWrappedSegmentIndex = 0;
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

    private void OpenMarkdownLink(
        string target,
        string? sourceFilePath,
        LargeFileViewerOptions options)
    {
        MarkdownLinkTarget resolved = MarkdownLinkTargetResolver.Resolve(target, sourceFilePath);
        try
        {
            switch (resolved.Kind)
            {
                case MarkdownLinkTargetKind.ExternalUri when
                    resolved.Uri is not null &&
                    options.UriLauncher is not null:
                    options.UriLauncher.Open(resolved.Uri);
                    break;

                case MarkdownLinkTargetKind.RelativeFile when
                    resolved.FilePath is not null &&
                    options.FileLauncher is not null:
                    string workingDirectory = Path.GetDirectoryName(resolved.FilePath) ?? string.Empty;
                    options.FileLauncher.OpenFile(resolved.FilePath, workingDirectory);
                    break;
            }
        }
        catch (Exception ex)
        {
            _dialogs.Message("Viewer", ex.Message);
        }
    }

    private void ShowUnsupported(string command) =>
        _dialogs.Message("Viewer", $"{command} is not supported yet.");

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
        return new ViewerTextLayout(line).Slice(scrollLeft, width);
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

        var layout = new ViewerTextLayout(line);
        int index = 0;
        while (index < line.Length)
        {
            int end = layout.SourceIndexAtCellBoundary(index, width);
            if (end <= index)
                end = index + Rune.GetRuneAt(line, index).Utf16SequenceLength;

            if (wordWrap && end < line.Length)
            {
                int breakAt = layout.LastWhitespaceBoundary(index, end);
                if (breakAt > index)
                    end = breakAt;
            }

            yield return new WrappedTextSegment(index, line[index..end]);
            index = end;
        }
    }

    private static long FormatPercent(long offset, long length)
    {
        if (length <= 0)
            return 0;

        return Math.Clamp(offset * 100 / length, 0, 100);
    }

    private sealed class ViewerTextLayout
    {
        private readonly string _source;
        private readonly int[] _sourceCellOffsets;

        public ViewerTextLayout(string source)
        {
            _source = source;
            _sourceCellOffsets = new int[source.Length + 1];
            var display = new StringBuilder(source.Length);
            int sourceIndex = 0;
            int cellOffset = 0;

            while (sourceIndex < source.Length)
            {
                Rune rune = Rune.GetRuneAt(source, sourceIndex);
                int length = rune.Utf16SequenceLength;
                for (int i = 0; i < length; i++)
                    _sourceCellOffsets[sourceIndex + i] = cellOffset;

                string rendered = rune.Value == '\t'
                    ? "    "
                    : Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.Control
                        ? " "
                        : rune.ToString();
                display.Append(rendered);
                cellOffset += ConsoleTextMetrics.GetCellWidth(rendered);
                sourceIndex += length;
                _sourceCellOffsets[sourceIndex] = cellOffset;
            }

            DisplayText = display.ToString();
            CellWidth = cellOffset;
        }

        public string DisplayText { get; }
        public int CellWidth { get; }

        public int CellOffsetFromSourceIndex(int sourceIndex) =>
            _sourceCellOffsets[Math.Clamp(sourceIndex, 0, _source.Length)];

        public string Slice(int scrollLeft, int width)
        {
            if (width <= 0)
                return string.Empty;

            scrollLeft = Math.Max(0, scrollLeft);
            var result = new StringBuilder(width);
            int displayOffset = 0;
            int written = 0;
            foreach (Rune rune in DisplayText.EnumerateRunes())
            {
                int runeWidth = ConsoleTextMetrics.GetCellWidth(rune);
                int runeEnd = displayOffset + runeWidth;
                if (runeWidth == 0)
                {
                    if (displayOffset >= scrollLeft && written > 0)
                        result.Append(rune.ToString());
                    continue;
                }

                if (runeEnd <= scrollLeft)
                {
                    displayOffset = runeEnd;
                    continue;
                }

                if (displayOffset < scrollLeft)
                {
                    int hiddenPart = runeEnd - scrollLeft;
                    if (hiddenPart > width)
                        break;

                    result.Append(' ', hiddenPart);
                    written += hiddenPart;
                    displayOffset = runeEnd;
                    continue;
                }

                if (written + runeWidth > width)
                    break;

                result.Append(rune.ToString());
                written += runeWidth;
                displayOffset = runeEnd;
            }

            return ConsoleTextMetrics.FitToCells(result.ToString(), width);
        }

        public int SourceIndexAtCellBoundary(int sourceStartIndex, int width)
        {
            int start = Math.Clamp(sourceStartIndex, 0, _source.Length);
            int startCells = CellOffsetFromSourceIndex(start);
            int index = start;
            while (index < _source.Length)
            {
                Rune rune = Rune.GetRuneAt(_source, index);
                int end = index + rune.Utf16SequenceLength;
                if (CellOffsetFromSourceIndex(end) - startCells > width)
                    break;

                index = end;
            }

            return index;
        }

        public int LastWhitespaceBoundary(int sourceStartIndex, int sourceEndIndex)
        {
            int boundary = sourceStartIndex;
            for (int index = sourceStartIndex; index < sourceEndIndex;)
            {
                Rune rune = Rune.GetRuneAt(_source, index);
                index += rune.Utf16SequenceLength;
                if (Rune.IsWhiteSpace(rune))
                    boundary = index;
            }

            return boundary;
        }
    }

    internal static bool TryGetLinkTarget(
        MouseConsoleInputEvent mouse,
        IReadOnlyList<ViewerLinkHit> linkHits,
        out string target)
    {
        target = string.Empty;
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Up ||
            mouse.Modifiers is not (MouseKeyModifiers.None or MouseKeyModifiers.Control))
        {
            return false;
        }

        foreach (ViewerLinkHit hit in linkHits)
        {
            Rect bounds = hit.Bounds;
            if (mouse.X < bounds.X ||
                mouse.X >= bounds.X + bounds.Width ||
                mouse.Y < bounds.Y ||
                mouse.Y >= bounds.Y + bounds.Height)
            {
                continue;
            }

            target = hit.Target;
            return true;
        }

        return false;
    }

    private sealed class LargeFileViewerLayer : InteractiveSurfaceLayer<LargeFileViewerFrame, ViewerInput>
    {
        internal static readonly UiTargetId Keyboard = Targets.Child("keyboard");
        internal static readonly UiTargetId Content = Targets.Child("content");
        internal static UiTargetId FunctionKeys => FunctionKeysController.InteractionTarget;

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
            LargeFileRenderView view = _viewer.Draw(context.Canvas, _filePath, _reader, _state, contentHeight, context.Size);
            var functionKeyActions = ViewerFunctionKeyBarActions(_state);
            Rect functionKeyBarBounds = context.Size.Height > 0
                ? new Rect(0, context.Size.Height - 1, context.Size.Width, 1)
                : new Rect(0, 0, 0, 0);
            IReadOnlyList<FunctionKeyBarActionHit<ConsoleKeyInfo>> functionKeyActionHits =
                FunctionKeysController.BuildActionHits(
                    functionKeyBarBounds.Y,
                    functionKeyBarBounds.Width,
                    functionKeyActions);

            return new LargeFileViewerFrame(
                context.Viewport,
                context.Size,
                context.Size.Height > 0 ? new Rect(0, 0, context.Size.Width, 1) : new Rect(0, 0, 0, 0),
                contentHeight > 0 ? new Rect(0, 1, context.Size.Width, contentHeight) : new Rect(0, 0, 0, 0),
                functionKeyBarBounds,
                contentHeight,
                view,
                functionKeyActions,
                functionKeyActionHits);
        }

        protected override UiInteractionFrame BuildInteractionFrameCore(LargeFileViewerFrame frame)
        {
            var builder = new UiInteractionFrameBuilder()
                .AddFocusEntry(Keyboard, 0, cursor: new UiCursorPlacement(0, 0, false))
                .SetDefaultFocusTarget(Keyboard)
                .SetKeyboardTarget(Keyboard);
            if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
                builder.AddHitRegion(Content, frame.ContentBounds);

            builder.AddFragment(FunctionKeysController.BuildInteractionFragment(frame.FunctionKeyActionHits));
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

            if (context.Target == Content &&
                TryGetLinkTarget(mouse, frame.View.LinkHits, out string linkTarget))
            {
                return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromLink(linkTarget));
            }

            if (FunctionKeysController.TryGetAction(
                    mouse,
                    context,
                    frame.FunctionKeyBarBounds.Y,
                    frame.FunctionKeyBarBounds.Width,
                    frame.FunctionKeyActions,
                    out ConsoleKeyInfo functionKey))
            {
                return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromKey(functionKey));
            }

            return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None);
        }
    }

    private sealed class LocalViewerSession : IDisposable
    {
        private readonly LocalFileMonitor _monitor;

        public LocalViewerSession(
            string filePath,
            RandomAccessFileByteReader reader,
            LargeFileViewerState state,
            LocalFileMonitor monitor,
            LocalFileSnapshot appliedSnapshot,
            bool pendingRefresh)
        {
            FilePath = filePath;
            Reader = reader;
            State = state;
            _monitor = monitor;
            AppliedSnapshot = appliedSnapshot;
            PendingRefresh = pendingRefresh;
        }

        public string FilePath { get; }
        public RandomAccessFileByteReader Reader { get; }
        public LargeFileViewerState State { get; }
        public LocalFileSnapshot AppliedSnapshot { get; private set; }
        public bool PendingRefresh { get; private set; }

        public void MarkDirty()
        {
            PendingRefresh = true;
            _monitor.MarkDirty();
        }

        public void DetectChanges()
        {
            bool watcherDirty = _monitor.TakeDirty();
            if (LocalFileMonitor.TryCaptureSnapshot(FilePath, out LocalFileSnapshot snapshot))
            {
                if (LocalFileMonitor.ShouldRefresh(AppliedSnapshot, snapshot, watcherDirty))
                    PendingRefresh = true;
                return;
            }

            if (watcherDirty)
                PendingRefresh = true;
        }

        public void Commit(LocalFileSnapshot snapshot)
        {
            AppliedSnapshot = snapshot;
            PendingRefresh = false;
        }

        public void Dispose()
        {
            _monitor.Dispose();
            Reader.Dispose();
        }
    }

    private readonly record struct ViewerViewportPosition(
        long ByteOffset,
        int WrappedSegmentIndex);

    private sealed record LargeFileViewerFrame(
        ConsoleViewport Viewport,
        ConsoleSize Size,
        Rect HeaderBounds,
        Rect ContentBounds,
        Rect FunctionKeyBarBounds,
        int ContentHeight,
        LargeFileRenderView View,
        IReadOnlyList<FunctionKeyBarAction<ConsoleKeyInfo>> FunctionKeyActions,
        IReadOnlyList<FunctionKeyBarActionHit<ConsoleKeyInfo>> FunctionKeyActionHits);

    private readonly record struct ViewerInput(ConsoleKeyInfo? Key, int? ScrollLines, string? LinkTarget)
    {
        public static ViewerInput None => new(null, null, null);

        public static ViewerInput FromKey(ConsoleKeyInfo key) => new(key, null, null);

        public static ViewerInput FromScroll(int lines) => new(null, lines, null);

        public static ViewerInput FromLink(string target) => new(null, null, target);
    }

    private sealed record LargeFileRenderView(
        IReadOnlyList<ScannedLine> Lines,
        long NextOffset,
        IReadOnlyList<ViewerLinkHit> LinkHits);

    internal sealed record ViewerLinkHit(Rect Bounds, string Target);

    private sealed record WrappedTextSegment(int StartIndex, string Text);

    private enum ViewerLoopAction
    {
        Close,
        NextFile,
        PreviousFile,
    }
}
