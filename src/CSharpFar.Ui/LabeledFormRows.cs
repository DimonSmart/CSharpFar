using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Reusable one-line form input with an inline label.</summary>
public sealed class LabeledTextInputRow : FormRow, IFormCursorProvider, IFormCompositeRow
{
    private readonly string _label;
    private readonly int _labelWidth;
    private readonly int? _inputWidth;
    private readonly FormTextInputField _field;

    public LabeledTextInputRow(string label, CommandLineState buffer, SingleLineTextHistoryState? history = null,
        TextInputRowState? state = null, int labelWidth = 22, int? inputWidth = null, bool maskInput = false)
    {
        _label = label;
        _labelWidth = labelWidth;
        _inputWidth = inputWidth;
        _field = new FormTextInputField(buffer, history, state ?? new TextInputRowState(), maskInput);
    }

    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public CommandLineState Buffer => _field.Buffer;
    public bool IsCompositeOpen => _field.History?.IsDropdownOpen == true;
    public Rect GetInputBounds(Rect rowBounds) => Layout(rowBounds).InputBounds;

    public override void Render(FormRowRenderContext context)
    {
        var layout = Layout(context.Bounds);
        context.Canvas.Write(layout.LabelBounds.X, layout.LabelBounds.Y, ScrollableFormDialog.Fit(_label, layout.LabelBounds.Width), context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        _field.Render(context, layout.InputBounds);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor) => _field.TryGetCursor(context, GetInputBounds(context.Bounds), out cursor);
    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) => _field.HandleKey(key, context);
    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => _field.HandleMouse(mouse, context, GetInputBounds(context.Bounds));
    public FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context) => _field.BuildCompositeFrame(GetInputBounds(context.RowBounds), context.Viewport);
    public void CommitCompositeFrame(FormCompositeFrame frame) { }
    public void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame) => _field.RenderCompositeOverlay(context, frame);
    public FormInputResult HandleCompositeKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame) => HandleKey(key, context);
    public FormInputResult HandleCompositeMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, string? childTargetId) => _field.HandleCompositeMouse(mouse, context, GetInputBounds(context.Bounds), frame, childTargetId);
    public bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) => _field.IsHistoryArrow(mouse, GetInputBounds(context.Bounds));
    public void CloseComposite() => _field.History?.Close();

    private (Rect LabelBounds, Rect InputBounds) Layout(Rect bounds)
    {
        int labelWidth = Math.Min(Math.Max(0, _labelWidth), bounds.Width);
        int inputWidth = Math.Min(Math.Max(0, _inputWidth ?? bounds.Width - labelWidth), Math.Max(0, bounds.Width - labelWidth));
        return (new Rect(bounds.X, bounds.Y, labelWidth, bounds.Height), new Rect(bounds.X + labelWidth, bounds.Y, inputWidth, bounds.Height));
    }
}

/// <summary>Reusable non-focusable inline label/value row.</summary>
public sealed class LabeledValueRow : FormRow
{
    private readonly string _label;
    private readonly Func<string> _value;
    private readonly int _labelWidth;

    public LabeledValueRow(string label, Func<string> value, int labelWidth = 22)
    {
        _label = label;
        _value = value;
        _labelWidth = labelWidth;
    }

    public override bool IsFocusable => false;

    public override void Render(FormRowRenderContext context)
    {
        int labelWidth = Math.Min(Math.Max(0, _labelWidth), context.Bounds.Width);
        int valueWidth = Math.Max(0, context.Bounds.Width - labelWidth);
        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_label, labelWidth), FarDialogStyles.Fill);
        context.Canvas.Write(context.Bounds.X + labelWidth, context.Bounds.Y, ScrollableFormDialog.Fit(_value() ?? string.Empty, valueWidth), FarDialogStyles.Fill);
    }
}
