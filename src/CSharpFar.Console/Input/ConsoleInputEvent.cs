namespace CSharpFar.Console.Input;

public abstract record ConsoleInputEvent;

[Flags]
public enum TerminalExtendedModifiers
{
    None = 0,
    Super = 1,
    Meta = 2,
}

public sealed record KeyConsoleInputEvent(ConsoleKeyInfo Key) : ConsoleInputEvent
{
    public TerminalExtendedModifiers ExtendedModifiers { get; init; }

    // Only a text editing owner can opt in to interpreting Super as an edit action.
    public bool TryGetMacTextEditingShortcut(out ConsoleKeyInfo shortcut)
    {
        shortcut = default;
        if (ExtendedModifiers != TerminalExtendedModifiers.Super ||
            (Key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return false;
        bool shift = (Key.Modifiers & ConsoleModifiers.Shift) != 0;
        if ((shift && Key.Key != ConsoleKey.Z) ||
            Key.Key is not (ConsoleKey.A or ConsoleKey.C or ConsoleKey.V or ConsoleKey.X or ConsoleKey.Z))
            return false;
        shortcut = new ConsoleKeyInfo('\0', Key.Key, shift, alt: false, control: true);
        return true;
    }

    /// <summary>
    /// Protocol-neutral text produced by this key event when it cannot be represented
    /// completely by <see cref="ConsoleKeyInfo.KeyChar"/>.
    /// </summary>
    public string? Text { get; init; }
}

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
    None,
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
