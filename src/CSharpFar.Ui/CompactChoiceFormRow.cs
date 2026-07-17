using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>A one-line labeled choice that keeps the compact FTP-style presentation.</summary>
public sealed class CompactChoiceFormRow<T> : FormRow, IFormCursorProvider
{
    private readonly ChoiceRow<T> _choice;
    private readonly string _label;

    public CompactChoiceFormRow(ChoiceRow<T> choice, string label)
    {
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        _label = label;
    }

    public override FormRowRole Role { get; init; } = FormRowRole.Option;
    public ChoiceRow<T> Choice => _choice;
    public T Value => _choice.Value;

    public override void Render(FormRowRenderContext context) =>
        _choice.Render(context.Screen, context.Bounds.X, context.Bounds.Y, context.Bounds.Width, _label, context.Focused);

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        if (!context.Focused || context.Bounds.Width <= 0)
        {
            cursor = default;
            return false;
        }

        int valueOffset = _label.Length + 2;
        cursor = new FormCursorPlacement(context.Bounds.X + Math.Min(context.Bounds.Width - 1, valueOffset), context.Bounds.Y);
        return true;
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        int before = _choice.SelectedIndex;
        bool isChoiceKey = key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Spacebar or ConsoleKey.Enter;
        if (!isChoiceKey)
            return FormInputResult.NotHandled;
        _choice.TryHandleKey(key);
        return _choice.SelectedIndex == before ? FormInputResult.Handled : FormInputResult.ValueChanged;
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        int before = _choice.SelectedIndex;
        var layout = new ChoiceRowLayout(
            ChoiceRowLayoutKind.Simple,
            [context.Bounds],
            []);
        if (!_choice.TryHandleMouse(mouse, layout))
            return FormInputResult.NotHandled;
        return _choice.SelectedIndex == before ? FormInputResult.Handled : FormInputResult.ValueChanged;
    }
}
