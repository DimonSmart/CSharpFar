using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record DialogButtonBarLayout(
    Rect AreaBounds,
    IReadOnlyList<Rect> ButtonBounds);

public readonly record struct DialogButtonBarStyle(
    CellStyle Normal,
    CellStyle Focused,
    CellStyle Pressed);

public readonly record struct DialogButtonBarState(
    int FocusedIndex,
    int? ArmedButtonIndex = null,
    bool IsPressed = false)
{
    public int? PressedButtonIndex => IsPressed ? ArmedButtonIndex : null;
}

public readonly record struct DialogButtonBarInputResult(
    bool IsHandled,
    DialogButtonBarState State,
    string? ButtonId = null,
    DialogButtonRole? ButtonRole = null,
    UiMouseCaptureRequestKind MouseCapture = UiMouseCaptureRequestKind.None);

public sealed class DialogButtonBar
{
    private readonly IReadOnlyList<DialogButton> _buttons;

    public DialogButtonBar(IReadOnlyList<DialogButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        _buttons = Array.AsReadOnly(buttons.ToArray());
    }

    public int Count => _buttons.Count;
    public int DesiredWidth => _buttons.Sum(button => FormatButton(button).Length) + Math.Max(0, _buttons.Count - 1);

    public static int MeasureWidth(IReadOnlyList<DialogButton> buttons) =>
        new DialogButtonBar(buttons).DesiredWidth;

    public DialogButtonBarState CreateState(int focusedIndex = 0) =>
        new(NormalizeIndex(focusedIndex));

    public DialogButtonBarLayout CalculateLayout(int x, int y, int width)
    {
        string[] labels = _buttons.Select(FormatButton).ToArray();
        int cursorX = x + Math.Max(0, (width - DesiredWidth) / 2);
        var areaBounds = new Rect(x, y, Math.Max(0, width), 1);
        var bounds = new List<Rect>(labels.Length);
        for (int i = 0; i < labels.Length; i++)
        {
            var visibleBounds = Intersect(new Rect(cursorX, y, labels[i].Length, 1), areaBounds);
            bounds.Add(visibleBounds.Width > 0 ? visibleBounds : new Rect(cursorX, y, 0, 1));
            cursorX += labels[i].Length + 1;
        }

        return new DialogButtonBarLayout(areaBounds, bounds.AsReadOnly());
    }

    public DialogButtonBarLayout Render(
        IUiCanvas screen,
        int x,
        int y,
        int width,
        DialogButtonBarState state,
        bool isFocused,
        DialogButtonBarStyle? style = null)
    {
        var layout = CalculateLayout(x, y, width);
        Render(screen, layout, state, isFocused, style);
        return layout;
    }

    public void Render(
        IUiCanvas screen,
        DialogButtonBarLayout layout,
        DialogButtonBarState state,
        bool isFocused,
        DialogButtonBarStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        DialogButtonBarStyle buttonStyle = style ?? FarDialogStyles.ButtonBar;
        screen.Write(
            layout.AreaBounds.X,
            layout.AreaBounds.Y,
            new string(' ', layout.AreaBounds.Width),
            buttonStyle.Normal);

        int focusedIndex = NormalizeIndex(state.FocusedIndex);
        int? pressedIndex = NormalizeOptionalIndex(state.PressedButtonIndex);
        for (int i = 0; i < _buttons.Count; i++)
        {
            string label = FormatButton(_buttons[i]);
            CellStyle renderedStyle = i == pressedIndex
                ? buttonStyle.Pressed
                : isFocused && i == focusedIndex
                    ? buttonStyle.Focused
                    : buttonStyle.Normal;
            Rect bounds = layout.ButtonBounds[i];
            if (bounds.Width > 0)
                screen.Write(bounds.X, bounds.Y, FitVisibleLabel(label, bounds.Width), renderedStyle);
        }
    }

    public DialogButtonBarInputResult HandleInput(
        ConsoleInputEvent input,
        DialogButtonBarLayout layout,
        DialogButtonBarState state) =>
        input switch
        {
            KeyConsoleInputEvent { Key: var key } => HandleKey(key, state),
            MouseConsoleInputEvent mouse => HandleMouse(mouse, layout, state),
            _ => new DialogButtonBarInputResult(false, NormalizeState(state)),
        };

