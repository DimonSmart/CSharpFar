using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed record SelectionListDialogResult<T>(
    bool IsConfirmed,
    T? SelectedItem,
    int SelectedIndex);

public sealed class SelectionListDialog<T>
{
    private const int DefaultMaxVisibleRows = 15;
    private const int DefaultMinWidth = 20;
    private static readonly UiTargetScope Targets = new("selection-list");
    private static readonly UiTargetId ListTarget = Targets.Child("list");
    private static readonly UiTargetId ScrollbarTarget = Targets.Child("list.scrollbar");

    private readonly RoutedScrollableList<T> _list;
    private readonly Func<T, string> _itemText;
    private readonly string _title;
    private readonly DialogFrameRenderer _frameRenderer = new();
    private Action<T, int>? _selectionChanged;
    private string? _emptyText;

    public SelectionListDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title)
    {
        _list = new RoutedScrollableList<T>(
            new ScrollableListState<T>(items),
            ListTarget,
            ScrollbarTarget);
        _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
        _title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public int SelectedIndex
    {
        get => _list.State.SelectedIndex;
        set => _list.State.SetSelectedIndex(value, 1);
    }

    public int ScrollTop
    {
        get => _list.State.ScrollTop;
        set => _list.State.SetFromInput(_list.State.SelectedIndex, value, 1);
    }

    public int MaxVisibleRows { get; set; } = DefaultMaxVisibleRows;

    public int? MaxWidth { get; set; }

    public int? MaxHeight { get; set; }

    public string? EmptyText
    {
        get => _emptyText;
        set => _emptyText = value;
    }

    public bool DoubleBorder { get; set; }

    public Action<T, int>? SelectionChanged
    {
        get => _selectionChanged;
        set => _selectionChanged = value;
    }

    public SelectionListDialogResult<T> Show(ModalDialogHost modalDialogs)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);
        bool initialSelectionNotified = false;
        return modalDialogs.RunInteractive<SelectionListFrame, SelectionListInput, SelectionListDialogResult<T>>(
            (context, _) =>
            {
                var frameLayout = CalculateLayout(context.Size);
                var list = _list.CalculateFrame(frameLayout.ContentBounds, frameLayout.ScrollbarBounds);
                var frame = new SelectionListFrame(frameLayout, list);
                RenderLayer(context.Canvas, frame);
                return frame;
            },
            frame => new UiInteractionFrameBuilder()
                .AddFragment(_list.BuildInteractionFragment(frame.List, 0, _list.State.HasItems && frame.List.ContentBounds.Width > 0 && frame.List.ContentBounds.Height > 0))
                .SetDefaultFocusTarget(_list.State.HasItems && frame.Layout.ContentBounds.Width > 0 && frame.Layout.ContentBounds.Height > 0 ? ListTarget : null)
                .Build(),
            (input, frame, route) =>
            {
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                    return (new SelectionListInput(input, ScrollableListInputResult.NotHandled), UiInputResult.HandledResult);

                RoutedScrollableListInputResult routed = _list.RouteInput(
                    input,
                    frame.List,
                    route,
                    confirmOnDoubleClick: true);
                return (new SelectionListInput(input, routed.ListResult), routed.UiResult);
            },
            (_, semantic) =>
            {
                if (semantic.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
                    NotifySelectionChanged();

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 } ||
                    semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed && _list.State.HasItems ||
                    semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } && !_list.State.HasItems)
                {
                    return ModalDialogLoopResult<SelectionListDialogResult<T>>.Complete(
                        _list.State.HasItems && semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed
                            ? Confirmed()
                            : Cancelled());
                }

                return ModalDialogLoopResult<SelectionListDialogResult<T>>.ContinueNoChange;
            },
            applyCommittedFrame: frame =>
            {
                if (_list.State.HasItems && !initialSelectionNotified)
                {
                    NotifySelectionChanged();
                    initialSelectionNotified = true;
                }
            });
    }

    private SelectionListDialogResult<T> Confirmed() =>
        new(true, _list.State.Items[SelectedIndex], SelectedIndex);

    private static SelectionListDialogResult<T> Cancelled() =>
        new(false, default, -1);

    private void NotifySelectionChanged() =>
        _selectionChanged?.Invoke(_list.State.Items[_list.State.SelectedIndex], _list.State.SelectedIndex);

    private void RenderLayer(IUiCanvas screen, SelectionListFrame frame)
    {
        var palette = UiTheme.Current;
        var layout = frame.Layout;

        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputField(palette);
        var emptyStyle = PaletteStyles.DialogFill(palette);
        var scrollState = frame.List.ItemCount > frame.List.ViewportRows ? new ScrollState { TotalItems = frame.List.ItemCount, ViewportItems = frame.List.ViewportRows, FirstVisibleIndex = frame.List.ScrollTop } : null;

        _frameRenderer.RenderFrame(
            screen,
            layout.Bounds,
            _title,
            DoubleBorder,
            PaletteStyles.DialogPopupOptions(palette),
            scrollState,
            (_, _) => _list.Render(screen, frame.List, new(_itemText, EmptyText ?? string.Empty, normalStyle, selectedStyle, emptyStyle)));

    }

    private SelectionListLayout CalculateLayout(ConsoleSize size)
    {
        int itemWidth = _list.State.Count == 0 ? ConsoleTextMetrics.GetCellWidth(EmptyText ?? string.Empty) : _list.State.Items.Max(item => ConsoleTextMetrics.GetCellWidth(_itemText(item)));
        int contentWidth = Math.Max(DefaultMinWidth, Math.Max(itemWidth, ConsoleTextMetrics.GetCellWidth(_title)) + 2);
        int maxWidth = MaxWidth.HasValue ? Math.Min(MaxWidth.Value, size.Width) : size.Width - 2;
        contentWidth = Math.Min(contentWidth, Math.Max(DefaultMinWidth, maxWidth - 2));

        int maxRows = Math.Max(1, Math.Min(MaxVisibleRows, MaxHeight.GetValueOrDefault(size.Height) - 2));
        int visibleRows = Math.Min(Math.Max(1, _list.State.Count == 0 ? 1 : _list.State.Count), Math.Max(1, Math.Min(maxRows, size.Height - 2)));
        int width = Math.Min(size.Width, contentWidth + 2);
        int height = Math.Min(size.Height, visibleRows + 2);
        Rect bounds = UiLayout.Center(size, width, height);
        Rect contentBounds = UiLayout.Inset(bounds, 1, 1);
        return new SelectionListLayout(
            bounds,
            contentBounds,
            contentBounds.Width > 0 && contentBounds.Height > 0 && _list.State.Count > contentBounds.Height
                ? new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height)
                : null,
            contentBounds.Height);
    }

    private readonly record struct SelectionListLayout(
        Rect Bounds,
        Rect ContentBounds,
        Rect? ScrollbarBounds,
        int VisibleRows);

    private readonly record struct SelectionListFrame(
        SelectionListLayout Layout,
        ScrollableListFrame List);

    private readonly record struct SelectionListInput(
        ConsoleInputEvent Input,
        ScrollableListInputResult ListResult);
}
