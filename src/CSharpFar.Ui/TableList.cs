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
    public static TableWidth Fixed(int width) => Create(width, width, false, 0);
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

public sealed class TableListDefinition<T>
{
    public required IReadOnlyList<TableColumn<T>> Columns { get; init; }
    public Func<T, T, bool>? SectionBreakBetween { get; init; }
}
public sealed record TableListColumnFrame(string Header, int X, int Width, TableColumnAlignment Alignment)
{
    internal int DefinitionIndex { get; init; }
}

public sealed class TableListFrame : ICompositeDialogContentFrame
{
    internal TableListFrame(Rect bounds, Rect headerBounds, ScrollableListFrame listFrame, int selectedIndex, IReadOnlyList<TableListColumnFrame> columns)
        => (Bounds, HeaderBounds, BodyBounds, SelectedIndex, ScrollTop, ViewportRows, _listFrame, Columns) = (bounds, headerBounds, listFrame.ContentBounds, selectedIndex, listFrame.ScrollTop, listFrame.ViewportRows, listFrame, columns);
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
public sealed class TableList<T> : ICompositeDialogContent
{
    private const int SeparatorWidth = 3;
    private static long _nextComponentId;
    private readonly IReadOnlyList<TableColumn<T>> _columns;
    private readonly Func<T, T, bool>? _sectionBreakBetween;
    private readonly RoutedScrollableList<PresentationRow> _list;
    private IReadOnlyList<T> _items;
    private readonly ListAppearance _appearance;
    public TableList(IReadOnlyList<T> items, TableListDefinition<T> definition, int selectedIndex = 0, ListAppearance appearance = ListAppearance.Dialog)
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(definition.Columns);
        if (definition.Columns.Count == 0) throw new ArgumentException("At least one table column is required.", nameof(definition));
        _columns = definition.Columns.ToArray(); _sectionBreakBetween = definition.SectionBreakBetween; _appearance = appearance; _items = items.ToArray(); var targets = new UiTargetScope($"table-list-{Interlocked.Increment(ref _nextComponentId)}");
        IReadOnlyList<PresentationRow> rows = BuildPresentationRows(_items);
        int selectedPresentation = _items.Count == 0 ? 0 : rows.Select((row, index) => (row, index)).First(pair => pair.row.LogicalIndex == Math.Clamp(selectedIndex, 0, _items.Count - 1)).index;
        _list = new(new ScrollableListState<PresentationRow>(rows, selectedPresentation), targets.Child("body"), targets.Child("scrollbar"));
    }
    public int SelectedIndex => _list.State.HasItems ? _list.State.Items[_list.State.SelectedIndex].LogicalIndex : -1;
    public T? SelectedItem => TryGetSelectedItem(out T item) ? item : default;
    public bool HasItems => _items.Count != 0;
    public int Count => _items.Count;
    public bool TryGetSelectedItem(out T item)
    {
        if (SelectedIndex >= 0) { item = _items[SelectedIndex]; return true; }
        item = default!; return false;
    }
    public void SetSelectedIndex(int index) => _list.State.SetSelectedIndex(PresentationIndex(index), Math.Max(1, _lastViewportRows));
    public void ReplaceItems(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        bool hadSelection = TryGetSelectedItem(out T selected);
        _items = items.ToArray();
        int selectedIndex = hadSelection ? _items.Select((item, index) => (item, index)).FirstOrDefault(pair => EqualityComparer<T>.Default.Equals(pair.item, selected)).index : 0;
        _list.State.ResetItems(BuildPresentationRows(_items), _items.Count == 0 ? 0 : PresentationIndex(selectedIndex));
    }
    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(identityKey);
        bool hadSelection = TryGetSelectedItem(out T selected);
        TKey selectedKey = hadSelection ? identityKey(selected) : default!;
        _items = items.ToArray();
        int selectedIndex = hadSelection ? _items.Select((item, index) => (item, index)).FirstOrDefault(pair => EqualityComparer<TKey>.Default.Equals(identityKey(pair.item), selectedKey)).index : 0;
        _list.State.ResetItems(BuildPresentationRows(_items), _items.Count == 0 ? 0 : PresentationIndex(selectedIndex));
    }
    private int _lastViewportRows = 1;
    public TableListFrame CalculateFrame(Rect bounds)
    {
        int headerHeight = Math.Min(2, Math.Max(0, bounds.Height)); Rect header = new(bounds.X, bounds.Y, bounds.Width, headerHeight);
        Rect body = new(bounds.X, bounds.Y + headerHeight, bounds.Width, Math.Max(0, bounds.Height - headerHeight));
        Rect? scrollbar = _list.State.Count > body.Height && body.Width > 0 && body.Height > 0 ? new(body.Right - 1, body.Y, 1, body.Height) : null;
        Rect content = scrollbar is null ? body : new(body.X, body.Y, Math.Max(0, body.Width - 1), body.Height);
        ScrollableListFrame listFrame = _list.CalculateFrame(content, scrollbar); _lastViewportRows = listFrame.ViewportRows;
        return new(bounds, header, listFrame, listFrame.SelectedIndex < 0 ? -1 : _list.State.Items[listFrame.SelectedIndex].LogicalIndex, CalculateColumns(content));
    }
    public void ApplyCommittedFrame(TableListFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _list.ApplyCommittedFrame(frame.ListFrame);
    }
    public void Render(IUiCanvas canvas, TableListFrame frame)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(frame);
        ListAppearanceStyles styles = ListAppearanceStyles.From(_appearance);
        RenderLine(canvas, frame.HeaderBounds, frame.Columns, c => c.Header, styles.Header);
        if (frame.HeaderBounds.Height > 1) canvas.Write(frame.HeaderBounds.X, frame.HeaderBounds.Y + 1, ConsoleTextMetrics.FitToCells(BuildSeparator(frame.Columns), frame.HeaderBounds.Width), styles.Border);
        canvas.FillRegion(frame.BodyBounds, styles.Normal);
        for (int row = 0; row < frame.BodyBounds.Height && frame.ScrollTop + row < _list.State.Count; row++)
        {
            int index = frame.ScrollTop + row; PresentationRow presentation = _list.State.Items[index];
            Rect rowBounds = new(frame.BodyBounds.X, frame.BodyBounds.Y + row, frame.BodyBounds.Width, 1);
            if (presentation.IsSectionBreak)
            {
                canvas.FillRegion(rowBounds, styles.Border);
                canvas.Write(rowBounds.X, rowBounds.Y, ConsoleTextMetrics.FitToCells(BuildSeparator(frame.Columns), rowBounds.Width), styles.Border);
            }
            else RenderRow(canvas, rowBounds, presentation.Item, index == frame.ListFrame.SelectedIndex, frame.Columns, styles);
        }
        _list.RenderScrollbar(canvas, frame.ListFrame, styles.Scrollbar);
    }
    public UiInteractionFrame BuildInteractionFrame(TableListFrame frame) { var builder = new UiInteractionFrameBuilder().AddFragment(BuildInteractionFragment(frame)); if (HasItems && frame.BodyBounds.Width > 0 && frame.BodyBounds.Height > 0) builder.SetDefaultFocusTarget(_list.ListTarget).SetKeyboardTarget(_list.ListTarget); return builder.Build(); }
    public UiInteractionFragment BuildInteractionFragment(TableListFrame frame, int focusOrder = 0) => HasItems && frame.BodyBounds.Width > 0 && frame.BodyBounds.Height > 0 ? _list.BuildInteractionFragment(frame.ListFrame, focusOrder) : UiInteractionFragment.Empty;
    public (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(ConsoleInputEvent input, TableListFrame frame, UiInputRouteContext route)
    {
        if (input is MouseConsoleInputEvent mouse && route.Target == _list.ListTarget && frame.BodyBounds.Contains(mouse.X, mouse.Y))
        {
            int presentationIndex = frame.ListFrame.ScrollTop + mouse.Y - frame.BodyBounds.Y;
            if (presentationIndex >= 0 && presentationIndex < _list.State.Count && _list.State.Items[presentationIndex].IsSectionBreak)
                return (ScrollableListInputResult.Handled, UiInputResult.HandledResult);
        }
        RoutedScrollableListInputResult result = _list.RouteInput(input, frame.ListFrame, route);
        if (result.ListResult.IsHandled)
            SkipSectionBreak(input, frame.ListFrame);
        return (result.ListResult, result.UiResult);
    }
    public bool IsTargetRoute(UiInputRouteContext route) => _list.IsTargetRoute(route);
    ICompositeDialogContentFrame ICompositeDialogContent.CalculateFrame(Rect bounds) => CalculateFrame(bounds);
    void ICompositeDialogContent.Render(IUiCanvas canvas, ICompositeDialogContentFrame frame) => Render(canvas, RequireFrame(frame));
    UiInteractionFragment ICompositeDialogContent.BuildInteractionFragment(ICompositeDialogContentFrame frame, int focusOrder) => BuildInteractionFragment(RequireFrame(frame), focusOrder);
    CompositeDialogContentInputResult ICompositeDialogContent.RouteInput(ConsoleInputEvent input, ICompositeDialogContentFrame frame, UiInputRouteContext route)
    {
        (ScrollableListInputResult semantic, UiInputResult uiResult) = RouteInput(input, RequireFrame(frame), route);
        return new(semantic.Kind switch
        {
            ScrollableListInputResultKind.SelectionChanged => CompositeDialogContentEventKind.SelectionChanged,
            ScrollableListInputResultKind.Confirmed => CompositeDialogContentEventKind.Confirmed,
            _ => CompositeDialogContentEventKind.NotHandled,
        }, uiResult, IsTargetRoute(route));
    }
    void ICompositeDialogContent.ApplyCommittedFrame(ICompositeDialogContentFrame frame) => ApplyCommittedFrame(RequireFrame(frame));
    private static TableListFrame RequireFrame(ICompositeDialogContentFrame frame) => frame as TableListFrame ?? throw new ArgumentException("Frame belongs to a different composite content component.", nameof(frame));
    private IReadOnlyList<PresentationRow> BuildPresentationRows(IReadOnlyList<T> items)
    {
        var rows = new List<PresentationRow>(items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            if (index > 0 && _sectionBreakBetween?.Invoke(items[index - 1], items[index]) == true)
                rows.Add(PresentationRow.SectionBreak);
            rows.Add(new(items[index], index));
        }
        return rows;
    }
    private int PresentationIndex(int logicalIndex)
    {
        if (logicalIndex < 0 || logicalIndex >= _items.Count) return 0;
        for (int index = 0; index < _list.State.Count; index++)
            if (!_list.State.Items[index].IsSectionBreak && _list.State.Items[index].LogicalIndex == logicalIndex) return index;
        return logicalIndex;
    }
    private void SkipSectionBreak(ConsoleInputEvent input, ScrollableListFrame frame)
    {
        int selected = _list.State.SelectedIndex;
        if (selected < 0 || !_list.State.Items[selected].IsSectionBreak) return;
        int direction = input is KeyConsoleInputEvent { Key.Key: ConsoleKey.UpArrow or ConsoleKey.PageUp } ? -1 : 1;
        int index = selected;
        while (index >= 0 && index < _list.State.Count && _list.State.Items[index].IsSectionBreak) index += direction;
        if (index < 0 || index >= _list.State.Count)
        {
            index = selected - direction;
            while (index >= 0 && index < _list.State.Count && _list.State.Items[index].IsSectionBreak) index -= direction;
        }
        _list.State.SetSelectedIndex(index, frame.ViewportRows);
    }
    private IReadOnlyList<TableListColumnFrame> CalculateColumns(Rect bounds)
    {
        var visible = _columns.Select((column, index) => new ColumnState(column, index)).ToList();
        int Footprint() => visible.Where(x => x.Visible).Sum(x => x.Width) + Math.Max(0, visible.Count(x => x.Visible) - 1) * SeparatorWidth;
        foreach (ColumnState state in visible.Where(x => x.Column.Width.Minimum < x.Width).OrderByDescending(x => x.Index))
        { int excess = Math.Max(0, Footprint() - bounds.Width); state.Width -= Math.Min(excess, state.Width - state.Column.Width.Minimum); }
        foreach (ColumnState state in visible.Where(x => x.Column.Width.IsOptional).OrderBy(x => x.Column.Width.Priority).ThenByDescending(x => x.Index).ToArray())
        { if (Footprint() > bounds.Width) state.Visible = false; }
        int x = bounds.X; var result = new List<TableListColumnFrame>();
        foreach (ColumnState state in visible.Where(x => x.Visible)) { int width = Math.Min(state.Width, Math.Max(0, bounds.Right - x)); if (width <= 0) break; result.Add(new(state.Column.Header, x, width, state.Column.Alignment) { DefinitionIndex = state.Index }); x += width + SeparatorWidth; }
        return result;
    }
    private void RenderRow(IUiCanvas canvas, Rect bounds, T item, bool selected, IReadOnlyList<TableListColumnFrame> columns, ListAppearanceStyles styles)
    {
        CellStyle baseStyle = selected ? styles.Selected : styles.Normal; canvas.FillRegion(bounds, baseStyle);
        for (int i = 0; i < columns.Count; i++) { TableListColumnFrame geometry = columns[i]; TableColumn<T> column = _columns[geometry.DefinitionIndex]; CellStyle style = column.Emphasized ? selected ? styles.SelectedEmphasized : styles.Emphasized : baseStyle; canvas.Write(geometry.X, bounds.Y, Fit(column.Value(item), geometry.Width, geometry.Alignment), style); if (i + 1 < columns.Count) canvas.Write(geometry.X + geometry.Width, bounds.Y, " │ ", baseStyle); }
    }
    private static void RenderLine(IUiCanvas canvas, Rect bounds, IReadOnlyList<TableListColumnFrame> columns, Func<TableListColumnFrame, string> text, CellStyle style) { if (bounds.Width <= 0 || bounds.Height <= 0) return; canvas.FillRegion(new(bounds.X, bounds.Y, bounds.Width, 1), style); for (int i = 0; i < columns.Count; i++) { TableListColumnFrame c = columns[i]; canvas.Write(c.X, bounds.Y, Fit(text(c), c.Width, c.Alignment), style); if (i + 1 < columns.Count) canvas.Write(c.X + c.Width, bounds.Y, " │ ", style); } if (columns.Count == 1 && columns[0].X + columns[0].Width < bounds.Right) canvas.Write(columns[0].X + columns[0].Width, bounds.Y, ConsoleTextMetrics.FitToCells(" │ ", bounds.Right - (columns[0].X + columns[0].Width)), style); }
    private static string BuildSeparator(IReadOnlyList<TableListColumnFrame> columns) => string.Join("─┼─", columns.Select(c => new string('─', c.Width)));
    private static string Fit(string text, int width, TableColumnAlignment alignment) { string clipped = ConsoleTextMetrics.TruncateEndToCells(text ?? string.Empty, width); int padding = Math.Max(0, width - ConsoleTextMetrics.GetCellWidth(clipped)); return alignment == TableColumnAlignment.Right ? new string(' ', padding) + clipped : clipped + new string(' ', padding); }
    private sealed class ColumnState(TableColumn<T> column, int index) { public TableColumn<T> Column { get; } = column; public int Index { get; } = index; public int Width { get; set; } = column.Width.Preferred; public bool Visible { get; set; } = true; }
    private readonly record struct PresentationRow(T Item, int LogicalIndex, bool IsSectionBreak = false)
    {
        public static PresentationRow SectionBreak => new(default!, -1, true);
    }
}
