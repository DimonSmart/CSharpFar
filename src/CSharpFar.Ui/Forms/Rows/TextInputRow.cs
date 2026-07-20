using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputRow : FormRow, IFormOverlayRow, IFormCursorProvider, IFormHistoryRow, IFormTransientOverlayRow
{
    private readonly FormTextInputField _field;
    private readonly int? _width;

    public TextInputRow(
        CommandLineState buffer,
        SingleLineTextHistoryState? history = null,
        TextInputRowState? state = null,
        int? width = null,
        bool maskInput = false)
    {
        _field = new FormTextInputField(buffer, history, state ?? new TextInputRowState(), maskInput);
        _width = width;
    }

    public CommandLineState Buffer => _field.Buffer;
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public SingleLineTextHistoryState? History => _field.History;
    public TextInputRowState State => _field.State;
    public int? Width => _width;
    public bool IsOverlayOpen => History?.IsDropdownOpen == true;

    public Rect GetInputBounds(Rect rowBounds) =>
        new(rowBounds.X, rowBounds.Y, Math.Min(rowBounds.Width, _width ?? rowBounds.Width), rowBounds.Height);

    public override void Render(FormRowRenderContext context)
    {
        _field.Render(context, GetInputBounds(context.Bounds));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        return _field.TryGetCursor(context, GetInputBounds(context.Bounds), out cursor);
    }

    public void RenderOverlay(FormRowRenderContext context)
    {
        _field.RenderOverlay(context, GetInputBounds(context.Bounds));
    }

    public bool IsHistoryArrow(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        return _field.IsHistoryArrow(mouse, GetInputBounds(context.Bounds));
    }

    public void CancelOverlay()
    {
        History?.Close();
        State.HistoryScrollbarDrag = null;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        return _field.HandleKey(key, context);
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        return _field.HandleMouse(mouse, context, GetInputBounds(context.Bounds));
    }
}

public sealed class TextInputRowState
{
    public ScrollBarDragState? HistoryScrollbarDrag;
}
