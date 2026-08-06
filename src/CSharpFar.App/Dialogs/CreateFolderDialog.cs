using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CreateFolderDialog
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 9;
    private const string Title = "Make folder";
    private const string Prompt = "Create the folder:";

    private readonly FormFieldFactory _fields;

    private readonly ModalFormHost _formDialogs;

    public CreateFolderDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        var fields = _fields;
        TextField folderName = fields.Text("folder-name", initialText ?? string.Empty,
            AppTextHistoryIds.CreateFolderName, submitOnEnter: true);
        var actions = FormControls.Buttons(
            "actions",
            DialogButton.Default("ok", "OK", 'O'),
            DialogButton.Cancel());
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows() =>
            form.SetRows(
                [
                    new LabelRow(Prompt),
                    FormControls.Text(folderName),
                    new SeparatorRow(FarDialogStyles.Border),
                    new LabelRow(error ?? string.Empty, FarDialogStyles.Error),
                ],
                [actions]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(Title, DialogWidth, DialogHeight, MinWidth: 40),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 2),
            (result) =>
            {
                if (result.IsCancelled)
                    return ModalDialogLoopResult<string?>.Complete(null);

                if (result.IsValueChanged)
                    error = null;

                if (result.IsSubmitted)
                {
                    string? accepted = TrySubmit(folderName, validate, ref error);
                    if (accepted is not null)
                        return ModalDialogLoopResult<string?>.Complete(accepted);
                }

                return ModalDialogLoopResult<string?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    private static string? TrySubmit(
        TextField folderName,
        Func<string, string?>? validate,
        ref string? error)
    {
        string text = folderName.TrimmedText;
        if (text.Length == 0)
            return null;

        error = validate?.Invoke(text);
        if (error is not null)
            return null;

        folderName.AcceptHistory();
        return text;
    }

}
