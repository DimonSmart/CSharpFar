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
        private ConsoleModifiers _functionKeyModifiers;
        private ScrollBarDragState? _scrollbarDrag;
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
        }

        protected override FileEditorFrame RenderFrameCore(UiRenderContext context)
        {
            bool visible = _pendingCustomCursorVisible ?? _committedCustomCursorVisible;
            return _editor.RenderFrame(_session, _functionKeyModifiers, context, visible);
        }

        protected override UiInteractionFrame BuildInteractionFrameCore(FileEditorFrame frame)
        {
            var regions = new List<UiHitRegion>();
            if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
                regions.Add(new UiHitRegion(Content, frame.ContentBounds));
            if (frame.ScrollBarBounds is { } bar &&
                frame.VerticalScrollState is { } scrollState &&
                ScrollBarInteraction.IsInteractive(bar, scrollState))
            {
                regions.Add(new UiHitRegion(Scrollbar, bar));
            }
            foreach (FunctionKeyHit hit in frame.FunctionKeyHits)
                regions.Add(new UiHitRegion(FunctionKeys, hit.Bounds));

            return new UiInteractionFrame(
                regions,
                new UiFocusFrame([new UiFocusEntry(Keyboard, 0, Cursor: frame.CursorPlacement)], Keyboard),
                Keyboard);
        }

        protected override void OnFrameCommitted(FileEditorFrame frame)
        {
            _session.Viewport.TopLine = frame.TopLine;
            _session.Viewport.LeftColumn = frame.LeftColumn;
            _session.SetSyntaxDiagnostics(frame.SyntaxDiagnostics);
            _session.RaiseRedraw(frame.TopLine, frame.ContentHeight);
            _committedCustomCursorVisible = frame.CustomCursorVisible;
            _pendingCustomCursorVisible = null;
            _nextWakeUtc = frame.UsesCustomCursor
                ? DateTimeOffset.UtcNow.AddMilliseconds(CustomCursorBlinkIntervalMs)
                : null;
            _scrollbarDrag = frame.ScrollBarBounds is { } bar &&
                frame.VerticalScrollState is { } state &&
                _scrollbarDrag is { } drag
                    ? ScrollBarInteraction.RebaseDrag(drag, bar, state.TotalItems, state.ViewportItems)
                    : null;
            if (frame.ContentBounds.Width <= 0 || frame.ContentBounds.Height <= 0)
                _mouseSelectionAnchor = null;
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
                _functionKeyModifiers = modifier.Modifiers;
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.ModifierChanged(modifier.Modifiers),
                    Invalidate: true);
            }

            if (input is KeyConsoleInputEvent key &&
                context.RouteKind == UiInputRouteKind.KeyboardTarget &&
                context.Target == Keyboard)
            {
                _mouseSelectionAnchor = null;
                _functionKeyModifiers = key.Key.Modifiers;
                return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.Keyboard(key.Key));
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
            if (context.Target == FunctionKeys && mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            {
                FunctionKeyHit? hit = frame.FunctionKeyHits.FirstOrDefault(value => value.Bounds.Contains(mouse.X, mouse.Y));
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    hit is { } value ? FileEditorInput.Keyboard(value.Key) : FileEditorInput.None);
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
            if (frame.ScrollBarBounds is not { } bar || frame.VerticalScrollState is not { } state)
                return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);

            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Up } && _scrollbarDrag is not null)
            {
                _scrollbarDrag = null;
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.None,
                    MouseCaptureRequest: UiMouseCaptureRequest.Release);
            }

            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Move } && _scrollbarDrag is { } drag)
            {
                int topLine = ScrollBarInteraction.FirstVisibleIndexForThumbY(bar, state, mouse.Y, drag.PointerOffsetInThumb);
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.ScrollbarToLine(topLine),
                    Invalidate: topLine != frame.TopLine);
            }

            if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down })
                return new InteractiveSurfaceRouteResult<FileEditorInput>(FileEditorInput.None);

            ScrollBarHitTestResult hit = ScrollBarInteraction.HitTest(bar, state, mouse.X, mouse.Y);
            if (hit.Part == ScrollBarHitPart.Thumb)
            {
                _scrollbarDrag = new ScrollBarDragState(bar, state.TotalItems, state.ViewportItems, hit.PointerOffsetInThumb);
                return new InteractiveSurfaceRouteResult<FileEditorInput>(
                    FileEditorInput.None,
                    MouseCaptureRequest: UiMouseCaptureRequest.Capture(Scrollbar, MouseButton.Left));
            }

            int clickedTopLine = ScrollBarInteraction.ApplyClick(state, hit.Part);
            return new InteractiveSurfaceRouteResult<FileEditorInput>(
                FileEditorInput.ScrollbarToLine(clickedTopLine),
                Invalidate: clickedTopLine != frame.TopLine);
        }

        public DateTimeOffset? GetNextWakeUtc() => _nextWakeUtc;

        public InteractiveSurfaceWakeResult HandleWake()
        {
            if (!HasCommittedFrame || !CommittedFrame.UsesCustomCursor)
                return InteractiveSurfaceWakeResult.NoChange;

            _pendingCustomCursorVisible = !_committedCustomCursorVisible;
            _nextWakeUtc = DateTimeOffset.UtcNow.AddMilliseconds(CustomCursorBlinkIntervalMs);
            return InteractiveSurfaceWakeResult.Changed;
        }

        public void RestoreVisibleCursorPhase()
        {
            _pendingCustomCursorVisible = true;
            _nextWakeUtc = HasCommittedFrame && CommittedFrame.UsesCustomCursor
                ? DateTimeOffset.UtcNow.AddMilliseconds(CustomCursorBlinkIntervalMs)
                : null;
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
        IReadOnlyList<FunctionKeyHit> FunctionKeyHits,
        UiCursorPlacement CursorPlacement,
        bool UsesCustomCursor,
        bool CustomCursorVisible,
        EditorSyntaxDiagnostics SyntaxDiagnostics,
        EditorSyntaxHighlightResult SyntaxResult);

    private sealed record FunctionKeyHit(Rect Bounds, ConsoleKeyInfo Key);

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
