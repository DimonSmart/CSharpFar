using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public sealed class ButtonRow : FormRow
{
    private readonly DialogButtonBar _buttonBar;
    private readonly DialogButtonBarStyle? _style;
    private DialogButtonBarState _state;

    public ButtonRow(
        IReadOnlyList<DialogButton> buttons,
        int focusedButtonIndex = 0,
        DialogButtonBarStyle? style = null)
    {
        _buttonBar = new DialogButtonBar(buttons);
        _state = _buttonBar.CreateState(focusedButtonIndex);
        _style = style;
    }

    public int FocusedButtonIndex => _state.FocusedIndex;
    public int? PressedButtonIndex => _state.PressedButtonIndex;
    public override FormRowRole Role { get; init; } = FormRowRole.ButtonBar;

    public override void Render(FormRowRenderContext context) =>
        _buttonBar.Render(
            context.Canvas,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            _state,
            context.Focused,
            _style);

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        DialogButtonBarInputResult result = _buttonBar.HandleKey(key, _state);
        _state = result.State;
        if (!result.IsHandled)
            return FormInputResult.NotHandled;

        return ButtonResult(result);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        var layout = _buttonBar.CalculateLayout(context.Bounds.X, context.Bounds.Y, context.Bounds.Width);
        DialogButtonBarInputResult result = _buttonBar.HandleMouse(mouse, layout, _state);
        _state = result.State;
        if (!result.IsHandled)
            return FormInputResult.NotHandled;

        return ButtonResult(result);
    }

    private static FormInputResult ButtonResult(DialogButtonBarInputResult result) =>
        result.ButtonId is null
            ? new FormInputResult(FormInputResultKind.Handled, MouseCapture: result.MouseCapture)
            : new FormInputResult(
                result.ButtonRole == DialogButtonRole.Cancel
                    ? FormInputResultKind.Cancel
                    : FormInputResultKind.Submit,
                result.ButtonId,
                result.MouseCapture);
}
