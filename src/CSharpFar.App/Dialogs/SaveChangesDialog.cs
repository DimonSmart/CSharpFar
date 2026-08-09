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
    private readonly DialogService _dialogs;

    public SaveChangesDialog(DialogService dialogs)
    {
        _dialogs = dialogs;
    }

    public SaveChangesChoice Show(string fileName)
    {
        var result = _dialogs.Choice(new ChoiceDialogOptions
        {
            Title = "Save Changes?",
            Lines = [Truncate($"\"{fileName}\" has been modified.", 48)],
            Buttons =
            [
                DialogButton.Default("save", "Save", 'S'),
                DialogButton.Action("discard", "Discard", 'D'),
                DialogButton.Cancel(),
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
