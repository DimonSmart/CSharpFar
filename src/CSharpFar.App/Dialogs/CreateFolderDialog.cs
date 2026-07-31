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

    private readonly ITextFieldHistoryProvider _history;

    private readonly ModalFormHost _formDialogs;

    public CreateFolderDialog(ModalDialogHost modalDialogs, ITextFieldHistoryProvider history)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        var fields = new FormFieldFactory(_history);
        TextField folderName = fields.Text("folder-name", initialText ?? string.Empty,
            AppTextHistoryIds.CreateFolderName, submitOnEnter: true);
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
                    new LabelRow(Prompt, FarDialogStyles.Fill),
                    folderName.Row,
                    new SeparatorRow(FarDialogStyles.Border),
                    new LabelRow(error ?? string.Empty, FarDialogStyles.Error),
                ],
                [actions]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(Title, DialogWidth, DialogHeight, MinWidth: 40),
            static layout =>
            {
                Rect bounds = layout.FrameBounds;
                int contentX = bounds.X + 2;
                int contentWidth = Math.Max(1, bounds.Width - 4);
                return new ModalFormLayout(
                    new Rect(contentX, bounds.Y + 1, contentWidth, Math.Max(1, bounds.Height - 4)),
                    new Rect(contentX, bounds.Bottom - 2, contentWidth, 1));
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
