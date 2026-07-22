using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Shared interaction mechanics for a single-line form text field.</summary>
internal sealed class FormTextInputField
{
    private readonly CommandLineState _buffer;
    private readonly SingleLineTextHistoryState? _history;
    private readonly TextInputRowState _state;
    private readonly bool _maskInput;

    public FormTextInputField(CommandLineState buffer, SingleLineTextHistoryState? history, TextInputRowState state, bool maskInput = false)
    {
        _buffer = buffer;
        _history = history;
        _state = state;
        _maskInput = maskInput;
    }

    public CommandLineState Buffer => _buffer;
    public SingleLineTextHistoryState? History => _history;
    public TextInputRowState State => _state;

    public void Render(FormRowRenderContext context, Rect bounds) =>
        SingleLineTextInput.Render(context.Canvas, bounds.X, bounds.Y, bounds.Width, _buffer,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input, _history, maskInput: _maskInput, renderDropdown: false);

    public void RenderOverlay(FormRowRenderContext context, Rect bounds)
    {
        if (_history is not null && context.Focused)
            SingleLineTextInput.RenderHistoryDropdown(context.Canvas, bounds.X, bounds.Y, bounds.Width, _history, context.CanvasHeight);
    }

    public bool TryGetCursor(FormRowRenderContext context, Rect bounds, out FormCursorPlacement cursor)
    {
        int textWidth = _history is null ? bounds.Width : Math.Max(1, bounds.Width - 1);
        cursor = new FormCursorPlacement(Math.Min(bounds.Right - 1, SingleLineTextInput.GetCursorX(bounds.X, textWidth, _buffer)), bounds.Y);
        return context.Focused && bounds.Width > 0;
    }

    public FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
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

    public FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, Rect bounds)
    {
        string before = _buffer.Text;
        if (_history is not null && SingleLineTextInput.TryHandleHistoryDropdownMouse(_history, _buffer, mouse, bounds.X, bounds.Y, bounds.Width, context.ScreenHeight, ref _state.HistoryScrollbarDrag))
            return _buffer.Text != before ? FormInputResult.ValueChanged : FormInputResult.Handled;

        if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down } || !bounds.Contains(mouse.X, mouse.Y))
            return FormInputResult.NotHandled;

        if (_history is not null && SingleLineTextInput.IsHistoryArrowHit(bounds.X, bounds.Width, bounds.Y, mouse.X, mouse.Y))
        {
            if (_history.IsDropdownOpen)
            {
                _history.Close();
                _state.HistoryScrollbarDrag = null;
                return FormInputResult.Handled;
            }
            return SingleLineTextInput.TryOpenHistoryDropdown(_history, bounds.Y, context.ScreenHeight) ? FormInputResult.Handled : FormInputResult.NotHandled;
        }

        int textWidth = _history is null ? bounds.Width : Math.Max(1, bounds.Width - 1);
        int visibleStart = Math.Max(0, _buffer.CursorPosition - Math.Max(0, textWidth - 1));
        int target = Math.Clamp(visibleStart + mouse.X - bounds.X, 0, _buffer.Text.Length);
        _buffer.MoveCursor(target - _buffer.CursorPosition);
        return FormInputResult.Handled;
    }

    public bool IsHistoryArrow(MouseConsoleInputEvent mouse, Rect bounds) =>
        _history is not null && SingleLineTextInput.IsHistoryArrowHit(bounds.X, bounds.Width, bounds.Y, mouse.X, mouse.Y);
}
