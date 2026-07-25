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
    private static readonly UiTargetId ListTarget = new("selection-list.list");
    private static readonly UiTargetId ScrollbarTarget = new("selection-list.list.scrollbar");

    private readonly RoutedScrollableList<T> _list;
    private readonly string _title;
    private readonly DialogFrameRenderer _frameRenderer = new();
    private Action<T, int>? _selectionChanged;

    public SelectionListDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title)
    {
        _list = new RoutedScrollableList<T>(
            new ScrollableList<T>(items, itemText),
            ListTarget,
            ScrollbarTarget);
        _title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public int SelectedIndex
    {
        get => _list.List.SelectedIndex;
        set => _list.List.SelectedIndex = value;
    }

    public int ScrollTop
    {
        get => _list.List.ScrollTop;
        set => _list.List.ScrollTop = value;
    }

    public int MaxVisibleRows { get; set; } = DefaultMaxVisibleRows;

    public int? MaxWidth { get; set; }

    public int? MaxHeight { get; set; }

    public string? EmptyText
    {
        get => _list.List.EmptyText;
        set => _list.List.EmptyText = value;
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
                var listState = _list.CalculateFrame(frameLayout.VisibleRows, frameLayout.ScrollbarBounds);
                var frame = new SelectionListFrame(frameLayout, listState);
                RenderLayer(context.Canvas, frame);
                return frame;
            },
            frame => new UiInteractionFrameBuilder()
                .AddFragment(_list.BuildInteractionFragment(frame.Layout.ContentBounds, frame.ListState, 0, _list.List.HasItems))
                .SetDefaultFocusTarget(_list.List.HasItems ? ListTarget : null)
                .Build(),
            (input, frame, route) =>
            {
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                    return (new SelectionListInput(input, ScrollableListInputResult.NotHandled), UiInputResult.HandledResult);

                RoutedScrollableListInputResult routed = _list.RouteInput(
                    input,
                    frame.Layout.ContentBounds,
                    frame.ListState,
                    route,
                    confirmOnDoubleClick: true);
                return (new SelectionListInput(input, routed.ListResult), routed.UiResult);
            },
            (_, semantic) =>
            {
                if (semantic.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
                    NotifySelectionChanged();

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 } ||
                    semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed && _list.List.HasItems ||
                    semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } && !_list.List.HasItems)
                {
                    return ModalDialogLoopResult<SelectionListDialogResult<T>>.Complete(
                        _list.List.HasItems && semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed
                            ? Confirmed()
                            : Cancelled());
                }

                return ModalDialogLoopResult<SelectionListDialogResult<T>>.Continue;
            },
            applyCommittedFrame: frame =>
            {
                _list.ApplyCommittedFrame(frame.ListState);
                if (_list.List.HasItems && !initialSelectionNotified)
                {
                    NotifySelectionChanged();
                    initialSelectionNotified = true;
                }
            });
    }

    private SelectionListDialogResult<T> Confirmed() =>
        new(true, _list.List.Items[SelectedIndex], SelectedIndex);

    private static SelectionListDialogResult<T> Cancelled() =>
        new(false, default, -1);

    private void NotifySelectionChanged() =>
        _selectionChanged?.Invoke(_list.List.Items[_list.List.SelectedIndex], _list.List.SelectedIndex);

    private void RenderLayer(IUiCanvas screen, SelectionListFrame frame)
    {
        var palette = UiTheme.Current;
        var layout = frame.Layout;

        var normalStyle = PaletteStyles.DialogFill(palette);
        var selectedStyle = PaletteStyles.InputField(palette);
        var emptyStyle = PaletteStyles.DialogFill(palette);
        var scrollState = _list.List.GetScrollState(layout.VisibleRows, frame.ListState.ScrollTop);

        _frameRenderer.RenderFrame(
            screen,
            layout.Bounds,
            _title,
            DoubleBorder,
            PaletteStyles.DialogPopupOptions(palette),
            scrollState,
            (_, _) => _list.Render(screen, layout.ContentBounds, frame.ListState, normalStyle, selectedStyle, emptyStyle));

    }

    private SelectionListLayout CalculateLayout(ConsoleSize size)
    {
        int itemWidth = _list.List.Count == 0 ? ConsoleTextMetrics.GetCellWidth(EmptyText ?? string.Empty) : _list.List.Items.Max(item => ConsoleTextMetrics.GetCellWidth(_list.List.ItemText(item)));
        int contentWidth = Math.Max(DefaultMinWidth, Math.Max(itemWidth, ConsoleTextMetrics.GetCellWidth(_title)) + 2);
        int maxWidth = MaxWidth.HasValue ? Math.Min(MaxWidth.Value, size.Width) : size.Width - 2;
        contentWidth = Math.Min(contentWidth, Math.Max(DefaultMinWidth, maxWidth - 2));

        int maxRows = Math.Max(1, Math.Min(MaxVisibleRows, MaxHeight.GetValueOrDefault(size.Height) - 2));
        int visibleRows = Math.Min(Math.Max(1, _list.List.Count == 0 ? 1 : _list.List.Count), Math.Max(1, Math.Min(maxRows, size.Height - 2)));
        int width = Math.Min(size.Width, contentWidth + 2);
        int height = Math.Min(size.Height, visibleRows + 2);
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);
        var contentBounds = new Rect(x + 1, y + 1, Math.Max(1, width - 2), Math.Max(1, height - 2));
        return new SelectionListLayout(
            bounds,
            contentBounds,
            new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height),
            contentBounds.Height);
    }

    private readonly record struct SelectionListLayout(
        Rect Bounds,
        Rect ContentBounds,
        Rect ScrollbarBounds,
        int VisibleRows);

    private readonly record struct SelectionListFrame(
        SelectionListLayout Layout,
        ScrollableListFrameState ListState);

    private readonly record struct SelectionListInput(
        ConsoleInputEvent Input,
        ScrollableListInputResult ListResult);
}
