using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class CommandHistoryCompletionRenderer
{
    public const int MaxVisibleRows = 8;

    private readonly ScreenRenderer _screen;
    private readonly CellStyle _normalStyle;
    private readonly CellStyle _selectedStyle;
    private readonly PopupRenderOptions _popupOptions;
    private readonly PopupRenderer _popupRenderer = new();

    public CommandHistoryCompletionRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        palette ??= PaletteRegistry.Default;
        _normalStyle = PaletteStyles.DialogFill(palette);
        _selectedStyle = PaletteStyles.InputField(palette);
        _popupOptions = PaletteStyles.DialogPopupOptions(palette) with
        {
            DrawShadow = false,
        };
    }

    public void Render(
        int commandLineRow,
        int totalWidth,
        IReadOnlyList<string> commands,
        int selectedIndex,
        int firstVisibleIndex = 0)
    {
        if (commandLineRow <= 0 || totalWidth <= 0 || commands.Count == 0)
            return;

        int maxContentRows = commandLineRow - 2;
        int visibleRows = Math.Min(Math.Min(MaxVisibleRows, maxContentRows), commands.Count);
        if (visibleRows <= 0)
            return;

        int safeSelectedIndex = Math.Clamp(selectedIndex, 0, commands.Count - 1);
        int scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(firstVisibleIndex, commands.Count, visibleRows);
        scrollTop = ScrollStateCalculator.EnsureIndexVisible(safeSelectedIndex, scrollTop, visibleRows);
        scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, commands.Count, visibleRows);
        int height = visibleRows + 2;
        var bounds = new Rect(0, commandLineRow - height, totalWidth, height);
        var popupOptions = _popupOptions with
        {
            VerticalScrollState = new ScrollState
            {
                TotalItems = commands.Count,
                ViewportItems = visibleRows,
                FirstVisibleIndex = scrollTop,
            },
        };

        _popupRenderer.RenderPopup(_screen, bounds, popupOptions, (_, contentBounds) =>
        {
            for (int row = 0; row < visibleRows; row++)
            {
                int itemIndex = scrollTop + row;
                string text = Fit(commands[itemIndex], contentBounds.Width);
                var style = itemIndex == safeSelectedIndex ? _selectedStyle : _normalStyle;
                _screen.Write(contentBounds.X, contentBounds.Y + row, text, style);
            }
        });
    }

    private static string Fit(string text, int width)
    {
        if (text.Length > width)
            return text[..width];

        return text.PadRight(width);
    }
}
