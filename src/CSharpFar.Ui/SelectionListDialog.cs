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
    private readonly ListView<T> _list;
    private readonly Func<T, string> _itemText;
    private readonly string _title;
    private readonly DialogAppearance _appearance;
    private readonly DialogFrameRenderer _frameRenderer = new();
    private Action<T, int>? _selectionChanged;

    public SelectionListDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title,
        DialogAppearance appearance = DialogAppearance.Popup)
    {
        _appearance = appearance;
        _list = new ListView<T>(
            items,
            itemText,
            behavior: ListViewBehavior.Selection,
            appearance: appearance == DialogAppearance.Popup ? ListAppearance.Menu : ListAppearance.Dialog);
        _itemText = itemText ?? throw new ArgumentNullException(nameof(itemText));
        _title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set => _list.SetSelectedIndex(value);
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
        get => _list.EmptyText;
        set => _list.EmptyText = value ?? string.Empty;
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
                var list = _list.CalculateFrame(frameLayout.ContentBounds);
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
                    return (new SelectionListInput(input, ScrollableListInputResult.NotHandled), UiInputResult.HandledResult);

                var routed = _list.RouteInput(
                    input,
                    frame.List,
                    route);
                return (new SelectionListInput(input, routed.Semantic), routed.UiResult);
            },
            (_, semantic) =>
            {
                if (semantic.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
                    NotifySelectionChanged();

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 } ||
                    semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed && _list.HasItems ||
                    semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } && !_list.HasItems)
                {
                    return ModalDialogLoopResult<SelectionListDialogResult<T>>.Complete(
                        _list.HasItems && semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed
                            ? Confirmed()
                            : Cancelled());
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

    private SelectionListDialogResult<T> Confirmed() =>
        new(true, _list.Items[SelectedIndex], SelectedIndex);

    private static SelectionListDialogResult<T> Cancelled() =>
        new(false, default, -1);

    private void NotifySelectionChanged() =>
        _selectionChanged?.Invoke(_list.Items[_list.SelectedIndex], _list.SelectedIndex);

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
            (_, _) => _list.Render(screen, frame.List));
    }

    private SelectionListLayout CalculateLayout(ConsoleSize size)
    {
        int desiredRows = Math.Min(
            Math.Max(1, MaxVisibleRows),
            Math.Max(1, _list.Count == 0 ? 1 : _list.Count));
        int verticalChrome = BorderChrome + ListDialogLayoutMetrics.VerticalContentChrome;
        int maxOuterHeight = MaxHeight.HasValue
            ? Math.Min(Math.Max(0, MaxHeight.Value), size.Height)
            : size.Height;
        int availableRows = Math.Max(0, maxOuterHeight - verticalChrome);
        int visibleRows = Math.Min(desiredRows, availableRows);
        int height = Math.Min(maxOuterHeight, desiredRows + verticalChrome);

        int itemWidth = _list.Count == 0
            ? ConsoleTextMetrics.GetCellWidth(EmptyText ?? string.Empty)
            : _list.Items.Max(item => ConsoleTextMetrics.GetCellWidth(_itemText(item)));
        int textViewportWidth = Math.Max(
            DefaultMinWidth,
            Math.Max(itemWidth, ConsoleTextMetrics.GetCellWidth(_title)) + 2);
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
        Rect contentBounds = ListDialogLayoutMetrics.InsetContent(frameContentBounds);
        return new SelectionListLayout(bounds, contentBounds);
    }

    private readonly record struct SelectionListLayout(
        Rect Bounds,
        Rect ContentBounds);

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
        ScrollableListInputResult ListResult);
}
