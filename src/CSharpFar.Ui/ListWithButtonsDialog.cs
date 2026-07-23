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
    private readonly ScrollableList<T> _list;
    private readonly DialogButtonBar _buttonBar;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public ListWithButtonsDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        IReadOnlyList<DialogButton> buttons,
        string title)
    {
        _list = new ScrollableList<T>(items, itemText);
        _buttonBar = new DialogButtonBar(buttons ?? throw new ArgumentNullException(nameof(buttons)));
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
        DialogButtonBarState buttonState = _buttonBar.CreateState();
        bool focusButtons = !_list.HasItems;
        return modalDialogs.Run(
            context =>
            {
                var frameLayout = CalculateLayout(context.Size);
                var scrollbarBounds = new Rect(frameLayout.FrameBounds.Right - 1, frameLayout.ListBounds.Y, 1, frameLayout.ListBounds.Height);
                var listState = _list.CalculateFrameState(frameLayout.ListBounds.Height, scrollbarBounds);
                bool frameFocusButtons = focusButtons || !_list.HasItems;
                var frame = new ListWithButtonsFrame(
                    frameLayout,
                    listState,
                    frameFocusButtons,
                    buttonState,
                    _buttonBar.CalculateLayout(frameLayout.ListBounds.X, frameLayout.ButtonY, frameLayout.ListBounds.Width));
                RenderLayer(context.Canvas, frame);
                return frame;
            },
            (input, committedFrame) =>
            {
                var layout = committedFrame.Layout;
                if (input is MouseConsoleInputEvent mouse &&
                    HandleMouse(mouse, committedFrame, ref buttonState, ref focusButtons, out var mouseResult))
                {
                    if (mouseResult is not null)
                        return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(mouseResult.ActionId == CancelActionId ? null : mouseResult);
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;

                if (focusButtons)
                {
                    DialogButtonBarInputResult buttonResult = _buttonBar.HandleInput(input, committedFrame.Buttons, buttonState);
                    buttonState = buttonResult.State;
                    if (buttonResult.IsHandled)
                    {
                        if (buttonResult.ButtonId is not null)
                            return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(buttonResult.ButtonId == CancelActionId ? null : CreateResult(buttonResult.ButtonId));
                        return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;
                    }
                }

                if (HandleKey(key, layout.ListBounds.Height, ref focusButtons, out var keyResult))
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(keyResult?.ActionId == CancelActionId ? null : keyResult);

                return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;
            },
            applyCommittedFrame: frame =>
            {
                _list.ApplyCommittedFrame(frame.ListState);
                focusButtons = frame.FocusButtons;
                buttonState = frame.ButtonState;
            });
    }

    private bool HandleKey(
        ConsoleKeyInfo key,
        int visibleRows,
        ref bool focusButtons,
        out ListWithButtonsDialogResult<T>? result)
    {
        result = null;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F10:
                result = CreateResult(CancelActionId);
                return true;
            case ConsoleKey.Tab:
                focusButtons = !_list.HasItems || !focusButtons;
                return false;
            case ConsoleKey.Delete:
                if (DeleteActionId is not null && _list.HasItems)
                {
                    result = CreateResult(DeleteActionId);
                    return true;
                }
                return false;
        }

        var listResult = _list.HandleKey(key, visibleRows);
        if (!listResult.IsHandled)
            return false;
        if (!_list.HasItems)
        {
            focusButtons = true;
            return false;
        }

        focusButtons = false;
        if (listResult.Kind == ScrollableListInputResultKind.Confirmed)
        {
            result = CreateResult(DefaultListActionId);
            return true;
        }

        return false;
    }

    private bool HandleMouse(
        MouseConsoleInputEvent mouse,
        ListWithButtonsFrame frame,
        ref DialogButtonBarState buttonState,
        ref bool focusButtons,
        out ListWithButtonsDialogResult<T>? result)
    {
        result = null;
        var layout = frame.Layout;

        var listInput = _list.HandleMouse(
            mouse,
            layout.ListBounds,
            frame.ListState,
            confirmOnDoubleClick: true);
        if (listInput.IsHandled)
        {
            focusButtons = false;
            if (listInput.Kind == ScrollableListInputResultKind.Confirmed)
                result = CreateResult(DefaultListActionId);
            return true;
        }

        DialogButtonBarInputResult buttonResult = _buttonBar.HandleMouse(mouse, frame.Buttons, buttonState);
        buttonState = buttonResult.State;
        if (buttonResult.IsHandled)
        {
            focusButtons = true;
            if (buttonResult.ButtonId is not null)
                result = CreateResult(buttonResult.ButtonId);
            return true;
        }

        return false;
    }

    private ListWithButtonsDialogResult<T> CreateResult(string actionId)
    {
        if (!_list.HasItems || SelectedIndex < 0 || SelectedIndex >= _list.Count)
            return new ListWithButtonsDialogResult<T>(actionId, default, -1);
        return new ListWithButtonsDialogResult<T>(actionId, _list.Items[SelectedIndex], SelectedIndex);
    }

    private void RenderLayer(
        IUiCanvas screen,
        ListWithButtonsFrame frame)
    {
        var layout = frame.Layout;
        var fill = FarDialogStyles.Fill;
        var border = FarDialogStyles.Border;
        var selected = FarDialogStyles.FocusedInput;
        var outerOptions = FarDialogStyles.OuterOptions;
        var frameOptions = FarDialogStyles.FrameOptions;
        var scrollState = _list.GetScrollState(layout.ListBounds.Height, frame.ListState.ScrollTop);

        _modalRenderer.Render(screen, layout.Bounds, Title, true, outerOptions, frameOptions, (_, modalLayout) =>
        {
            if (scrollState is not null)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    screen,
                    new Rect(modalLayout.FrameBounds.Right - 1, layout.ListBounds.Y, 1, layout.ListBounds.Height),
                    scrollState,
                    new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                    border);
            }

            _list.Render(screen, layout.ListBounds, frame.ListState, fill, selected, fill);

            _buttonBar.Render(
                screen,
                frame.Buttons,
                frame.ButtonState,
                frame.FocusButtons);
        });

    }

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

    private readonly record struct ListWithButtonsLayout(
        Rect Bounds,
        Rect FrameBounds,
        Rect ListBounds,
        int ButtonY);

    private readonly record struct ListWithButtonsFrame(
        ListWithButtonsLayout Layout,
        ScrollableListFrameState ListState,
        bool FocusButtons,
        DialogButtonBarState ButtonState,
        DialogButtonBarLayout Buttons);
}
