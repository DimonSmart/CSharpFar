using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TextInputRow : FormRow, IFormCursorProvider, IFormCompositeOwner
{
    private readonly FormTextInputField _field;
    private readonly int? _width;
    private readonly IFormCompositeController _compositeController;

    public TextInputRow(TextField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field.Input;
        _width = field.Width;
        Id = field.Id;
        SubmitOnEnter = field.SubmitOnEnter;
        _compositeController = new TextInputCompositeController(_field, layout => GetInputBounds(layout.RowBounds));
    }

    internal TextInputRow(
        CommandLineState buffer,
        SingleLineTextHistoryState? history = null,
        int? width = null,
        bool maskInput = false)
    {
        _field = new FormTextInputField(buffer, history, maskInput);
        _width = width;
        _compositeController = new TextInputCompositeController(_field, layout => GetInputBounds(layout.RowBounds));
    }

    public CommandLineState Buffer => _field.Buffer;
    internal FormTextInputField Input => _field;
    public override FormRowRole Role { get; init; } = FormRowRole.TextInput;
    public bool Enabled { get => _field.Enabled; set => _field.Enabled = value; }
    public string? DisabledReason { get => _field.DisabledReason; set => _field.DisabledReason = value; }
    public override bool IsEnabled => Enabled;
    public int? Width => _width;
    public void CloseHistory() => _compositeController.Close();
    IFormCompositeController IFormCompositeOwner.CompositeController => _compositeController;

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


    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        FormInputResult result = _field.HandleKey(key, context);
        return result.Kind == FormInputResultKind.OverlayChanged && SubmitOnEnter
            ? FormInputResult.Submit()
            : result;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        Rect bounds = GetInputBounds(context.Bounds);
        return _field.IsHistoryArrow(mouse, bounds)
            ? FormInputResult.NotHandled
            : _field.HandleMouse(mouse, context, bounds);
    }
}
