using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum TableColumnAlignment { Left, Right }

public sealed record TableWidth
{
    private TableWidth(int preferred, int minimum, bool optional, int priority) { Preferred = preferred; Minimum = minimum; IsOptional = optional; Priority = priority; }
    public int Preferred { get; }
    public int Minimum { get; }
    public bool IsOptional { get; }
    public int Priority { get; }
    public static TableWidth Fixed(int width) => new(width, width, false, 0);
    public static TableWidth Flexible(int preferred, int minimum) => Create(preferred, minimum, false, 0);
    public static TableWidth Optional(int preferred, int minimum = 0, int priority = 0) => Create(preferred, minimum, true, priority);
    private static TableWidth Create(int preferred, int minimum, bool optional, int priority)
    {
        if (preferred < 0) throw new ArgumentOutOfRangeException(nameof(preferred));
        if (minimum < 0 || minimum > preferred) throw new ArgumentOutOfRangeException(nameof(minimum));
        return new(preferred, minimum, optional, priority);
    }
}

public sealed class TableColumn<T>
{
    private TableColumn(string header, Func<T, string> value, TableWidth width, TableColumnAlignment alignment, bool emphasized)
        => (Header, Value, Width, Alignment, Emphasized) = (header, value, width, alignment, emphasized);
    public string Header { get; }
    public Func<T, string> Value { get; }
    public TableWidth Width { get; }
    public TableColumnAlignment Alignment { get; }
    public bool Emphasized { get; }
    public static TableColumn<T> Text(string header, Func<T, string> value, int width, TableColumnAlignment alignment = TableColumnAlignment.Left, bool emphasized = false)
        => Text(header, value, TableWidth.Fixed(width), alignment, emphasized);
    public static TableColumn<T> Text(string header, Func<T, string> value, TableWidth width, TableColumnAlignment alignment = TableColumnAlignment.Left, bool emphasized = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(header); ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(width);
        return new(header, value, width, alignment, emphasized);
    }
}

public sealed class TableListDefinition<T> { public required IReadOnlyList<TableColumn<T>> Columns { get; init; } }
public sealed record TableListColumnFrame(string Header, int X, int Width, TableColumnAlignment Alignment);

[Obsolete("Use the theme-backed Render overload.")]
public sealed class TableListPresentation
{
    public required CellStyle Header { get; init; }
    public required CellStyle Separator { get; init; }
    public required CellStyle Normal { get; init; }
    public required CellStyle Selected { get; init; }
    public required CellStyle Emphasized { get; init; }
    public required CellStyle EmphasizedSelected { get; init; }
    public required CellStyle Scrollbar { get; init; }
    public static TableListPresentation Dialog(ConsolePalette palette) => new() { Header = FarDialogStyles.Title, Separator = FarDialogStyles.Border, Normal = FarDialogStyles.Fill, Selected = FarDialogStyles.FocusedInput, Emphasized = FarDialogStyles.Title, EmphasizedSelected = FarDialogStyles.FocusedInput, Scrollbar = FarDialogStyles.Border };
}

public sealed class TableListFrame
{
    internal TableListFrame(Rect bounds, Rect headerBounds, ScrollableListFrame listFrame, IReadOnlyList<TableListColumnFrame> columns)
        => (Bounds, HeaderBounds, BodyBounds, SelectedIndex, ScrollTop, ViewportRows, _listFrame, Columns) = (bounds, headerBounds, listFrame.ContentBounds, listFrame.SelectedIndex, listFrame.ScrollTop, listFrame.ViewportRows, listFrame, columns);
    private readonly ScrollableListFrame _listFrame;
    public Rect Bounds { get; }
    public Rect HeaderBounds { get; }
    public Rect BodyBounds { get; }
    public int SelectedIndex { get; }
    public int ScrollTop { get; }
    public int ViewportRows { get; }
    public IReadOnlyList<TableListColumnFrame> Columns { get; }
    public bool HasScrollbar => _listFrame.Scrollbar is not null;
    internal ScrollableListFrame ListFrame => _listFrame;
}

