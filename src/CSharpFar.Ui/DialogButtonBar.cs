using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record DialogButtonBarLayout(
    Rect AreaBounds,
    IReadOnlyList<Rect> ButtonBounds);

public sealed class DialogButtonBar
{
    private readonly IReadOnlyList<DialogButton> _buttons;

    public DialogButtonBar(IReadOnlyList<DialogButton> buttons)
    {
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        _buttons = buttons;
    }

    public int Count => _buttons.Count;

    public DialogButtonBarLayout CalculateLayout(int x, int y, int width)
    {
        string[] labels = _buttons.Select(FormatButton).ToArray();
        int totalWidth = labels.Sum(label => label.Length) + Math.Max(0, labels.Length - 1);
        int cursorX = x + Math.Max(0, (width - totalWidth) / 2);
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
        int focusedIndex,
        CellStyle normalStyle,
        CellStyle focusedStyle)
    {
        var layout = CalculateLayout(x, y, width);
        Render(screen, layout, focusedIndex, normalStyle, focusedStyle);
        return layout;
    }

    public void Render(
        IUiCanvas screen,
        DialogButtonBarLayout layout,
        int focusedIndex,
        CellStyle normalStyle,
        CellStyle focusedStyle)
    {
        screen.Write(
            layout.AreaBounds.X,
            layout.AreaBounds.Y,
            new string(' ', layout.AreaBounds.Width),
            normalStyle);

        for (int i = 0; i < _buttons.Count; i++)
        {
            string label = FormatButton(_buttons[i]);
            var style = i == focusedIndex ? focusedStyle : normalStyle;
            Rect bounds = layout.ButtonBounds[i];
            if (bounds.Width > 0)
                screen.Write(bounds.X, bounds.Y, FitVisibleLabel(label, bounds.Width), style);
        }
    }

    public bool TryHandleInput(ConsoleInputEvent input, DialogButtonBarLayout layout, ref int focusedIndex, out string? buttonId)
    {
        buttonId = null;

        switch (input)
        {
            case KeyConsoleInputEvent { Key: var key }:
                return HandleKey(key, ref focusedIndex, out buttonId);
            case MouseConsoleInputEvent mouse:
                return TryHandleMouse(mouse, layout, ref focusedIndex, out buttonId);
            default:
                return false;
        }
    }

    public bool TryHandleKey(ConsoleKeyInfo key, ref int focusedIndex, out string? buttonId) =>
        HandleKey(key, ref focusedIndex, out buttonId);

    private bool HandleKey(ConsoleKeyInfo key, ref int focusedIndex, out string? buttonId)
    {
        buttonId = null;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                focusedIndex = focusedIndex <= 0 ? _buttons.Count - 1 : focusedIndex - 1;
                return true;
            case ConsoleKey.RightArrow:
                focusedIndex = (focusedIndex + 1) % _buttons.Count;
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                if (!_buttons[focusedIndex].IsEnabled)
                    return true;

                buttonId = _buttons[focusedIndex].Id;
                return true;
        }

        if (key.KeyChar > ' ')
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (!_buttons[i].IsEnabled ||
                    char.ToUpperInvariant(key.KeyChar) != char.ToUpperInvariant(_buttons[i].HotKey))
                    continue;

                focusedIndex = i;
                buttonId = _buttons[i].Id;
                return true;
            }
        }

        return false;
    }

    public bool TryHandleMouse(MouseConsoleInputEvent mouse, DialogButtonBarLayout layout, ref int focusedIndex, out string? buttonId)
    {
        buttonId = null;

        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down)
            return false;

        for (int i = 0; i < layout.ButtonBounds.Count; i++)
        {
            if (!Contains(layout.ButtonBounds[i], mouse.X, mouse.Y))
                continue;

            focusedIndex = i;
            if (_buttons[i].IsEnabled)
                buttonId = _buttons[i].Id;
            return true;
        }

        return false;
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
