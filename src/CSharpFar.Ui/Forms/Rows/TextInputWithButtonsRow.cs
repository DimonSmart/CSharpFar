using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputWithButtonsRow : FormRow, IFormOverlayRow, IFormCursorProvider
{
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;

    private readonly string _label;
    private readonly CommandLineState _buffer;
    private readonly TextInputRowState _state;
    private readonly DialogButtonBar _buttonBar;
    private readonly int _inputWidth;
    private readonly int _buttonAreaWidth;
    private readonly string _commandPrefix;

    public TextInputWithButtonsRow(
        string label,
        CommandLineState buffer,
        IReadOnlyList<DialogButton> buttons,
        string commandPrefix,
        int inputWidth,
        int buttonAreaWidth,
        TextInputRowState? state = null)
    {
        _label = label;
        _buffer = buffer;
        _buttonBar = new DialogButtonBar(buttons);
        _commandPrefix = commandPrefix;
        _inputWidth = inputWidth;
        _buttonAreaWidth = buttonAreaWidth;
        _state = state ?? new TextInputRowState();
    }

    public CommandLineState Buffer => _buffer;
    public TextInputRowState State => _state;

    public override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        int labelWidth = layout.InputBounds.X - context.Bounds.X;
        context.Screen.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(_label.PadRight(labelWidth), labelWidth),
            FarDialogStyles.Fill);

        SingleLineTextInput.Render(
            context.Screen,
            layout.InputBounds.X,
            layout.InputBounds.Y,
            layout.InputBounds.Width,
            _buffer,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input,
            history: null,
            renderDropdown: false);

        if (layout.ButtonAreaBounds.Width > 0)
        {
            _buttonBar.Render(
                context.Screen,
                layout.ButtonLayout,
                focusedIndex: 0,
                FarDialogStyles.Fill,
                context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        }
    }

    public void RenderOverlay(FormRowRenderContext context)
    {
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Bounds);
        int cursorX = Math.Min(
            layout.InputBounds.Right - 1,
            SingleLineTextInput.GetCursorX(layout.InputBounds.X, layout.InputBounds.Width, _buffer));
        cursor = new FormCursorPlacement(cursorX, layout.InputBounds.Y);
        return context.Focused && layout.InputBounds.Width > 0;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        string? error = null;
        string before = _buffer.Text;
        TextInputKeyResult result = SingleLineTextInput.HandleKey(
            _buffer,
            key,
            ref error,
            history: null,
            availableDropdownContentRows: 0);

        return result switch
        {
            TextInputKeyResult.TextChanged when _buffer.Text != before => FormInputResult.ValueChanged,
            TextInputKeyResult.TextChanged => FormInputResult.Handled,
            TextInputKeyResult.Handled => FormInputResult.Handled,
            _ => FormInputResult.NotHandled,
        };
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        var layout = CalculateLayout(context.Bounds);
        int focusedButton = 0;
        if (_buttonBar.TryHandleMouse(mouse, layout.ButtonLayout, ref focusedButton, out string? buttonId))
            return buttonId is null
                ? FormInputResult.Handled
                : FormInputResult.Submit(_commandPrefix + buttonId);

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            mouse.Y != layout.InputBounds.Y ||
            mouse.X < layout.InputBounds.X ||
            mouse.X >= layout.InputBounds.Right)
        {
            return FormInputResult.NotHandled;
        }

        int target = Math.Clamp(mouse.X - layout.InputBounds.X, 0, Math.Min(_buffer.Text.Length, layout.InputBounds.Width));
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }

    private TextInputWithButtonsLayout CalculateLayout(Rect rowBounds)
    {
        int labelWidth = Math.Min(_label.Length + 1, Math.Max(0, rowBounds.Width));
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

