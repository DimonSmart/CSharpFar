using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct FormGridCell(int Column, int Row, Rect Bounds);

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

    public bool TryGetCell(int column, int row, out FormGridCell cell)
    {
        foreach (FormGridCell candidate in _cells)
        {
            if (candidate.Column == column && candidate.Row == row)
            {
                cell = candidate;
                return true;
            }
        }
        cell = default;
        return false;
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

public sealed class EnabledGridNavigator
{
    private readonly IReadOnlyList<(int Column, int Row)> _enabled;

    public EnabledGridNavigator(FormGridLayout layout, Func<FormGridCell, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(isEnabled);
        _enabled = layout.Cells.Where(isEnabled).Select(cell => (cell.Column, cell.Row)).ToArray();
        Current = _enabled.FirstOrDefault();
    }

    public (int Column, int Row) Current { get; private set; }
    public bool HasEnabledCells => _enabled.Count > 0;

    public bool SelectPointer(int x, int y, FormGridLayout layout)
    {
        FormGridCell? cell = layout.Cells.LastOrDefault(value => value.Bounds.Contains(x, y));
        if (cell is not { } value || !_enabled.Contains((value.Column, value.Row)))
            return false;
        Current = (value.Column, value.Row);
        return true;
    }

    public bool MoveHorizontal(int direction) => Move(direction, 0);
    public bool MoveVertical(int direction) => Move(0, direction);

    public FormInputResult MoveTab(int direction)
    {
        int index = Enumerable.Range(0, _enabled.Count).FirstOrDefault(value => _enabled[value] == Current);
        int next = index + direction;
        if (next < 0) return FormInputResult.MoveFocusPrevious;
        if (next >= _enabled.Count) return FormInputResult.MoveFocusNext;
        Current = _enabled[next];
        return FormInputResult.Handled;
    }

    private bool Move(int columnDelta, int rowDelta)
    {
        if (!HasEnabledCells) return false;
        IEnumerable<(int Column, int Row)> candidates = _enabled
            .Where(cell => columnDelta != 0 ? Math.Sign(cell.Column - Current.Column) == Math.Sign(columnDelta) : Math.Sign(cell.Row - Current.Row) == Math.Sign(rowDelta))
            .OrderBy(cell => Math.Abs(cell.Column - Current.Column) + Math.Abs(cell.Row - Current.Row));
        (int Column, int Row) next = candidates.FirstOrDefault();
        if (next == default && !_enabled.Contains(default)) return false;
        Current = next;
        return true;
    }
}
