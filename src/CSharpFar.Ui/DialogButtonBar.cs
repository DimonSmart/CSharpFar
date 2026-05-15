using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class DialogButtonBar
{
    private readonly IReadOnlyList<DialogButton> _buttons;
    private Rect[] _lastBounds;

    public DialogButtonBar(IReadOnlyList<DialogButton> buttons)
    {
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        _buttons = buttons;
        _lastBounds = new Rect[buttons.Count];
    }

    public int Count => _buttons.Count;

    public void Render(
        ScreenRenderer screen,
        int x,
        int y,
        int width,
        int focusedIndex,
        CellStyle normalStyle,
        CellStyle focusedStyle)
    {
        string[] labels = _buttons.Select(FormatButton).ToArray();
        int totalWidth = labels.Sum(label => label.Length) + Math.Max(0, labels.Length - 1);
        int cursorX = x + Math.Max(0, (width - totalWidth) / 2);

        screen.Write(x, y, new string(' ', Math.Max(0, width)), normalStyle);

        for (int i = 0; i < labels.Length; i++)
        {
            string label = labels[i];
            var style = i == focusedIndex ? focusedStyle : normalStyle;
            screen.Write(cursorX, y, label, style);
            _lastBounds[i] = new Rect(cursorX, y, label.Length, 1);
            cursorX += label.Length + 1;
        }
    }

    public bool TryHandleInput(ConsoleInputEvent input, ref int focusedIndex, out string? buttonId)
    {
        buttonId = null;

        switch (input)
        {
            case KeyConsoleInputEvent { Key: var key }:
                return TryHandleKey(key, ref focusedIndex, out buttonId);
            case MouseConsoleInputEvent mouse:
                return TryHandleMouse(mouse, ref focusedIndex, out buttonId);
            default:
                return false;
        }
    }

    private bool TryHandleKey(ConsoleKeyInfo key, ref int focusedIndex, out string? buttonId)
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
                buttonId = _buttons[focusedIndex].Id;
                return true;
        }

        if (key.KeyChar > ' ')
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (char.ToUpperInvariant(key.KeyChar) != char.ToUpperInvariant(_buttons[i].HotKey))
                    continue;

                focusedIndex = i;
                buttonId = _buttons[i].Id;
                return true;
            }
        }

        return false;
    }

    private bool TryHandleMouse(MouseConsoleInputEvent mouse, ref int focusedIndex, out string? buttonId)
    {
        buttonId = null;

        if (mouse.Button != MouseButton.Left || mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
            return false;

        for (int i = 0; i < _lastBounds.Length; i++)
        {
            if (!Contains(_lastBounds[i], mouse.X, mouse.Y))
                continue;

            focusedIndex = i;
            buttonId = _buttons[i].Id;
            return true;
        }

        return false;
    }

    private static string FormatButton(DialogButton button) =>
        button.IsDefault ? $"{{ {button.Text} }}" : $"[ {button.Text} ]";

    private static bool Contains(Rect rect, int x, int y) =>
        x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;
}
