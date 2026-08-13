using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Theme-backed selectable one-column list with committed-frame routing.</summary>
public sealed class ListView<T>
{
    private static long _nextId;
    private readonly RoutedScrollableList<T> _list;
    private readonly Func<T, string> _itemText;
    private readonly string _emptyText;
    private int _viewportRows = 1;
    public ListView(IReadOnlyList<T> items, Func<T, string> itemText, string emptyText = "No items", RoutedScrollableListOptions? behavior = null, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items); _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText)); _emptyText = emptyText ?? string.Empty;
        var targets = new UiTargetScope($"list-view-{Interlocked.Increment(ref _nextId)}");
        _list = new(new ScrollableListState<T>(items, selectedIndex), targets.Child("items"), targets.Child("scrollbar"), behavior);
    }
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
    public ScrollableListFrame CalculateFrame(Rect bounds)
    {
        Rect? scrollbar = _list.State.Count > bounds.Height && bounds.Width > 0 && bounds.Height > 0 ? new(bounds.Right - 1, bounds.Y, 1, bounds.Height) : null;
        Rect content = scrollbar is null ? bounds : new(bounds.X, bounds.Y, Math.Max(0, bounds.Width - 1), bounds.Height);
        ScrollableListFrame frame = _list.CalculateFrame(content, scrollbar); _viewportRows = frame.ViewportRows; return frame;
    }
    public void Render(IUiCanvas canvas, ScrollableListFrame frame)
    {
        _list.Render(canvas, frame, new(_itemText, _emptyText, FarDialogStyles.Fill, FarDialogStyles.FocusedInput, FarDialogStyles.Fill));
        _list.RenderScrollbar(canvas, frame, FarDialogStyles.Border);
    }
    public UiInteractionFragment BuildInteractionFragment(ScrollableListFrame frame, int focusOrder = 0) => _list.BuildInteractionFragment(frame, focusOrder, frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0);
    public (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(ConsoleInputEvent input, ScrollableListFrame frame, UiInputRouteContext route) { RoutedScrollableListInputResult result = _list.RouteInput(input, frame, route); return (result.ListResult, result.UiResult); }
    public bool IsTargetRoute(UiInputRouteContext route) => _list.IsTargetRoute(route);
    public UiTargetId ListTarget => _list.ListTarget;
}
