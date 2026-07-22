using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

internal enum DialogActionOutcomeKind
{
    Activated,
    Cancelled,
}

internal readonly record struct DialogActionOutcome(
    DialogActionOutcomeKind Kind,
    int ButtonIndex,
    string? ButtonId);

/// <summary>Hosts the standard fixed-footer button form for simple modal dialogs.</summary>
internal sealed class DialogActionController
{
    private readonly DialogButton[] _buttons;
    private readonly int? _cancelButtonIndex;
    private readonly ScrollableFormDialog _form;

    public DialogActionController(
        IReadOnlyList<DialogButton> buttons,
        int defaultButtonIndex,
        int? cancelButtonIndex,
        CellStyle normalStyle,
        CellStyle focusedStyle)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        _buttons = buttons.ToArray();
        int normalizedDefaultIndex = NormalizeIndex(defaultButtonIndex);
        _cancelButtonIndex = cancelButtonIndex is int index ? NormalizeIndex(index) : null;
        var actions = new ButtonRow(_buttons, normalStyle, focusedStyle, normalizedDefaultIndex) { Id = "actions" };
        _form = new ScrollableFormDialog();
        _form.SetRows([], [actions]);
    }

    public ScrollableFormFrame Render(FormRenderContext context, UiFocusScope focusScope) =>
        _form.Render(context, focusScope);

    public UiInteractionFrame BuildInteractionFrame(ScrollableFormFrame frame) =>
        _form.BuildInteractionFrame(frame);

    public UiInteractionFragment BuildInteractionFragment(ScrollableFormFrame frame) =>
        _form.BuildInteractionFragment(frame);

    public FormRouteResult RouteInput(
        ConsoleInputEvent input,
        ScrollableFormFrame frame,
        UiInputRouteContext route) =>
        _form.RouteInput(input, frame, route);

    public DialogActionOutcome? Interpret(FormInputResult result)
    {
        if (result.Kind == FormInputResultKind.Submit && TryGetButtonIndex(result.Command, out int submitIndex))
            return Activated(submitIndex);

        // ButtonRow reports an ID named "cancel" as Cancel. It is still a button
        // activation when the command identifies one of this controller's buttons.
        if (result.Kind == FormInputResultKind.Cancel && TryGetButtonIndex(result.Command, out int cancelCommandIndex))
            return Activated(cancelCommandIndex);

        return result.Kind == FormInputResultKind.Cancel ? Cancelled() : null;
    }

    private DialogActionOutcome Activated(int index) =>
        new(DialogActionOutcomeKind.Activated, index, _buttons[index].Id);

    private DialogActionOutcome Cancelled() =>
        _cancelButtonIndex is int index
            ? new DialogActionOutcome(DialogActionOutcomeKind.Cancelled, index, _buttons[index].Id)
            : new DialogActionOutcome(DialogActionOutcomeKind.Cancelled, -1, null);

    private bool TryGetButtonIndex(string? buttonId, out int index)
    {
        if (buttonId is not null)
        {
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].Id == buttonId)
                {
                    index = i;
                    return true;
                }
            }
        }

        index = -1;
        return false;
    }

    private int NormalizeIndex(int index) => Math.Clamp(index, 0, _buttons.Length - 1);
}
