using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Asks the user to confirm a destructive action. Returns true if confirmed.</summary>
public sealed class ConfirmDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 7;

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public ConfirmDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    /// <summary>
    /// Draws the dialog and waits for input. Returns true if confirmed.
    /// </summary>
    public bool Show(string title, string question, string itemName)
    {
        var actions = new DialogActionController(
        [
            new DialogButton("ok", "OK", 'O', IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
        ], 0, 1);
        return _modalDialogs.RunInteractive<ScrollableFormFrame, DialogActionOutcome?, bool>(
            (context, focusScope) => RenderLayer(context, focusScope, actions, title, question, itemName),
            actions.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = actions.RouteInput(input, frame, route);
                return (actions.Interpret(result.FormResult), result.UiResult);
            },
            (_, outcome) =>
            {
                if (outcome is { } action)
                    return ModalDialogLoopResult<bool>.Complete(action.Kind == DialogActionOutcomeKind.Activated && action.ButtonId == "ok");
                return ModalDialogLoopResult<bool>.Continue;
            });
    }

    private ScrollableFormFrame RenderLayer(UiRenderContext context, IUiFocusState focusScope, DialogActionController actions, string title, string question, string itemName)
    {
        ScrollableFormFrame? frame = null;
        IUiCanvas screen = context.Canvas;
        var outerBounds = _modalRenderer.CenteredOuterBounds(context.Size, DialogWidth, DialogHeight, minWidth: 20, minHeight: 5);

        _modalRenderer.Render(screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);

            screen.Write(contentX, bounds.Y + 1, Truncate(question, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);

            string truncatedName = Truncate(itemName, contentWidth);
            int nameX = contentX + Math.Max(0, (contentWidth - truncatedName.Length) / 2);
            screen.Write(contentX, bounds.Y + 2, new string(' ', contentWidth), FarDialogStyles.Fill);
            screen.Write(nameX, bounds.Y + 2, truncatedName, FarDialogStyles.Fill);

            frame = actions.Render(
                new FormRenderContext(
                    context,
                    new Rect(contentX, bounds.Y + bounds.Height - 3, contentWidth, 1),
                    FarDialogStyles.Border,
                    new Rect(contentX, bounds.Y + bounds.Height - 2, contentWidth, 1)),
                focusScope);
        });
        return frame ?? throw new InvalidOperationException("Confirm dialog did not render its form frame.");
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
