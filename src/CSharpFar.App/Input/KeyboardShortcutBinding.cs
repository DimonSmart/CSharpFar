namespace CSharpFar.App.Input;

internal sealed record KeyboardShortcutBinding(
    string CommandId,
    ConsoleKey Key,
    ConsoleModifiers Modifiers,
    string DisplayText);
