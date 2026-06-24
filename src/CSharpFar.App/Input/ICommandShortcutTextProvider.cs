namespace CSharpFar.App.Input;

internal interface ICommandShortcutTextProvider
{
    string? GetPrimaryShortcutText(string commandId);
}
