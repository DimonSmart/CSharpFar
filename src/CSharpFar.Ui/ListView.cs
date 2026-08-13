using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum ListViewBehavior { Selection, Popup, Overlay }

public sealed class ListViewFrame
{
    internal ListViewFrame(ScrollableListFrame frame) => _frame = frame;
    private readonly ScrollableListFrame _frame;
    public Rect Bounds => _frame.ContentBounds;
    public int SelectedIndex => _frame.SelectedIndex;
    public int ScrollTop => _frame.ScrollTop;
    public int ViewportRows => _frame.ViewportRows;
    public bool HasScrollbar => _frame.Scrollbar is not null;
    public int ItemCount => _frame.ItemCount;
    internal ScrollableListFrame InnerFrame => _frame;
}

/// <summary>Theme-backed selectable one-column list with committed-frame routing.</summary>
public sealed class ListView<T>
{
    private static long _nextId;
    private readonly RoutedScrollableList<T> _list;
    private readonly Func<T, string> _itemText;
    private readonly ListAppearance _appearance;
    private int _viewportRows = 1;

    public ListView(IReadOnlyList<T> items, Func<T, string> itemText, string emptyText = "No items", ListViewBehavior behavior = ListViewBehavior.Selection, ListAppearance appearance = ListAppearance.Dialog, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items); _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText)); EmptyText = emptyText ?? string.Empty; _appearance = appearance;
        var targets = new UiTargetScope($"list-view-{Interlocked.Increment(ref _nextId)}");
        _list = new(new ScrollableListState<T>(items, selectedIndex), targets.Child("items"), targets.Child("scrollbar"), ToOptions(behavior));
    }

    public string EmptyText { get; set; }
    public int SelectedIndex => _list.State.SelectedIndex;
    public T? SelectedItem => _list.State.TryGetSelectedItem(out T item) ? item : default;
    public bool HasItems => _list.State.HasItems;
    public int Count => _list.State.Count;
    public int ScrollTop => _list.State.ScrollTop;
    public IReadOnlyList<T> Items => _list.State.Items;
    public bool TryGetSelectedItem(out T item) => _list.State.TryGetSelectedItem(out item);
    public void SetSelectedIndex(int index) => _list.State.SetSelectedIndex(index, _viewportRows);
    public void SetScrollTop(int scrollTop) => _list.State.ApplyPosition(new ScrollableListPosition(_list.State.Count, _list.State.SelectedIndex, scrollTop), _viewportRows);
    public void ReplaceItems(IReadOnlyList<T> items) => _list.State.ReplaceItems(items, _viewportRows);
    public void ReplaceItems<TKey>(IReadOnlyList<T> items, Func<T, TKey> identityKey) where TKey : notnull => _list.State.ReplaceItems(items, identityKey, _viewportRows);
    public ListViewFrame CalculateFrame(Rect bounds)
    {
        Rect? scrollbar = _list.State.Count > bounds.Height && bounds.Width > 0 && bounds.Height > 0 ? new(bounds.Right - 1, bounds.Y, 1, bounds.Height) : null;
        Rect content = scrollbar is null ? bounds : new(bounds.X, bounds.Y, Math.Max(0, bounds.Width - 1), bounds.Height);
        ScrollableListFrame frame = _list.CalculateFrame(content, scrollbar); _viewportRows = frame.ViewportRows; return new(frame);
    }
    internal void ApplyCommittedFrame(ListViewFrame frame) => _list.ApplyCommittedFrame(frame.InnerFrame);
    public void Render(IUiCanvas canvas, ListViewFrame frame)
    {
        ListAppearanceStyles styles = ListAppearanceStyles.From(_appearance);
        _list.Render(canvas, frame.InnerFrame, new(_itemText, EmptyText, styles.Normal, styles.Selected, styles.Normal));
        _list.RenderScrollbar(canvas, frame.InnerFrame, styles.Scrollbar);
    }
    public UiInteractionFragment BuildInteractionFragment(ListViewFrame frame, int focusOrder = 0) => _list.BuildInteractionFragment(frame.InnerFrame, focusOrder, frame.Bounds.Width > 0 && frame.Bounds.Height > 0);
    public (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(ConsoleInputEvent input, ListViewFrame frame, UiInputRouteContext route) { RoutedScrollableListInputResult result = _list.RouteInput(input, frame.InnerFrame, route); return (result.ListResult, result.UiResult); }
    public bool IsTargetRoute(UiInputRouteContext route) => _list.IsTargetRoute(route);
    internal UiTargetId ListTarget => _list.ListTarget;
    private static RoutedScrollableListOptions ToOptions(ListViewBehavior behavior) => behavior switch
    {
        ListViewBehavior.Selection => RoutedScrollableListOptions.SelectionDialog,
        ListViewBehavior.Popup => RoutedScrollableListOptions.DropdownPopup,
        ListViewBehavior.Overlay => RoutedScrollableListOptions.CommandCompletion,
        _ => throw new ArgumentOutOfRangeException(nameof(behavior)),
    };
}