/// <summary>Theme-backed adaptive selectable table.</summary>
public sealed class TableList<T>
{
    private const int SeparatorWidth = 3;
    private static long _nextComponentId;
    private readonly IReadOnlyList<TableColumn<T>> _columns;
    private readonly RoutedScrollableList<T> _list;
    private TableListPresentation? _presentation;
    public TableList(IReadOnlyList<T> items, TableListDefinition<T> definition, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(definition.Columns);
        if (definition.Columns.Count == 0) throw new ArgumentException("At least one table column is required.", nameof(definition));
        _columns = definition.Columns.ToArray(); var targets = new UiTargetScope($"table-list-{Interlocked.Increment(ref _nextComponentId)}");
        _list = new(new ScrollableListState<T>(items, selectedIndex), targets.Child("body"), targets.Child("scrollbar"));
    }
    public int SelectedIndex => _list.State.SelectedIndex;
    public T? SelectedItem => _list.State.TryGetSelectedItem(out T item) ? item : default;
    public bool HasItems => _list.State.HasItems;
    public int Count => _list.State.Count;
    public bool TryGetSelectedItem(out T item) => _list.State.TryGetSelectedItem(out item);
    public void SetSelectedIndex(int index) => _list.State.SetSelectedIndex(index, Math.Max(1, _lastViewportRows));
    public void ReplaceItems(IReadOnlyList<T> items) => _list.State.ReplaceItems(items, Math.Max(1, _lastViewportRows));
    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey) where TKey : notnull => _list.State.ReplaceItems(items, identityKey, Math.Max(1, _lastViewportRows));
    private int _lastViewportRows = 1;
    public TableListFrame CalculateFrame(Rect bounds)
    {
        int headerHeight = Math.Min(2, Math.Max(0, bounds.Height)); Rect header = new(bounds.X, bounds.Y, bounds.Width, headerHeight);
        Rect body = new(bounds.X, bounds.Y + headerHeight, bounds.Width, Math.Max(0, bounds.Height - headerHeight));
        Rect? scrollbar = _list.State.Count > body.Height && body.Width > 0 && body.Height > 0 ? new(body.Right - 1, body.Y, 1, body.Height) : null;
        Rect content = scrollbar is null ? body : new(body.X, body.Y, Math.Max(0, body.Width - 1), body.Height);
        ScrollableListFrame listFrame = _list.CalculateFrame(content, scrollbar); _lastViewportRows = listFrame.ViewportRows;
        return new(bounds, header, listFrame, CalculateColumns(content));
    }
    public void Render(IUiCanvas canvas, TableListFrame frame)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(frame);
        TableListPresentation? presentation = _presentation;
        RenderLine(canvas, frame.HeaderBounds, frame.Columns, c => c.Header, presentation?.Header ?? FarDialogStyles.Title);
        if (frame.HeaderBounds.Height > 1) canvas.Write(frame.HeaderBounds.X, frame.HeaderBounds.Y + 1, ConsoleTextMetrics.FitToCells(BuildSeparator(frame.Columns), frame.HeaderBounds.Width), presentation?.Separator ?? FarDialogStyles.Border);
        canvas.FillRegion(frame.BodyBounds, presentation?.Normal ?? FarDialogStyles.Fill);
        for (int row = 0; row < frame.BodyBounds.Height && frame.ScrollTop + row < _list.State.Count; row++)
        { int index = frame.ScrollTop + row; RenderRow(canvas, new(frame.BodyBounds.X, frame.BodyBounds.Y + row, frame.BodyBounds.Width, 1), _list.State.Items[index], index == frame.SelectedIndex, frame.Columns); }
        _list.RenderScrollbar(canvas, frame.ListFrame, presentation?.Scrollbar ?? FarDialogStyles.Border);
    }
    [Obsolete("Use the theme-backed Render overload.")]
    public void Render(IUiCanvas canvas, TableListFrame frame, TableListPresentation presentation) { _presentation = presentation; try { Render(canvas, frame); } finally { _presentation = null; } }
    public UiInteractionFrame BuildInteractionFrame(TableListFrame frame) { var builder = new UiInteractionFrameBuilder().AddFragment(BuildInteractionFragment(frame)); if (HasItems && frame.BodyBounds.Width > 0 && frame.BodyBounds.Height > 0) builder.SetDefaultFocusTarget(_list.ListTarget).SetKeyboardTarget(_list.ListTarget); return builder.Build(); }
    public UiInteractionFragment BuildInteractionFragment(TableListFrame frame, int focusOrder = 0) => HasItems ? _list.BuildInteractionFragment(frame.ListFrame, focusOrder, frame.BodyBounds.Width > 0 && frame.BodyBounds.Height > 0) : UiInteractionFragment.Empty;
    public (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(ConsoleInputEvent input, TableListFrame frame, UiInputRouteContext route) { RoutedScrollableListInputResult result = _list.RouteInput(input, frame.ListFrame, route); return (result.ListResult, result.UiResult); }
    public bool IsTargetRoute(UiInputRouteContext route) => _list.IsTargetRoute(route);
    private IReadOnlyList<TableListColumnFrame> CalculateColumns(Rect bounds)
    {
        var visible = _columns.Select((column, index) => new ColumnState(column, index)).ToList();
        int Footprint() => visible.Where(x => x.Visible).Sum(x => x.Width) + Math.Max(0, visible.Count(x => x.Visible) - 1) * SeparatorWidth;
        foreach (ColumnState state in visible.Where(x => !x.Column.Width.IsOptional && x.Column.Width.Minimum < x.Width).OrderByDescending(x => x.Index))
        { int excess = Math.Max(0, Footprint() - bounds.Width); state.Width -= Math.Min(excess, state.Width - state.Column.Width.Minimum); }
        foreach (ColumnState state in visible.Where(x => x.Column.Width.IsOptional).OrderByDescending(x => x.Column.Width.Priority).ThenByDescending(x => x.Index).ToArray())
        { if (Footprint() > bounds.Width) state.Visible = false; }
        int x = bounds.X; var result = new List<TableListColumnFrame>();
        foreach (ColumnState state in visible.Where(x => x.Visible)) { int width = Math.Min(state.Width, Math.Max(0, bounds.Right - x)); if (width <= 0) break; result.Add(new(state.Column.Header, x, width, state.Column.Alignment)); x += width + SeparatorWidth; }
        return result;
    }
    private void RenderRow(IUiCanvas canvas, Rect bounds, T item, bool selected, IReadOnlyList<TableListColumnFrame> columns)
    {
        TableListPresentation? presentation = _presentation; CellStyle baseStyle = selected ? presentation?.Selected ?? FarDialogStyles.FocusedInput : presentation?.Normal ?? FarDialogStyles.Fill; canvas.FillRegion(bounds, baseStyle);
        for (int i = 0; i < columns.Count; i++) { TableListColumnFrame geometry = columns[i]; TableColumn<T> column = _columns.First(x => x.Header == geometry.Header); CellStyle style = column.Emphasized ? selected ? presentation?.EmphasizedSelected ?? FarDialogStyles.FocusedInput : presentation?.Emphasized ?? FarDialogStyles.Title : baseStyle; canvas.Write(geometry.X, bounds.Y, Fit(column.Value(item), geometry.Width, geometry.Alignment), style); if (i + 1 < columns.Count) canvas.Write(geometry.X + geometry.Width, bounds.Y, " │ ", baseStyle); }
    }
    private static void RenderLine(IUiCanvas canvas, Rect bounds, IReadOnlyList<TableListColumnFrame> columns, Func<TableListColumnFrame, string> text, CellStyle style) { if (bounds.Width <= 0 || bounds.Height <= 0) return; canvas.FillRegion(new(bounds.X, bounds.Y, bounds.Width, 1), style); for (int i = 0; i < columns.Count; i++) { TableListColumnFrame c = columns[i]; canvas.Write(c.X, bounds.Y, Fit(text(c), c.Width, c.Alignment), style); if (i + 1 < columns.Count) canvas.Write(c.X + c.Width, bounds.Y, " │ ", style); } if (columns.Count == 1 && columns[0].X + columns[0].Width < bounds.Right) canvas.Write(columns[0].X + columns[0].Width, bounds.Y, ConsoleTextMetrics.FitToCells(" │ ", bounds.Right - (columns[0].X + columns[0].Width)), style); }
    private static string BuildSeparator(IReadOnlyList<TableListColumnFrame> columns) => string.Join("─┼─", columns.Select(c => new string('─', c.Width)));
    private static string Fit(string text, int width, TableColumnAlignment alignment) { string clipped = ConsoleTextMetrics.TruncateEndToCells(text ?? string.Empty, width); int padding = Math.Max(0, width - ConsoleTextMetrics.GetCellWidth(clipped)); return alignment == TableColumnAlignment.Right ? new string(' ', padding) + clipped : clipped + new string(' ', padding); }
    private sealed class ColumnState(TableColumn<T> column, int index) { public TableColumn<T> Column { get; } = column; public int Index { get; } = index; public int Width { get; set; } = column.Width.Preferred; public bool Visible { get; set; } = true; }
}
