using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum TransientPopupPlacementMode
{
    CenteredBottomInsideAnchor,
    AboveAnchor,
}

/// <summary>Semantic placement policy for temporary popup content.</summary>
public sealed class TransientPopupPlacement
{
    public required Func<ConsoleSize, Rect> Anchor { get; init; }
    public TransientPopupPlacementMode Mode { get; init; }
    public int PreferredWidth { get; init; } = 34;
    public int MinimumWidth { get; init; } = 10;
    public int HorizontalInset { get; init; } = 2;
    public int VerticalInset { get; init; } = 1;
    public int MaxVisibleRows { get; init; } = 8;
    public int ReservedRowsAbove { get; init; }

    internal TransientPopupLayout CalculateText(ConsoleSize size)
    {
        Rect anchor = Anchor(size);
        const int height = 3;
        int maxWidth = Math.Max(0, anchor.Width - (HorizontalInset * 2));
        if (maxWidth < MinimumWidth || anchor.Height < height + VerticalInset)
            return TransientPopupLayout.Hidden;

        int width = Math.Min(PreferredWidth, maxWidth);
        int x = anchor.X + Math.Max(HorizontalInset, (anchor.Width - width) / 2);
        int y = Math.Max(anchor.Y + VerticalInset, anchor.Bottom - height - VerticalInset);
        var popup = new Rect(x, y, width, height);
        return new TransientPopupLayout(popup, PopupRenderer.GetContentBounds(popup, drawBorder: true), 1);
    }

    internal TransientPopupLayout CalculateSelection(ConsoleSize size, int itemCount)
    {
        if (itemCount <= 0)
            return TransientPopupLayout.Hidden;

        Rect anchor = Anchor(size);
        if (Mode != TransientPopupPlacementMode.AboveAnchor)
            throw new InvalidOperationException("Selection popups currently require AboveAnchor placement.");

        int availableRows = Math.Max(0, anchor.Y - ReservedRowsAbove);
        int visibleRows = Math.Min(Math.Min(MaxVisibleRows, itemCount), Math.Max(0, availableRows - 2));
        if (visibleRows <= 0 || anchor.Width < MinimumWidth)
            return TransientPopupLayout.Hidden;

        int width = PreferredWidth <= 0 ? anchor.Width : Math.Min(PreferredWidth, anchor.Width);
        width = Math.Max(MinimumWidth, width);
        int height = visibleRows + 2;
        int x = anchor.X;
        int y = anchor.Y - height;
        var popup = new Rect(x, y, width, height);
        return new TransientPopupLayout(popup, PopupRenderer.GetContentBounds(popup, drawBorder: true), visibleRows);
    }
}

internal readonly record struct TransientPopupLayout(Rect PopupBounds, Rect ContentBounds, int VisibleRows)
{
    public static TransientPopupLayout Hidden { get; } = new(default, default, 0);
    public bool IsVisible => VisibleRows > 0;
}

public sealed record TransientTextPromptAppearance(
    PopupRenderOptions Popup,
    CellStyle TextStyle,
    CellStyle SelectionStyle);

public sealed class TransientTextPromptDefinition
{
    public required string Id { get; init; }
    public required Func<bool> IsActive { get; init; }
    public required Func<string> Text { get; init; }
    public required Action<string> TextChanged { get; init; }
    public required TransientPopupPlacement Placement { get; init; }
    public required TransientTextPromptAppearance Appearance { get; init; }
    public string? Title { get; init; }
    public Func<ConsoleKeyInfo, bool>? TryActivate { get; init; }
    public Func<ConsoleKeyInfo, char?>? AdditionalCharacter { get; init; }
    public Func<char, bool>? CharacterFilter { get; init; }
    public Action? Confirmed { get; init; }
    public Action? Cancelled { get; init; }
    public bool DismissOnUnhandledKey { get; init; }
    public bool DismissOnOutsideClick { get; init; } = true;
    public bool BubbleOutsideClick { get; init; } = true;
}

