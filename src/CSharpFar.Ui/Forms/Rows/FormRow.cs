using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

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
    internal virtual bool MovesFocusOnUnhandledEnter => false;
    internal virtual IFormFocusTarget? FocusTarget => null;
    internal virtual void CollectTextFields(ISet<TextField> fields) { }
    internal virtual int Height => 1;
    internal virtual int DesiredWidth => 0;
    internal abstract void Render(FormRowRenderContext context);
    internal virtual FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context) => FormInputResult.NotHandled;
    internal virtual FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => FormInputResult.NotHandled;
}

public sealed class LabelRow : FormRow
{
    private readonly string _text;
    private readonly CellStyle _style;
    private readonly TextAlignment _alignment;

    internal LabelRow(string text, TextAlignment alignment = TextAlignment.Left) : this(text, FarDialogStyles.Fill, alignment) { }

    internal LabelRow(string text, CellStyle style, TextAlignment alignment = TextAlignment.Left)
    {
        _text = text;
        _style = style;
        _alignment = alignment;
    }

    internal override bool IsFocusable => false;
    internal override int DesiredWidth => ConsoleTextMetrics.GetCellWidth(_text);

    internal override void Render(FormRowRenderContext context)
    {
        string text = ScrollableFormDialog.Fit(_text, context.Bounds.Width);
        int padding = Math.Max(0, context.Bounds.Width - ConsoleTextMetrics.GetCellWidth(text));
        int left = _alignment switch { TextAlignment.Center => padding / 2, TextAlignment.Right => padding, _ => 0 };
        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, new string(' ', left) + text, _style);
    }
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
