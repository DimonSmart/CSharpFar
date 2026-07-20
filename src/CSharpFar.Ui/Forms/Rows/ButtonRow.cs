using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ButtonRow : FormRow
{
    private readonly DialogButtonBar _buttonBar;
    private readonly CellStyle _normalStyle;
    private readonly CellStyle _focusedStyle;

    public ButtonRow(
        IReadOnlyList<DialogButton> buttons,
        CellStyle normalStyle,
        CellStyle focusedStyle,
        int focusedButtonIndex = 0)
        : this(new DialogButtonBar(buttons), normalStyle, focusedStyle)
    {
        FocusedButtonIndex = buttons.Count == 0 ? 0 : Math.Clamp(focusedButtonIndex, 0, buttons.Count - 1);
    }

    public ButtonRow(DialogButtonBar buttonBar, CellStyle normalStyle, CellStyle focusedStyle)
    {
        _buttonBar = buttonBar;
        _normalStyle = normalStyle;
        _focusedStyle = focusedStyle;
    }

    public int FocusedButtonIndex { get; private set; }
    public override FormRowRole Role { get; init; } = FormRowRole.ButtonBar;

    public override void Render(FormRowRenderContext context) =>
        _buttonBar.Render(
            context.Screen,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            FocusedButtonIndex,
            _normalStyle,
            context.Focused ? _focusedStyle : _normalStyle);

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        int focusedButton = FocusedButtonIndex;
        if (!_buttonBar.TryHandleKey(key, ref focusedButton, out string? buttonId))
            return FormInputResult.NotHandled;

        FocusedButtonIndex = focusedButton;
        return ButtonResult(buttonId);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int focusedButton = FocusedButtonIndex;
        var layout = _buttonBar.CalculateLayout(context.Bounds.X, context.Bounds.Y, context.Bounds.Width);
        if (!_buttonBar.TryHandleMouse(mouse, layout, ref focusedButton, out string? buttonId))
            return FormInputResult.NotHandled;

        FocusedButtonIndex = focusedButton;
        return ButtonResult(buttonId);
    }

    private static FormInputResult ButtonResult(string? buttonId) =>
        buttonId switch
        {
            null => FormInputResult.Handled,
            "cancel" => FormInputResult.Cancel(buttonId),
            _ => FormInputResult.Submit(buttonId),
        };
}