public sealed record TransientTextPromptFrame(
    bool Active,
    bool PopupVisible,
    ConsoleViewport Viewport,
    Rect PopupBounds,
    Rect InputBounds,
    UiCursorPlacement? Cursor,
    string Text)
{
    public string SearchText => Text;
}

/// <summary>Reusable transient single-line prompt. Rendering, editing, focus and pointer plumbing stay inside CSharpFar.Ui.</summary>
public class TransientTextPromptLayer : UiLayer<TransientTextPromptFrame>
{
    private readonly TransientTextPromptDefinition _definition;
    private readonly UiTargetId _inputTarget;
    private readonly SingleLineTextEditState _editor = new();
    private readonly PopupRenderer _popupRenderer = new();
    private string? _error;

    public TransientTextPromptLayer(TransientTextPromptDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentNullException.ThrowIfNull(definition.IsActive);
        ArgumentNullException.ThrowIfNull(definition.Text);
        ArgumentNullException.ThrowIfNull(definition.TextChanged);
        ArgumentNullException.ThrowIfNull(definition.Placement);
        ArgumentNullException.ThrowIfNull(definition.Appearance);
        _inputTarget = new UiTargetId(definition.Id + ".input");
    }

    public override UiLayerInputPolicy InputPolicy =>
        _definition.TryActivate is not null || (HasCommittedFrame && CommittedFrame.Active)
            ? UiLayerInputPolicy.Bubble
            : UiLayerInputPolicy.None;

    protected override TransientTextPromptFrame RenderFrame(UiRenderContext context)
    {
        if (!_definition.IsActive())
            return Hidden(context.Viewport);

        SynchronizeEditor();
        TransientPopupLayout layout = _definition.Placement.CalculateText(context.Size);
        if (!layout.IsVisible)
            return new TransientTextPromptFrame(true, false, context.Viewport, default, default, null, _editor.Text);

        _popupRenderer.RenderPopup(
            context.Canvas,
            layout.PopupBounds,
            _definition.Appearance.Popup,
            (canvas, content) => SingleLineTextInput.Render(
                canvas,
                content.X,
                content.Y,
                content.Width,
                _editor,
                _definition.Appearance.TextStyle,
                _definition.Appearance.SelectionStyle));
        RenderTitle(context.Canvas, layout.PopupBounds);

        int cursorX = SingleLineTextInput.GetCursorX(layout.ContentBounds.X, layout.ContentBounds.Width, _editor);
        UiCursorPlacement? cursor = layout.ContentBounds.Contains(cursorX, layout.ContentBounds.Y)
            ? new UiCursorPlacement(cursorX, layout.ContentBounds.Y)
            : null;
        return new TransientTextPromptFrame(
            true,
            true,
            context.Viewport,
            layout.PopupBounds,
            layout.ContentBounds,
            cursor,
            _editor.Text);
    }

