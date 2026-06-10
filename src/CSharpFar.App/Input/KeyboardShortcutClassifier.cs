namespace CSharpFar.App.Input;

internal static class KeyboardShortcutClassifier
{
    public static bool IsPlainControlKey(ConsoleKeyInfo key, ConsoleKey consoleKey, char controlChar)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        return !hasAlt && !hasShift &&
               ((hasControl && key.Key == consoleKey) ||
                key.KeyChar == controlChar);
    }

    public static bool IsPlainControlEnter(ConsoleKeyInfo key) =>
        HasOnlyControlModifier(key) && key.Key == ConsoleKey.Enter;

    public static bool IsPlainControlOpenBracket(ConsoleKeyInfo key) =>
        IsPlainControlBracket(key, ConsoleKey.Oem4, '[', '\u001b');

    public static bool IsPlainControlCloseBracket(ConsoleKeyInfo key) =>
        IsPlainControlBracket(key, ConsoleKey.Oem6, ']', '\u001d');

    public static bool IsPlainControlBackslash(ConsoleKeyInfo key) =>
        HasOnlyControlModifier(key) &&
        (key.Key == ConsoleKey.Oem5 || key.KeyChar == '\u001c');

    public static bool HasOnlyControlModifier(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        return hasControl && !hasAlt && !hasShift;
    }

    private static bool IsPlainControlBracket(
        ConsoleKeyInfo key,
        ConsoleKey consoleKey,
        char printableChar,
        char controlChar)
    {
        if (!HasOnlyControlModifier(key))
            return false;

        return key.Key == consoleKey ||
               key.KeyChar == printableChar ||
               (key.Key != ConsoleKey.Escape && key.KeyChar == controlChar);
    }
}
