using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public abstract class FormRow : IFormRow
{
    public virtual string? Id { get; init; }
    public virtual FormRowRole Role { get; init; } = FormRowRole.Normal;
    public virtual bool SubmitOnEnter { get; init; }
    public virtual bool IsEnabled => true;
    public virtual bool IsFocusable => IsEnabled;
    public virtual int Height => 1;
    public abstract void Render(FormRowRenderContext context);
    public virtual FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) => FormInputResult.NotHandled;
    public virtual FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => FormInputResult.NotHandled;
}

public sealed class LabelRow : FormRow
{
    private readonly string _text;
    private readonly CellStyle _style;

    public LabelRow(string text) : this(text, FarDialogStyles.Fill) { }

    public LabelRow(string text, CellStyle style)
    {
        _text = text;
        _style = style;
    }

    public override bool IsFocusable => false;

    public override void Render(FormRowRenderContext context) =>
        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_text, context.Bounds.Width), _style);
}

public sealed class SeparatorRow : FormRow
{
    private readonly CellStyle _style;

    public SeparatorRow() : this(FarDialogStyles.Fill) { }

    public SeparatorRow(CellStyle style)
    {
        _style = style;
    }

    public override bool IsFocusable => false;

    public override void Render(FormRowRenderContext context)
    {
        if (context.Bounds.Width <= 0)
            return;

        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, new string('─', context.Bounds.Width), _style);
    }
}

public sealed class SpacerRow : FormRow
{
    private readonly CellStyle _style;

    public SpacerRow(int height = 1) : this(FarDialogStyles.Fill, height) { }

    public SpacerRow(CellStyle style, int height = 1)
    {
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        _style = style;
        Height = height;
    }

    public override bool IsFocusable => false;
    public override int Height { get; }

    public override void Render(FormRowRenderContext context) =>
        context.Canvas.FillRegion(context.Bounds, _style);
}

