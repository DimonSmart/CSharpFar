using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class FormFooter
{
    internal static FormRow Error(Func<string?> error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new DynamicLabelRow(error, DialogStyles.Error);
    }

    public static IReadOnlyList<FormRow> ErrorAndButtons(Func<string?> error, ButtonRow buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        return [Error(error), buttons];
    }

    internal static FormRow ErrorLines(
        Func<IReadOnlyList<string>> lines,
        int height,
        ConsoleColor? foreground = null,
        bool marqueeFirstLine = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        return new DynamicErrorLinesRow(lines, height, foreground, marqueeFirstLine);
    }

    private sealed class DynamicErrorLinesRow(
        Func<IReadOnlyList<string>> lines,
        int height,
        ConsoleColor? foreground,
        bool marqueeFirstLine) : FormRow
    {
        internal override bool IsFocusable => false;
        internal override int Height => height;

        internal override void Render(FormRowRenderContext context)
        {
            if (context.Bounds.Width <= 0)
                return;

            IReadOnlyList<string> messages = lines();
            CellStyle style = foreground is { } color
                ? new CellStyle(color, DialogStyles.Fill.Background)
                : DialogStyles.Error;
            for (int i = 0; i < Math.Min(height, messages.Count); i++)
            {
                string message = messages[i];
                if (string.IsNullOrEmpty(message))
                    continue;

                Rect bounds = new(context.Bounds.X, context.Bounds.Y + i, context.Bounds.Width, 1);
                string displayed = marqueeFirstLine && i == 0 &&
                    ConsoleTextMetrics.GetCellWidth(message) > bounds.Width
                    ? context.RenderHoverMarquee(new HoverMarqueeRegistration((this, i), message, bounds, bounds.Width))
                    : ScrollableFormDialog.Fit(message, bounds.Width);
                context.Canvas.Write(bounds.X, bounds.Y, displayed, style);
            }
        }
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
