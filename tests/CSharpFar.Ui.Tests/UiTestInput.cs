using CSharpFar.Console.Input;

namespace CSharpFar.Ui.Tests;

internal static class UiTestInput
{
    public static KeyConsoleInputEvent Key(ConsoleKey key, char keyChar = '\0', bool shift = false, bool alt = false, bool control = false) =>
        new(new ConsoleKeyInfo(keyChar, key, shift, alt, control));

    public static MouseConsoleInputEvent Mouse(
        int x,
        int y,
        MouseEventKind kind = MouseEventKind.Down,
        MouseButton button = MouseButton.Left,
        MouseKeyModifiers modifiers = MouseKeyModifiers.None) =>
        new(x, y, button, kind, modifiers);
}
