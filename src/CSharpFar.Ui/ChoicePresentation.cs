using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct ChoiceLayoutHitTarget(int Index, Rect Bounds, Rect MarkerBounds);

/// <summary>Validated immutable geometry for one compact or segmented choice presentation.</summary>
public sealed class ChoiceLayout
{
    private ChoiceLayout(IReadOnlyList<Rect> rowBounds, IReadOnlyList<ChoiceLayoutHitTarget> targets, bool segmented)
    {
        if (rowBounds.Count == 0) throw new ArgumentException("Choice layout requires a row.", nameof(rowBounds));
        if (!segmented && targets.Count != 0) throw new ArgumentException("Compact choice layouts have no option targets.", nameof(targets));
        RowBounds = Array.AsReadOnly(rowBounds.ToArray());
        Targets = Array.AsReadOnly(targets.ToArray());
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

    public static ChoiceLayout Segmented<T>(ChoiceSelection<T> selection, Func<T, string> format, Rect bounds, string label, int startIndex = 0, int? endIndex = null) =>
        SegmentedCore(selection, format, bounds, label, startIndex, endIndex ?? selection.Items.Count);

    public static ChoiceLayout MultilineSegmented<T>(ChoiceSelection<T> selection, Func<T, string> format, Rect bounds, string label, IReadOnlyList<int> segmentEndIndices)
    {
        ArgumentNullException.ThrowIfNull(segmentEndIndices);
        if (segmentEndIndices.Count == 0) throw new ArgumentException("At least one segment is required.", nameof(segmentEndIndices));
        var rows = new List<Rect>(segmentEndIndices.Count);
        var targets = new List<ChoiceLayoutHitTarget>();
        int start = 0;
        foreach (int end in segmentEndIndices)
        {
            if (end < start || end > selection.Items.Count) throw new ArgumentOutOfRangeException(nameof(segmentEndIndices));
            ChoiceLayout line = SegmentedCore(selection, format, new Rect(bounds.X, bounds.Y + rows.Count, bounds.Width, 1), rows.Count == 0 ? label : string.Empty, start, end);
            rows.AddRange(line.RowBounds);
            targets.AddRange(line.Targets);
            start = end;
        }
        if (start != selection.Items.Count) throw new ArgumentException("The final segment must include every choice.", nameof(segmentEndIndices));
        return ChoiceLayout.Segmented(rows, targets);
    }

    private static ChoiceLayout SegmentedCore<T>(ChoiceSelection<T> selection, Func<T, string> format, Rect bounds, string label, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(format);
        if (start < 0 || end < start || end > selection.Items.Count) throw new ArgumentOutOfRangeException(nameof(start));
        int column = ConsoleTextMetrics.GetCellWidth(string.IsNullOrEmpty(label) ? string.Empty : label + " ");
        var targets = new List<ChoiceLayoutHitTarget>();
        for (int index = start; index < end; index++)
        {
            int optionWidth = ConsoleTextMetrics.GetCellWidth($"( ) {format(selection.Items[index])}");
            Rect option = new(bounds.X + column, bounds.Y, optionWidth, 1);
            Rect visible = Intersect(option, bounds);
            if (visible.Width > 0) targets.Add(new(index, visible, Intersect(new Rect(option.X, option.Y, 3, 1), bounds)));
            column += optionWidth + 1;
        }
        return ChoiceLayout.Segmented([bounds], targets);
    }

    private static Rect Intersect(Rect value, Rect bounds)
    {
        int x = Math.Max(value.X, bounds.X), y = Math.Max(value.Y, bounds.Y);
        return new(x, y, Math.Max(0, Math.Min(value.Right, bounds.Right) - x), Math.Max(0, Math.Min(value.Bottom, bounds.Bottom) - y));
    }
}

public readonly record struct ChoiceRenderOptions(CellStyle FillStyle, CellStyle FocusedStyle, bool Focused);

public static class ChoiceRenderer
{
    public static void Render<T>(IUiCanvas canvas, ChoiceLayout layout, ChoiceSelection<T> selection, Func<T, string> format, string label, ChoiceRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(format);
        CellStyle style = options.Focused ? options.FocusedStyle : options.FillStyle;
        if (!layout.IsSegmented)
        {
            Rect bounds = layout.RowBounds[0];
            canvas.Write(bounds.X, bounds.Y, ConsoleTextMetrics.FitToCells($"{label}: {format(selection.Value)}", Math.Max(0, bounds.Width)), style);
            return;
        }
        foreach (Rect row in layout.RowBounds) canvas.Write(row.X, row.Y, string.Empty, style);
        Rect firstRow = layout.RowBounds[0];
        if (!string.IsNullOrEmpty(label))
            canvas.Write(firstRow.X, firstRow.Y, ConsoleTextMetrics.FitToCells(label + " ", Math.Max(0, firstRow.Width)), style);
        foreach (IGrouping<int, ChoiceLayoutHitTarget> rowTargets in layout.Targets.GroupBy(target => target.Bounds.Y))
            foreach (ChoiceLayoutHitTarget target in rowTargets)
            {
                string text = $"{(target.Index == selection.SelectedIndex ? "(x)" : "( )")} {format(selection.Items[target.Index])}";
                canvas.Write(target.Bounds.X, target.Bounds.Y, ConsoleTextMetrics.FitToCells(text, target.Bounds.Width), style);
            }
    }

    public static bool TryGetSelectedMarkerBounds<T>(ChoiceLayout layout, ChoiceSelection<T> selection, out Rect bounds)
    {
        foreach (ChoiceLayoutHitTarget target in layout.Targets)
        {
            if (target.Index == selection.SelectedIndex)
            {
                bounds = target.MarkerBounds;
                return true;
            }
        }
        bounds = default;
        return false;
    }
}

public enum ChoiceInputResultKind { NotHandled, Handled, ValueChanged }

public static class ChoiceInput
{
    public static ChoiceInputResultKind HandleKey<T>(ChoiceSelection<T> selection, ConsoleKeyInfo key)
    {
        bool choiceKey = key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Spacebar or ConsoleKey.Enter;
        if (!choiceKey) return ChoiceInputResultKind.NotHandled;
        bool changed = key.Key == ConsoleKey.LeftArrow ? selection.SelectPrevious() : selection.SelectNext();
        return changed ? ChoiceInputResultKind.ValueChanged : ChoiceInputResultKind.Handled;
    }
    public static ChoiceInputResultKind HandleMouse<T>(ChoiceSelection<T> selection, MouseConsoleInputEvent mouse, ChoiceLayout layout)
    {
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down || !layout.RowBounds.Any(bounds => bounds.Contains(mouse.X, mouse.Y))) return ChoiceInputResultKind.NotHandled;
        if (!layout.IsSegmented) return selection.SelectNext() ? ChoiceInputResultKind.ValueChanged : ChoiceInputResultKind.Handled;
        foreach (ChoiceLayoutHitTarget target in layout.Targets)
        {
            if (target.Bounds.Contains(mouse.X, mouse.Y))
                return selection.SelectIndex(target.Index) == ChoiceSelectionResult.Changed ? ChoiceInputResultKind.ValueChanged : ChoiceInputResultKind.Handled;
        }
        return ChoiceInputResultKind.NotHandled;
    }
}
