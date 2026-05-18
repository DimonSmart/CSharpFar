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
        new MessageDialog(Screen, CurrentPalette).Show(title, message);

    public int ShowMessage(string title, string message, IReadOnlyList<string> buttons) =>
        new MessageDialog(Screen, CurrentPalette).ShowButtons(title, message, buttons);

    public string? Input(string title, string prompt, string? initialText = null) =>
        new FarNetInputDialog(Screen, CurrentPalette).Show(title, prompt, initialText);

    public bool Confirm(string title, string question, string itemName) =>
        new ConfirmDialog(Screen).Show(title, question, itemName);
}
