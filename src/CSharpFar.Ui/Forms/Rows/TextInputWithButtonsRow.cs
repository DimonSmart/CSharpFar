using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputWithButtonsRow : FormRow, IFormCursorProvider, IFormCompositeRow
{
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;

    private readonly string _label;
    private readonly FormTextInputField _field;
    private readonly DialogButtonBar _buttonBar;
    private DialogButtonBarState _buttonState;
    private readonly int? _inputWidth;
    private readonly int _buttonAreaWidth;

    public TextInputWithButtonsRow(
        string label,
        TextField field,
        IReadOnlyList<DialogButton> buttons,
        int buttonAreaWidth)
    {
        ArgumentNullException.ThrowIfNull(field);
        _label = label;
        _field = field.Input;
        _buttonBar = new DialogButtonBar(buttons);
        _buttonState = _buttonBar.CreateState();
        _inputWidth = field.Width;
        _buttonAreaWidth = buttonAreaWidth;
        Id = field.Id;
        SubmitOnEnter = field.SubmitOnEnter;
    }

    public CommandLineState Buffer => _field.Buffer;
    internal FormTextInputField Input => _field;
    public bool IsCompositeOpen => _field.History?.IsDropdownOpen == true;

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
        FormInputResult result = _field.HandleKey(key, context);
        return result.Kind == FormInputResultKind.OverlayChanged && SubmitOnEnter
            ? FormInputResult.Submit()
            : result;
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
                    buttonResult.ButtonId,
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

    public FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context) =>
        _field.BuildCompositeFrame(CalculateLayout(context.RowBounds).InputBounds, context.Viewport);

    public void CommitCompositeFrame(FormCompositeFrame frame) { }

    public void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame) =>
        _field.RenderCompositeOverlay(context, frame);

    public FormInputResult HandleCompositeKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame) =>
        HandleKey(key, context);

    public FormInputResult HandleCompositeMouse(
        MouseConsoleInputEvent mouse,
        FormRowMouseContext context,
        FormCompositeFrame frame,
        string? childTargetId) =>
        _field.HandleCompositeMouse(mouse, context, CalculateLayout(context.Bounds).InputBounds, frame, childTargetId);

    public bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) =>
        _field.IsHistoryArrow(mouse, CalculateLayout(context.Bounds).InputBounds);

    public void CloseComposite() => _field.History?.Close();

    private TextInputWithButtonsLayout CalculateLayout(Rect rowBounds)
    {
        int labelWidth = Math.Min(ConsoleTextMetrics.GetCellWidth(_label) + 1, Math.Max(0, rowBounds.Width));
        int inputX = rowBounds.X + labelWidth;
        int remainingAfterLabel = Math.Max(0, rowBounds.Width - labelWidth);
        int inputWidth = Math.Min(_inputWidth ?? remainingAfterLabel, remainingAfterLabel);
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

