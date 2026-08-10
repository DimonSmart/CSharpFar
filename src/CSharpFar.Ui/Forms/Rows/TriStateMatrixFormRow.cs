using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record TriStateMatrixColumn(string Id, string Label);

public sealed record TriStateMatrixRow(string Id, string Label, IReadOnlyList<CheckState> Values);

/// <summary>A compact, labeled matrix of tri-state values for related flags.</summary>
public sealed class TriStateMatrixFormRow : FormRow, IFormFocusTarget, IFormCursorProvider
{
    private const int LabelGap = 1;
    private const int ColumnGap = 1;
    private readonly TriStateMatrixColumn[] _columns;
    private readonly TriStateMatrixRow[] _rows;
    private readonly CheckState[,] _values;
    private readonly FormGridShape _shape;
    private readonly FormGridNavigationState _navigation = new();

    internal TriStateMatrixFormRow(IReadOnlyList<TriStateMatrixColumn> columns, IReadOnlyList<TriStateMatrixRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        if (rows.Count == 0) throw new ArgumentException("At least one row is required.", nameof(rows));
        if (columns.Any(static column => string.IsNullOrWhiteSpace(column.Id) || column.Label is null) ||
            rows.Any(static row => string.IsNullOrWhiteSpace(row.Id) || row.Label is null || row.Values is null))
            throw new ArgumentException("Matrix identifiers, labels, and values are required.");
        if (columns.Select(static column => column.Id).Distinct(StringComparer.Ordinal).Count() != columns.Count ||
            rows.Select(static row => row.Id).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new ArgumentException("Matrix identifiers must be unique.");
        if (rows.Any(row => row.Values.Count != columns.Count))
            throw new ArgumentException("Every matrix row must have a value for every column.", nameof(rows));

        _columns = columns.ToArray();
        _rows = rows.ToArray();
        _values = new CheckState[_rows.Length, _columns.Length];
        for (int row = 0; row < _rows.Length; row++)
            for (int column = 0; column < _columns.Length; column++)
                _values[row, column] = _rows[row].Values[column];
        _shape = new FormGridShape(Enumerable.Repeat(_rows.Length, _columns.Length).ToArray());
    }

    internal override FormRowRole Role { get; init; } = FormRowRole.Option;
    internal override int Height => _rows.Length + 1;
    internal override int DesiredWidth =>
        _rows.Max(row => ConsoleTextMetrics.GetCellWidth(row.Label)) + LabelGap +
        _columns.Max(column => Math.Max(ConsoleTextMetrics.GetCellWidth(column.Label), ConsoleTextMetrics.GetCellWidth("[ ]"))) * _columns.Length +
        ColumnGap * (_columns.Length - 1);
    public bool Enabled { get; set; } = true;
    internal override bool IsEnabled => Enabled;
    public CheckState GetValue(string rowId, string columnId) => _values[FindRow(rowId), FindColumn(columnId)];
    public void SetValue(string rowId, string columnId, CheckState value) => _values[FindRow(rowId), FindColumn(columnId)] = value;

    internal override void Render(FormRowRenderContext context)
    {
        context.Canvas.FillRegion(context.Bounds, FarDialogStyles.Fill);
        (int labelWidth, FormGridLayout layout) = CalculateLayout(context.Bounds);
        CellStyle style = DisabledFormControlPresentation.Style(Enabled, FarDialogStyles.Fill);
        for (int column = 0; column < _columns.Length; column++)
        {
            if (layout.TryGetCell(new FormGridPosition(column, 0), out FormGridCell header))
                context.Canvas.Write(header.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_columns[column].Label, header.Bounds.Width), style);
        }
        FormGridPosition? current = _navigation.ResolveCurrent(_shape, static _ => true);
        for (int row = 0; row < _rows.Length; row++)
        {
            context.Canvas.Write(context.Bounds.X, context.Bounds.Y + row + 1, ScrollableFormDialog.Fit(_rows[row].Label, labelWidth), style);
            for (int column = 0; column < _columns.Length; column++)
            {
                if (!layout.TryGetCell(new FormGridPosition(column, row), out FormGridCell cell))
                    continue;
                char marker = _values[row, column] switch { CheckState.Checked => 'x', CheckState.Indeterminate => '-', _ => ' ' };
                context.Canvas.Write(cell.Bounds.X, context.Bounds.Y + row + 1, ScrollableFormDialog.Fit($"[{marker}]", cell.Bounds.Width),
                    context.Focused && Enabled && current == new FormGridPosition(column, row) ? FarDialogStyles.FocusedInput : style);
            }
        }
    }

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!Enabled || !_navigation.EnsureCurrent(_shape, static _ => true)) return FormInputResult.NotHandled;
        if (key.Key == ConsoleKey.LeftArrow) return Move(_navigation.MoveHorizontal(-1, _shape, static _ => true));
        if (key.Key == ConsoleKey.RightArrow) return Move(_navigation.MoveHorizontal(1, _shape, static _ => true));
        if (key.Key == ConsoleKey.UpArrow) return Move(_navigation.MoveVertical(-1, _shape, static _ => true));
        if (key.Key == ConsoleKey.DownArrow) return Move(_navigation.MoveVertical(1, _shape, static _ => true));
        if (key.Key == ConsoleKey.Tab) return _navigation.MoveTab(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1, _shape, static _ => true);
        if (key.Key is not (ConsoleKey.Spacebar or ConsoleKey.Enter)) return FormInputResult.NotHandled;
        FormGridPosition cell = _navigation.Current!.Value;
        _values[cell.Row, cell.Column] = _values[cell.Row, cell.Column] switch
        {
            CheckState.Indeterminate => CheckState.Checked,
            CheckState.Checked => CheckState.Unchecked,
            _ => CheckState.Checked,
        };
        return FormInputResult.ValueChanged;
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (!Enabled || mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down }) return FormInputResult.NotHandled;
        (_, FormGridLayout layout) = CalculateLayout(context.Bounds);
        int row = mouse.Y - context.Bounds.Y - 1;
        if (row < 0 || row >= _rows.Length || !_navigation.SelectPointer(mouse.X, mouse.Y - 1, layout, static _ => true) || _navigation.Current is not { } cell)
            return FormInputResult.NotHandled;
        _values[cell.Row, cell.Column] = _values[cell.Row, cell.Column] switch { CheckState.Indeterminate => CheckState.Checked, CheckState.Checked => CheckState.Unchecked, _ => CheckState.Checked };
        return FormInputResult.ValueChanged;
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        (_, FormGridLayout layout) = CalculateLayout(context.Bounds);
        FormGridPosition? current = _navigation.ResolveCurrent(_shape, static _ => true);
        if (Enabled && context.Focused && current is { } cell && layout.TryGetCell(cell, out FormGridCell value))
        {
            cursor = new FormCursorPlacement(value.Bounds.X + 1, context.Bounds.Y + cell.Row + 1);
            return value.Bounds.Width >= 3;
        }
        cursor = default;
        return false;
    }

    private (int LabelWidth, FormGridLayout Layout) CalculateLayout(Rect bounds)
    {
        int labelWidth = Math.Min(_rows.Select(row => ConsoleTextMetrics.GetCellWidth(row.Label)).Max(), Math.Max(0, bounds.Width));
        Rect values = new(bounds.X + labelWidth + Math.Min(LabelGap, Math.Max(0, bounds.Width - labelWidth)), bounds.Y, Math.Max(0, bounds.Width - labelWidth - LabelGap), 1);
        return (labelWidth, FormGridLayout.Calculate(_shape, values, ColumnGap));
    }

    private int FindRow(string id) => Array.FindIndex(_rows, row => row.Id == id) is var index && index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(id));
    private int FindColumn(string id) => Array.FindIndex(_columns, column => column.Id == id) is var index && index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(id));
    private static FormInputResult Move(bool _) => FormInputResult.Handled;
}
