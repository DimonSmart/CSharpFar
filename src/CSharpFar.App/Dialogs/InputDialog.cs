using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Modal single-line input dialog.
/// Draws a centered box, collects a string, validates on Enter, cancels on Esc.
/// </summary>
internal sealed class InputDialog
{
    private readonly ScreenRenderer _screen;

    public InputDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    /// <summary>
    /// Shows the dialog and returns the entered text, or <c>null</c> if the user pressed Esc.
    /// </summary>
    /// <param name="title">Title shown in the top border.</param>
    /// <param name="prompt">Label shown above the input field.</param>
    /// <param name="validate">
    /// Called with the trimmed input on Enter.
    /// Return <c>null</c> to accept, or an error string to display and re-prompt.
    /// </param>
    public string? Show(
        string title,
        string prompt,
        string? initialText = null,
        Func<string, string?>? validate = null,
        bool allowEmpty = false,
        bool maskInput = false)
    {
        var result = new SingleLineInputDialog(_screen).Show(new SingleLineInputDialogOptions
        {
            Title = title,
            Prompt = prompt,
            InitialText = initialText ?? string.Empty,
            AllowEmpty = allowEmpty,
            MaskInput = maskInput,
            HistoryKey = maskInput ? null : $"{title}\n{prompt}",
            Validate = validate,
        });

        return result.IsConfirmed ? result.Text : null;
    }
}
