namespace CSharpFar.App.Editor;

public sealed record EditorKeyCommandBinding(
    string CommandId,
    ConsoleKey Key,
    ConsoleModifiers Modifiers,
    EditorKeyCommandStatus Status,
    string Description);
