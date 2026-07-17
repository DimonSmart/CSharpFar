using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;

namespace CSharpFar.App.Input;

internal sealed class ApplicationDirectoryShortcutKeyboardHandler
{
    private readonly KeyboardInputContext _context;

    public ApplicationDirectoryShortcutKeyboardHandler(KeyboardInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationKeyboardInput input)
    {
        ConsoleKeyInfo key = input.Key;
        if ((key.Modifiers & ConsoleModifiers.Control) == 0 ||
            (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) != 0)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        int? number = key.Key switch
        {
            ConsoleKey.D0 or ConsoleKey.NumPad0 => 0,
            ConsoleKey.D1 or ConsoleKey.NumPad1 => 1,
            ConsoleKey.D2 or ConsoleKey.NumPad2 => 2,
            ConsoleKey.D3 or ConsoleKey.NumPad3 => 3,
            ConsoleKey.D4 or ConsoleKey.NumPad4 => 4,
            ConsoleKey.D5 or ConsoleKey.NumPad5 => 5,
            ConsoleKey.D6 or ConsoleKey.NumPad6 => 6,
            ConsoleKey.D7 or ConsoleKey.NumPad7 => 7,
            ConsoleKey.D8 or ConsoleKey.NumPad8 => 8,
            ConsoleKey.D9 or ConsoleKey.NumPad9 => 9,
            _ => null,
        };

        if (number is null)
            return ApplicationInputHandlingResult.NotHandled;

        var hit = input.Frame.DirectoryShortcutBar?.Shortcuts
            .FirstOrDefault(shortcut => shortcut.ShortcutNumber == number.Value);
        if (hit is null)
            return ApplicationInputHandlingResult.FromHandled(true);

        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(
                DirectoryShortcutCommandIds.Navigate,
                new NavigateToCommittedDirectoryShortcutArgs(number.Value, hit.Path, input.ActiveSide)));
    }
}
