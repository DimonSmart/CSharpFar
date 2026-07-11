using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ModuleUiServices
{
    public required ScreenRenderer Screen { get; init; }

    public required ModalDialogHost ModalDialogs { get; init; }

    public required Func<ConsolePalette> Palette { get; init; }

    public ConsolePalette CurrentPalette => Palette();

    /// <summary>
    /// Captures the console only for legacy external-module compatibility.
    /// Application-owned interactive UI must use <see cref="ModalDialogs"/>;
    /// a snapshot is not a resize-safe window lifecycle.
    /// </summary>
    public ScreenSnapshot SaveScreen()
    {
        var size = Screen.GetSize();
        return Screen.Capture(new Rect(0, 0, size.Width, size.Height));
    }

    /// <summary>
    /// Restores a legacy compatibility snapshot. Do not use this for
    /// application-owned modal or interactive UI because it is not resize-safe.
    /// </summary>
    public void RestoreScreen(ScreenSnapshot snapshot) =>
        Screen.Restore(snapshot);

    public void ShowMessage(string title, string message) =>
        new MessageDialog(ModalDialogs).Show(title, message);

    public int ShowMessage(string title, string message, IReadOnlyList<string> buttons) =>
        new MessageDialog(ModalDialogs).ShowButtons(title, message, buttons);

    public string? Input(string title, string prompt, string? initialText = null) =>
        new ModuleInputDialog(ModalDialogs).Show(title, prompt, initialText);

    public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) =>
        new ModuleMenuDialog(ModalDialogs).Show(title, items, selected);

    public void ShowHelp(string title, IReadOnlyList<string> lines) =>
        new ModuleHelpDialog(ModalDialogs).Show(title, lines);

    public bool Confirm(string title, string question, string itemName) =>
        new ConfirmDialog(ModalDialogs).Show(title, question, itemName);
}
