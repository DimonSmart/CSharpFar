using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct FormGridPosition(int Column, int Row);

public readonly record struct FormGridCell(int Column, int Row, Rect Bounds)
{
    public FormGridPosition Position => new(Column, Row);
}

public enum FormGridTraversalOrder
{
    RowMajor,
    ColumnMajor,
}

public sealed class FormGridShape
{
    private readonly IReadOnlyList<FormGridPosition> _positions;

    public FormGridShape(IReadOnlyList<int> rowsPerColumn)
    {
        ArgumentNullException.ThrowIfNull(rowsPerColumn);
        if (rowsPerColumn.Count == 0 || rowsPerColumn.Any(rows => rows < 0))
            throw new ArgumentException("A grid requires non-negative row counts and at least one column.", nameof(rowsPerColumn));
        RowsPerColumn = Array.AsReadOnly(rowsPerColumn.ToArray());
        _positions = Array.AsReadOnly(RowsPerColumn.SelectMany((rows, column) => Enumerable.Range(0, rows).Select(row => new FormGridPosition(column, row))).ToArray());
    }

    public IReadOnlyList<int> RowsPerColumn { get; }
    public int ColumnCount => RowsPerColumn.Count;
    public IReadOnlyList<FormGridPosition> Positions => _positions;
    public bool Contains(FormGridPosition position) => position.Column >= 0 && position.Column < ColumnCount && position.Row >= 0 && position.Row < RowsPerColumn[position.Column];
    public IReadOnlyList<FormGridPosition> GetPositions(FormGridTraversalOrder order) => order switch
    {
        FormGridTraversalOrder.RowMajor => _positions.OrderBy(position => position.Row).ThenBy(position => position.Column).ToArray(),
        FormGridTraversalOrder.ColumnMajor => _positions,
        _ => throw new ArgumentOutOfRangeException(nameof(order)),
    };
}

/// <summary>Immutable geometry for the small, form-owned grids used by CSharpFar.</summary>
public sealed class FormGridLayout
{
    private readonly IReadOnlyList<FormGridCell> _cells;

    private FormGridLayout(FormGridShape shape, Rect bounds, IReadOnlyList<FormGridCell> cells)
    {
        Shape = shape;
        Bounds = bounds;
        _cells = cells;
    }

    public FormGridShape Shape { get; }
    public Rect Bounds { get; }
    public IReadOnlyList<FormGridCell> Cells => _cells;
    public int ColumnCount => Shape.ColumnCount;

    public bool TryHitTest(int x, int y, out FormGridCell cell)
    {
        foreach (FormGridCell candidate in _cells)
        {
            if (candidate.Bounds.Contains(x, y))
            {
                cell = candidate;
                return true;
            }
        }
        cell = default;
        return false;
    }

    public bool TryGetCell(FormGridPosition position, out FormGridCell cell)
    {
        foreach (FormGridCell candidate in _cells)
        {
            if (candidate.Column == position.Column && candidate.Row == position.Row)
            {
                cell = candidate;
                return true;
            }
        }
        cell = default;
        return false;
    }

    public bool TryGetCell(int column, int row, out FormGridCell cell) =>
        TryGetCell(new FormGridPosition(column, row), out cell);

    public IReadOnlyList<FormGridPosition> GetPositions(FormGridTraversalOrder order) => Shape.GetPositions(order);

    public static FormGridLayout Calculate(FormGridShape shape, Rect bounds, int columnGap = 2)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (columnGap < 0)
            throw new ArgumentOutOfRangeException(nameof(columnGap));

