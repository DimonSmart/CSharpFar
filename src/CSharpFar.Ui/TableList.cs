using CSharpFar.Console;
using CSharpFar.Console.Input;
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

public sealed class TableListPresentation
{
    public required CellStyle Header { get; init; }
    public required CellStyle Separator { get; init; }
    public required CellStyle Normal { get; init; }
    public required CellStyle Selected { get; init; }
    public required CellStyle Emphasized { get; init; }
    public required CellStyle EmphasizedSelected { get; init; }
    public required CellStyle Scrollbar { get; init; }

    public static TableListPresentation Dialog(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new()
        {
            Header = PaletteStyles.DialogTitle(palette),
            Separator = PaletteStyles.DialogBorder(palette),
            Normal = PaletteStyles.DialogFill(palette),
            Selected = PaletteStyles.InputField(palette),
            Emphasized = PaletteStyles.DialogHighlight(palette),
            EmphasizedSelected = PaletteStyles.InputHighlight(palette),
            Scrollbar = PaletteStyles.DialogBorder(palette),
        };
    }
}

/// <summary>Immutable semantic state of a calculated table frame.</summary>
public sealed class TableListFrame
{
    internal TableListFrame(Rect bounds, Rect headerBounds, ScrollableListFrame listFrame)
    {
        Bounds = bounds;
        HeaderBounds = headerBounds;
        BodyBounds = listFrame.ContentBounds;
        SelectedIndex = listFrame.SelectedIndex;
        ScrollTop = listFrame.ScrollTop;
        ViewportRows = listFrame.ViewportRows;
        _listFrame = listFrame;
    }

    private readonly ScrollableListFrame _listFrame;
    public Rect Bounds { get; }
    public Rect HeaderBounds { get; }
    public Rect BodyBounds { get; }
    public int SelectedIndex { get; }
    public int ScrollTop { get; }
    public int ViewportRows { get; }
    public bool HasScrollbar => _listFrame.Scrollbar is not null;
    internal ScrollableListFrame ListFrame => _listFrame;
}

/// <summary>Fixed-column tabular component with standard selectable-list behavior.</summary>
public sealed class TableList<T>
{
    private static long _nextComponentId;
    private readonly IReadOnlyList<TableColumn<T>> _columns;
    private readonly RoutedScrollableList<T> _list;

    public TableList(IReadOnlyList<T> items, TableListDefinition<T> definition, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Columns);
        if (definition.Columns.Count == 0) throw new ArgumentException("At least one table column is required.", nameof(definition));

        _columns = definition.Columns.ToArray();
        var targets = new UiTargetScope($"table-list-{Interlocked.Increment(ref _nextComponentId)}");
        _list = new RoutedScrollableList<T>(
            new ScrollableListState<T>(items, selectedIndex),
            targets.Child("body"),
            targets.Child("scrollbar"));
    }

    public int SelectedIndex => _list.State.SelectedIndex;
    public T? SelectedItem => _list.State.TryGetSelectedItem(out T item) ? item : default;
    public bool HasItems => _list.State.HasItems;
    public int Count => _list.State.Count;

    public bool TryGetSelectedItem(out T item) => _list.State.TryGetSelectedItem(out item);

    public void SetSelectedIndex(int index, int viewportRows) => _list.State.SetSelectedIndex(index, viewportRows);

    public TableListFrame CalculateFrame(Rect bounds)
    {
        int headerHeight = Math.Min(2, Math.Max(0, bounds.Height));
        Rect headerBounds = new(bounds.X, bounds.Y, bounds.Width, headerHeight);
        Rect bodyBounds = new(bounds.X, bounds.Y + headerHeight, bounds.Width, Math.Max(0, bounds.Height - headerHeight));
        Rect? scrollbarBounds = _list.State.Count > bodyBounds.Height && bodyBounds.Width > 0 && bodyBounds.Height > 0
            ? new Rect(bodyBounds.Right, bodyBounds.Y, 1, bodyBounds.Height)
            : null;
        return new TableListFrame(bounds, headerBounds, _list.CalculateFrame(bodyBounds, scrollbarBounds));
    }

    public void Render(IUiCanvas canvas, TableListFrame frame, TableListPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(presentation);

        RenderLine(canvas, frame.HeaderBounds, _columns.Select(column => (column.Header, column.Alignment)), presentation.Header, fill: true);
        if (frame.HeaderBounds.Height > 1)
            canvas.Write(frame.HeaderBounds.X, frame.HeaderBounds.Y + 1, ConsoleTextMetrics.FitToCells(BuildSeparator(), frame.HeaderBounds.Width), presentation.Separator);

        canvas.FillRegion(frame.BodyBounds, presentation.Normal);
        for (int row = 0; row < frame.BodyBounds.Height && frame.ScrollTop + row < _list.State.Count; row++)
        {
            int index = frame.ScrollTop + row;
            RenderRow(canvas, new Rect(frame.BodyBounds.X, frame.BodyBounds.Y + row, frame.BodyBounds.Width, 1), _list.State.Items[index], index == frame.SelectedIndex, presentation);
        }

        _list.RenderScrollbar(canvas, frame.ListFrame, presentation.Scrollbar);
    }

    public UiInteractionFrame BuildInteractionFrame(TableListFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        bool isEnabled = HasItems && frame.BodyBounds.Width > 0 && frame.BodyBounds.Height > 0;
        if (!isEnabled)
            return UiInteractionFrame.Empty;

        var builder = new UiInteractionFrameBuilder()
            .AddFragment(_list.BuildInteractionFragment(frame.ListFrame, 0, isEnabled));

        return builder.SetDefaultFocusTarget(_list.ListTarget).SetKeyboardTarget(_list.ListTarget).Build();
    }

    public (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(ConsoleInputEvent input, TableListFrame frame, UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(route);
        RoutedScrollableListInputResult result = _list.RouteInput(input, frame.ListFrame, route);
        return (result.ListResult, result.UiResult);
    }

    private void RenderRow(IUiCanvas canvas, Rect bounds, T item, bool selected, TableListPresentation presentation)
    {
        int x = bounds.X;
        int remaining = bounds.Width;
        for (int i = 0; i < _columns.Count; i++)
        {
            if (remaining <= 0) break;
            TableColumn<T> column = _columns[i];
            int width = Math.Min(column.Width, remaining);
            CellStyle style = column.Emphasized
                ? selected ? presentation.EmphasizedSelected : presentation.Emphasized
                : selected ? presentation.Selected : presentation.Normal;
            canvas.Write(x, bounds.Y, Fit(column.Value(item), width, column.Alignment), style);
            x += width;
            remaining -= width;
            if (remaining <= 0 || i == _columns.Count - 1) break;
            int separatorWidth = Math.Min(3, remaining);
            canvas.Write(x, bounds.Y, ConsoleTextMetrics.FitToCells(" │ ", separatorWidth), selected ? presentation.Selected : presentation.Normal);
            x += separatorWidth;
            remaining -= separatorWidth;
        }
        if (remaining > 0) canvas.Write(x, bounds.Y, new string(' ', remaining), selected ? presentation.Selected : presentation.Normal);
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
