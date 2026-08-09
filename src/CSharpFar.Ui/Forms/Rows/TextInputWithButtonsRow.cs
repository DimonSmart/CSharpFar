using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputWithButtonsRow : FormRow, IFormFocusTarget, IFormCursorProvider, IFormCompositeOwner, IFormLabeledRow
{
    internal override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    internal override IFormFocusTarget FocusTarget => _focusTarget;

    private readonly string _label;
    private readonly FormTextInputField _field;
    private readonly TextField _focusTarget;
    private readonly DialogButtonBar _buttonBar;
    private DialogButtonBarState _buttonState;
    private readonly int? _inputWidth;
    private readonly IFormCompositeController _compositeController;

    internal TextInputWithButtonsRow(
        string label,
        TextField field,
        IReadOnlyList<DialogButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(field);
        _focusTarget = field;
        _label = label;
        _field = field.Input;
        _buttonBar = new DialogButtonBar(buttons);
        _buttonState = _buttonBar.CreateState();
        _inputWidth = field.Width;
        Id = field.Id;
        SubmitOnEnter = field.SubmitOnEnter;
        _compositeController = new TextInputCompositeController(_field, layout => CalculateLayout(layout).InputBounds);
    }

    internal CommandLineState Buffer => _field.Buffer;
    public bool Enabled { get => _field.Enabled; set => _field.Enabled = value; }
    public string? DisabledReason { get => _field.DisabledReason; set => _field.DisabledReason = value; }
    internal override bool IsEnabled => Enabled;
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;
    internal FormTextInputField Input => _field;
    IFormCompositeController IFormCompositeOwner.CompositeController => _compositeController;

    internal override void Render(FormRowRenderContext context)
    {
        var layout = CalculateLayout(context.Layout);
        int labelWidth = context.Layout.LabelBounds?.Width ?? 0;
        context.Canvas.Write(
            context.Bounds.X,
            context.Bounds.Y,
            ScrollableFormDialog.Fit(!Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label, labelWidth),
            DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Fill));

        _field.Render(context, layout.InputBounds);

        if (layout.ButtonAreaBounds.Width > 0)
        {
            _buttonBar.Render(
                context.Canvas,
                layout.ButtonLayout,
                _buttonState,
                context.Focused && Enabled,
                Enabled ? null : new DialogButtonBarStyle(
                    FarDialogStyles.DisabledControl(FarDialogStyles.Fill),
                    FarDialogStyles.DisabledControl(FarDialogStyles.Fill),
                    FarDialogStyles.DisabledControl(FarDialogStyles.Fill)));
        }
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Layout);
        return _field.TryGetCursor(context, layout.InputBounds, out cursor);
    }

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        FormInputResult result = _field.HandleKey(key, context);
        return result.Kind == FormInputResultKind.OverlayChanged && SubmitOnEnter
            ? FormInputResult.Submit()
            : result;
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled)
            return FormInputResult.NotHandled;
        var layout = CalculateLayout(context.Layout);
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

        return _field.IsHistoryArrow(mouse, layout.InputBounds)
            ? FormInputResult.NotHandled
            : _field.HandleMouse(mouse, context, layout.InputBounds);
    }


    private TextInputWithButtonsLayout CalculateLayout(FormRowLayout rowLayout)
    {
        Rect available = rowLayout.ControlBounds;
        int buttonWidth = Math.Min(_buttonBar.DesiredWidth, Math.Max(0, available.Width));
        int gap = buttonWidth > 0 && available.Width > buttonWidth ? 1 : 0;
        int inputAvailable = Math.Max(0, available.Width - buttonWidth - gap);
        int inputWidth = Math.Min(_inputWidth ?? inputAvailable, inputAvailable);
        var inputBounds = new Rect(available.X, available.Y, inputWidth, 1);
        int buttonX = available.Right - buttonWidth;
        var buttonBounds = new Rect(buttonX, available.Y, buttonWidth, 1);
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

