using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Reusable one-line form input with an inline label.</summary>
public sealed class LabeledTextInputRow : FormRow, IFormOverlayRow, IFormCursorProvider, IFormHistoryRow
{
    private readonly string _label;
    private readonly CommandLineState _buffer;
    private readonly SingleLineTextHistoryState? _history;
    private readonly TextInputRowState _state;
    private readonly int _labelWidth;
    private readonly int? _inputWidth;
    private readonly bool _maskInput;

    public LabeledTextInputRow(
        string label,
        CommandLineState buffer,
        SingleLineTextHistoryState? history = null,
        TextInputRowState? state = null,
        int labelWidth = 22,
        int? inputWidth = null,
        bool maskInput = false)
    {
        _label = label;
        _buffer = buffer;
        _history = history;
        _state = state ?? new TextInputRowState();
        _labelWidth = labelWidth;
        _inputWidth = inputWidth;
        _maskInput = maskInput;
    }

    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public CommandLineState Buffer => _buffer;
    public SingleLineTextHistoryState? History => _history;
    public TextInputRowState State => _state;

    public Rect GetInputBounds(Rect rowBounds) => Layout(rowBounds).InputBounds;

    public override void Render(FormRowRenderContext context)
    {
        var layout = Layout(context.Bounds);
        context.Screen.Write(layout.LabelBounds.X, layout.LabelBounds.Y, ScrollableFormDialog.Fit(_label, layout.LabelBounds.Width).PadRight(layout.LabelBounds.Width), context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        SingleLineTextInput.Render(context.Screen, layout.InputBounds.X, layout.InputBounds.Y, layout.InputBounds.Width, _buffer, context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input, FarDialogStyles.Input, _history, maskInput: _maskInput, renderDropdown: false);
    }

    public void RenderOverlay(FormRowRenderContext context)
    {
        if (_history is null || !context.Focused)
            return;

        var layout = Layout(context.Bounds);
        SingleLineTextInput.RenderHistoryDropdown(context.Screen, layout.InputBounds.X, layout.InputBounds.Y, layout.InputBounds.Width, _history, context.ScreenHeight);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        var layout = Layout(context.Bounds);
        int textWidth = _history is null ? layout.InputBounds.Width : Math.Max(1, layout.InputBounds.Width - 1);
        cursor = new FormCursorPlacement(Math.Min(layout.InputBounds.Right - 1, SingleLineTextInput.GetCursorX(layout.InputBounds.X, textWidth, _buffer)), layout.InputBounds.Y);
        return context.Focused && layout.InputBounds.Width > 0;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        string? error = null;
        string before = _buffer.Text;
        TextInputKeyResult result = SingleLineTextInput.HandleKey(_buffer, key, ref error, _history, context.AvailableDropdownContentRows);
        return result switch
        {
            TextInputKeyResult.TextChanged when _buffer.Text != before => FormInputResult.ValueChanged,
            TextInputKeyResult.TextChanged or TextInputKeyResult.Handled => FormInputResult.Handled,
            _ => FormInputResult.NotHandled,
        };
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        var layout = Layout(context.Bounds);
        string before = _buffer.Text;
        if (_history is not null && SingleLineTextInput.TryHandleHistoryDropdownMouse(_history, _buffer, mouse, layout.InputBounds.X, layout.InputBounds.Y, layout.InputBounds.Width, context.ScreenHeight, ref _state.HistoryScrollbarDrag))
            return _buffer.Text != before ? FormInputResult.ValueChanged : FormInputResult.Handled;

        if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down } || !layout.InputBounds.Contains(mouse.X, mouse.Y))
            return FormInputResult.NotHandled;

        if (_history is not null && SingleLineTextInput.IsHistoryArrowHit(layout.InputBounds.X, layout.InputBounds.Width, layout.InputBounds.Y, mouse.X, mouse.Y))
        {
            if (_history.IsDropdownOpen)
            {
                _history.Close();
                _state.HistoryScrollbarDrag = null;
                return FormInputResult.Handled;
            }

            return SingleLineTextInput.TryOpenHistoryDropdown(_history, layout.InputBounds.Y, context.ScreenHeight) ? FormInputResult.Handled : FormInputResult.NotHandled;
        }

        int textWidth = _history is null ? layout.InputBounds.Width : Math.Max(1, layout.InputBounds.Width - 1);
        int target = Math.Clamp(mouse.X - layout.InputBounds.X, 0, Math.Min(_buffer.Text.Length, textWidth));
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }

    public bool IsHistoryArrow(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        var layout = Layout(context.Bounds);
        return _history is not null && SingleLineTextInput.IsHistoryArrowHit(layout.InputBounds.X, layout.InputBounds.Width, layout.InputBounds.Y, mouse.X, mouse.Y);
    }

    private (Rect LabelBounds, Rect InputBounds) Layout(Rect bounds)
    {
        int labelWidth = Math.Min(Math.Max(0, _labelWidth), bounds.Width);
        int inputWidth = Math.Min(Math.Max(0, _inputWidth ?? bounds.Width - labelWidth), Math.Max(0, bounds.Width - labelWidth));
        return (new Rect(bounds.X, bounds.Y, labelWidth, bounds.Height), new Rect(bounds.X + labelWidth, bounds.Y, inputWidth, bounds.Height));
    }
}

/// <summary>Reusable focusable inline label/value row for state that cannot be edited.</summary>
public sealed class LabeledValueRow : FormRow, IFormCursorProvider
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

    public override void Render(FormRowRenderContext context)
    {
        int labelWidth = Math.Min(Math.Max(0, _labelWidth), context.Bounds.Width);
        int valueWidth = Math.Max(0, context.Bounds.Width - labelWidth);
        CellStyle style = context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill;
        context.Screen.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_label, labelWidth).PadRight(labelWidth), style);
        context.Screen.Write(context.Bounds.X + labelWidth, context.Bounds.Y, ScrollableFormDialog.Fit(_value(), valueWidth).PadRight(valueWidth), style);
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = new FormCursorPlacement(context.Bounds.X, context.Bounds.Y);
        return false;
    }
}
