namespace CSharpFar.App.Editor;

public static class EditorCommandBindings
{
    private static readonly IReadOnlyList<EditorCommandBinding> Plain =
    [
        new(2, "Save", ConsoleKey.F2, 0),
        new(3, "Mark", ConsoleKey.F3, 0),
        new(4, "Rect", ConsoleKey.F4, 0),
        new(5, "Copy", ConsoleKey.F5, 0),
        new(6, "Move", ConsoleKey.F6, 0),
        new(7, "Find", ConsoleKey.F7, 0),
        new(8, "Del", ConsoleKey.F8, 0),
        new(10, "Close", ConsoleKey.F10, 0),
    ];

    private static readonly IReadOnlyList<EditorCommandBinding> Shift =
    [
        new(2, "Format", ConsoleKey.F2, ConsoleModifiers.Shift),
        new(7, "Next", ConsoleKey.F7, ConsoleModifiers.Shift),
    ];

    private static readonly IReadOnlyList<EditorCommandBinding> Alt =
    [
        new(7, "Prev", ConsoleKey.F7, ConsoleModifiers.Alt),
    ];

    private static readonly IReadOnlyList<EditorCommandBinding> Control =
    [
        new(7, "Replace", ConsoleKey.F7, ConsoleModifiers.Control),
    ];

    public static IReadOnlyList<EditorCommandBinding> ForModifiers(ConsoleModifiers modifiers)
    {
        if ((modifiers & ConsoleModifiers.Control) != 0)
            return Control;
        if ((modifiers & ConsoleModifiers.Alt) != 0)
            return Alt;
        if ((modifiers & ConsoleModifiers.Shift) != 0)
            return Shift;
        return Plain;
    }
}
