using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public abstract class FormRow
{
    internal FormRow()
    {
    }

    public virtual string? Id { get; init; }
    internal virtual FormRowRole Role { get; init; } = FormRowRole.Normal;
    internal virtual bool SubmitOnEnter { get; init; }
    internal virtual bool IsEnabled => true;
    internal virtual bool IsFocusable => IsEnabled;
    internal virtual int Height => 1;
    internal abstract void Render(FormRowRenderContext context);
    internal virtual FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) => FormInputResult.NotHandled;
    internal virtual FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => FormInputResult.NotHandled;
}

public sealed class LabelRow : FormRow
{
    private readonly string _text;
    private readonly CellStyle _style;

    internal LabelRow(string text) : this(text, FarDialogStyles.Fill) { }

    internal LabelRow(string text, CellStyle style)
    {
        _text = text;
        _style = style;
    }

    internal override bool IsFocusable => false;

    internal override void Render(FormRowRenderContext context) =>
        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_text, context.Bounds.Width), _style);
}

public sealed class SeparatorRow : FormRow
{
    private readonly CellStyle _style;

    internal SeparatorRow() : this(FarDialogStyles.Fill) { }

    internal SeparatorRow(CellStyle style)
    {
        _style = style;
    }

    internal override bool IsFocusable => false;

    internal override void Render(FormRowRenderContext context)
    {
        if (context.Bounds.Width <= 0)
            return;

        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, new string('─', context.Bounds.Width), _style);
    }
}

public sealed class SpacerRow : FormRow
{
    private readonly CellStyle _style;

    internal SpacerRow(int height = 1) : this(FarDialogStyles.Fill, height) { }

    internal SpacerRow(CellStyle style, int height = 1)
    {
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        _style = style;
        Height = height;
    }

    internal override bool IsFocusable => false;
    internal override int Height { get; }

    internal override void Render(FormRowRenderContext context) =>
        context.Canvas.FillRegion(context.Bounds, _style);
}