    public DialogButtonBarInputResult HandleKey(ConsoleKeyInfo key, DialogButtonBarState state)
    {
        state = NormalizeState(state) with { ArmedButtonIndex = null, IsPressed = false };
        int focusedIndex = state.FocusedIndex;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                return Handled(state with { FocusedIndex = focusedIndex <= 0 ? _buttons.Count - 1 : focusedIndex - 1 });
            case ConsoleKey.RightArrow:
                return Handled(state with { FocusedIndex = (focusedIndex + 1) % _buttons.Count });
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                return _buttons[focusedIndex].IsEnabled
                    ? Activated(state, focusedIndex)
                    : Handled(state);
        }

        if (key.KeyChar > ' ')
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (!_buttons[i].IsEnabled ||
                    char.ToUpperInvariant(key.KeyChar) != char.ToUpperInvariant(_buttons[i].HotKey))
                {
                    continue;
                }

                return Activated(state with { FocusedIndex = i }, i);
            }
        }

        return new DialogButtonBarInputResult(false, state);
    }

    public DialogButtonBarInputResult HandleMouse(
        MouseConsoleInputEvent mouse,
        DialogButtonBarLayout layout,
        DialogButtonBarState state)
    {
        state = NormalizeState(state);
        if (mouse.Button != MouseButton.Left)
            return new DialogButtonBarInputResult(false, state);

        if (mouse.Kind == MouseEventKind.Down)
        {
            int? hitIndex = HitTest(layout, mouse.X, mouse.Y);
            if (hitIndex is not int index)
                return new DialogButtonBarInputResult(false, state);

            state = state with { FocusedIndex = index, ArmedButtonIndex = null, IsPressed = false };
            if (!_buttons[index].IsEnabled)
                return Handled(state);

            return new DialogButtonBarInputResult(
                true,
                state with { ArmedButtonIndex = index, IsPressed = true },
                MouseCapture: UiMouseCaptureRequestKind.Capture);
        }

        if (state.ArmedButtonIndex is not int armedIndex)
            return new DialogButtonBarInputResult(false, state);

        bool isOverArmedButton = Contains(layout.ButtonBounds[armedIndex], mouse.X, mouse.Y);
        if (mouse.Kind == MouseEventKind.Move)
            return Handled(state with { IsPressed = isOverArmedButton });

        if (mouse.Kind != MouseEventKind.Up)
            return new DialogButtonBarInputResult(false, state);

        var releasedState = state with { ArmedButtonIndex = null, IsPressed = false };
        return isOverArmedButton && _buttons[armedIndex].IsEnabled
            ? Activated(releasedState, armedIndex, UiMouseCaptureRequestKind.Release)
            : new DialogButtonBarInputResult(
                true,
                releasedState,
                MouseCapture: UiMouseCaptureRequestKind.Release);
    }

    private DialogButtonBarInputResult Activated(
        DialogButtonBarState state,
        int index,
        UiMouseCaptureRequestKind mouseCapture = UiMouseCaptureRequestKind.None) =>
        new(true, state, _buttons[index].Id, _buttons[index].Role, mouseCapture);

    private static DialogButtonBarInputResult Handled(DialogButtonBarState state) =>
        new(true, state);

    private DialogButtonBarState NormalizeState(DialogButtonBarState state)
    {
        int? armedIndex = NormalizeOptionalIndex(state.ArmedButtonIndex) is int index && _buttons[index].IsEnabled
            ? index
            : null;
        return state with
        {
            FocusedIndex = NormalizeIndex(state.FocusedIndex),
            ArmedButtonIndex = armedIndex,
            IsPressed = armedIndex is not null && state.IsPressed,
        };
    }

    private int NormalizeIndex(int index) => Math.Clamp(index, 0, _buttons.Count - 1);

    private int? NormalizeOptionalIndex(int? index) =>
        index is >= 0 && index < _buttons.Count ? index : null;

    private static int? HitTest(DialogButtonBarLayout layout, int x, int y)
    {
        for (int i = 0; i < layout.ButtonBounds.Count; i++)
        {
            if (Contains(layout.ButtonBounds[i], x, y))
                return i;
        }

        return null;
    }

    private static string FormatButton(DialogButton button) =>
        button.IsDefault ? $"{{ {button.Text} }}" : $"[ {button.Text} ]";

    private static bool Contains(Rect rect, int x, int y) =>
        x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;

    private static Rect Intersect(Rect value, Rect bounds)
    {
        int x = Math.Max(value.X, bounds.X);
        int y = Math.Max(value.Y, bounds.Y);
        int right = Math.Min(value.Right, bounds.Right);
        int bottom = Math.Min(value.Bottom, bounds.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static string FitVisibleLabel(string label, int width) =>
        label.Length <= width ? label : label[..width];
}