    protected override UiInteractionFrame BuildInteractionFrame(TransientTextPromptFrame frame)
    {
        if (!frame.Active)
            return UiInteractionFrame.Empty;

        var builder = new UiInteractionFrameBuilder()
            .AddFocusEntry(_inputTarget, 0, isEnabled: true, frame.Cursor)
            .SetDefaultFocusTarget(_inputTarget);
        if (frame.PopupVisible)
            builder.AddHitRegion(_inputTarget, frame.InputBounds);
        return builder.Build();
    }

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        TransientTextPromptFrame frame,
        UiInputRouteContext context) =>
        input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };

    private UiInputResult RouteKey(ConsoleKeyInfo key, TransientTextPromptFrame frame)
    {
        if (!frame.Active)
        {
            if (_definition.TryActivate?.Invoke(key) != true)
                return UiInputResult.NotHandled;
            SynchronizeEditor();
            return UiInputResult.HandledAndInvalidate;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            _definition.Cancelled?.Invoke();
            return UiInputResult.HandledAndInvalidate;
        }

        if (KeyboardShortcut.IsPlainEnter(key) && _definition.Confirmed is not null)
        {
            _definition.Confirmed();
            return UiInputResult.HandledAndInvalidate;
        }

        char? additional = _definition.AdditionalCharacter?.Invoke(key);
        if (additional is { } extra && AcceptCharacter(extra))
        {
            _editor.Insert(extra);
            PublishText();
            return UiInputResult.HandledAndInvalidate;
        }

        if (IsRejectedPrintable(key))
            return DismissUnhandled();

        TextInputKeyResult result = SingleLineTextInput.HandleKey(_editor, key, ref _error);
        if (result == TextInputKeyResult.TextChanged)
        {
            PublishText();
            return UiInputResult.HandledAndInvalidate;
        }
        if (result == TextInputKeyResult.Handled)
            return UiInputResult.HandledAndInvalidate;

        return DismissUnhandled();
    }

    private UiInputResult RouteMouse(
        MouseConsoleInputEvent mouse,
        TransientTextPromptFrame frame,
        UiInputRouteContext route)
    {
        if (!frame.Active)
            return UiInputResult.NotHandled;

        if (route.Target == _inputTarget)
            return UiInputResult.HandledResult;

        if (!_definition.DismissOnOutsideClick ||
            mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down })
        {
            return UiInputResult.NotHandled;
        }

        _definition.Cancelled?.Invoke();
        return _definition.BubbleOutsideClick
            ? UiInputResult.InvalidateOnly()
            : UiInputResult.HandledAndInvalidate;
    }

    private UiInputResult DismissUnhandled()
    {
        if (!_definition.DismissOnUnhandledKey)
            return UiInputResult.NotHandled;
        _definition.Cancelled?.Invoke();
        return UiInputResult.InvalidateOnly();
    }

    private bool IsRejectedPrintable(ConsoleKeyInfo key) =>
        key.KeyChar >= ' ' &&
        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0 &&
        !AcceptCharacter(key.KeyChar);

    private bool AcceptCharacter(char value) =>
        _definition.CharacterFilter?.Invoke(value) ?? true;

    private void PublishText() =>
        _definition.TextChanged(_editor.Text);

    private void SynchronizeEditor()
    {
        string text = _definition.Text() ?? string.Empty;
        if (!string.Equals(_editor.Text, text, StringComparison.Ordinal))
            _editor.SetText(text);
    }

    private void RenderTitle(IUiCanvas canvas, Rect popupBounds)
    {
        if (string.IsNullOrEmpty(_definition.Title) || popupBounds.Width <= 4)
            return;

        string title = " " + ConsoleTextMetrics.TruncateToCells(_definition.Title, popupBounds.Width - 4) + " ";
        CellStyle style = _definition.Appearance.Popup.TitleStyle ?? _definition.Appearance.Popup.BorderStyle;
        canvas.Write(popupBounds.X + 2, popupBounds.Y, title, style);
    }

    private static TransientTextPromptFrame Hidden(ConsoleViewport viewport) =>
        new(false, false, viewport, default, default, null, string.Empty);

    private static class KeyboardShortcut
    {
        public static bool IsPlainEnter(ConsoleKeyInfo key) =>
            key.Key == ConsoleKey.Enter && key.Modifiers == 0;
    }
}

public enum TransientSelectionActivationSource
{
    Keyboard,
    Pointer,
}

public enum TransientPopupAction
{
    KeepOpen,
    Refresh,
    Dismiss,
    DismissAndContinue,
}

public readonly record struct TransientSelectionActivation<T>(
    T Item,
    int Index,
    TransientSelectionActivationSource Source);

public readonly record struct TransientSelectionCommand<T>(
    string Command,
    T? Item,
    int SelectedIndex);

public sealed record TransientSelectionPopupAppearance(
    PopupRenderOptions Popup,
    CellStyle NormalStyle,
    CellStyle SelectedStyle,
    CellStyle EmptyStyle);

