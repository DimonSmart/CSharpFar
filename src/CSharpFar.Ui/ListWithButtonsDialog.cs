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
        int focusedButton = 0;
        bool focusButtons = !_list.HasItems;
        ScrollBarDragState? scrollbarDrag = null;
        return modalDialogs.Run(
            context =>
            {
                var frameLayout = CalculateLayout(context.Size);
                var listState = _list.CalculateFrameState(frameLayout.ListBounds.Height);
                bool frameFocusButtons = focusButtons || !_list.HasItems;
                int frameFocusedButton = Math.Clamp(focusedButton, 0, _buttonBar.Count - 1);
                var frame = new ListWithButtonsFrame(
                    frameLayout,
                    listState,
                    frameFocusButtons,
                    frameFocusedButton,
                    _buttonBar.CalculateLayout(frameLayout.ListBounds.X, frameLayout.ButtonY, frameLayout.ListBounds.Width));
                RenderLayer(context.Screen, frame);
                return frame;
            },
            (input, committedFrame) =>
            {
            var layout = committedFrame.Layout;
            if (input is MouseConsoleInputEvent mouse &&
                HandleMouse(mouse, committedFrame, ref focusedButton, ref focusButtons, ref scrollbarDrag, out var mouseResult))
            {
                if (mouseResult is not null)
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(mouseResult.ActionId == CancelActionId ? null : mouseResult);
                return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;

            if (focusButtons && _buttonBar.TryHandleInput(input, committedFrame.Buttons, ref focusedButton, out string? buttonId))
            {
                if (buttonId is not null)
                    return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(buttonId == CancelActionId ? null : CreateResult(buttonId));
                return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;
            }

            if (HandleKey(key, layout.ListBounds.Height, ref focusButtons, out var keyResult))
                return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Complete(keyResult?.ActionId == CancelActionId ? null : keyResult);

            return ModalDialogLoopResult<ListWithButtonsDialogResult<T>?>.Continue;
            },
            applyCommittedFrame: frame =>
            {
                _list.SelectedIndex = frame.ListState.SelectedIndex;
                _list.ScrollTop = frame.ListState.ScrollTop;
                focusButtons = frame.FocusButtons;
                focusedButton = frame.FocusedButton;
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
        ref int focusedButton,
        ref bool focusButtons,
        ref ScrollBarDragState? scrollbarDrag,
        out ListWithButtonsDialogResult<T>? result)
    {
        result = null;
        var layout = frame.Layout;

        var scrollbarBounds = new Rect(
            layout.FrameBounds.Right - 1,
            layout.ListBounds.Y,
            1,
            layout.ListBounds.Height);
        var listInput = _list.HandleMouse(
            mouse,
            layout.ListBounds,
            scrollbarBounds,
            layout.ListBounds.Height,
            ref scrollbarDrag,
            confirmOnClick: false,
            confirmOnDoubleClick: true);
        if (listInput.IsHandled)
        {
            focusButtons = false;
            if (listInput.Kind == ScrollableListInputResultKind.Confirmed)
                result = CreateResult(DefaultListActionId);
            return true;
        }

        if (_buttonBar.TryHandleMouse(mouse, frame.Buttons, ref focusedButton, out string? buttonId))
        {
            focusButtons = true;
            if (buttonId is not null)
                result = CreateResult(buttonId);
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
        ScreenRenderer screen,
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
                frame.FocusedButton,
                fill,
                frame.FocusButtons ? selected : fill);
        });

        screen.SetCursorVisible(false);
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
        int FocusedButton,
        DialogButtonBarLayout Buttons);
}
