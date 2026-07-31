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
        TextInputRow pathRow = filePath.AsRow();
        var dropdown = new DropdownSelect<EditorNewFileEncodingOption>(_codePages, static item => item.Label);
        var codePageRow = new DropdownSelectFormRow<EditorNewFileEncodingOption>(string.Empty, dropdown)
        {
            Id = "code-page",
        };
        var actions = new ButtonRow(
            [
                new DialogButton("ok", "OK", 'O', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
            ])
        {
            Id = "actions",
        };
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows() =>
            form.SetRows(
                [
                    new LabelRow("Open/create file:", FarDialogStyles.Fill),
                    pathRow,
                    new SeparatorRow(FarDialogStyles.Fill, drawLine: false),
                    new LabelRow("Code page:", FarDialogStyles.Fill),
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
                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);

                if (result.Kind == FormInputResultKind.ValueChanged)
                {
                    if (form.FocusedRowId == "file-path")
                    {
                        error = null;
                        codePageRow.CloseComposite();
                    }
                    else if (form.FocusedRowId == "code-page")
                    {
                        error = null;
                        pathRow.CloseComposite();
                    }
                }

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    int confirmedCodePageIndex = codePageRow.ConfirmedSelectedIndex;
                    codePageRow.CloseComposite();
                    var accepted = TrySubmit(filePath, confirmedCodePageIndex, validate, ref error);
                    if (accepted is not null)
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(accepted);
                }

                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    private OpenCreateFileDialogResult? TrySubmit(
        TextField filePath,
        int codePageIndex,
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
        return new OpenCreateFileDialogResult(path, _codePages[codePageIndex]);
    }

}
