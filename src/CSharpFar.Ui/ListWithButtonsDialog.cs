using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

internal sealed record ListWithButtonsDialogResult<T>(
    string ActionId,
    T? SelectedItem,
    int SelectedIndex);

internal readonly record struct ListWithButtonsDialogLoopResult<TResult>(
    bool IsComplete,
    bool IsChanged,
    TResult Result)
{
    public static ListWithButtonsDialogLoopResult<TResult> ContinueNoChange => new(false, false, default!);
    public static ListWithButtonsDialogLoopResult<TResult> ContinueChanged => new(false, true, default!);
    public static ListWithButtonsDialogLoopResult<TResult> Complete(TResult result) => new(true, false, result);
}

internal sealed class ListWithButtonsDialog<T>
{
    private readonly ListView<T> _list;
    private readonly ScrollableFormDialog _form = new();
    private readonly ModalDialogRenderer _modalRenderer = new();

    public ListWithButtonsDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        IReadOnlyList<DialogButton> buttons,
        string title)
    {
        _list = new ListView<T>(items, itemText ?? throw new ArgumentNullException(nameof(itemText)), behavior: ListViewBehavior.Selection, appearance: ListAppearance.Dialog);
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
        set => _list.EmptyText = value ?? string.Empty;
    }

    public string DefaultListActionId { get; set; } = "default";

    public string CancelActionId { get; set; } = "cancel";

    public string? DeleteActionId { get; set; }

    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set => _list.SetSelectedIndex(value);
    }

    public int ScrollTop
    {
        get => _list.ScrollTop;
        set => _list.SetScrollTop(value);
    }

    public void RefreshItems(IReadOnlyList<T> items) => _list.ReplaceItems(items);

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
                    _ => ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.ContinueNoChange,
                };
            },
            applyCommittedFrame: frame => _list.ApplyCommittedFrame(frame.List));
    }

    public TResult Show<TResult>(
        ModalDialogHost modalDialogs,
        Func<ListWithButtonsDialogResult<T>?, ListWithButtonsDialogLoopResult<TResult>> handleAction)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);
        ArgumentNullException.ThrowIfNull(handleAction);

        return modalDialogs.RunInteractive<ListWithButtonsFrame, ListWithButtonsInput, TResult>(
            Render,
            BuildInteractionFrame,
            RouteInput,
            (_, semantic) =>
            {
                ListWithButtonsDialogResult<T>? action = GetAction(semantic);
                if (action is null && !IsCancel(semantic))
                    return ModalDialogLoopResult<TResult>.ContinueNoChange;

                ListWithButtonsDialogLoopResult<TResult> outcome = handleAction(action);
                return outcome.IsComplete
                    ? ModalDialogLoopResult<TResult>.Complete(outcome.Result)
                    : outcome.IsChanged
                        ? ModalDialogLoopResult<TResult>.ContinueChanged
                        : ModalDialogLoopResult<TResult>.ContinueNoChange;
            },
            applyCommittedFrame: frame => _list.ApplyCommittedFrame(frame.List));
    }

    private bool IsCancel(ListWithButtonsInput semantic) =>
        semantic.FormResult is { Kind: FormInputResultKind.Cancel, Command: null } ||
        semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
        semantic.FormResult.Command == CancelActionId;

    private ListWithButtonsDialogResult<T>? GetAction(ListWithButtonsInput semantic)
    {
        if (IsCancel(semantic))
            return null;
        if (semantic.FormResult.Command is string buttonId)
            return CreateResult(buttonId);
        if (semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed)
            return CreateResult(DefaultListActionId);
        if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete } && DeleteActionId is not null && _list.HasItems)
            return CreateResult(DeleteActionId);
        return null;
    }

    private ListWithButtonsFrame Render(UiRenderContext context, IUiFocusState focusScope)
    {
        ListWithButtonsLayout layout = CalculateLayout(context.Size);
        ListViewFrame list = _list.CalculateFrame(layout.ListBounds);
        ScrollableFormFrame? buttons = null;
        _modalRenderer.Render(context.Canvas, layout.Modal, Title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, _) =>
        {
            buttons = layout.FooterBounds.Height > 0
                ? _form.Render(
                    new FormRenderContext(
                        context,
                        layout.ListBounds,
                        FarDialogStyles.Border,
                        layout.FooterBounds),
                    focusScope,
                    [new UiFocusEntry(_list.ListTarget, 0, _list.HasItems)],
                    _list.HasItems ? _list.ListTarget : null)
                : EmptyFormFrame(context, layout.ListBounds);

            _list.Render(context.Canvas, list);
        });
        return new ListWithButtonsFrame(layout, list, buttons ?? throw new InvalidOperationException("List dialog did not render its button form."));
    }

    private UiInteractionFrame BuildInteractionFrame(ListWithButtonsFrame frame)
    {
        UiTargetId? listTarget = frame.List.Bounds.Width > 0 && frame.List.Bounds.Height > 0 && frame.List.SelectedIndex >= 0
            ? _list.ListTarget
            : null;
        var builder = new UiInteractionFrameBuilder()
            .AddFragment(_list.BuildInteractionFragment(frame.List, 0))
            .AddFragment(_form.BuildInteractionFragment(frame.Buttons))
            .SetDefaultFocusTarget(listTarget ?? frame.Buttons.DefaultTarget);
        return builder.Build();
    }

    private (ListWithButtonsInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        ListWithButtonsFrame frame,
        UiInputRouteContext route)
    {
        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete } && DeleteActionId is not null && _list.HasItems)
        {
            return (
                new ListWithButtonsInput(input, FormInputResult.NotHandled, ScrollableListInputResult.Handled),
                UiInputResult.HandledResult);
        }

        bool isListRoute = _list.IsTargetRoute(route);
        if (!isListRoute)
        {
            FormRouteResult formResult = _form.RouteInput(input, frame.Buttons, route);
            return (new ListWithButtonsInput(input, formResult.FormResult, ScrollableListInputResult.NotHandled), formResult.UiResult);
        }

        var routedResult = _list.RouteInput(
            input,
            frame.List,
            route);
        if (routedResult.Semantic.IsHandled)
            return (new ListWithButtonsInput(input, FormInputResult.NotHandled, routedResult.Semantic), routedResult.UiResult);

        if (UiFocusRouting.TryHandleTraversal(input, out UiInputResult focusResult))
            return (new ListWithButtonsInput(input, FormInputResult.NotHandled, routedResult.Semantic), focusResult);

        FormRouteResult fallbackFormResult = _form.RouteInput(input, frame.Buttons, route);
        return (new ListWithButtonsInput(input, fallbackFormResult.FormResult, routedResult.Semantic), fallbackFormResult.UiResult);
    }

    private ListWithButtonsDialogResult<T> CreateResult(string actionId) =>
        !_list.HasItems || SelectedIndex < 0 || SelectedIndex >= _list.Count
            ? new ListWithButtonsDialogResult<T>(actionId, default, -1)
            : new ListWithButtonsDialogResult<T>(actionId, _list.Items[SelectedIndex], SelectedIndex);

    private static ScrollableFormFrame EmptyFormFrame(UiRenderContext context, Rect bodyBounds) =>
        new(context.Viewport, bodyBounds, null, 0, context.Viewport.Height, 0, [], null);

    private ListWithButtonsLayout CalculateLayout(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(MinDialogWidth, size.Width - 2));
        int targetListRows = Math.Min(MaxVisibleRows, Math.Max(1, _list.Count));
        int height = Math.Min(targetListRows + 7, Math.Max(8, size.Height - 2));
        ModalDialogRenderer.Layout modal = _modalRenderer.CalculateLayout(size, width, height);
        VerticalLayoutSplit sections = UiLayout.SplitBottom(modal.ContentBounds, footerHeight: 1, gap: 1);
        Rect listBounds = UiLayout.Inset(sections.Body, left: 2, top: 0, right: 2, bottom: 0);
        return new ListWithButtonsLayout(modal, listBounds, sections.Footer);
    }

    private readonly record struct ListWithButtonsLayout(ModalDialogRenderer.Layout Modal, Rect ListBounds, Rect FooterBounds);
    private readonly record struct ListWithButtonsFrame(ListWithButtonsLayout Layout, ListViewFrame List, ScrollableFormFrame Buttons);
    private readonly record struct ListWithButtonsInput(ConsoleInputEvent Input, FormInputResult FormResult, ScrollableListInputResult ListResult);
}
