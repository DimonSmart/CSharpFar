using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputWithButtonsRow : FormRow, IFormCursorProvider, IFormCompositeRow, IFormLabeledRow
{
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;

    private readonly string _label;
    private readonly FormTextInputField _field;
    private readonly DialogButtonBar _buttonBar;
    private DialogButtonBarState _buttonState;
    private readonly int? _inputWidth;
    private readonly int? _legacyButtonAreaWidth;

    public TextInputWithButtonsRow(
        string label,
        TextField field,
        IReadOnlyList<DialogButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(field);
        _label = label;
        _field = field.Input;
        _buttonBar = new DialogButtonBar(buttons);
        _buttonState = _buttonBar.CreateState();
        _inputWidth = field.Width;
        Id = field.Id;
        SubmitOnEnter = field.SubmitOnEnter;
    }

    internal TextInputWithButtonsRow(
        string label,
        TextField field,
        IReadOnlyList<DialogButton> buttons,
        int buttonAreaWidth)
        : this(label, field, buttons)
    {
        _legacyButtonAreaWidth = buttonAreaWidth;
    }

    public CommandLineState Buffer => _field.Buffer;
    public bool Enabled { get => _field.Enabled; set => _field.Enabled = value; }
    public string? DisabledReason { get => _field.DisabledReason; set => _field.DisabledReason = value; }
    public override bool IsEnabled => Enabled;
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;
    internal FormTextInputField Input => _field;
    public bool IsCompositeOpen => Enabled && _field.History?.IsDropdownOpen == true;

    public override void Render(FormRowRenderContext context)
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

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = CalculateLayout(context.Layout);
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

        return _field.HandleMouse(mouse, context, layout.InputBounds);
    }

    public FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context) =>
        _field.BuildCompositeFrame(CalculateLayout(context.Layout).InputBounds, context.Viewport);

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
        _field.HandleCompositeMouse(mouse, context, CalculateLayout(context.Layout).InputBounds, frame, childTargetId);

    public bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) =>
        _field.IsHistoryArrow(mouse, CalculateLayout(context.Layout).InputBounds);

    public void CloseComposite() => _field.History?.Close();

    private TextInputWithButtonsLayout CalculateLayout(FormRowLayout rowLayout)
    {
        Rect available = rowLayout.ControlBounds;
        int buttonWidth = Math.Min(_legacyButtonAreaWidth ?? _buttonBar.DesiredWidth, Math.Max(0, available.Width));
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

