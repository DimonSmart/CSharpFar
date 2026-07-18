namespace CSharpFar.Console.Input;

public abstract record ConsoleInputEvent;

public sealed record KeyConsoleInputEvent(ConsoleKeyInfo Key) : ConsoleInputEvent;

public sealed record ModifierKeyConsoleInputEvent(ConsoleModifiers Modifiers) : ConsoleInputEvent;

public sealed record MouseConsoleInputEvent(
    int X,
    int Y,
    MouseButton Button,
    MouseEventKind Kind,
    MouseKeyModifiers Modifiers) : ConsoleInputEvent;

public sealed record ConsoleResizeInputEvent : ConsoleInputEvent;

// ── Mouse enums ─────────────────────────────────────────────────────────────

public enum MouseButton
{
    Left,
    Right,
    Middle,
    WheelUp,
    WheelDown,
}

public enum MouseEventKind
{
    Down,
    Up,
    DoubleClick,
    Move,
    Wheel,
}

[Flags]
public enum MouseKeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}
