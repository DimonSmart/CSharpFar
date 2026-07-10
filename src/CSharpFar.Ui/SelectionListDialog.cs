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

    private readonly ScrollableList<T> _list;
    private readonly string _title;
    private readonly DialogFrameRenderer _frameRenderer = new();

    public SelectionListDialog(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title)
    {
        _list = new ScrollableList<T>(items, itemText);
        _title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public int SelectedIndex
    {
        get => _list.SelectedIndex;
        set => _list.SelectedIndex = value;
    }

    public int ScrollTop
    {
        get => _list.ScrollTop;
        set => _list.ScrollTop = value;
    }

    public int MaxVisibleRows { get; set; } = DefaultMaxVisibleRows;

    public int? MaxWidth { get; set; }

    public int? MaxHeight { get; set; }

    public string? EmptyText
    {
        get => _list.EmptyText;
        set => _list.EmptyText = value;
    }

    public bool DoubleBorder { get; set; }

    public Action<T, int>? SelectionChanged
    {
        get => _list.SelectionChanged;
        set => _list.SelectionChanged = value;
    }

    public SelectionListDialogResult<T> Show(ScreenRenderer screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var size = screen.GetSize();
        var saved = screen.Capture(new Rect(0, 0, size.Width, size.Height));
        ScrollBarDragState? scrollbarDrag = null;
        bool initialSelectionNotified = false;

        try
        {
            while (true)
            {
                var layout = CalculateLayout(size);
                _list.Normalize(layout.VisibleRows);
                if (_list.HasItems && !initialSelectionNotified)
                {
                    SelectionChanged?.Invoke(_list.Items[SelectedIndex], SelectedIndex);
                    initialSelectionNotified = true;
                }

                Draw(screen, layout);
                var input = screen.ReadInput();

                if (input is MouseConsoleInputEvent mouse &&
                    HandleMouse(mouse, layout, ref scrollbarDrag, out bool confirmed))
                {
                    if (confirmed)
                        return Confirmed();
                    continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    continue;

                if (HandleKey(key, layout.VisibleRows, out bool isConfirmed))
                    return isConfirmed && _list.HasItems ? Confirmed() : Cancelled();
            }
        }
        finally
        {
            screen.Restore(saved);
            screen.SetCursorVisible(false);
        }
    }

    public SelectionListDialogResult<T> Show(ModalDialogHost modalDialogs)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);
        ScrollBarDragState? scrollbarDrag = null;
        SelectionListLayout layout = default;
        bool initialSelectionNotified = false;

        using var session = modalDialogs.Open(context =>
        {
            layout = CalculateLayout(context.Size);
            _list.Normalize(layout.VisibleRows);
            if (_list.HasItems && !initialSelectionNotified)
            {
                SelectionChanged?.Invoke(_list.Items[SelectedIndex], SelectedIndex);
                initialSelectionNotified = true;
            }
            RenderLayer(context.Screen, layout);
        });

        while (true)
        {
            session.Render();
            var input = session.ReadInput();
            if (input is MouseConsoleInputEvent mouse &&
                HandleMouse(mouse, layout, ref scrollbarDrag, out bool confirmed))
            {
                if (confirmed)
                    return Confirmed();
                continue;
            }

            if (input is KeyConsoleInputEvent { Key: var key } &&
                HandleKey(key, layout.VisibleRows, out bool isConfirmed))
                return isConfirmed && _list.HasItems ? Confirmed() : Cancelled();
        }
    }

    private SelectionListDialogResult<T> Confirmed() =>
        new(true, _list.Items[SelectedIndex], SelectedIndex);

    private static SelectionListDialogResult<T> Cancelled() =>
        new(false, default, -1);

    private bool HandleKey(ConsoleKeyInfo key, int visibleRows, out bool confirmed)
    {
        confirmed = false;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F10:
                return true;
        }

        var result = _list.HandleKey(key, visibleRows);
        if (result.Kind == ScrollableListInputResultKind.Confirmed)
        {
            confirmed = true;
            return true;
        }

        return key.Key == ConsoleKey.Enter && !_list.HasItems;
    }

    private bool HandleMouse(
        MouseConsoleInputEvent mouse,
        SelectionListLayout layout,
        ref ScrollBarDragState? scrollbarDrag,
        out bool confirmed)
    {
        var result = _list.HandleMouse(
            mouse,
            layout.ContentBounds,
            layout.ScrollbarBounds,
            layout.VisibleRows,
            ref scrollbarDrag,
            confirmOnClick: true,
            confirmOnDoubleClick: true);
        confirmed = result.Kind == ScrollableListInputResultKind.Confirmed;
        return result.IsHandled;
    }

    private void Draw(ScreenRenderer screen, SelectionListLayout layout)
    {
        using var frame = screen.BeginFrame();
        RenderLayer(screen, layout);
    }

    private void RenderLayer(ScreenRenderer screen, SelectionListLayout layout)
    {
        var palette = UiTheme.Current;

        _list.NormalStyle = PaletteStyles.DialogFill(palette);
        _list.SelectedStyle = PaletteStyles.InputField(palette);
        _list.EmptyStyle = PaletteStyles.DialogFill(palette);
        var scrollState = _list.GetScrollState(layout.VisibleRows);

        _frameRenderer.RenderFrame(
            screen,
            layout.Bounds,
            _title,
            DoubleBorder,
            PaletteStyles.DialogPopupOptions(palette),
            scrollState,
            (_, _) => _list.Render(screen, layout.ContentBounds));

        screen.SetCursorVisible(false);
    }

    private SelectionListLayout CalculateLayout(ConsoleSize size)
    {
        int itemWidth = _list.Count == 0 ? EmptyText?.Length ?? 0 : _list.Items.Max(item => _list.ItemText(item).Length);
        int contentWidth = Math.Max(DefaultMinWidth, Math.Max(itemWidth, _title.Length) + 2);
        int maxWidth = MaxWidth.HasValue ? Math.Min(MaxWidth.Value, size.Width) : size.Width - 2;
        contentWidth = Math.Min(contentWidth, Math.Max(DefaultMinWidth, maxWidth - 2));

        int maxRows = Math.Max(1, Math.Min(MaxVisibleRows, MaxHeight.GetValueOrDefault(size.Height) - 2));
        int visibleRows = Math.Min(Math.Max(1, _list.Count == 0 ? 1 : _list.Count), Math.Max(1, Math.Min(maxRows, size.Height - 2)));
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
}