public sealed class TransientSelectionPopupDefinition<T>
{
    public required string Id { get; init; }
    public required Func<bool> IsVisible { get; init; }
    public required Func<IReadOnlyList<T>> Items { get; init; }
    public required Func<T, string> ItemText { get; init; }
    public required Func<int> SelectedIndex { get; init; }
    public required Action<int> SelectionChanged { get; init; }
    public required TransientPopupPlacement Placement { get; init; }
    public required TransientSelectionPopupAppearance Appearance { get; init; }
    public Func<T, object>? ItemIdentity { get; init; }
    public Func<TransientSelectionActivation<T>, TransientPopupAction>? Activated { get; init; }
    public IReadOnlyDictionary<ConsoleKey, string>? KeyboardCommands { get; init; }
    public Func<TransientSelectionCommand<T>, TransientPopupAction>? Command { get; init; }
    public Action? Dismissed { get; init; }
    public bool ActivateOnMouseDown { get; init; }
    public bool FocusList { get; init; } = true;
    public bool DismissOnOutsideClick { get; init; }
    public bool BubbleOutsideClick { get; init; } = true;
}

public sealed record TransientSelectionPopupItemFrame<T>(int AbsoluteIndex, T Item, string Text, Rect Bounds);

public sealed record TransientSelectionPopupFrame<T>(
    bool Visible,
    ConsoleViewport Viewport,
    Rect PopupBounds,
    Rect ContentBounds,
    IReadOnlyList<TransientSelectionPopupItemFrame<T>> Items,
    Rect? ScrollbarBounds,
    int VisibleRows,
    int MatchCount,
    ScrollableListFrame List);

/// <summary>Reusable transient selectable popup with committed routed-list interaction.</summary>
public class TransientSelectionPopupLayer<T> : UiLayer<TransientSelectionPopupFrame<T>>
{
    private readonly TransientSelectionPopupDefinition<T> _definition;
    private readonly PopupRenderer _popupRenderer = new();
    private readonly ScrollableListState<T> _state = new([]);
    private readonly RoutedScrollableList<T> _list;
    private readonly ScrollableListRenderOptions<T> _presentation;
    private IReadOnlyList<T> _lastItems = [];

    public TransientSelectionPopupLayer(TransientSelectionPopupDefinition<T> definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentNullException.ThrowIfNull(definition.IsVisible);
        ArgumentNullException.ThrowIfNull(definition.Items);
        ArgumentNullException.ThrowIfNull(definition.ItemText);
        ArgumentNullException.ThrowIfNull(definition.SelectedIndex);
        ArgumentNullException.ThrowIfNull(definition.SelectionChanged);
        ArgumentNullException.ThrowIfNull(definition.Placement);
        ArgumentNullException.ThrowIfNull(definition.Appearance);

        var targets = new UiTargetScope(definition.Id);
        _list = new RoutedScrollableList<T>(
            _state,
            targets.Child("list"),
            targets.Child("list.scrollbar"),
            new RoutedScrollableListOptions(
                definition.FocusList ? RoutedListFocusBehavior.FocusOnPointer : RoutedListFocusBehavior.None,
                definition.FocusList ? RoutedListKeyboardRouting.FocusedTargetOnly : RoutedListKeyboardRouting.LayerAndFocusedTarget,
                definition.ActivateOnMouseDown ? ListConfirmationBehavior.EnterOrMouseDown : ListConfirmationBehavior.EnterOrDoubleClick));
        _presentation = new ScrollableListRenderOptions<T>(
            definition.ItemText,
            string.Empty,
            definition.Appearance.NormalStyle,
            definition.Appearance.SelectedStyle,
            definition.Appearance.EmptyStyle);
    }

    public override UiLayerInputPolicy InputPolicy =>
        HasCommittedFrame && CommittedFrame.Visible && _definition.IsVisible()
            ? UiLayerInputPolicy.Bubble
            : UiLayerInputPolicy.None;

