namespace CSharpFar.App.Input;

internal sealed class NullCommandShortcutTextProvider : ICommandShortcutTextProvider
{
    public static readonly NullCommandShortcutTextProvider Instance = new();

    private NullCommandShortcutTextProvider()
    {
    }

    public string? GetPrimaryShortcutText(string commandId) => null;
}
