using CSharpFar.App.Commands;

namespace CSharpFar.App.Input;

internal sealed class DefaultKeyboardShortcutBindingProvider
{
    private static readonly IReadOnlyList<KeyboardShortcutBinding> Bindings =
    [
        new(
            ApplicationCommandIds.TogglePanels,
            ConsoleKey.O,
            ConsoleModifiers.Control,
            "Ctrl+O"),
    ];

    public IReadOnlyList<KeyboardShortcutBinding> GetBindings() => Bindings;
}
