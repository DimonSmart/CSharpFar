using System.Globalization;
using System.Text;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private const int BinaryBytesPerRow = 16;
    // This bounds normal rendering materialization only. Physical line boundaries
    // and pathological wrapped-line navigation use chunked scanning beyond it.
    private const int MaxWrappedLineCaptureBytes = 4 * 1024 * 1024;
    private const int LivePollMs = 250;
    private const int FastHorizontalTextScrollCells = 20;
    private const int FastPageMultiplier = 5;
    private const int MaxWrappedLayoutCacheEntries = 256;

    private static readonly UiTargetScope Targets = new("viewer");

    private static readonly FunctionKeyBarController<ConsoleKeyInfo> FunctionKeysController =
        new(Targets.Child("function-key-bar"));

    private readonly ModalDialogHost _modalDialogs;
    private readonly DialogService _dialogs;
    private readonly CSharpFarPalette _palette;
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly FormFieldFactory _fields;
    private readonly ILocalFileMonitorFactory _localFileMonitorFactory;
    private readonly ILocalFileByteReaderFactory _localFileByteReaderFactory;
    private readonly ViewerLayoutCache<WrappedLayoutCacheKey, WrappedLineLayout> _wrappedLayoutCache =
        new(MaxWrappedLayoutCacheEntries);
    private readonly ViewerAsyncCoordinator _async = new();
    private LargeFileViewerState? _layoutCacheState;
    private LineScanner? _layoutCacheScanner;
    private MaximumViewportCacheEntry? _maximumViewportCache;
    private string? _initialLoadError;

    public LargeFileViewer(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        DialogService dialogs,
        FormFieldFactory fields,
        CSharpFarPalette? palette = null,
        ILocalFileMonitorFactory? localFileMonitorFactory = null,
        ILocalFileByteReaderFactory? localFileByteReaderFactory = null)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _modalDialogs = modalDialogs;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _palette = palette ?? CSharpFarPaletteRegistry.Default;
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _localFileMonitorFactory = localFileMonitorFactory ?? LocalFileMonitorFactory.Instance;
        _localFileByteReaderFactory = localFileByteReaderFactory ?? LocalFileByteReaderFactory.Instance;
    }

    public void Show(string filePath) => Show(filePath, null);

    internal void Show(string filePath, LargeFileViewerOptions? options)
    {
        options ??= new LargeFileViewerOptions();
        _initialLoadError = null;
        LocalViewerSession? session = null;

        try
        {
            _async.BeginSession();
            session = OpenViewerFile(filePath);

            while (true)
            {
                var reader = session.Reader;
                var state = session.State;
                StartInitialContentLoad(filePath, reader, state, session);
                var layer = new LargeFileViewerLayer(this, filePath, reader, state);
                var action = _surfaces.RunTimed(
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
                    getNextWakeUtc: () => GetNextViewerWakeUtc(state),
                    handleWake: frame => HandleLocalWake(filePath, session, frame));
                if (action == ViewerLoopAction.Close)
                {
                    ShowInitialLoadErrorIfAny();
                    return;
                }

                if (!TryMoveToSibling(options, action == ViewerLoopAction.NextFile ? 1 : -1, out string nextPath))
                {
                    ShowUnsupported(action == ViewerLoopAction.NextFile ? "Next file" : "Previous file");
                    continue;
                }

                var presentationMode = state.PresentationMode;
                _async.BeginSession();
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
            _async.Dispose();
            session?.Dispose();
        }
    }

    internal void ShowVirtual(string filePath, IFileByteReader reader, LargeFileViewerOptions? options)
    {
        options ??= new LargeFileViewerOptions();

        try
        {
            _async.BeginSession();
            var cache = new BlockCache(reader);
            LineScanner scanner = LineScanner.CreateLoading(cache, reader);
            var state = new LargeFileViewerState(cache, scanner, contentReady: false);
            StartInitialContentLoad(filePath, reader, state, localSession: null);

            var layer = new LargeFileViewerLayer(this, filePath, reader, state);
            var action = _surfaces.RunTimed(
                layer,
                (routed, input) => HandleViewerInput(
                    filePath,
                    hasPhysicalSourcePath: false,
                    reader,
                    state,
                    options,
                    routed.Frame,
                    input,
                    localSession: null),
                getNextWakeUtc: () => GetNextViewerWakeUtc(state),
                handleWake: _ => HandleVirtualWake());

            if (action == ViewerLoopAction.Close)
                ShowInitialLoadErrorIfAny();
        }
        catch (Exception ex)
        {
            _dialogs.Message("Viewer", ex.Message);
        }
        finally
        {
            _async.Dispose();
        }
    }

    private LocalViewerSession OpenViewerFile(string filePath)
    {
        ILocalFileByteReader reader = _localFileByteReaderFactory.Create(filePath);
        try
        {
            var cache = new BlockCache(reader);
            LineScanner scanner = LineScanner.CreateLoading(cache, reader);
            var state = new LargeFileViewerState(cache, scanner, contentReady: false);
            return new LocalViewerSession(
                filePath,
                reader,
                state,
                new LocalFileMonitoringSession(
                    filePath,
                    new LocalFileSnapshot(false, 0, DateTime.MinValue),
                    _localFileMonitorFactory));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private InteractiveSurfaceWakeResult<ViewerLoopAction> HandleVirtualWake()
    {
        bool changed = _async.DrainUiCompletions();
        if (_initialLoadError is not null)
            return InteractiveSurfaceWakeResult<ViewerLoopAction>.Complete(
                ViewerLoopAction.Close,
                invalidate: changed);

        return changed
            ? InteractiveSurfaceWakeResult<ViewerLoopAction>.Changed
            : InteractiveSurfaceWakeResult<ViewerLoopAction>.NoChange;
    }

    private void ShowInitialLoadErrorIfAny()
    {
        if (_initialLoadError is not { } error)
            return;

        _initialLoadError = null;
        _dialogs.Message("Viewer", error);
    }

    private DateTimeOffset? GetNextViewerWakeUtc(LargeFileViewerState state)
    {
        DateTimeOffset? next = state.LiveMode == ViewerLiveMode.Off
            ? null
            : DateTimeOffset.UtcNow.AddMilliseconds(LivePollMs);
        if (_async.HasPending)
        {
            DateTimeOffset asyncWake = DateTimeOffset.UtcNow.AddMilliseconds(25);
            if (next is null || asyncWake < next.Value)
                next = asyncWake;
        }

        return next;
    }

    private InteractiveSurfaceWakeResult<ViewerLoopAction> HandleLocalWake(
        string filePath,
        LocalViewerSession session,
        LargeFileViewerFrame frame)
    {
        bool changed = _async.DrainUiCompletions();
        if (session.InitialLoadFailed)
            return InteractiveSurfaceWakeResult<ViewerLoopAction>.Complete(
                ViewerLoopAction.Close,
                invalidate: changed);

        session.DetectNotifications();
        if (session.PendingRefresh)
            TryRefreshLocalFile(session, filePath, frame.ContentHeight, frame.Size.Width);
        else
            RequestLocalMonitorPoll(session, filePath, frame.ContentHeight, frame.Size.Width);
        return changed
            ? InteractiveSurfaceWakeResult<ViewerLoopAction>.Changed
            : InteractiveSurfaceWakeResult<ViewerLoopAction>.NoChange;
    }

    private bool TryRefreshLocalFile(
        LocalViewerSession session,
        string filePath,
        int contentHeight,
        int width) =>
        RequestLocalRefresh(session, filePath, contentHeight, width);

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
        if (localSession is null ||
            state.LiveMode != ViewerLiveMode.Off ||
            !ShouldPreserveOffStateOnTransient(input))
        {
            return HandleViewerInputCore(
                filePath,
                hasPhysicalSourcePath,
                reader,
                state,
                options,
                frame,
                input,
                localSession);
        }

        int failureVersion = localSession.Reader.TransientFailureVersion;
        ViewerInteractionCheckpoint checkpoint = ViewerInteractionCheckpoint.Capture(state);
        var result = HandleViewerInputCore(
            filePath,
            hasPhysicalSourcePath,
            reader,
            state,
            options,
            frame,
            input,
            localSession);

        if (localSession.Reader.TransientFailureVersion == failureVersion)
            return result;

        SetLiveMode(localSession, state, checkpoint.LiveMode);
        checkpoint.Restore(state);
        return ModalDialogLoopResult<ViewerLoopAction>.ContinueNoChange;
    }

    private static bool ShouldPreserveOffStateOnTransient(ViewerInput input)
    {
        if (input.ScrollLines is not null)
            return true;

        return input.Key?.Key is
            ConsoleKey.UpArrow or
            ConsoleKey.DownArrow or
            ConsoleKey.PageUp or
            ConsoleKey.PageDown or
            ConsoleKey.Home or
            ConsoleKey.End or
            ConsoleKey.F2 or
            ConsoleKey.F4 or
            ConsoleKey.F5 or
            ConsoleKey.H or
            ConsoleKey.G;
    }

    private ModalDialogLoopResult<ViewerLoopAction> HandleViewerInputCore(
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

        if (!state.IsContentReady &&
            (key.Key == ConsoleKey.F7 ||
             key.Key == ConsoleKey.F8 ||
             key.Key == ConsoleKey.G))
        {
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueNoChange;
        }

        if (key.Key == ConsoleKey.Escape && _async.CancelLatest("search"))
        {
            InvalidatePreparedRender();
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueChanged;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.Up));
                break;

            case ConsoleKey.DownArrow:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.Down));
                break;

            case ConsoleKey.LeftArrow when control && shift:
                _async.InvalidateState();
                state.HorizontalOffset = 0;
                InvalidatePreparedRender();
                break;

            case ConsoleKey.RightArrow when control && shift:
                _async.InvalidateState();
                MoveHorizontalToCurrentLineEnd(state, view, size.Width);
                InvalidatePreparedRender();
                break;

            case ConsoleKey.LeftArrow when control:
                _async.InvalidateState();
                MoveHorizontal(state, -(state.IsHexMode ? 1 : FastHorizontalTextScrollCells));
                InvalidatePreparedRender();
                break;

            case ConsoleKey.RightArrow when control:
                _async.InvalidateState();
                MoveHorizontal(state, state.IsHexMode ? 1 : FastHorizontalTextScrollCells);
                InvalidatePreparedRender();
                break;

            case ConsoleKey.LeftArrow when shift:
            case ConsoleKey.RightArrow when shift:
                ShowUnsupported("Viewer text selection");
                break;

            case ConsoleKey.LeftArrow:
                _async.InvalidateState();
                MoveHorizontal(state, -1);
                InvalidatePreparedRender();
                break;

            case ConsoleKey.RightArrow:
                _async.InvalidateState();
                MoveHorizontal(state, 1);
                InvalidatePreparedRender();
                break;

            case ConsoleKey.PageUp when alt:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.FastPageUp));
                break;

            case ConsoleKey.PageDown when alt:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.FastPageDown));
                break;

            case ConsoleKey.PageUp:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.PageUp));
                break;

            case ConsoleKey.PageDown:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.PageDown));
                break;

            case ConsoleKey.Home:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.Home));
                break;

            case ConsoleKey.End:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.End));
                break;

            case ConsoleKey.F1:
                new HelpViewer(_surfaces, _palette).Show();
                break;

            case ConsoleKey.F2 when shift && !alt && !control:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.ToggleWordWrap));
                break;

            case ConsoleKey.F2 when !shift && !alt && !control:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.ToggleWrap));
                break;

            case ConsoleKey.F3 when !shift && !alt && !control:
            case ConsoleKey.NumPad5 when !shift && !alt && !control:
                return ModalDialogLoopResult<ViewerLoopAction>.Complete(ViewerLoopAction.Close);

            case ConsoleKey.F4 when !shift && !alt && !control:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.ToggleViewMode));
                break;

            case ConsoleKey.F5 when alt:
                ShowUnsupported("Print");
                break;

            case ConsoleKey.F5 when !shift && !alt && !control:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.TogglePresentation));
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
                JumpToPosition(filePath, reader, state, contentHeight, size.Width, localSession);
                break;

            case ConsoleKey.F8 when control:
                ShowUnsupported("Ctrl+F8");
                break;

            case ConsoleKey.F8 when shift:
                ChangeEncoding(filePath, reader, state, contentHeight, size);
                break;

            case ConsoleKey.F8 when !shift && !alt && !control:
                CycleCommonEncoding(filePath, reader, state, contentHeight, size.Width);
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

                ViewerLiveMode nextLiveMode = state.LiveMode switch
                {
                    ViewerLiveMode.Off => ViewerLiveMode.Watch,
                    ViewerLiveMode.Watch => ViewerLiveMode.Tail,
                    _ => ViewerLiveMode.Off,
                };
                SetLiveMode(localSession, state, nextLiveMode);
                if (state.LiveMode != ViewerLiveMode.Off)
                {
                    localSession.DetectNotifications();
                    TryRefreshLocalFile(localSession, filePath, contentHeight, size.Width);
                    if (state.LiveMode == ViewerLiveMode.Tail &&
                        !_async.IsChannelPending("refresh") &&
                        !localSession.PendingRefresh)
                    {
                        QueueNavigation(
                            filePath, reader, state, contentHeight, size.Width, localSession,
                            new ViewerNavigationRequest(ViewerNavigationKind.End));
                    }
                }
                break;

            case ConsoleKey.G when !shift && !alt && !control:
                JumpToPosition(filePath, reader, state, contentHeight, size.Width, localSession);
                break;

            case ConsoleKey.H when !shift && !alt && !control:
                QueueNavigation(
                    filePath, reader, state, contentHeight, size.Width, localSession,
                    new ViewerNavigationRequest(ViewerNavigationKind.ToggleViewMode));
                break;

            case ConsoleKey.Spacebar when !shift && !alt && !control:
                RepeatSearch(filePath, reader, state, searchBackward: false, size.Width);
                break;

            case ConsoleKey.U when control:
                _async.InvalidateState();
                state.SearchMatch = null;
                InvalidatePreparedRender();
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
        _ = view;
        ViewerNavigationKind kind = lines < 0
            ? ViewerNavigationKind.Up
            : ViewerNavigationKind.Down;
        for (int i = 0; i < Math.Abs(lines); i++)
        {
            QueueNavigation(
                sourcePath,
                reader,
                state,
                contentHeight,
                width,
                localSession,
                new ViewerNavigationRequest(kind));
        }
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
            state.LastViewportWidth = size.Width;
            state.LastContentHeight = contentHeight;
            InvalidatePreparedRender();
        }

        DrawHeader(canvas, filePath, reader, state, size);

        if (!state.IsContentReady)
        {
            DrawLoadingContent(canvas, contentHeight, size.Width, " Loading...");
            DrawFooter(canvas, size, state);
            return new LargeFileRenderView([], state.TopByteOffset, []);
        }

        ViewerPreparedContent? prepared = GetOrRequestRenderContent(
            filePath,
            reader,
            state,
            contentHeight,
            size.Width);
        LargeFileRenderView view;
        if (prepared is null)
        {
            DrawLoadingContent(canvas, contentHeight, size.Width, RenderPlaceholder);
            view = new LargeFileRenderView([], state.TopByteOffset, []);
        }
        else
        {
            view = prepared switch
            {
                ViewerPreparedHexContent hex =>
                    DrawBinaryContent(canvas, state, hex, contentHeight, size.Width),
                ViewerPreparedWrappedContent wrapped =>
                    DrawWrappedTextContent(canvas, filePath, reader, state, wrapped, contentHeight, size.Width),
                ViewerPreparedTextContent text =>
                    DrawUnwrappedTextContent(canvas, filePath, state, text, contentHeight, size.Width),
                _ => throw new InvalidOperationException("Unknown prepared viewer content."),
            };
        }

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

    private void DrawLoadingContent(IUiCanvas canvas, int contentHeight, int width, string placeholder)
    {
        string loading = width <= 0
            ? string.Empty
            : ConsoleTextMetrics.FitToCells(placeholder, width);
        for (int row = 0; row < contentHeight; row++)
        {
            string text = row == 0 ? loading : string.Empty;
            canvas.WriteForced(
                0,
                row + 1,
                text.PadRight(Math.Max(0, width)),
                CSharpFarPaletteStyles.CommandLine(_palette));
        }
    }

    private LargeFileRenderView DrawUnwrappedTextContent(
        IUiCanvas canvas,
        string sourcePath,
        LargeFileViewerState state,
        ViewerPreparedTextContent prepared,
        int contentHeight,
        int width)
    {
        ScannedLines scanned = prepared.Scanned;
        IReadOnlyList<PresentedLine> presented = prepared.Presented;
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
        ViewerPreparedWrappedContent prepared,
        int contentHeight,
        int width)
    {
        IReadOnlyList<ScannedLine> candidates = prepared.Lines;
        var lines = new List<ScannedLine>();
        var linkHits = new List<ViewerLinkHit>();
        int row = 0;
        long nextOffset = Math.Clamp(
            state.TopByteOffset,
            state.LineScanner.ContentStartOffset,
            reader.Length);
        bool firstPhysicalLine = true;

        for (int lineIndex = 0; lineIndex < candidates.Count && row < contentHeight; lineIndex++)
        {
            ScannedLine line = candidates[lineIndex];
            lines.Add(line);
            nextOffset = line.NextOffset;

            if (RequiresStreamingWrappedLayout(line))
            {
                if (!prepared.StreamingLayouts.TryGetValue(line.StartOffset, out LongWrappedLineLayout? streamed))
                    throw new InvalidOperationException("Streaming wrapped line layout was not prepared.");

                foreach (WrappedTextSegment segment in streamed.Segments)
                {
                    if (row >= contentHeight)
                        break;
                    WriteRawWrappedTextLine(canvas, segment.Text, row + 1, width);
                    row++;
                }
            }
            else
            {
                if (!prepared.Layouts.TryGetValue(line.StartOffset, out WrappedLineLayout? layout))
                    throw new InvalidOperationException("Wrapped line layout was not prepared.");
                PresentedLine presented = layout.PresentedLine;
                IReadOnlyList<WrappedTextSegment> segments = layout.Segments;
                int firstSegment = firstPhysicalLine
                    ? Math.Clamp(state.TopWrappedSegmentIndex, 0, Math.Max(0, segments.Count - 1))
                    : 0;

                for (int segmentIndex = firstSegment;
                     segmentIndex < segments.Count && row < contentHeight;
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
            }

            firstPhysicalLine = false;
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
        LargeFileViewerState state,
        ViewerPreparedHexContent prepared,
        int contentHeight,
        int width)
    {
        for (int row = 0; row < contentHeight; row++)
        {
            ScannedLine preparedRow = prepared.Rows[row];
            var style = IsHexMatchOnRow(state.SearchMatch, preparedRow.StartOffset)
                ? CSharpFarPaletteStyles.InputHighlight(_palette)
                : CSharpFarPaletteStyles.CommandLine(_palette);
            canvas.WriteForced(
                0,
                row + 1,
                FormatLine(preparedRow.Text, state.HorizontalOffset, width),
                style);
        }

        return new LargeFileRenderView(prepared.Rows, prepared.NextOffset, []);
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
            : PresentedLine.Raw(presented.Source) with
            {
                PreserveLineLayout = presented.PreserveLineLayout,
            };
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
        StartSearch(filePath, reader, state, request, searchBackward, width);
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
            localSession.DetectNotifications();
            TryRefreshLocalFile(localSession, filePath, contentHeight, width);
            return;
        }

        RequestScannerReload(filePath, reader, state, contentHeight, width);
    }



    private void ChangeEncoding(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        ConsoleSize size)
    {
        var items = TextEncodingCatalog.CreateViewerCatalog(state.LineScanner.Detection);
        LineScanner originalScanner = state.LineScanner;
        long originalTopByteOffset = state.TopByteOffset;
        int originalWrappedSegment = state.TopWrappedSegmentIndex;
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
                if (TryApplyEncodingChangeFromCache(
                        filePath,
                        reader,
                        state,
                        item.Selection,
                        anchorByteOffset,
                        originalViewMode,
                        contentHeight,
                        size.Width))
                {
                    _surfaces.RequestRedraw();
                }
            },
        });
        TextEncodingCatalogItem? selected = result.IsConfirmed ? result.SelectedItem : null;

        if (selected is null)
        {
            state.ResetScanner(originalScanner, originalSelection);
            state.ViewMode = originalViewMode;
            state.TopByteOffset = originalTopByteOffset;
            state.TopWrappedSegmentIndex = originalWrappedSegment;
            state.HorizontalOffset = originalHorizontalOffset;
            state.LiveMode = originalLiveMode;
            InvalidatePreparedRender();
            return;
        }

        if (state.EncodingSelection != selected.Selection)
        {
            ApplyEncodingChangeWithDialog(
                filePath,
                reader,
                state,
                selected.Selection,
                anchorByteOffset,
                originalViewMode,
                contentHeight,
                size.Width);
        }

        state.LiveMode = originalLiveMode;
        InvalidatePreparedRender();
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

    private void CycleCommonEncoding(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        int[] commonCodePages = [65001, 866, 1251];
        int currentCodePage = state.EncodingSelection.Kind == TextEncodingSelectionKind.Explicit
            ? state.EncodingSelection.CodePage ?? -1
            : -1;
        int currentIndex = Array.IndexOf(commonCodePages, currentCodePage);
        int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % commonCodePages.Length;
        RequestEncodingChange(
            sourcePath,
            reader,
            state,
            TextEncodingSelection.Explicit(commonCodePages[nextIndex]),
            state.TopByteOffset,
            state.ViewMode,
            contentHeight,
            width);
    }



    private bool TryMoveToSibling(
        LargeFileViewerOptions options,
        int direction,
        out string filePath)
    {
        filePath = string.Empty;
        if (!options.HasSiblingFiles)
            return false;

        int? selectedIndex = _dialogs.Operation<string, int?, int?>(
            new OperationDialogDefinition<string, int?, int?>
            {
                Title = direction > 0 ? "Next file" : "Previous file",
                PreferredWidth = 48,
                PreferredHeight = 8,
                MinWidth = 30,
                MinHeight = 8,
                Operation = async cancellationToken =>
                {
                    await Task.Yield();
                    int index = options.CurrentFileIndex + direction;
                    while (index >= 0 && index < options.FilePaths.Count)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (File.Exists(options.FilePaths[index]))
                            return index;

                        index += direction;
                    }

                    return null;
                },
                Synchronize = () => new OperationDialogState<string>(
                    status: "Checking sibling files... (Esc to cancel)"),
                HandleCancel = () => OperationDialogOutcome<int?>.Complete(null),
                HandleOperationCompleted = index => OperationDialogOutcome<int?>.Complete(index),
                Complete = index => index,
            });

        if (selectedIndex is not { } index)
            return false;

        options.CurrentFileIndex = index;
        filePath = options.FilePaths[index];
        return true;
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
        private readonly LocalFileMonitoringSession _monitoring;

        public LocalViewerSession(
            string filePath,
            ILocalFileByteReader reader,
            LargeFileViewerState state,
            LocalFileMonitoringSession monitoring)
        {
            FilePath = filePath;
            Reader = reader;
            State = state;
            _monitoring = monitoring;
        }

        public string FilePath { get; }
        public ILocalFileByteReader Reader { get; }
        public LargeFileViewerState State { get; }
        public bool PendingRefresh => _monitoring.PendingRefresh;
        public bool InitialLoadFailed { get; private set; }

        public void MarkInitialLoadFailed() => InitialLoadFailed = true;

        public void StartMonitoring() => _monitoring.Activate();

        public void StopMonitoring() => _monitoring.Deactivate();

        public void MarkDirty() => _monitoring.MarkDirty();

        public void DetectNotifications() => _monitoring.DetectNotifications();

        public void ObservePolledSnapshot(LocalFileSnapshot snapshot) =>
            _monitoring.ObservePolledSnapshot(snapshot);

        public void DetectChanges() => _monitoring.DetectChanges();

        public long BeginRefresh() => _monitoring.BeginRefresh();

        public void Commit(LocalFileSnapshot snapshot, long refreshVersion) =>
            _monitoring.Commit(snapshot, refreshVersion);

        public void Dispose()
        {
            _monitoring.Dispose();
            Reader.Dispose();
        }
    }

    private readonly record struct ViewerInteractionCheckpoint(
        long TopByteOffset,
        int TopWrappedSegmentIndex,
        int HorizontalOffset,
        ViewerLiveMode LiveMode,
        bool WrapLines,
        bool WordWrap,
        LargeFileViewMode ViewMode,
        ViewerPresentationMode PresentationMode,
        ViewerSearchMatch? SearchMatch)
    {
        public static ViewerInteractionCheckpoint Capture(LargeFileViewerState state) =>
            new(
                state.TopByteOffset,
                state.TopWrappedSegmentIndex,
                state.HorizontalOffset,
                state.LiveMode,
                state.WrapLines,
                state.WordWrap,
                state.ViewMode,
                state.PresentationMode,
                state.SearchMatch);

        public void Restore(LargeFileViewerState state)
        {
            state.TopByteOffset = TopByteOffset;
            state.TopWrappedSegmentIndex = TopWrappedSegmentIndex;
            state.HorizontalOffset = HorizontalOffset;
            state.LiveMode = LiveMode;
            state.WrapLines = WrapLines;
            state.WordWrap = WordWrap;
            state.ViewMode = ViewMode;
            state.PresentationMode = PresentationMode;
            state.SearchMatch = SearchMatch;
        }
    }

    private sealed class StreamingWrappedLineBuilder
    {
        private const int MaxPendingSourceChars = 64 * 1024;

        private readonly int _width;
        private readonly bool _wordWrap;
        private readonly int _captureStartSegment;
        private readonly int _captureSegmentCount;
        private readonly StringBuilder _pendingText = new();
        private readonly List<PendingRune> _pendingRunes = [];
        private readonly List<WrappedTextSegment> _captured = [];
        private int _pendingCellWidth;
        private int _nextSourceIndex;
        private int _segmentCount;

        public StreamingWrappedLineBuilder(
            int width,
            bool wordWrap,
            int captureStartSegment,
            int captureSegmentCount)
        {
            _width = Math.Max(1, width);
            _wordWrap = wordWrap;
            _captureStartSegment = Math.Max(0, captureStartSegment);
            _captureSegmentCount = Math.Max(0, captureSegmentCount);
        }

        public void Append(string text)
        {
            foreach (Rune rune in text.EnumerateRunes())
            {
                int cellWidth = GetRenderedCellWidth(rune);
                while (_pendingRunes.Count > 0 &&
                       _pendingCellWidth + cellWidth > _width)
                {
                    EmitSegment(moreText: true);
                }

                AddRune(rune, cellWidth);

                if ((_pendingRunes.Count == 1 && cellWidth > _width) ||
                    _pendingText.Length >= MaxPendingSourceChars)
                {
                    EmitPrefix(_pendingRunes.Count);
                }
            }
        }

        public LongWrappedLineLayout Complete()
        {
            if (_pendingRunes.Count > 0)
                EmitPrefix(_pendingRunes.Count);
            else if (_segmentCount == 0)
                CaptureSegment(0, string.Empty);

            return new LongWrappedLineLayout(_segmentCount, _captured);
        }

        private void EmitSegment(bool moreText)
        {
            int breakCount = _pendingRunes.Count;
            if (moreText && _wordWrap)
            {
                for (int i = _pendingRunes.Count - 1; i >= 0; i--)
                {
                    if (_pendingRunes[i].IsWhitespace)
                    {
                        breakCount = i + 1;
                        break;
                    }
                }
            }

            EmitPrefix(Math.Max(1, breakCount));
        }

        private void EmitPrefix(int runeCount)
        {
            runeCount = Math.Clamp(runeCount, 1, _pendingRunes.Count);
            int charCount = 0;
            int cellWidth = 0;
            for (int i = 0; i < runeCount; i++)
            {
                charCount += _pendingRunes[i].CharLength;
                cellWidth += _pendingRunes[i].CellWidth;
            }

            PendingRune first = _pendingRunes[0];
            string? capturedText = ShouldCaptureCurrentSegment()
                ? _pendingText.ToString(0, charCount)
                : null;

            _pendingText.Remove(0, charCount);
            _pendingRunes.RemoveRange(0, runeCount);
            _pendingCellWidth -= cellWidth;

            CaptureSegment(first.SourceStart, capturedText);
        }

        private bool ShouldCaptureCurrentSegment() =>
            _captureSegmentCount > 0 &&
            _segmentCount >= _captureStartSegment &&
            _segmentCount < _captureStartSegment + _captureSegmentCount;

        private void CaptureSegment(int sourceStart, string? text)
        {
            if (text is not null)
                _captured.Add(new WrappedTextSegment(sourceStart, text));
            _segmentCount++;
        }

        private void AddRune(Rune rune, int cellWidth)
        {
            Span<char> chars = stackalloc char[2];
            int charLength = rune.EncodeToUtf16(chars);
            _pendingText.Append(chars[..charLength]);
            _pendingRunes.Add(new PendingRune(
                _nextSourceIndex,
                charLength,
                cellWidth,
                Rune.IsWhiteSpace(rune)));
            _nextSourceIndex += charLength;
            _pendingCellWidth += cellWidth;
        }

        private static int GetRenderedCellWidth(Rune rune)
        {
            if (rune.Value == '\t')
                return 4;
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
                return 1;
            return ConsoleTextMetrics.GetCellWidth(rune);
        }

        private readonly record struct PendingRune(
            int SourceStart,
            int CharLength,
            int CellWidth,
            bool IsWhitespace);
    }

    private sealed record WrappedLineLayout(
        PresentedLine PresentedLine,
        IReadOnlyList<WrappedTextSegment> Segments);

    private sealed record WrappedLayoutCacheKey(
        LineScanner Scanner,
        string SourcePath,
        long StartOffset,
        long NextOffset,
        int Width,
        bool WordWrap,
        ViewerPresentationMode PresentationMode,
        int SearchCharacterIndex,
        int SearchCharacterLength);

    private sealed record MaximumViewportCacheKey(
        LineScanner Scanner,
        string SourcePath,
        long SourceLength,
        int ContentHeight,
        int Width,
        LargeFileViewMode ViewMode,
        bool WrapLines,
        bool WordWrap,
        ViewerPresentationMode PresentationMode,
        long SearchLineStartOffset,
        int SearchCharacterIndex,
        int SearchCharacterLength);

    private sealed record MaximumViewportCacheEntry(
        MaximumViewportCacheKey Key,
        ViewerViewportPosition Position);

    private sealed record LongWrappedLineLayout(
        int SegmentCount,
        IReadOnlyList<WrappedTextSegment> Segments);

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
