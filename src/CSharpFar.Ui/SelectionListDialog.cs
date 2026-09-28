using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record SelectionListDialogResult<T>(
    bool IsConfirmed,
    T? SelectedItem,
    int SelectedIndex);

internal sealed class SelectionListDialog<T>
{
    private const int DefaultMaxVisibleRows = 15;
    private const int DefaultMinWidth = 20;
    private const int BorderChrome = 2;
    private const string FilterLabel = "Filter: ";

    private readonly IReadOnlyList<SelectionEntry> _allItems;
    private readonly ListView<SelectionEntry> _list;
    private readonly Func<T, string> _itemText;
    private readonly CommandLineState _filter = new();
    private readonly string _title;
    private readonly DialogAppearance _appearance;
    private readonly DialogFrameRenderer _frameRenderer = new();
    private Action<T, int>? _selectionChanged;
    private Func<T, string>? _searchText;
    private string _emptyText = "No items";

    public SelectionListDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title,
        DialogAppearance appearance = DialogAppearance.Popup)
    {
        ArgumentNullException.ThrowIfNull(items);
        _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
        _title = title ?? throw new ArgumentNullException(nameof(title));
        _appearance = appearance;
        _allItems = items
            .Select((item, index) => new SelectionEntry(item, index))
            .ToArray();
        _list = new ListView<SelectionEntry>(
            _allItems,
            entry => _itemText(entry.Item),
            behavior: ListViewBehavior.Selection,
            appearance: appearance == DialogAppearance.Popup ? ListAppearance.Menu : ListAppearance.Dialog);
    }

    public int SelectedIndex
    {
        get => _list.TryGetSelectedItem(out var entry) ? entry.OriginalIndex : -1;
        set
        {
            if (_allItems.Count == 0)
                return;

            int originalIndex = Math.Clamp(value, 0, _allItems.Count - 1);
            int filteredIndex = FindFilteredIndex(originalIndex);
            if (filteredIndex >= 0)
                _list.SetSelectedIndex(filteredIndex);
        }
    }

    public int ScrollTop
    {
        get => _list.ScrollTop;
        set => _list.SetScrollTop(value);
    }

    public int MaxVisibleRows { get; set; } = DefaultMaxVisibleRows;

    public int? MaxWidth { get; set; }

    public int? MaxHeight { get; set; }

    public string? EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value ?? string.Empty;
            UpdateEmptyText();
        }
    }

    public bool DoubleBorder { get; set; }

    public bool EnableFilter { get; set; }

    public Func<T, string>? SearchText
    {
        get => _searchText;
        set => _searchText = value;
    }

    public Action<T, int>? SelectionChanged
    {
        get => _selectionChanged;
        set => _selectionChanged = value;
    }

    public SelectionListDialogResult<T> Show(ModalDialogHost modalDialogs)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);
        UpdateEmptyText();
        bool initialSelectionNotified = false;
        return modalDialogs.RunInteractive<SelectionListFrame, SelectionListInput, SelectionListDialogResult<T>>(
            (context, _) =>
            {
                var frameLayout = CalculateLayout(context.Size);
                var list = _list.CalculateFrame(frameLayout.ListBounds);
                var frame = new SelectionListFrame(frameLayout, list);
                RenderLayer(context.Canvas, frame);
                return frame;
            },
            frame => new UiInteractionFrameBuilder()
                .AddFragment(_list.BuildInteractionFragment(frame.List, 0))
                .Build(),
            (input, frame, route) =>
            {
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                    return (new SelectionListInput(input, ScrollableListInputResult.NotHandled, IsFilterInput: false), UiInputResult.HandledResult);

                if (EnableFilter &&
                    input is KeyConsoleInputEvent { Key: var key } &&
                    IsFilterInputKey(key))
                {
                    return (new SelectionListInput(input, ScrollableListInputResult.NotHandled, IsFilterInput: true), UiInputResult.HandledResult);
                }

                var routed = _list.RouteInput(
                    input,
                    frame.List,
                    route);
                return (new SelectionListInput(input, routed.Semantic, IsFilterInput: false), routed.UiResult);
            },
            (_, semantic) =>
            {
                if (semantic.IsFilterInput &&
                    semantic.Input is KeyConsoleInputEvent { Key: var filterKey })
                {
                    int previousIndex = SelectedIndex;
                    string before = _filter.Text;
                    string? error = null;
                    SingleLineTextInput.HandleKey(_filter, filterKey, ref error);
                    if (string.Equals(before, _filter.Text, StringComparison.Ordinal))
                        return ModalDialogLoopResult<SelectionListDialogResult<T>>.ContinueNoChange;

                    ApplyFilter(previousIndex);
                    if (_list.HasItems && SelectedIndex != previousIndex)
                        NotifySelectionChanged();
                    return ModalDialogLoopResult<SelectionListDialogResult<T>>.ContinueChanged;
                }

                if (semantic.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
                    NotifySelectionChanged();

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                {
                    return ModalDialogLoopResult<SelectionListDialogResult<T>>.Complete(Cancelled());
                }

                if (semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed && _list.HasItems)
                {
                    return ModalDialogLoopResult<SelectionListDialogResult<T>>.Complete(Confirmed());
                }

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } && !_list.HasItems)
                {
                    return EnableFilter
                        ? ModalDialogLoopResult<SelectionListDialogResult<T>>.ContinueNoChange
                        : ModalDialogLoopResult<SelectionListDialogResult<T>>.Complete(Cancelled());
                }

                return ModalDialogLoopResult<SelectionListDialogResult<T>>.ContinueNoChange;
            },
            applyCommittedFrame: frame =>
            {
                _list.ApplyCommittedFrame(frame.List);
                if (_list.HasItems && !initialSelectionNotified)
                {
                    NotifySelectionChanged();
                    initialSelectionNotified = true;
                }
            });
    }

    private SelectionListDialogResult<T> Confirmed()
    {
        var entry = _list.Items[_list.SelectedIndex];
        return new SelectionListDialogResult<T>(true, entry.Item, entry.OriginalIndex);
    }

    private static SelectionListDialogResult<T> Cancelled() =>
        new(false, default, -1);

    private void NotifySelectionChanged()
    {
        if (_list.TryGetSelectedItem(out var entry))
            _selectionChanged?.Invoke(entry.Item, entry.OriginalIndex);
    }

    private void ApplyFilter(int previousOriginalIndex)
    {
        IReadOnlyList<SelectionEntry> filteredItems;
        if (!EnableFilter || _filter.Text.Length == 0)
        {
            filteredItems = _allItems;
        }
        else
        {
            string filter = _filter.Text;
            filteredItems = _allItems
                .Where(entry => SearchTextFor(entry.Item).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        _list.ReplaceItems(filteredItems);
        if (filteredItems.Count > 0)
        {
            int preservedIndex = previousOriginalIndex >= 0
                ? FindFilteredIndex(previousOriginalIndex)
                : -1;
            _list.SetSelectedIndex(preservedIndex >= 0 ? preservedIndex : 0);
        }

        UpdateEmptyText();
    }

    private int FindFilteredIndex(int originalIndex)
    {
        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.Items[index].OriginalIndex == originalIndex)
                return index;
        }

        return -1;
    }

    private string SearchTextFor(T item) =>
        (_searchText ?? _itemText)(item) ?? string.Empty;

    private void UpdateEmptyText() =>
        _list.EmptyText = EnableFilter && _filter.Text.Length > 0
            ? "No matches"
            : _emptyText;

    private void RenderLayer(IUiCanvas screen, SelectionListFrame frame)
    {
        using IDisposable appearanceScope = DialogStyles.UseAppearance(_appearance);
        var layout = frame.Layout;
        PopupRenderOptions renderOptions = _appearance == DialogAppearance.Popup
            ? MenuPopupOptions()
            : DialogStyles.PopupOptions;

        _frameRenderer.RenderFrame(
            screen,
            layout.Bounds,
            _title,
            DoubleBorder,
            renderOptions,
            (_, _) =>
            {
                if (layout.FilterBounds is { } filterBounds)
                    RenderFilter(screen, filterBounds);
                _list.Render(screen, frame.List);
            });
    }

    private void RenderFilter(IUiCanvas screen, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        int labelWidth = Math.Min(bounds.Width, ConsoleTextMetrics.GetCellWidth(FilterLabel));
        screen.Write(
            bounds.X,
            bounds.Y,
            ConsoleTextMetrics.FitToCells(FilterLabel, labelWidth),
            DialogStyles.Fill);
        int inputWidth = Math.Max(0, bounds.Width - labelWidth);
        if (inputWidth > 0)
        {
            SingleLineTextInput.Render(
                screen,
                bounds.X + labelWidth,
                bounds.Y,
                inputWidth,
                _filter,
                DialogStyles.FocusedInput,
                DialogStyles.FocusedInput);
        }
    }

    private SelectionListLayout CalculateLayout(ConsoleSize size)
    {
        int desiredRows = Math.Min(
            Math.Max(1, MaxVisibleRows),
            Math.Max(1, _list.Count == 0 ? 1 : _list.Count));
        int filterRows = EnableFilter ? 1 : 0;
        int verticalChrome = BorderChrome + ListDialogLayoutMetrics.VerticalContentChrome + filterRows;
        int maxOuterHeight = MaxHeight.HasValue
            ? Math.Min(Math.Max(0, MaxHeight.Value), size.Height)
            : size.Height;
        int availableRows = Math.Max(0, maxOuterHeight - verticalChrome);
        int visibleRows = Math.Min(desiredRows, availableRows);
        int height = Math.Min(maxOuterHeight, desiredRows + verticalChrome);

        int itemWidth = _allItems.Count == 0
            ? ConsoleTextMetrics.GetCellWidth(EmptyText ?? string.Empty)
            : _allItems.Max(entry => ConsoleTextMetrics.GetCellWidth(_itemText(entry.Item)));
        int filterWidth = EnableFilter ? ConsoleTextMetrics.GetCellWidth(FilterLabel) + DefaultMinWidth : 0;
        int textViewportWidth = Math.Max(
            DefaultMinWidth,
            Math.Max(
                Math.Max(itemWidth, ConsoleTextMetrics.GetCellWidth(_title)) + 2,
                filterWidth));
        bool needsScrollbar = visibleRows > 0 && _list.Count > visibleRows;
        int horizontalChrome = BorderChrome + ListDialogLayoutMetrics.HorizontalContentChrome;
        int naturalWidth = textViewportWidth + horizontalChrome + (needsScrollbar ? 1 : 0);
        int width;
        if (MaxWidth.HasValue)
        {
            int maxOuterWidth = Math.Min(Math.Max(0, MaxWidth.Value), size.Width);
            width = Math.Min(naturalWidth, maxOuterWidth);
        }
        else
        {
            int maxOuterWidth = Math.Max(0, size.Width - BorderChrome);
            int availableTextViewportWidth = maxOuterWidth - horizontalChrome - (needsScrollbar ? 1 : 0);
            int constrainedTextViewportWidth = Math.Min(
                textViewportWidth,
                Math.Max(DefaultMinWidth, availableTextViewportWidth));
            width = Math.Min(
                size.Width,
                constrainedTextViewportWidth + horizontalChrome + (needsScrollbar ? 1 : 0));
        }

        Rect bounds = UiLayout.Center(size, width, height);
        Rect frameContentBounds = UiLayout.Inset(bounds, 1, 1);
        Rect paddedBounds = ListDialogLayoutMetrics.InsetContent(frameContentBounds);
        Rect? filterBounds = null;
        Rect listBounds = paddedBounds;
        if (EnableFilter && paddedBounds.Height > 0)
        {
            filterBounds = new Rect(paddedBounds.X, paddedBounds.Y, paddedBounds.Width, 1);
            listBounds = new Rect(
                paddedBounds.X,
                paddedBounds.Y + 1,
                paddedBounds.Width,
                Math.Max(0, paddedBounds.Height - 1));
        }

        return new SelectionListLayout(bounds, filterBounds, listBounds);
    }

    private static bool IsFilterInputKey(ConsoleKeyInfo key)
    {
        bool printable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        return printable || key.Key == ConsoleKey.Backspace;
    }

    private readonly record struct SelectionEntry(T Item, int OriginalIndex);

    private readonly record struct SelectionListLayout(
        Rect Bounds,
        Rect? FilterBounds,
        Rect ListBounds);

    private static PopupRenderOptions MenuPopupOptions()
    {
        ListAppearanceStyles styles = ListAppearanceStyles.From(ListAppearance.Menu);
        return new() { BorderStyle = styles.Border, BackgroundStyle = styles.Normal, ShadowStyle = DialogStyles.Shadow, TitleStyle = styles.Header };
    }

    private readonly record struct SelectionListFrame(
        SelectionListLayout Layout,
        ListViewFrame List);

    private readonly record struct SelectionListInput(
        ConsoleInputEvent Input,
        ScrollableListInputResult ListResult,
        bool IsFilterInput);
}