    public Rect? CalculatePopupBounds(ConsoleSize size)
    {
        IReadOnlyList<T> items = _definition.Items() ?? [];
        TransientPopupLayout layout = _definition.Placement.CalculateSelection(size, items.Count);
        return layout.IsVisible ? layout.PopupBounds : null;
    }

    protected override TransientSelectionPopupFrame<T> RenderFrame(UiRenderContext context)
    {
        IReadOnlyList<T> items = _definition.Items() ?? [];
        TransientPopupLayout layout = _definition.Placement.CalculateSelection(context.Size, items.Count);
        if (!_definition.IsVisible() || !layout.IsVisible)
            return Hidden(context.Viewport, items.Count);

        SynchronizeState(items, layout.VisibleRows);
        Rect candidateScrollbar = new(layout.PopupBounds.Right - 1, layout.ContentBounds.Y, 1, layout.ContentBounds.Height);
        ScrollableListFrame candidate = _list.CalculateFrame(layout.ContentBounds, candidateScrollbar);
        Rect? scrollbarBounds = candidate.Scrollbar is null ? null : candidateScrollbar;
        ScrollableListFrame listFrame = _list.CalculateFrame(layout.ContentBounds, scrollbarBounds);
        var popupOptions = _definition.Appearance.Popup with
        {
            VerticalScrollbarFrame = listFrame.Scrollbar,
            VerticalScrollState = null,
        };
        _popupRenderer.RenderPopup(context.Canvas, layout.PopupBounds, popupOptions, (canvas, _) =>
            _list.Render(canvas, listFrame, _presentation));

        var visibleItems = new List<TransientSelectionPopupItemFrame<T>>(layout.VisibleRows);
        for (int row = 0; row < layout.VisibleRows && listFrame.ScrollTop + row < _state.Count; row++)
        {
            int index = listFrame.ScrollTop + row;
            T item = _state.Items[index];
            visibleItems.Add(new TransientSelectionPopupItemFrame<T>(
                index,
                item,
                _definition.ItemText(item),
                new Rect(layout.ContentBounds.X, layout.ContentBounds.Y + row, layout.ContentBounds.Width, 1)));
        }

        return new TransientSelectionPopupFrame<T>(
            true,
            context.Viewport,
            layout.PopupBounds,
            layout.ContentBounds,
            visibleItems,
            scrollbarBounds,
            layout.VisibleRows,
            _state.Count,
            listFrame);
    }

    protected override UiInteractionFrame BuildInteractionFrame(TransientSelectionPopupFrame<T> frame)
    {
        if (!frame.Visible)
            return UiInteractionFrame.Empty;
        return new UiInteractionFrameBuilder()
            .AddFragment(_list.BuildInteractionFragment(frame.List, 0))
            .Build();
    }