        int columnCount = shape.ColumnCount;
        int gap = columnCount == 1 ? 0 : Math.Min(columnGap, Math.Max(0, bounds.Width) / (columnCount - 1));
        int available = Math.Max(0, bounds.Width - gap * (columnCount - 1));
        int baseWidth = available / columnCount;
        int remainder = available % columnCount;
        int x = bounds.X;
        var cells = new List<FormGridCell>();
        for (int column = 0; column < columnCount; column++)
        {
            int width = baseWidth + (column < remainder ? 1 : 0);
            for (int row = 0; row < shape.RowsPerColumn[column]; row++)
                cells.Add(new FormGridCell(column, row, new Rect(x, bounds.Y + row, width, 1)));
            x += width + gap;
        }
        return new FormGridLayout(shape, bounds, cells.AsReadOnly());
    }

    public static FormGridLayout Calculate(Rect bounds, IReadOnlyList<int> rowsPerColumn, int columnGap = 2) =>
        Calculate(new FormGridShape(rowsPerColumn), bounds, columnGap);
}

/// <summary>Mutable focus state; all navigation is calculated from the current grid and enabled predicate.</summary>
public sealed class FormGridNavigationState
{
    public FormGridPosition? Current { get; private set; }

    public FormGridPosition? ResolveCurrent(FormGridShape shape, Func<FormGridPosition, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(isEnabled);
        if (Current is { } current && shape.Contains(current) && isEnabled(current))
            return current;
        foreach (FormGridPosition position in shape.GetPositions(FormGridTraversalOrder.RowMajor))
            if (isEnabled(position))
                return position;
        return null;
    }

    public bool EnsureCurrent(FormGridShape shape, Func<FormGridPosition, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(isEnabled);
        FormGridPosition? resolved = ResolveCurrent(shape, isEnabled);
        Current = resolved;
        return resolved is not null;
    }

    public bool SelectPointer(int x, int y, FormGridLayout layout, Func<FormGridCell, bool> isEnabled)
    {
        if (!layout.TryHitTest(x, y, out FormGridCell cell) || !isEnabled(cell))
            return false;
        Current = cell.Position;
        return true;
    }

    public bool MoveHorizontal(int direction, FormGridShape shape, Func<FormGridPosition, bool> isEnabled) =>
        Move(direction, 0, shape, isEnabled);

    public bool MoveVertical(int direction, FormGridShape shape, Func<FormGridPosition, bool> isEnabled) =>
        Move(0, direction, shape, isEnabled);

    public FormInputResult MoveTab(int direction, FormGridShape shape, Func<FormGridPosition, bool> isEnabled)
    {
        if (direction == 0 || !EnsureCurrent(shape, isEnabled))
            return FormInputResult.NotHandled;
        FormGridPosition[] enabled = shape.GetPositions(FormGridTraversalOrder.RowMajor).Where(isEnabled).ToArray();
        int current = Array.IndexOf(enabled, Current!.Value);
        int next = current + Math.Sign(direction);
        if (next < 0) return FormInputResult.MoveFocusPrevious;
        if (next >= enabled.Length) return FormInputResult.MoveFocusNext;
        Current = enabled[next];
        return FormInputResult.Handled;
    }

    private bool Move(int columnDelta, int rowDelta, FormGridShape shape, Func<FormGridPosition, bool> isEnabled)
    {
        if ((columnDelta == 0 && rowDelta == 0) || !EnsureCurrent(shape, isEnabled))
            return false;
        FormGridPosition current = Current!.Value;
        IEnumerable<FormGridPosition> candidates = shape.Positions.Where(isEnabled);
        candidates = columnDelta != 0
            ? candidates.Where(cell => Math.Sign(cell.Column - current.Column) == Math.Sign(columnDelta)).OrderBy(cell => Math.Abs(cell.Row - current.Row)).ThenBy(cell => Math.Abs(cell.Column - current.Column))
            : candidates.Where(cell => cell.Column == current.Column && Math.Sign(cell.Row - current.Row) == Math.Sign(rowDelta)).OrderBy(cell => Math.Abs(cell.Row - current.Row));
        FormGridPosition? next = candidates.Cast<FormGridPosition?>().FirstOrDefault();
        if (next is null)
            return false;
        Current = next.Value;
        return true;
    }
}
