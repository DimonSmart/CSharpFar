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

/// <summary>Immutable geometry for the small, form-owned grids used by CSharpFar.</summary>
public sealed class FormGridLayout
{
    private readonly IReadOnlyList<FormGridCell> _cells;

    private FormGridLayout(Rect bounds, IReadOnlyList<FormGridCell> cells)
    {
        Bounds = bounds;
        _cells = cells;
    }

    public Rect Bounds { get; }
    public IReadOnlyList<FormGridCell> Cells => _cells;
    public int ColumnCount => _cells.Select(cell => cell.Column).DefaultIfEmpty(-1).Max() + 1;

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

    public IReadOnlyList<FormGridPosition> GetPositions(FormGridTraversalOrder order)
    {
        IEnumerable<FormGridCell> cells = order switch
        {
            FormGridTraversalOrder.RowMajor => _cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column),
            FormGridTraversalOrder.ColumnMajor => _cells.OrderBy(cell => cell.Column).ThenBy(cell => cell.Row),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        return cells.Select(cell => cell.Position).ToArray();
    }

    public static FormGridLayout Calculate(Rect bounds, IReadOnlyList<int> rowsPerColumn, int columnGap = 2)
    {
        ArgumentNullException.ThrowIfNull(rowsPerColumn);
        if (rowsPerColumn.Count == 0)
            throw new ArgumentException("A grid requires at least one column.", nameof(rowsPerColumn));
        if (rowsPerColumn.Any(rows => rows < 0))
            throw new ArgumentOutOfRangeException(nameof(rowsPerColumn));
        if (columnGap < 0)
            throw new ArgumentOutOfRangeException(nameof(columnGap));

        int columnCount = rowsPerColumn.Count;
        int gap = columnCount == 1 ? 0 : Math.Min(columnGap, Math.Max(0, bounds.Width) / (columnCount - 1));
        int available = Math.Max(0, bounds.Width - gap * (columnCount - 1));
        int baseWidth = available / columnCount;
        int remainder = available % columnCount;
        int x = bounds.X;
        var cells = new List<FormGridCell>();
        for (int column = 0; column < columnCount; column++)
        {
            int width = baseWidth + (column < remainder ? 1 : 0);
            for (int row = 0; row < rowsPerColumn[column]; row++)
                cells.Add(new FormGridCell(column, row, new Rect(x, bounds.Y + row, width, 1)));
            x += width + gap;
        }
        return new FormGridLayout(bounds, cells.AsReadOnly());
    }
}

/// <summary>Mutable focus state; all navigation is calculated from the current grid and enabled predicate.</summary>
public sealed class FormGridNavigationState
{
    public FormGridPosition? Current { get; private set; }

    public bool EnsureCurrent(FormGridLayout layout, Func<FormGridCell, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(isEnabled);
        if (Current is { } current && layout.TryGetCell(current, out FormGridCell currentCell) && isEnabled(currentCell))
            return true;

        foreach (FormGridPosition position in layout.GetPositions(FormGridTraversalOrder.RowMajor))
        {
            if (!layout.TryGetCell(position, out FormGridCell cell) || !isEnabled(cell))
                continue;
            Current = position;
            return true;
        }

        Current = null;
        return false;
    }

    public bool SelectPointer(int x, int y, FormGridLayout layout, Func<FormGridCell, bool> isEnabled)
    {
        if (!layout.TryHitTest(x, y, out FormGridCell cell) || !isEnabled(cell))
            return false;
        Current = cell.Position;
        return true;
    }

    public bool MoveHorizontal(int direction, FormGridLayout layout, Func<FormGridCell, bool> isEnabled) =>
        Move(direction, 0, layout, isEnabled);

    public bool MoveVertical(int direction, FormGridLayout layout, Func<FormGridCell, bool> isEnabled) =>
        Move(0, direction, layout, isEnabled);

    public FormInputResult MoveTab(int direction, FormGridLayout layout, Func<FormGridCell, bool> isEnabled)
    {
        if (direction == 0 || !EnsureCurrent(layout, isEnabled))
            return FormInputResult.NotHandled;
        FormGridPosition[] enabled = layout.GetPositions(FormGridTraversalOrder.RowMajor)
            .Where(position => layout.TryGetCell(position, out FormGridCell cell) && isEnabled(cell)).ToArray();
        int current = Array.IndexOf(enabled, Current!.Value);
        int next = current + Math.Sign(direction);
        if (next < 0) return FormInputResult.MoveFocusPrevious;
        if (next >= enabled.Length) return FormInputResult.MoveFocusNext;
        Current = enabled[next];
        return FormInputResult.Handled;
    }

    private bool Move(int columnDelta, int rowDelta, FormGridLayout layout, Func<FormGridCell, bool> isEnabled)
    {
        if ((columnDelta == 0 && rowDelta == 0) || !EnsureCurrent(layout, isEnabled))
            return false;
        FormGridPosition current = Current!.Value;
        FormGridCell[] enabled = layout.Cells.Where(isEnabled).ToArray();
        IEnumerable<FormGridCell> candidates = columnDelta != 0
            ? enabled.Where(cell => Math.Sign(cell.Column - current.Column) == Math.Sign(columnDelta))
                .OrderBy(cell => Math.Abs(cell.Row - current.Row)).ThenBy(cell => Math.Abs(cell.Column - current.Column))
            : enabled.Where(cell => cell.Column == current.Column && Math.Sign(cell.Row - current.Row) == Math.Sign(rowDelta))
                .OrderBy(cell => Math.Abs(cell.Row - current.Row));
        FormGridCell? next = candidates.Cast<FormGridCell?>().FirstOrDefault();
        if (next is null)
            return false;
        Current = next.Value.Position;
        return true;
    }
}
