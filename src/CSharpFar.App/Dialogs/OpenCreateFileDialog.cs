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
    private const int DialogHeight = 11;
    private const string Title = "Editor";

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly IReadOnlyList<EditorNewFileEncodingOption> _codePages;

    public OpenCreateFileDialog(ModalDialogHost modalDialogs)
        : this(modalDialogs, EditorNewFileEncodingOption.CreateCatalog())
    {
    }

    internal OpenCreateFileDialog(
        ModalDialogHost modalDialogs,
        IReadOnlyList<EditorNewFileEncodingOption> codePages)
    {
        _modalDialogs = modalDialogs;
        _codePages = codePages.Count == 0
            ? [new EditorNewFileEncodingOption("Default", null, EmitByteOrderMark: false)]
            : codePages;
    }

    public OpenCreateFileDialogResult? Show(
        string? initialPath = null,
        Func<string, string?>? validate = null)
    {
        var filePath = new CommandLineState();
        if (!string.IsNullOrEmpty(initialPath))
            filePath.SetText(initialPath);

        SingleLineTextHistoryState history = HistoryRegistry.GetOrCreate("OpenCreateFileDialog.FilePath");
        var pathState = new TextInputRowState();
        var pathRow = new TextInputRow(filePath, history, pathState)
        {
            Id = "file-path",
            SubmitOnEnter = true,
        };
        var dropdown = new DropdownSelect<EditorNewFileEncodingOption>(_codePages, static item => item.Label);
        var codePageRow = new DropdownSelectFormRow<EditorNewFileEncodingOption>(string.Empty, dropdown)
        {
            Id = "code-page",
        };
        var actions = new ButtonRow(
            [
                new DialogButton("ok", "OK", 'O', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput)
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

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, OpenCreateFileDialogResult?>(
            (context, focusScope) => Draw(context, focusScope, form),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
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
                        codePageRow.CloseDropdown();
                    }
                    else if (form.FocusedRowId == "code-page")
                    {
                        error = null;
                        history.Close();
                        pathState.HistoryScrollbarDrag = null;
                    }
                }

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    int confirmedCodePageIndex = codePageRow.ConfirmedSelectedIndex;
                    codePageRow.CancelOverlay();
                    var accepted = TrySubmit(filePath, history, confirmedCodePageIndex, validate, ref error);
                    if (accepted is not null)
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(accepted);
                }

                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private OpenCreateFileDialogResult? TrySubmit(
        CommandLineState filePath,
        SingleLineTextHistoryState history,
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

        history.Add(path);
        return new OpenCreateFileDialogResult(path, _codePages[codePageIndex]);
    }

    private ScrollableFormFrame Draw(UiRenderContext context, IUiFocusState focusScope, ScrollableFormDialog form)
    {
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(
            context.Canvas,
            OuterBounds(context.Size),
            Title,
            doubleBorder: true,
            FarDialogStyles.OuterOptions,
            FarDialogStyles.FrameOptions,
            (_, layout) =>
            {
                Rect content = layout.ContentBounds;
                int contentX = content.X + 1;
                int contentWidth = Math.Max(1, content.Width - 2);
                var bodyBounds = new Rect(contentX, content.Y, contentWidth, Math.Max(1, content.Height - 2));
                var footerBounds = new Rect(contentX, layout.FrameBounds.Bottom - 2, contentWidth, 1);
                frame = form.Render(
                    new FormRenderContext(context, bodyBounds, FarDialogStyles.Border, footerBounds),
                    focusScope);
            });

        return frame ?? throw new InvalidOperationException("Open/create file dialog did not render a form frame.");
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
    }
}