    protected override void OnFrameCommitted(TransientSelectionPopupFrame<T> frame) =>
        _list.ApplyCommittedFrame(frame.List);

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        TransientSelectionPopupFrame<T> frame,
        UiInputRouteContext context)
    {
        if (!frame.Visible || frame.MatchCount == 0)
            return UiInputResult.NotHandled;
        if (!FrameMatchesSource(frame))
            return UiInputResult.HandledAndInvalidate;

        return input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame, context),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };
    }

    private UiInputResult RouteKey(ConsoleKeyInfo key, TransientSelectionPopupFrame<T> frame, UiInputRouteContext route)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            _definition.Dismissed?.Invoke();
            return UiInputResult.HandledAndInvalidate;
        }

        if (_definition.KeyboardCommands is not null &&
            _definition.KeyboardCommands.TryGetValue(key.Key, out string? command) &&
            _definition.Command is not null)
        {
            T? selected = _state.TryGetSelectedItem(out T item) ? item : default;
            return MapAction(_definition.Command(new TransientSelectionCommand<T>(command, selected, _state.SelectedIndex)));
        }

        RoutedScrollableListInputResult routed = _list.RouteInput(new KeyConsoleInputEvent(key), frame.List, route);
        if (!routed.ListResult.IsHandled)
            return UiInputResult.NotHandled;
        if (routed.ListResult.Kind == ScrollableListInputResultKind.Confirmed)
            return Activate(frame.List.SelectedIndex, TransientSelectionActivationSource.Keyboard);
        if (routed.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
            PublishSelection();
        return routed.UiResult;
    }

    private UiInputResult RouteMouse(MouseConsoleInputEvent mouse, TransientSelectionPopupFrame<T> frame, UiInputRouteContext route)
    {
        RoutedScrollableListInputResult routed = _list.RouteInput(mouse, frame.List, route);
        if (routed.ListResult.IsHandled)
        {
            if (routed.ListResult.Kind == ScrollableListInputResultKind.Confirmed)
                return Activate(_state.SelectedIndex, TransientSelectionActivationSource.Pointer);
            if (routed.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
                PublishSelection();
            return routed.UiResult;
        }

        if (_definition.DismissOnOutsideClick &&
            mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down })
        {
            _definition.Dismissed?.Invoke();
            return _definition.BubbleOutsideClick
                ? UiInputResult.InvalidateOnly()
                : UiInputResult.HandledAndInvalidate;
        }

        return UiInputResult.NotHandled;
    }

    private UiInputResult Activate(int index, TransientSelectionActivationSource source)
    {
        if (_definition.Activated is null || index < 0 || index >= _state.Count)
            return UiInputResult.HandledResult;
        return MapAction(_definition.Activated(new TransientSelectionActivation<T>(_state.Items[index], index, source)));
    }

    private UiInputResult MapAction(TransientPopupAction action) => action switch
    {
        TransientPopupAction.KeepOpen => UiInputResult.HandledResult,
        TransientPopupAction.Refresh => UiInputResult.HandledAndInvalidate,
        TransientPopupAction.Dismiss => UiInputResult.HandledAndInvalidate,
        TransientPopupAction.DismissAndContinue => UiInputResult.InvalidateOnly(),
        _ => UiInputResult.HandledResult,
    };

    private void SynchronizeState(IReadOnlyList<T> items, int visibleRows)
    {
        if (!SameItems(_lastItems, items))
        {
            if (_definition.ItemIdentity is null)
                _state.ReplaceItems(items, visibleRows);
            else
                _state.ReplaceItems(items, _definition.ItemIdentity, visibleRows);
            _lastItems = Array.AsReadOnly(items.ToArray());
        }

        int requestedSelection = _definition.SelectedIndex();
        if (_state.Count > 0 && requestedSelection >= 0 && requestedSelection < _state.Count && requestedSelection != _state.SelectedIndex)
            _state.SetSelectedIndex(requestedSelection, visibleRows);
    }

    private void PublishSelection()
    {
        if (_state.SelectedIndex >= 0)
            _definition.SelectionChanged(_state.SelectedIndex);
    }

    private bool FrameMatchesSource(TransientSelectionPopupFrame<T> frame)
    {
        IReadOnlyList<T> source = _definition.Items() ?? [];
        if (!_definition.IsVisible() || source.Count != frame.MatchCount)
            return false;
        foreach (TransientSelectionPopupItemFrame<T> item in frame.Items)
        {
            if (item.AbsoluteIndex >= source.Count ||
                !EqualityComparer<T>.Default.Equals(source[item.AbsoluteIndex], item.Item))
                return false;
        }
        return true;
    }

    private static bool SameItems(IReadOnlyList<T> left, IReadOnlyList<T> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private TransientSelectionPopupFrame<T> Hidden(ConsoleViewport viewport, int count) =>
        new(false, viewport, default, default, [], null, 0, count, _list.CalculateFrame(default, null));
}
