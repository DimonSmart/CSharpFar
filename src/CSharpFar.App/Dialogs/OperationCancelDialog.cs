using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class OperationCancelDialog
{
    private const int DialogWidth = 46;
    private const int DialogHeight = 8;
    private const string YesButton = "yes";
    private const string NoButton = "no";

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public OperationCancelDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public bool Show(
        string interruptedMessage = "Operation has been interrupted",
        string confirmationMessage = "Do you really want to cancel it?")
    {
        var buttons = new ButtonRow(
        [
            new DialogButton(YesButton, "Yes", 'Y', IsDefault: true),
            new DialogButton(NoButton, "No", 'N'),
        ],
        WarningDialogStyles.Fill,
        WarningDialogStyles.ButtonFocus)
        { Id = "actions" };
        var form = new ScrollableFormDialog();
        form.SetRows([], [buttons]);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, bool>(
            (context, focusScope) => Draw(context, focusScope, form, interruptedMessage, confirmationMessage),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
            (_, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<bool>.Complete(false);
                if (result.Kind == FormInputResultKind.Submit)
                    return ModalDialogLoopResult<bool>.Complete(result.Command == YesButton);
                return ModalDialogLoopResult<bool>.Continue;
            });
    }

    private ScrollableFormFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        ScrollableFormDialog form,
        string interruptedMessage,
        string confirmationMessage)
    {
        ScrollableFormFrame? frame = null;
        int x = Math.Max(0, (context.Size.Width - DialogWidth) / 2);
        int y = Math.Max(0, (context.Size.Height - DialogHeight) / 2);
        var bounds = new Rect(x, y, DialogWidth, DialogHeight);

        _modalRenderer.Render(context.Canvas, bounds, string.Empty, true, WarningDialogStyles.OuterOptions, WarningDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect contentBounds = layout.ContentBounds;
            int contentX = contentBounds.X + 1;
            int contentWidth = Math.Max(1, contentBounds.Width - 2);

            context.Canvas.Write(contentX, contentBounds.Y, Center(interruptedMessage, contentWidth), WarningDialogStyles.Fill);
            context.Canvas.Write(contentX, contentBounds.Y + 1, Center(confirmationMessage, contentWidth), WarningDialogStyles.Fill);

            frame = form.Render(
                new FormRenderContext(
                    context,
                    new Rect(contentX, contentBounds.Bottom - 2, contentWidth, 1),
                    WarningDialogStyles.Border,
                    new Rect(contentX, contentBounds.Bottom - 1, contentWidth, 1)),
                focusScope);
        });

        return frame ?? throw new InvalidOperationException("Operation cancel dialog did not render its form frame.");
    }

    private static string Center(string text, int width)
    {
        if (text.Length >= width)
            return text[..width];

        int left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - left - text.Length);
    }
}
