using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class FormFooter
{
    public static IReadOnlyList<FormRow> ErrorAndButtons(Func<string?> error, ButtonRow buttons)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(buttons);
        return [new DynamicLabelRow(error, FarDialogStyles.Error), buttons];
    }

    private sealed class DynamicLabelRow(Func<string?> text, CellStyle style) : FormRow
    {
        internal override bool IsFocusable => false;

        internal override void Render(FormRowRenderContext context) =>
            context.Canvas.Write(
                context.Bounds.X,
                context.Bounds.Y,
                ScrollableFormDialog.Fit(text() ?? string.Empty, context.Bounds.Width),
                style);
    }
}
