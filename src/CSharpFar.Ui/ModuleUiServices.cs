using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ModuleUiServices
{
    public required ScreenRenderer Screen { get; init; }

    public required Func<ConsolePalette> Palette { get; init; }

    public ConsolePalette CurrentPalette => Palette();

    public ScreenSnapshot SaveScreen()
    {
        var size = Screen.GetSize();
        return Screen.Capture(new Rect(0, 0, size.Width, size.Height));
    }

    public void RestoreScreen(ScreenSnapshot snapshot) =>
        Screen.Restore(snapshot);

    public void ShowMessage(string title, string message) =>
        new MessageDialog(Screen).Show(title, message);

    public int ShowMessage(string title, string message, IReadOnlyList<string> buttons) =>
        new MessageDialog(Screen).ShowButtons(title, message, buttons);

    public string? Input(string title, string prompt, string? initialText = null) =>
        new ModuleInputDialog(Screen).Show(title, prompt, initialText);

    public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) =>
        new ModuleMenuDialog(Screen).Show(title, items, selected);

    public void ShowHelp(string title, IReadOnlyList<string> lines) =>
        new ModuleHelpDialog(Screen).Show(title, lines);

    public bool Confirm(string title, string question, string itemName) =>
        new ConfirmDialog(Screen).Show(title, question, itemName);
}
