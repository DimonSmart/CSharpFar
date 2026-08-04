using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>A compact labeled row of tri-state checkboxes using the shared form-grid mechanics.</summary>
public sealed class TriStateCheckBoxColumnsRow : FormRow, IFormCursorProvider
{
    private readonly string _label;
    private readonly IReadOnlyList<TriStateCheckBoxLine> _columns;
    private readonly int _labelWidth;
    private readonly int _columnGap;
    private readonly FormGridShape _shape;
    private readonly FormGridNavigationState _navigation = new();

    internal TriStateCheckBoxColumnsRow(string label, IReadOnlyList<TriStateCheckBoxLine> columns, int labelWidth, int columnGap = 1)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        if (columns.Any(static column => column is null)) throw new ArgumentException("Columns cannot contain null entries.", nameof(columns));
        if (labelWidth < 0) throw new ArgumentOutOfRangeException(nameof(labelWidth));
        if (columnGap < 0) throw new ArgumentOutOfRangeException(nameof(columnGap));
        _label = label;
        _columns = columns.ToArray();
        _labelWidth = labelWidth;
        _columnGap = columnGap;
        _shape = new FormGridShape(Enumerable.Repeat(1, _columns.Count).ToArray());
    }

    internal override FormRowRole Role { get; init; } = FormRowRole.Option;
    internal override bool IsFocusable => _columns.Any(static column => column.Enabled);

    internal override void Render(FormRowRenderContext context)
    {
        context.Canvas.FillRegion(context.Bounds, FarDialogStyles.Fill);
        context.Canvas.Write(context.Bounds.X, context.Bounds.Y, _label, FarDialogStyles.Fill);
        FormGridLayout layout = CalculateLayout(context.Bounds);
        FormGridPosition? effectivePosition = _navigation.ResolveCurrent(_shape, IsCellEnabled);
        foreach (FormGridCell cell in layout.Cells)
        {
            TriStateCheckBoxLine line = _columns[cell.Column];
            CellStyle fill = line.Enabled ? FarDialogStyles.Fill : FarDialogStyles.DisabledControl(FarDialogStyles.Fill);
            line.Render(context.Canvas, cell.Bounds.X, cell.Bounds.Y, cell.Bounds.Width, context.Focused && effectivePosition == cell.Position && line.Enabled, fill, FarDialogStyles.FocusedInput);
        }
    }

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!_navigation.EnsureCurrent(_shape, IsCellEnabled)) return FormInputResult.NotHandled;
        if (key.Key == ConsoleKey.LeftArrow) return Move(_navigation.MoveHorizontal(-1, _shape, IsCellEnabled));
        if (key.Key == ConsoleKey.RightArrow) return Move(_navigation.MoveHorizontal(1, _shape, IsCellEnabled));
        if (key.Key == ConsoleKey.Tab) return _navigation.MoveTab(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1, _shape, IsCellEnabled);
        FormGridPosition position = _navigation.Current!.Value;
        return _columns[position.Column].TryHandleKey(key) ? FormInputResult.ValueChanged : FormInputResult.NotHandled;
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down) return FormInputResult.NotHandled;
        FormGridLayout layout = CalculateLayout(context.Bounds);
        if (!_navigation.SelectPointer(mouse.X, mouse.Y, layout, cell => IsCellEnabled(cell.Position)) || _navigation.Current is not { } position || !layout.TryGetCell(position, out FormGridCell cell)) return FormInputResult.NotHandled;
        return _columns[position.Column].TryHandleMouse(mouse, cell.Bounds) ? FormInputResult.ValueChanged : FormInputResult.NotHandled;
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        FormGridLayout layout = CalculateLayout(context.Bounds);
        FormGridPosition? effectivePosition = _navigation.ResolveCurrent(_shape, IsCellEnabled);
        if (context.Focused && effectivePosition is { } position && layout.TryGetCell(position, out FormGridCell cell))
        {
            cursor = new FormCursorPlacement(cell.Bounds.X + 1, cell.Bounds.Y);
            return cell.Bounds.Width >= 3;
        }
        cursor = default;
        return false;
    }

    private FormGridLayout CalculateLayout(Rect bounds)
    {
        int labelWidth = Math.Min(_labelWidth, Math.Max(0, bounds.Width));
        return FormGridLayout.Calculate(_shape, new Rect(bounds.X + labelWidth, bounds.Y, Math.Max(0, bounds.Width - labelWidth), 1), _columnGap);
    }
    private bool IsCellEnabled(FormGridPosition cell) => _columns[cell.Column].Enabled;
    private static FormInputResult Move(bool _) => FormInputResult.Handled;
}
