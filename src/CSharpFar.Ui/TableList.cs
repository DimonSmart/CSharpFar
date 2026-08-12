using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum TableColumnAlignment { Left, Right }

public sealed class TableColumn<T>
{
    private TableColumn(string header, Func<T, string> value, int width, TableColumnAlignment alignment, bool emphasized)
    {
        Header = header;
        Value = value;
        Width = width;
        Alignment = alignment;
        Emphasized = emphasized;
    }

    public string Header { get; }
    public Func<T, string> Value { get; }
    public int Width { get; }
    public TableColumnAlignment Alignment { get; }
    public bool Emphasized { get; }

    public static TableColumn<T> Text(string header, Func<T, string> value, int width, TableColumnAlignment alignment = TableColumnAlignment.Left, bool emphasized = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(header);
        ArgumentNullException.ThrowIfNull(value);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return new TableColumn<T>(header, value, width, alignment, emphasized);
    }
}

public sealed class TableListDefinition<T>
{
    public required IReadOnlyList<TableColumn<T>> Columns { get; init; }
}

/// <summary>Fixed-column tabular presentation over the standard selectable-list state and routing.</summary>
public sealed class TableList<T>
{
    private readonly IReadOnlyList<TableColumn<T>> _columns;

    public TableList(TableListDefinition<T> definition, ScrollableListState<T> state, UiTargetId listTarget, UiTargetId scrollbarTarget)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Columns);
        if (definition.Columns.Count == 0) throw new ArgumentException("At least one table column is required.", nameof(definition));
        _columns = definition.Columns.ToArray();
        List = new RoutedScrollableList<T>(state ?? throw new ArgumentNullException(nameof(state)), listTarget, scrollbarTarget);
    }

    public RoutedScrollableList<T> List { get; }

    public void Render(IUiCanvas canvas, ScrollableListFrame frame, Rect headerBounds, CellStyle headerStyle, CellStyle separatorStyle, CellStyle normalStyle, CellStyle selectedStyle, CellStyle emphasizedStyle, CellStyle emphasizedSelectedStyle)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        RenderLine(canvas, headerBounds, _columns.Select(column => (column.Header, column.Alignment)), headerStyle, fill: true);
        if (headerBounds.Height > 1)
            canvas.Write(headerBounds.X, headerBounds.Y + 1, ConsoleTextMetrics.FitToCells(BuildSeparator(), headerBounds.Width), separatorStyle);

        canvas.FillRegion(frame.ContentBounds, normalStyle);
        for (int row = 0; row < frame.ContentBounds.Height && frame.ScrollTop + row < List.State.Count; row++)
        {
            int index = frame.ScrollTop + row;
            RenderRow(canvas, new Rect(frame.ContentBounds.X, frame.ContentBounds.Y + row, frame.ContentBounds.Width, 1), List.State.Items[index], index == frame.SelectedIndex, normalStyle, selectedStyle, emphasizedStyle, emphasizedSelectedStyle);
        }
    }

    private void RenderRow(IUiCanvas canvas, Rect bounds, T item, bool selected, CellStyle normalStyle, CellStyle selectedStyle, CellStyle emphasizedStyle, CellStyle emphasizedSelectedStyle)
    {
        int x = bounds.X;
        int remaining = bounds.Width;
        for (int i = 0; i < _columns.Count; i++)
        {
            if (remaining <= 0) break;
            TableColumn<T> column = _columns[i];
            int width = Math.Min(column.Width, remaining);
            CellStyle style = column.Emphasized
                ? selected ? emphasizedSelectedStyle : emphasizedStyle
                : selected ? selectedStyle : normalStyle;
            canvas.Write(x, bounds.Y, Fit(column.Value(item), width, column.Alignment), style);
            x += width;
            remaining -= width;
            if (remaining <= 0 || i == _columns.Count - 1) break;
            int separatorWidth = Math.Min(3, remaining);
            canvas.Write(x, bounds.Y, ConsoleTextMetrics.FitToCells(" │ ", separatorWidth), selected ? selectedStyle : normalStyle);
            x += separatorWidth;
            remaining -= separatorWidth;
        }
        if (remaining > 0) canvas.Write(x, bounds.Y, new string(' ', remaining), selected ? selectedStyle : normalStyle);
    }

    private void RenderLine(IUiCanvas canvas, Rect bounds, IEnumerable<(string Text, TableColumnAlignment Alignment)> cells, CellStyle style, bool fill)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        int x = bounds.X;
        int remaining = bounds.Width;
        foreach (((string text, TableColumnAlignment alignment), TableColumn<T> column, int index) in cells.Zip(_columns).Select((pair, index) => (pair.First, pair.Second, index)))
        {
            if (remaining <= 0) break;
            int width = Math.Min(column.Width, remaining);
            canvas.Write(x, bounds.Y, Fit(text, width, alignment), style);
            x += width;
            remaining -= width;
            if (remaining <= 0 || index == _columns.Count - 1) break;
            int separatorWidth = Math.Min(3, remaining);
            canvas.Write(x, bounds.Y, ConsoleTextMetrics.FitToCells(" │ ", separatorWidth), style);
            x += separatorWidth;
            remaining -= separatorWidth;
        }
        if (fill && remaining > 0) canvas.Write(x, bounds.Y, new string(' ', remaining), style);
    }

    private string BuildSeparator() => string.Join("─┼─", _columns.Select(column => new string('─', column.Width)));

    private static string Fit(string text, int width, TableColumnAlignment alignment)
    {
        string clipped = ConsoleTextMetrics.TruncateEndToCells(text ?? string.Empty, width);
        int padding = Math.Max(0, width - ConsoleTextMetrics.GetCellWidth(clipped));
        return alignment == TableColumnAlignment.Right
            ? new string(' ', padding) + clipped
            : clipped + new string(' ', padding);
    }
}
