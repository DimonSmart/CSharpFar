using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CreateFolderDialog
{
    private const string Title = "Make folder";
    private const string Prompt = "Create the folder:";

    private readonly DialogService _dialogs;

    public CreateFolderDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        return _dialogs.Input(new SingleLineInputDialogOptions
        {
            Title = Title,
            Prompt = Prompt,
            InitialText = initialText ?? string.Empty,
            History = AppTextHistoryIds.CreateFolderName,
            Validate = validate,
        });
    }
}
