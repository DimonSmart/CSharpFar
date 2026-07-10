using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

public enum SaveChangesChoice { Save, Discard, Cancel }

/// <summary>
/// Asks whether to save, discard, or cancel closing a modified file.
/// S/Enter → Save, D → Discard, C/Esc → Cancel.
/// </summary>
internal sealed class SaveChangesDialog
{
    private readonly ModalDialogHost _modalDialogs;

    public SaveChangesDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public SaveChangesChoice Show(string fileName)
    {
        var result = new ChoiceDialog(_modalDialogs).Show(new ChoiceDialogOptions
        {
            Title = "Save Changes?",
            Lines = [Truncate($"\"{fileName}\" has been modified.", 48)],
            Buttons =
            [
                new DialogButton("save", "Save", 'S', IsDefault: true),
                new DialogButton("discard", "Discard", 'D'),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            DefaultButtonIndex = 0,
            CancelButtonIndex = 2,
        });

        return result.ButtonId switch
        {
            "save" => SaveChangesChoice.Save,
            "discard" => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
