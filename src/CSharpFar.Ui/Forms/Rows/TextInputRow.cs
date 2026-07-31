using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputRow : FormRow, IFormCursorProvider, IFormCompositeRow
{
    private readonly FormTextInputField _field;
    private readonly int? _width;

    public TextInputRow(
        CommandLineState buffer,
        SingleLineTextHistoryState? history = null,
        int? width = null,
        bool maskInput = false)
    {
        _field = new FormTextInputField(buffer, history, maskInput);
        _width = width;
    }

    public CommandLineState Buffer => _field.Buffer;
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public int? Width => _width;
    public bool IsCompositeOpen => _field.History?.IsDropdownOpen == true;

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

    public FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context) => _field.BuildCompositeFrame(GetInputBounds(context.RowBounds), context.Viewport);
    public void CommitCompositeFrame(FormCompositeFrame frame) { }
    public void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame) => _field.RenderCompositeOverlay(context, frame);
    public FormInputResult HandleCompositeKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame) => HandleKey(key, context);
    public FormInputResult HandleCompositeMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, string? childTargetId) => _field.HandleCompositeMouse(mouse, context, GetInputBounds(context.Bounds), frame, childTargetId);
    public bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame) => _field.IsHistoryArrow(mouse, GetInputBounds(context.Bounds));
    public void CloseComposite() => _field.History?.Close();

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        FormInputResult result = _field.HandleKey(key, context);
        return result.Kind == FormInputResultKind.OverlayChanged && SubmitOnEnter
            ? FormInputResult.Submit()
            : result;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        return _field.HandleMouse(mouse, context, GetInputBounds(context.Bounds));
    }
}
