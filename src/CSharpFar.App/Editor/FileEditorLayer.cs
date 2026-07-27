using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed partial class FileEditor
{
    private sealed class FileEditorLayer : InteractiveSurfaceLayer<FileEditorFrame, FileEditorInput>
    {
        internal static readonly UiTargetId Keyboard = new("editor.keyboard");
        internal static readonly UiTargetId Content = new("editor.content");
        internal static readonly UiTargetId Scrollbar = new("editor.vertical-scrollbar");
        internal static readonly UiTargetId FunctionKeys = new("editor.function-key-bar");

        private readonly FileEditor _editor;
        private readonly EditorSession _session;
        private readonly VerticalScrollbarController _verticalScrollbar = new();
        private readonly PendingInvalidation<EditorRenderPart> _invalidation =
            new(EditorRenderPart.Full);
        private ConsoleModifiers _functionKeyModifiers;
        private EditorPosition? _mouseSelectionAnchor;
        private bool _committedCustomCursorVisible = true;
        private bool? _pendingCustomCursorVisible;
        private DateTimeOffset? _nextWakeUtc;

        public FileEditorLayer(FileEditor editor, EditorSession session)
            : base(
                (_, _) => throw new InvalidOperationException("FileEditorLayer uses overridden rendering."),
                _ => UiInteractionFrame.Empty,
                (_, _, _) => new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None))
        {
            _editor = editor;
            _session = session;
            _invalidation.RequestFull();
        }

        protected override FileEditorFrame RenderFrameCore(UiRenderContext context)
        {
            PendingInvalidationSnapshot<EditorRenderPart> attempt =
                _invalidation.SnapshotForRenderAttempt();
            EditorRenderPart parts = attempt.Parts;
            if (!HasCommittedFrame ||
                parts == EditorRenderPart.None ||
                context.Viewport != CommittedFrame.Viewport ||
                !context.Size.Equals(CommittedFrame.Size) ||
                !_editor.CanRenderPartial(
                    CommittedFrame,
                    _session,
                    context,
                    parts))
            {
                parts = EditorRenderPart.Full;
            }

            bool visible = _pendingCustomCursorVisible ?? _committedCustomCursorVisible;
            FileEditorFrame frame = parts.HasFlag(EditorRenderPart.Full)
                ? _editor.RenderFullFrame(_session, _functionKeyModifiers, context, visible)
                : _editor.RenderPartialFrame(
                    CommittedFrame,
                    _session,
                    _functionKeyModifiers,
                    context,
                    visible,
                    parts);
            context.PublishOnStable(attempt, _invalidation.Commit);
            return frame with
            {
                VerticalScrollbarFrame = _verticalScrollbar.CalculateFrame(
                    frame.ScrollBarBounds,
                    frame.VerticalScrollState),
            };
        }

        protected override UiInteractionFrame BuildInteractionFrameCore(FileEditorFrame frame)
        {
            var builder = new UiInteractionFrameBuilder()
                .AddFocusEntry(Keyboard, 0, cursor: frame.CursorPlacement)
                .SetDefaultFocusTarget(Keyboard)
                .SetKeyboardTarget(Keyboard);
            if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
                builder.AddHitRegion(Content, frame.ContentBounds);
            if (frame.VerticalScrollbarFrame is { } scrollbar)
            {
                builder.AddHitRegion(Scrollbar, scrollbar.Bounds);
            }
            if (frame.FunctionKeyBarBounds.Width > 0 && frame.FunctionKeyBarBounds.Height > 0)
                builder.AddHitRegion(FunctionKeys, frame.FunctionKeyBarBounds);

            return builder.Build();
        }

        protected override void OnFrameCommitted(FileEditorFrame frame)
        {
            if (frame.RenderedParts.HasFlag(EditorRenderPart.Full))
            {
                _session.Viewport.TopLine = frame.TopLine;
                _session.Viewport.LeftColumn = frame.LeftColumn;
                _session.SetSyntaxDiagnostics(frame.SyntaxDiagnostics);
                _session.RaiseRedraw(frame.TopLine, frame.ContentHeight);
                _verticalScrollbar.ApplyCommittedFrame(frame.VerticalScrollbarFrame);
                if (frame.ContentBounds.Width <= 0 || frame.ContentBounds.Height <= 0)
                    _mouseSelectionAnchor = null;
            }
            else if (frame.RenderedParts.HasFlag(EditorRenderPart.CursorVisual))
            {
                _session.RaiseRedraw(_session.Cursor.Line, 1);
            }

            _committedCustomCursorVisible = frame.CustomCursorVisible;
            _pendingCustomCursorVisible = null;
            _nextWakeUtc = frame.UsesCustomCursor
                ? DateTimeOffset.UtcNow.AddMilliseconds(CustomCursorBlinkIntervalMs)
                : null;
        }

        protected override InteractiveSurfaceRouteResult<FileEditorInput> RouteSemanticInput(
            ConsoleInputEvent input,
            FileEditorFrame frame,
            UiInputRouteContext context)
        {
            if (input is ModifierKeyConsoleInputEvent modifier &&
                context.RouteKind == UiInputRouteKind.KeyboardTarget &&
                context.Target == Keyboard)
            {
                if (_functionKeyModifiers == modifier.Modifiers)
                {
                    return new InteractiveSurfaceRouteResult<FileEditorInput>(
                        FileEditorInput.ModifierChanged(modifier.Modifiers));
                }

                _functionKeyModifiers = modifier.Modifiers;
                _invalidation.Request(EditorRenderPart.FunctionKeyBar);
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.ModifierChanged(modifier.Modifiers),
                    Invalidate: true);
            }

            if (input is KeyConsoleInputEvent key &&
                context.RouteKind == UiInputRouteKind.KeyboardTarget &&
                context.Target == Keyboard)
            {
                _mouseSelectionAnchor = null;
                bool modifiersChanged = _functionKeyModifiers != key.Key.Modifiers;
                _functionKeyModifiers = key.Key.Modifiers;
                if (modifiersChanged)
                    _invalidation.Request(EditorRenderPart.FunctionKeyBar);
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.Keyboard(key.Key),
                    Invalidate: modifiersChanged);
            }

            if (input is MouseConsoleInputEvent mouse)
                return RouteMouse(mouse, frame, context);

            return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);
        }

        private InteractiveSurfaceRouteResult<FileEditorInput> RouteMouse(
            MouseConsoleInputEvent mouse,
            FileEditorFrame frame,
            UiInputRouteContext context)
        {
            if (context.Target == FunctionKeys &&
                _editor.TryGetFunctionKeyAction(mouse, frame, out ConsoleKeyInfo key))
            {
                return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.Keyboard(key));
            }

            if (context.Target == Content)
                return RouteContentMouse(mouse, frame);

            if (context.Target == Scrollbar)
                return RouteScrollbarMouse(mouse, frame);

            return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);
        }

        private InteractiveSurfaceRouteResult<FileEditorInput> RouteContentMouse(
            MouseConsoleInputEvent mouse,
            FileEditorFrame frame)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                const int wheelLines = 3;
                return mouse.Button switch
                {
                    MouseButton.WheelUp => new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.MouseWheel(-wheelLines)),
                    MouseButton.WheelDown => new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.MouseWheel(wheelLines)),
                    _ => new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None),
                };
            }

            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.DoubleClick } &&
                _editor.TryGetTextMousePosition(mouse, frame, clampToContent: false, out EditorPosition doubleClickPosition))
            {
                _mouseSelectionAnchor = null;
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.TextMouseDoubleClick(doubleClickPosition),
                    MouseCaptureRequest: UiMouseCaptureRequest.Release);
            }

            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down } &&
                _editor.TryGetTextMousePosition(mouse, frame, clampToContent: false, out EditorPosition downPosition))
            {
                _mouseSelectionAnchor = downPosition;
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.TextMouseDown(downPosition),
                    MouseCaptureRequest: UiMouseCaptureRequest.Capture(Content, MouseButton.Left));
            }

            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Move } &&
                _mouseSelectionAnchor is { } moveAnchor &&
                _editor.TryGetTextMousePosition(mouse, frame, clampToContent: true, out EditorPosition movePosition))
            {
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.TextMouseDrag(moveAnchor, movePosition),
                    Invalidate: true);
            }

            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Up } &&
                _mouseSelectionAnchor is { } upAnchor)
            {
                _editor.TryGetTextMousePosition(mouse, frame, clampToContent: true, out EditorPosition upPosition);
                _mouseSelectionAnchor = null;
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.TextMouseUp(upAnchor, upPosition),
                    MouseCaptureRequest: UiMouseCaptureRequest.Release);
            }

            return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);
        }

        private InteractiveSurfaceRouteResult<FileEditorInput> RouteScrollbarMouse(
            MouseConsoleInputEvent mouse,
            FileEditorFrame frame)
        {
            if (frame.VerticalScrollbarFrame is not { } scrollbarFrame)
                return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);

            VerticalScrollbarInputResult result = _verticalScrollbar.HandleMouse(mouse, scrollbarFrame);
            if (!result.IsHandled)
                return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);

            UiMouseCaptureRequest capture = result.DragStarted
                ? UiMouseCaptureRequest.Capture(Scrollbar, MouseButton.Left)
                : result.DragEnded ? UiMouseCaptureRequest.Release : UiMouseCaptureRequest.None;
            return new InteractiveSurfaceRouteResult<FileEditorInput>(
                result.PositionChanged
                    ? FileEditorInput.ScrollbarToLine(result.FirstVisibleIndex)
                    : FileEditorInput.None,
                Invalidate: result.PositionChanged,
                MouseCaptureRequest: capture);
        }

        public DateTimeOffset? GetNextWakeUtc() => _nextWakeUtc;

        public InteractiveSurfaceWakeResult HandleWake()
        {
            if (!HasCommittedFrame || !CommittedFrame.UsesCustomCursor)
                return InteractiveSurfaceWakeResult.NoChange;

            _pendingCustomCursorVisible = !_committedCustomCursorVisible;
            _invalidation.Request(EditorRenderPart.CursorVisual);
            _nextWakeUtc = DateTimeOffset.UtcNow.AddMilliseconds(CustomCursorBlinkIntervalMs);
            return InteractiveSurfaceWakeResult.Changed;
        }

        public bool RestoreVisibleCursorPhase()
        {
            bool changed = HasCommittedFrame &&
                CommittedFrame.UsesCustomCursor &&
                !_committedCustomCursorVisible;
            _pendingCustomCursorVisible = true;
            if (changed)
                _invalidation.Request(EditorRenderPart.CursorVisual);
            _nextWakeUtc = HasCommittedFrame && CommittedFrame.UsesCustomCursor
                ? DateTimeOffset.UtcNow.AddMilliseconds(CustomCursorBlinkIntervalMs)
                : null;
            return changed;
        }

        public void RequestAfterSemanticChange()
        {
            EditorRenderPart parts = HasCommittedFrame
                ? _editor.ClassifyRenderChange(CommittedFrame, _session)
                : EditorRenderPart.Full;
            if (parts == EditorRenderPart.None)
                parts = EditorRenderPart.Full;
            _invalidation.Request(parts);
        }
    }

    private sealed record FileEditorFrame(
        EditorSession Session,
        ConsoleViewport Viewport,
        ConsoleSize Size,
        Rect HeaderBounds,
        Rect ContentBounds,
        Rect StatusBarBounds,
        Rect FunctionKeyBarBounds,
        int ContentHeight,
        int ContentWidth,
        int TopLine,
        int LeftColumn,
        int FirstLineAfterVisibleRange,
        Rect? ScrollBarBounds,
        ScrollState? VerticalScrollState,
        VerticalScrollbarFrame? VerticalScrollbarFrame,
        IReadOnlyList<FunctionKeyBarAction<ConsoleKeyInfo>> FunctionKeyActions,
        UiCursorPlacement CursorPlacement,
        bool UsesCustomCursor,
        bool CustomCursorVisible,
        EditorSyntaxDiagnostics SyntaxDiagnostics,
        EditorSyntaxHighlightResult SyntaxResult,
        EditorRenderFingerprint Fingerprint,
        EditorRenderPart RenderedParts);

    private sealed record EditorRenderFingerprint(
        long DocumentRevision,
        long CleanRevision,
        EditorPosition Cursor,
        EditorSelection? Selection,
        int TopLine,
        int LeftColumn,
        bool SyntaxHighlightingEnabled,
        string SyntaxLanguage,
        string SyntaxTheme,
        ConsoleViewport Viewport,
        ConsoleSize Size,
        bool UsesCustomCursor,
        int FormatCodePage,
        bool EmitByteOrderMark,
        EditorLineEnding LineEnding,
        bool CanUndo,
        bool CanRedo,
        int TabSize,
        bool ExpandTabs);

    [Flags]
    private enum EditorRenderPart
    {
        None = 0,
        Cursor = 1 << 0,
        Status = 1 << 1,
        FunctionKeyBar = 1 << 2,
        CursorVisual = 1 << 3,
        Full = 1 << 30,
    }

    private readonly record struct FileEditorInput(
        FileEditorInputKind Kind,
        ConsoleKeyInfo? Key = null,
        ConsoleModifiers Modifiers = default,
        int ScrollLines = 0,
        int TopLine = 0,
        EditorPosition? Anchor = null,
        EditorPosition? Position = null)
    {
        public static FileEditorInput None => new(FileEditorInputKind.None);
        public static FileEditorInput Keyboard(ConsoleKeyInfo key) => new(FileEditorInputKind.Keyboard, Key: key);
        public static FileEditorInput ModifierChanged(ConsoleModifiers modifiers) => new(FileEditorInputKind.ModifierChanged, Modifiers: modifiers);
        public static FileEditorInput MouseWheel(int lines) => new(FileEditorInputKind.MouseWheel, ScrollLines: lines);
        public static FileEditorInput ScrollbarToLine(int topLine) => new(FileEditorInputKind.ScrollbarToLine, TopLine: topLine);
        public static FileEditorInput TextMouseDown(EditorPosition position) => new(FileEditorInputKind.TextMouseDown, Position: position);
        public static FileEditorInput TextMouseDoubleClick(EditorPosition position) => new(FileEditorInputKind.TextMouseDoubleClick, Position: position);
        public static FileEditorInput TextMouseDrag(EditorPosition anchor, EditorPosition position) => new(FileEditorInputKind.TextMouseDrag, Anchor: anchor, Position: position);
        public static FileEditorInput TextMouseUp(EditorPosition anchor, EditorPosition position) => new(FileEditorInputKind.TextMouseUp, Anchor: anchor, Position: position);
    }

    private enum FileEditorInputKind
    {
        None,
        Keyboard,
        ModifierChanged,
        TextMouseDown,
        TextMouseDoubleClick,
        TextMouseDrag,
        TextMouseUp,
        MouseWheel,
        ScrollbarToLine,
    }
}
