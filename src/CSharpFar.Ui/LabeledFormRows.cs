using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Reusable one-line form input with an inline label.</summary>
public sealed class LabeledTextInputRow : FormRow, IFormCursorProvider, IFormCompositeRow, IFormLabeledRow, IFormLabelWidthOverride
{
    private readonly string _label;
    private readonly int? _inputWidth;
    private readonly int? _labelWidthOverride;
    private readonly FormTextInputField _field;

    internal LabeledTextInputRow(string label, CommandLineState buffer, SingleLineTextHistoryState? history = null,
        int? labelWidth = null, int? inputWidth = null, bool maskInput = false)
    {
        _label = label;
        _labelWidthOverride = labelWidth;
        _inputWidth = inputWidth;
        _field = new FormTextInputField(buffer, history, maskInput);
    }

    public LabeledTextInputRow(string label, TextField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _label = label;
        _inputWidth = field.Width;
        _field = field.Input;
        Id = field.Id;
        SubmitOnEnter = field.SubmitOnEnter;
    }

    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public bool Enabled { get => _field.Enabled; set => _field.Enabled = value; }
    public string? DisabledReason { get => _field.DisabledReason; set => _field.DisabledReason = value; }
    public override bool IsEnabled => Enabled;
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;
    int? IFormLabelWidthOverride.LabelWidthOverride => _labelWidthOverride;
    public CommandLineState Buffer => _field.Buffer;
    internal FormTextInputField Input => _field;
    public bool IsCompositeOpen => Enabled && _field.History?.IsDropdownOpen == true;
    public Rect GetInputBounds(Rect rowBounds) => new(rowBounds.X, rowBounds.Y, Math.Max(0, _inputWidth ?? rowBounds.Width), rowBounds.Height);

    public override void Render(FormRowRenderContext context)
    {
        FormRowLayout layout = context.Layout;
        string label = !Enabled ? DisabledFormControlPresentation.WithReason(_label, DisabledReason) : _label;
        if (layout.LabelBounds is Rect labelBounds)
            context.Canvas.Write(labelBounds.X, labelBounds.Y, ScrollableFormDialog.Fit(label, labelBounds.Width),
                DisabledFormControlPresentation.Style(Enabled, context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill));
        _field.Render(context, layout.ControlBounds);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor) => _field.TryGetCursor(context, context.Layout.ControlBounds, out cursor);
    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        FormInputResult result = _field.HandleKey(key, context);
        return result.Kind == FormInputResultKind.OverlayChanged && SubmitOnEnter
            ? FormInputResult.Submit()
            : result;
    }
    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => _field.HandleMouse(mouse, context, context.Layout.ControlBounds);
    public FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context) => _field.BuildCompositeFrame(context.Layout.ControlBounds, context.Viewport);
    public void CommitCompositeFrame(FormCompositeFrame frame) { }
    public void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame) => _field.RenderCompositeOverlay(context, frame);
    public FormInputResult HandleCompositeKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame) => HandleKey(key, context);
    public FormInputResult HandleCompositeMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, string? childTargetId) => _field.HandleCompositeMouse(mouse, context, context.Layout.ControlBounds, frame, childTargetId);
    public bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) => _field.IsHistoryArrow(mouse, context.Layout.ControlBounds);
    public void CloseComposite() => _field.History?.Close();

}

/// <summary>Reusable non-focusable inline label/value row.</summary>
public sealed class LabeledValueRow : FormRow, IFormLabeledRow
{
    private readonly string _label;
    private readonly Func<string> _value;

    public LabeledValueRow(string label, Func<string> value)
    {
        _label = label;
        _value = value;
    }

    internal LabeledValueRow(string label, Func<string> value, int labelWidth)
        : this(label, value)
    {
        _ = labelWidth;
    }

    public override bool IsFocusable => false;
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;

    public override void Render(FormRowRenderContext context)
    {
        if (context.Layout.LabelBounds is Rect labelBounds)
            context.Canvas.Write(labelBounds.X, labelBounds.Y, ScrollableFormDialog.Fit(_label, labelBounds.Width), FarDialogStyles.Fill);
        context.Canvas.Write(context.Layout.ControlBounds.X, context.Layout.ControlBounds.Y,
            ScrollableFormDialog.Fit(_value() ?? string.Empty, context.Layout.ControlBounds.Width), FarDialogStyles.Fill);
    }
}
