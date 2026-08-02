using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct ChoiceLayoutHitTarget(int Index, Rect Bounds, Rect MarkerBounds);

/// <summary>Immutable, presentation-specific geometry for a choice selection.</summary>
public sealed class ChoiceLayout
{
    private ChoiceLayout(IReadOnlyList<Rect> rowBounds, IReadOnlyList<ChoiceLayoutHitTarget> targets, bool segmented)
    {
        RowBounds = rowBounds;
        Targets = targets;
        IsSegmented = segmented;
    }
    public IReadOnlyList<Rect> RowBounds { get; }
    public IReadOnlyList<ChoiceLayoutHitTarget> Targets { get; }
    public bool IsSegmented { get; }
    public static ChoiceLayout Compact(Rect bounds) => new([bounds], [], false);
    internal static ChoiceLayout Segmented(IReadOnlyList<Rect> rows, IReadOnlyList<ChoiceLayoutHitTarget> targets) => new(rows, targets, true);
}

public static class ChoiceLayoutCalculator
{
    public static ChoiceLayout Compact(Rect bounds) => ChoiceLayout.Compact(bounds);

    public static ChoiceLayout Segmented<T>(ChoiceSelection<T> selection, Func<T, string> format, Rect bounds, string label, int startIndex = 0, int? endIndex = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(format);
        int start = Math.Clamp(startIndex, 0, selection.Items.Count);
        int end = Math.Clamp(endIndex ?? selection.Items.Count, start, selection.Items.Count);
        int column = ConsoleTextMetrics.GetCellWidth(string.IsNullOrEmpty(label) ? string.Empty : label + " ");
        var targets = new List<ChoiceLayoutHitTarget>();
        for (int index = start; index < end; index++)
        {
            int optionWidth = ConsoleTextMetrics.GetCellWidth($"( ) {format(selection.Items[index])}");
            Rect option = new(bounds.X + column, bounds.Y, optionWidth, 1);
            Rect visible = Intersect(option, bounds);
            if (visible.Width > 0) targets.Add(new ChoiceLayoutHitTarget(index, visible, Intersect(new Rect(option.X, option.Y, 3, 1), bounds)));
            column += optionWidth + 1;
        }
        return ChoiceLayout.Segmented([bounds], targets);
    }

    private static Rect Intersect(Rect value, Rect bounds)
    {
        int x = Math.Max(value.X, bounds.X), y = Math.Max(value.Y, bounds.Y);
        return new Rect(x, y, Math.Max(0, Math.Min(value.Right, bounds.Right) - x), Math.Max(0, Math.Min(value.Bottom, bounds.Bottom) - y));
    }
}

public readonly record struct ChoiceRenderOptions(CellStyle FillStyle, CellStyle FocusedStyle, bool Focused);

public static class ChoiceRenderer
{
    public static void Render<T>(IUiCanvas canvas, ChoiceLayout layout, ChoiceSelection<T> selection, Func<T, string> format, string label, ChoiceRenderOptions options)
    {
        Rect bounds = layout.RowBounds[0];
        string text = layout.IsSegmented
            ? (string.IsNullOrEmpty(label) ? string.Empty : label + " ") + string.Join(' ', selection.Items.Select((item, index) => $"{(index == selection.SelectedIndex ? "(x)" : "( )")} {format(item)}"))
            : $"{label}: {format(selection.Value)}";
        canvas.Write(bounds.X, bounds.Y, bounds.Width <= 0 ? string.Empty : ConsoleTextMetrics.FitToCells(text, bounds.Width), options.Focused ? options.FocusedStyle : options.FillStyle);
    }
}

public static class ChoiceInput
{
    public static bool HandleKey<T>(ChoiceSelection<T> selection, ConsoleKeyInfo key) => key.Key switch
    {
        ConsoleKey.LeftArrow => selection.SelectPrevious(),
        ConsoleKey.RightArrow or ConsoleKey.Spacebar or ConsoleKey.Enter => selection.SelectNext(),
        _ => false,
    };

    public static bool HandleMouse<T>(ChoiceSelection<T> selection, MouseConsoleInputEvent mouse, ChoiceLayout layout)
    {
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down || !layout.RowBounds.Any(bounds => bounds.Contains(mouse.X, mouse.Y))) return false;
        if (!layout.IsSegmented) return selection.SelectNext();
        foreach (ChoiceLayoutHitTarget target in layout.Targets)
            if (target.Bounds.Contains(mouse.X, mouse.Y)) return selection.SelectIndex(target.Index);
        return false;
    }
}
