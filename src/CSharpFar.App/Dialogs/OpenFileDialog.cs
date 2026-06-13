using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

public enum OpenFileChoice { View, Edit, Cancel }

/// <summary>
/// Asks whether to open a file in the viewer or the editor.
/// V → View, E → Edit, Esc/C → Cancel.
/// </summary>
internal sealed class OpenFileDialog
{
    private readonly ScreenRenderer _screen;

    public OpenFileDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public OpenFileChoice Show(string fileName)
    {
        var result = new ChoiceDialog(_screen).Show(new ChoiceDialogOptions
        {
            Title = "Open File",
            Lines = [Truncate($"Open \"{fileName}\" as:", 48)],
            Buttons =
            [
                new DialogButton("view", "View", 'V'),
                new DialogButton("edit", "Edit", 'E'),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            DefaultButtonIndex = 0,
            CancelButtonIndex = 2,
        });

        return result.ButtonId switch
        {
            "view" => OpenFileChoice.View,
            "edit" => OpenFileChoice.Edit,
            _ => OpenFileChoice.Cancel,
        };
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
