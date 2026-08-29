namespace CSharpFar.Ui;

public interface ICommandShortcutTextProvider
{
    string? GetPrimaryShortcutText(string commandId);
}

public sealed class NullCommandShortcutTextProvider : ICommandShortcutTextProvider
{
    public static NullCommandShortcutTextProvider Instance { get; } = new();

    private NullCommandShortcutTextProvider()
    {
    }

    public string? GetPrimaryShortcutText(string commandId) => null;
}

public enum TopMenuPointerActionKind
{
    ActivateForPanel,
    OpenTopItem,
    ActivateDropdownItem,
    ConsumeDropdownSurface,
    Scrollbar,
}

public readonly record struct TopMenuPointerAction(TopMenuPointerActionKind Kind, int ItemIndex = -1);
