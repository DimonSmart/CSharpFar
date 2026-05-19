namespace CSharpFar.App.Editor;

public sealed record EditorCommandBinding(int KeyNumber, string Label, ConsoleKey Key, ConsoleModifiers Modifiers);
