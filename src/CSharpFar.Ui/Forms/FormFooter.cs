using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class FormFooter
{
    internal static FormRow Error(Func<string?> error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new DynamicLabelRow(error, FarDialogStyles.Error);
    }

    public static IReadOnlyList<FormRow> ErrorAndButtons(Func<string?> error, ButtonRow buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        return [Error(error), buttons];
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
