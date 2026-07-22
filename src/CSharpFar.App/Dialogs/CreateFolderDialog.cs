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

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public CreateFolderDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        var folderName = new CommandLineState();
        if (initialText is not null)
            folderName.SetText(initialText);

        SingleLineTextHistoryState history = HistoryRegistry.GetOrCreate("CreateFolderDialog.FolderName");
        var inputState = new TextInputRowState();
        var input = new TextInputRow(folderName, history, inputState)
        {
            Id = "folder-name",
            SubmitOnEnter = true,
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
                    new LabelRow(Prompt, FarDialogStyles.Fill),
                    input,
                    new SeparatorRow(FarDialogStyles.Border),
                    new LabelRow(error ?? string.Empty, FarDialogStyles.Error),
                ],
                [actions]);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, string?>(
            (context, focusScope) => Draw(context, focusScope, form),
            form.BuildInteractionFrame,
            (inputEvent, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(inputEvent, frame, route);
                return (result.FormResult, result.UiResult);
            },
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<string?>.Complete(null);

                if (result.Kind == FormInputResultKind.ValueChanged)
                    error = null;

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    string? accepted = TrySubmit(folderName, history, validate, ref error);
                    if (accepted is not null)
                        return ModalDialogLoopResult<string?>.Complete(accepted);
                }

                return ModalDialogLoopResult<string?>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private static string? TrySubmit(
        CommandLineState folderName,
        SingleLineTextHistoryState history,
        Func<string, string?>? validate,
        ref string? error)
    {
        string text = folderName.Text.Trim();
        if (text.Length == 0)
            return null;

        error = validate?.Invoke(text);
        if (error is not null)
            return null;

        history.Add(text);
        return text;
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
                Rect bounds = layout.FrameBounds;
                int contentX = bounds.X + 2;
                int contentWidth = Math.Max(1, bounds.Width - 4);
                var bodyBounds = new Rect(contentX, bounds.Y + 1, contentWidth, Math.Max(1, bounds.Height - 4));
                var footerBounds = new Rect(contentX, bounds.Bottom - 2, contentWidth, 1);
                frame = form.Render(
                    new FormRenderContext(context, bodyBounds, FarDialogStyles.Border, footerBounds),
                    focusScope);
            });

        return frame ?? throw new InvalidOperationException("Create folder dialog did not render a form frame.");
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
    }
}
