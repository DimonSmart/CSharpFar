using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public sealed class ButtonRow : FormRow
{
    private DialogButtonBar _buttonBar;
    private DialogButton[] _buttons;
    private readonly FormControlTone _tone;
    private DialogButtonBarState _state;

    internal ButtonRow(
        IReadOnlyList<DialogButton> buttons,
        int focusedButtonIndex = 0,
        FormControlTone tone = FormControlTone.Default)
    {
        _buttons = buttons.ToArray();
        _buttonBar = new DialogButtonBar(_buttons);
        _state = _buttonBar.CreateState(focusedButtonIndex);
        _tone = tone;
    }

    internal int FocusedButtonIndex => _state.FocusedIndex;
    internal int? PressedButtonIndex => _state.PressedButtonIndex;
    internal override FormRowRole Role { get; init; } = FormRowRole.ButtonBar;

    public void SetButtons(IReadOnlyList<DialogButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        DialogButton[] updatedButtons = buttons.ToArray();
        if (_buttons.SequenceEqual(updatedButtons))
            return;

        int focusedIndex = Math.Clamp(_state.FocusedIndex, 0, Math.Max(0, updatedButtons.Length - 1));
        int? armedIndex = _state.ArmedButtonIndex is int index &&
            index >= 0 &&
            index < updatedButtons.Length &&
            updatedButtons[index].IsEnabled
            ? index
            : null;
        _buttons = updatedButtons;
        _buttonBar = new DialogButtonBar(_buttons);
        _state = new DialogButtonBarState(focusedIndex, armedIndex, armedIndex is not null && _state.IsPressed);
    }

    internal override void Render(FormRowRenderContext context) =>
        _buttonBar.Render(
            context.Canvas,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            _state,
            context.Focused,
            _tone == FormControlTone.Warning ? WarningDialogStyles.ButtonBar : null);

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        DialogButtonBarInputResult result = _buttonBar.HandleKey(key, _state);
        _state = result.State;
        if (!result.IsHandled)
            return FormInputResult.NotHandled;

        return ButtonResult(result);
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
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
