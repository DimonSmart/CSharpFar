using CSharpFar.App.Editor;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record OpenCreateFileDialogResult(
    string FilePath,
    EditorNewFileEncodingOption CodePage);

internal sealed class OpenCreateFileDialog
{
    private const int DialogWidth = 72;
    private const int DialogHeight = 12;
    private const string Title = "Editor";

    private readonly FormFieldFactory _fields;

    private readonly DialogService _dialogs;
    private readonly IReadOnlyList<EditorNewFileEncodingOption> _codePages;

    public OpenCreateFileDialog(DialogService dialogs, FormFieldFactory fields)
        : this(dialogs, EditorNewFileEncodingOption.CreateCatalog(), fields)
    {
    }

    internal OpenCreateFileDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
        : this(new DialogService(modalDialogs, fields), fields)
    {
    }

    internal OpenCreateFileDialog(
        DialogService dialogs,
        IReadOnlyList<EditorNewFileEncodingOption> codePages,
        FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _codePages = codePages.Count == 0
            ? [new EditorNewFileEncodingOption("Default", null, EmitByteOrderMark: false)]
            : codePages;
    }

    public OpenCreateFileDialogResult? Show(
        string? initialPath = null,
        Func<string, string?>? validate = null)
    {
        TextField filePath = _fields.Text("file-path", initialPath ?? string.Empty,
            AppTextHistoryIds.OpenCreateFilePath, submitOnEnter: true);
        TextInputRow pathRow = FormControls.Text(filePath);
        var codePageRow = FormControls.Dropdown(
            "code-page", string.Empty, _codePages, static item => item.Label, _codePages[0]);
        var actions = FormControls.Buttons(
            "actions",
            DialogButton.Default("ok", "OK", 'O'),
            DialogButton.Cancel());
        string? error = null;
        return _dialogs.Form(
            new FormDialogOptions(Title, DialogWidth, DialogHeight, MinWidth: 44),
            rows: () =>
            [
                FormControls.Label("Open/create file:"),
                pathRow,
                FormControls.Spacer(),
                FormControls.Label("Code page:"),
                codePageRow,
                FormControls.Error(() => error),
                FormControls.Spacer(),
            ],
            footer: () => [actions],
            (result) =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<OpenCreateFileDialogResult?>.Complete(null);

                if (result.IsValueChanged)
                {
                    if (result.SourceRowId == "file-path")
                    {
                        error = null;
                    }
                    else if (result.SourceRowId == "code-page")
                    {
                        error = null;
                    }
                }

                if (result.IsSubmitted)
                {
                    EditorNewFileEncodingOption selectedCodePage = codePageRow.Value;
                    var accepted = TrySubmit(filePath, selectedCodePage, validate, ref error);
                    if (accepted is not null)
                        return FormDialogOutcome<OpenCreateFileDialogResult?>.Complete(accepted);
                }

                return FormDialogOutcome<OpenCreateFileDialogResult?>.Continue();
            });
    }

    private OpenCreateFileDialogResult? TrySubmit(
        TextField filePath,
        EditorNewFileEncodingOption codePage,
        Func<string, string?>? validate,
        ref string? error)
    {
        string path = filePath.Text.Trim();
        if (path.Length == 0)
        {
            error = "File path is required.";
            return null;
        }

        error = validate?.Invoke(path);
        if (error is not null)
            return null;

        filePath.AcceptHistory();
        return new OpenCreateFileDialogResult(path, codePage);
    }

}
