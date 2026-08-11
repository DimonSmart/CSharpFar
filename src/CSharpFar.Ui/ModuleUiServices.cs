using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ModuleUiServices
{
    public required ScreenRenderer Screen { get; init; }

    /// <summary>Low-level escape hatch for custom modal UI with its own composition or lifecycle.</summary>
    public required ModalDialogHost ModalDialogs { get; init; }

    public required Func<ConsolePalette> Palette { get; init; }

    public required FormFieldFactory Fields { get; init; }

    /// <summary>Recommended API for standard module dialogs and forms.</summary>
    public required DialogService Dialogs { get; init; }

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
        Dialogs.Message(title, message);

    public int ShowMessage(string title, string message, IReadOnlyList<string> buttons) =>
        Dialogs.Message(title, message, buttons);

    public string? Input(string title, string prompt, string? initialText = null) =>
        Dialogs.Input(new SingleLineInputDialogOptions
        {
            Title = title,
            Prompt = prompt,
            InitialText = initialText ?? string.Empty,
            AllowEmpty = true,
        });

    public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) =>
        ShowMenuCore(title, items, selected);

    public void ShowHelp(string title, IReadOnlyList<string> lines) =>
        new ModuleHelpDialog(ModalDialogs).Show(title, lines);

    public bool Confirm(string title, string question, string itemName) =>
        Dialogs.Confirm(title, question, itemName);

    private int? ShowMenuCore(string title, IReadOnlyList<string> items, int selected)
    {
        if (items.Count == 0)
            return null;

        SelectionListDialogResult<string> result = Dialogs.Select(
            items, static item => item, title, selected, maxVisibleRows: 10);
        return result.IsConfirmed ? result.SelectedIndex : null;
    }
}
