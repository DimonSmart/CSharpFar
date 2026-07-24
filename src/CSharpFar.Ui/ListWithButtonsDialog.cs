using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed record ListWithButtonsDialogResult<T>(
    string ActionId,
    T? SelectedItem,
    int SelectedIndex);

public sealed class ListWithButtonsDialog<T>
{
    private static readonly UiTargetId ListTarget = new("list-with-buttons.list");
    private static readonly UiTargetId ScrollbarTarget = new("list-with-buttons.list.scrollbar");

    private readonly ScrollableList<T> _list;
    private readonly ScrollableFormDialog _form = new();
    private readonly ModalDialogRenderer _modalRenderer = new();

    public ListWithButtonsDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        IReadOnlyList<DialogButton> buttons,
        string title)
    {
        _list = new ScrollableList<T>(items, itemText);
        _form.SetRows([], [new ButtonRow(buttons ?? throw new ArgumentNullException(nameof(buttons))) { Id = "actions" }]);
        Title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public string Title { get; }

    public int DialogWidth { get; set; } = 68;

    public int MinDialogWidth { get; set; } = 40;

    public int MaxVisibleRows { get; set; } = 12;

    public string? EmptyText
    {
        get => _list.EmptyText;
        set => _list.EmptyText = value;
    }

    public string DefaultListActionId { get; set; } = "default";

    public string CancelActionId { get; set; } = "cancel";

    public string? DeleteActionId { get; set; }

    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set => _list.SelectedIndex = value;
    }

    public int ScrollTop
    {
        get => _list.ScrollTop;
        set => _list.ScrollTop = value;
    }

    public ListWithButtonsDialogResult<T>? Show(ModalDialogHost modalDialogs)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);

        return modalDialogs.RunInteractive<ListWithButtonsFrame, ListWithButtonsInput, ListWithButtonsDialogResult<T>?>(
            Render,
            BuildInteractionFrame,
            RouteInput,
            (_, semantic) =>
            {
                if (semantic.FormResult is { Kind: FormInputResultKind.Cancel, Command: null } ||
                    semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
                {
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(null);
                }

                if (semantic.FormResult.Command is string buttonId)
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(
                        buttonId == CancelActionId ? null : CreateResult(buttonId));

                return semantic.ListResult.Kind switch
                {
                    ScrollableListInputResultKind.Confirmed => ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(CreateResult(DefaultListActionId)),
                    _ when semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete } && DeleteActionId is not null && _list.HasItems =>
                        ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(CreateResult(DeleteActionId)),
                    _ => ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue,
                };
            },
            applyCommittedFrame: frame => _list.ApplyCommittedFrame(frame.ListState));
    }

    private ListWithButtonsFrame Render(UiRenderContext context, IUiFocusState focusScope)
    {
        ListWithButtonsLayout layout = CalculateLayout(context.Size);
        ScrollableListFrameState listState = _list.CalculateFrameState(
            layout.ListBounds.Height,
            _list.Count > layout.ListBounds.Height ? new Rect(layout.FrameBounds.Right - 1, layout.ListBounds.Y, 1, layout.ListBounds.Height) : null);
        ScrollableFormFrame? buttons = null;
        _modalRenderer.Render(context.Canvas, layout.Bounds, Title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, _) =>
        {
            buttons = _form.Render(
                new FormRenderContext(
                    context,
                    layout.ListBounds,
                    FarDialogStyles.Border,
                    new Rect(layout.ListBounds.X, layout.ButtonY, layout.ListBounds.Width, 1)),
                focusScope,
                [new UiFocusEntry(ListTarget, 0, _list.HasItems)],
                _list.HasItems ? ListTarget : null);

            if (_list.GetScrollState(layout.ListBounds.Height, listState.ScrollTop) is { } scrollState)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    context.Canvas,
                    listState.ScrollbarBounds!.Value,
                    scrollState,
                    new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                    FarDialogStyles.Border);
            }

            _list.Render(context.Canvas, layout.ListBounds, listState, FarDialogStyles.Fill, FarDialogStyles.FocusedInput, FarDialogStyles.Fill);
        });
        return new ListWithButtonsFrame(layout, listState, buttons ?? throw new InvalidOperationException("List dialog did not render its button form."));
    }

    private UiInteractionFrame BuildInteractionFrame(ListWithButtonsFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddHitRegion(ListTarget, frame.Layout.ListBounds)
            .AddFocusEntry(ListTarget, 0, frame.ListState.SelectedIndex >= 0)
            .AddFragment(_form.BuildInteractionFragment(frame.Buttons))
            .SetDefaultFocusTarget(frame.ListState.SelectedIndex >= 0 ? ListTarget : frame.Buttons.DefaultTarget);
        if (frame.ListState.ScrollbarBounds is Rect scrollbarBounds)
            builder.AddHitRegion(ScrollbarTarget, scrollbarBounds);
        return builder.Build();
    }

    private (ListWithButtonsInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        ListWithButtonsFrame frame,
        UiInputRouteContext route)
    {
        _list.ApplyCommittedFrame(frame.ListState);
        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete } && DeleteActionId is not null && _list.HasItems)
        {
            return (
                new ListWithButtonsInput(input, FormInputResult.NotHandled, ScrollableListInputResult.Handled),
                UiInputResult.HandledResult);
        }

        bool isListRoute = route.Target == ListTarget || route.Target == ScrollbarTarget;
        if (!isListRoute || input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Tab or ConsoleKey.Escape })
        {
            FormRouteResult formResult = _form.RouteInput(input, frame.Buttons, route);
            return (new ListWithButtonsInput(input, formResult.FormResult, ScrollableListInputResult.NotHandled), formResult.UiResult);
        }

        ScrollableListInputResult listResult = input switch
        {
            KeyConsoleInputEvent { Key: var key } => _list.HandleKey(key, frame.ListState.ViewportRows),
            MouseConsoleInputEvent mouse => _list.HandleMouse(mouse, frame.Layout.ListBounds, frame.ListState, confirmOnDoubleClick: true),
            _ => ScrollableListInputResult.NotHandled,
        };
        return (new ListWithButtonsInput(input, FormInputResult.NotHandled, listResult), ToUiInputResult(input, listResult));
    }

    private static UiInputResult ToUiInputResult(ConsoleInputEvent input, ScrollableListInputResult result)
    {
        if (!result.IsHandled)
            return UiInputResult.NotHandled;

        if (result.DragStarted)
            return UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, invalidate: true);
        if (result.DragEnded)
            return UiInputResult.ReleaseMouse(invalidate: true);

        return input is MouseConsoleInputEvent { Button: MouseButton.Left, Kind: MouseEventKind.Down }
            ? new UiInputResult(true, true, UiFocusRequest.Set(ListTarget), UiMouseCaptureRequest.None)
            : UiInputResult.HandledAndInvalidate;
    }

    private ListWithButtonsDialogResult<T> CreateResult(string actionId) =>
        !_list.HasItems || SelectedIndex < 0 || SelectedIndex >= _list.Count
            ? new ListWithButtonsDialogResult<T>(actionId, default, -1)
            : new ListWithButtonsDialogResult<T>(actionId, _list.Items[SelectedIndex], SelectedIndex);

    private ListWithButtonsLayout CalculateLayout(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(MinDialogWidth, size.Width - 2));
        int targetListRows = Math.Min(MaxVisibleRows, Math.Max(1, _list.Count));
        int height = Math.Min(targetListRows + 7, Math.Max(8, size.Height - 2));
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);
        var frameBounds = new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
        var contentBounds = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));
        int buttonY = contentBounds.Bottom - 1;
        var listBounds = new Rect(contentBounds.X + 2, contentBounds.Y, Math.Max(1, contentBounds.Width - 4), Math.Max(1, buttonY - contentBounds.Y - 1));
        return new ListWithButtonsLayout(bounds, frameBounds, listBounds, buttonY);
    }

    private readonly record struct ListWithButtonsLayout(Rect Bounds, Rect FrameBounds, Rect ListBounds, int ButtonY);
    private readonly record struct ListWithButtonsFrame(ListWithButtonsLayout Layout, ScrollableListFrameState ListState, ScrollableFormFrame Buttons);
    private readonly record struct ListWithButtonsInput(ConsoleInputEvent Input, FormInputResult FormResult, ScrollableListInputResult ListResult);
}
