using CSharpFar.App.Editor;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
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

    private readonly ModalFormHost _formDialogs;
    private readonly IReadOnlyList<EditorNewFileEncodingOption> _codePages;

    public OpenCreateFileDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
        : this(modalDialogs, EditorNewFileEncodingOption.CreateCatalog(), fields)
    {
    }

    internal OpenCreateFileDialog(
        ModalDialogHost modalDialogs,
        IReadOnlyList<EditorNewFileEncodingOption> codePages,
        FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
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
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows() =>
            form.SetRows(
                [
                    new LabelRow("Open/create file:"),
                    pathRow,
                    new SpacerRow(),
                    new LabelRow("Code page:"),
                    codePageRow,
                    new LabelRow(error ?? string.Empty, FarDialogStyles.Error),
                ],
                [actions]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(Title, DialogWidth, DialogHeight, MinWidth: 44),
            static layout =>
            {
                Rect content = layout.ContentBounds;
                return new ModalFormLayout(
                    new Rect(content.X, content.Y, content.Width, Math.Max(1, content.Height - 2)),
                    new Rect(content.X, layout.FrameBounds.Bottom - 2, content.Width, 1));
            },
            (routed, result) =>
            {
                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);

                if (result.Kind == FormInputResultKind.ValueChanged)
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

                if (FormDialogInput.ShouldSubmit(routed, result, form))
                {
                    EditorNewFileEncodingOption selectedCodePage = codePageRow.Value;
                    var accepted = TrySubmit(filePath, selectedCodePage, validate, ref error);
                    if (accepted is not null)
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(accepted);
                }

                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
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
