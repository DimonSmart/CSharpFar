using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Reusable one-line form input with an inline label.</summary>
public sealed class LabeledTextInputRow : FormRow, IFormOverlayRow, IFormCursorProvider, IFormHistoryRow, IFormTransientOverlayRow
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
    public SingleLineTextHistoryState? History => _field.History;
    public TextInputRowState State => _field.State;
    public bool IsOverlayOpen => History?.IsDropdownOpen == true;
    public Rect GetInputBounds(Rect rowBounds) => Layout(rowBounds).InputBounds;

    public override void Render(FormRowRenderContext context)
    {
        var layout = Layout(context.Bounds);
        context.Canvas.Write(layout.LabelBounds.X, layout.LabelBounds.Y, ScrollableFormDialog.Fit(_label, layout.LabelBounds.Width).PadRight(layout.LabelBounds.Width), context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        _field.Render(context, layout.InputBounds);
    }

    public void RenderOverlay(FormRowRenderContext context) => _field.RenderOverlay(context, GetInputBounds(context.Bounds));
    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor) => _field.TryGetCursor(context, GetInputBounds(context.Bounds), out cursor);
    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) => _field.HandleKey(key, context);
    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => _field.HandleMouse(mouse, context, GetInputBounds(context.Bounds));
    public bool IsHistoryArrow(MouseConsoleInputEvent mouse, FormRowMouseContext context) => _field.IsHistoryArrow(mouse, GetInputBounds(context.Bounds));
    public void CancelOverlay()
    {
        History?.Close();
    }

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
        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_label, labelWidth).PadRight(labelWidth), FarDialogStyles.Fill);
        context.Canvas.Write(context.Bounds.X + labelWidth, context.Bounds.Y, ScrollableFormDialog.Fit(_value() ?? string.Empty, valueWidth).PadRight(valueWidth), FarDialogStyles.Fill);
    }
}
