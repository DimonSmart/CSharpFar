using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputWithButtonsRow : FormRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;

    private readonly string _label;
    private readonly FormTextInputField _field;
    private readonly DialogButtonBar _buttonBar;
    private DialogButtonBarState _buttonState;
    private readonly int _inputWidth;
    private readonly int _buttonAreaWidth;
    private readonly string _commandPrefix;

    public TextInputWithButtonsRow(
        string label,
        CommandLineState buffer,
        IReadOnlyList<DialogButton> buttons,
        string commandPrefix,
        int inputWidth,
        int buttonAreaWidth)
    {
        _label = label;
        _field = new FormTextInputField(buffer, history: null);
        _buttonBar = new DialogButtonBar(buttons);
        _buttonState = _buttonBar.CreateState();
        _commandPrefix = commandPrefix;
        _inputWidth = inputWidth;
        _buttonAreaWidth = buttonAreaWidth;
    }

    public CommandLineState Buffer => _field.Buffer;

    public override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        int labelWidth = layout.InputBounds.X - context.Bounds.X;
        context.Canvas.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(_label, labelWidth),
            FarDialogStyles.Fill);

        _field.Render(context, layout.InputBounds);

        if (layout.ButtonAreaBounds.Width > 0)
        {
            _buttonBar.Render(
                context.Canvas,
                layout.ButtonLayout,
                _buttonState,
                context.Focused);
        }
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        return _field.TryGetCursor(context, layout.InputBounds, out cursor);
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        return _field.HandleKey(key, context);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        DialogButtonBarInputResult buttonResult = _buttonBar.HandleMouse(mouse, layout.ButtonLayout, _buttonState);
        _buttonState = buttonResult.State;
        if (buttonResult.IsHandled)
        {
            return buttonResult.ButtonId is null
                ? new FormInputResult(FormInputResultKind.Handled, MouseCapture: buttonResult.MouseCapture)
                : new FormInputResult(
                    buttonResult.ButtonRole == DialogButtonRole.Cancel
                        ? FormInputResultKind.Cancel
                        : FormInputResultKind.Submit,
                    _commandPrefix + buttonResult.ButtonId,
                    buttonResult.MouseCapture);
        }

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            mouse.Y != layout.InputBounds.Y ||
            mouse.X < layout.InputBounds.X ||
            mouse.X >= layout.InputBounds.Right)
        {
            return FormInputResult.NotHandled;
        }

        return _field.HandleMouse(mouse, context, layout.InputBounds);
    }

    private TextInputWithButtonsLayout CalculateLayout(Rect rowBounds)
    {
        int labelWidth = Math.Min(ConsoleTextMetrics.GetCellWidth(_label) + 1, Math.Max(0, rowBounds.Width));
        int inputX = rowBounds.X + labelWidth;
        int remainingAfterLabel = Math.Max(0, rowBounds.Width - labelWidth);
        int inputWidth = Math.Min(_inputWidth, remainingAfterLabel);
        var inputBounds = new Rect(inputX, rowBounds.Y, inputWidth, 1);
        int buttonX = inputBounds.Right + 1;
        int buttonWidth = Math.Min(_buttonAreaWidth, Math.Max(0, rowBounds.Right - buttonX));
        var buttonBounds = new Rect(buttonX, rowBounds.Y, buttonWidth, 1);
        return new TextInputWithButtonsLayout(
            inputBounds,
            buttonBounds,
            _buttonBar.CalculateLayout(buttonBounds.X, buttonBounds.Y, buttonBounds.Width));
    }

    private readonly record struct TextInputWithButtonsLayout(
        Rect InputBounds,
        Rect ButtonAreaBounds,
        DialogButtonBarLayout ButtonLayout);
}

