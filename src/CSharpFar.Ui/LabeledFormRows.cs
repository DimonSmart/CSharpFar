using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Reusable one-line form input with an inline label.</summary>
public sealed class LabeledTextInputRow : FormRow, IFormFocusTarget, IFormCursorProvider, IFormCompositeOwner, IFormLabeledRow
{
    private readonly string _label;
    private readonly int? _inputWidth;
    private readonly FormTextInputField _field;
    private readonly TextField? _focusTarget;
    private readonly IFormCompositeController _compositeController;

    internal LabeledTextInputRow(string label, CommandLineState buffer, SingleLineTextHistoryState? history = null,
        int? inputWidth = null, bool maskInput = false)
    {
        _label = label;
        _inputWidth = inputWidth;
        _field = new FormTextInputField(buffer, history, maskInput);
        _compositeController = new TextInputCompositeController(_field, GetInputBounds);
    }

    internal LabeledTextInputRow(string label, TextField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _focusTarget = field;
        _label = label;
        _inputWidth = field.Width;
        _field = field.Input;
        Id = field.Id;
        SubmitOnEnter = field.SubmitOnEnter;
        _compositeController = new TextInputCompositeController(_field, GetInputBounds);
    }

    internal override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    internal override IFormFocusTarget? FocusTarget => _focusTarget;
    internal override bool MovesFocusOnUnhandledEnter => !SubmitOnEnter;
    public bool Enabled { get => _field.Enabled; set => _field.Enabled = value; }
    public string? DisabledReason { get => _field.DisabledReason; set => _field.DisabledReason = value; }
    internal override bool IsEnabled => Enabled;
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;
    internal CommandLineState Buffer => _field.Buffer;
    internal FormTextInputField Input => _field;
    IFormCompositeController IFormCompositeOwner.CompositeController => _compositeController;
    internal Rect GetInputBounds(Rect rowBounds) => new(rowBounds.X, rowBounds.Y, Math.Max(0, _inputWidth ?? rowBounds.Width), rowBounds.Height);

    internal override void Render(FormRowRenderContext context)
    {
        FormRowLayout layout = context.Layout;
        if (layout.LabelBounds is Rect labelBounds)
            context.Canvas.Write(labelBounds.X, labelBounds.Y, ScrollableFormDialog.Fit(_label, labelBounds.Width),
                DisabledFormControlPresentation.Style(Enabled, context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill));
        _field.Render(context, GetInputBounds(layout));
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor) => _field.TryGetCursor(context, GetInputBounds(context.Layout), out cursor);
    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        FormInputResult result = _field.HandleKey(key, context);
        return result.Kind == FormInputResultKind.OverlayChanged && SubmitOnEnter
            ? FormInputResult.Submit()
            : result;
    }
    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        Rect bounds = GetInputBounds(context.Layout);
        return _field.IsHistoryArrow(mouse, bounds) ? FormInputResult.NotHandled : _field.HandleMouse(mouse, context, bounds);
    }

    private Rect GetInputBounds(FormRowLayout layout) => new(
        layout.ControlBounds.X,
        layout.ControlBounds.Y,
        Math.Min(layout.ControlBounds.Width, _inputWidth ?? layout.ControlBounds.Width),
        layout.ControlBounds.Height);

}

/// <summary>Reusable non-focusable inline label/value row.</summary>
public sealed class LabeledValueRow : FormRow, IFormLabeledRow
{
    private readonly string _label;
    private readonly Func<string> _value;

    internal LabeledValueRow(string label, Func<string> value)
    {
        _label = label;
        _value = value;
    }

    internal override bool IsFocusable => false;
    int IFormLabeledRow.DesiredLabelWidth => ConsoleTextMetrics.GetCellWidth(_label);
    bool IFormLabeledRow.UseSharedLabelColumn => true;

    internal override void Render(FormRowRenderContext context)
    {
        if (context.Layout.LabelBounds is Rect labelBounds)
            context.Canvas.Write(labelBounds.X, labelBounds.Y, ScrollableFormDialog.Fit(_label, labelBounds.Width), FarDialogStyles.Fill);
        context.Canvas.Write(context.Layout.ControlBounds.X, context.Layout.ControlBounds.Y,
            ScrollableFormDialog.Fit(_value() ?? string.Empty, context.Layout.ControlBounds.Width), FarDialogStyles.Fill);
    }
}
