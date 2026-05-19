using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class FarNetMenuDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public FarNetMenuDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public int? Show(string title, IReadOnlyList<string> items, int selected)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return null;

        selected = Math.Clamp(selected, 0, items.Count - 1);
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        try
        {
            _screen.SetCursorVisible(false);
            while (true)
            {
                Draw(title, items, selected, size);
                var key = _screen.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        return null;
                    case ConsoleKey.Enter:
                        return selected;
                    case ConsoleKey.UpArrow:
                        selected = selected == 0 ? items.Count - 1 : selected - 1;
                        break;
                    case ConsoleKey.DownArrow:
                        selected = selected == items.Count - 1 ? 0 : selected + 1;
                        break;
                    case ConsoleKey.Home:
                        selected = 0;
                        break;
                    case ConsoleKey.End:
                        selected = items.Count - 1;
                        break;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private void Draw(string title, IReadOnlyList<string> items, int selected, ConsoleSize size)
    {
        int contentWidth = Math.Min(
            Math.Max(items.Max(static item => item.Length), title.Length) + 2,
            Math.Max(20, size.Width - 4));
        int height = Math.Min(items.Count + 2, Math.Max(3, size.Height - 2));
        int x = Math.Max(0, (size.Width - contentWidth - 2) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, contentWidth + 2, height);

        new DialogFrameRenderer().RenderFrame(
            _screen,
            bounds,
            title,
            false,
            PaletteStyles.DialogPopupOptions(_palette),
            (_, _) =>
            {
                int visibleRows = height - 2;
                int first = Math.Clamp(selected - visibleRows + 1, 0, Math.Max(0, items.Count - visibleRows));
                for (int row = 0; row < visibleRows; row++)
                {
                    int index = first + row;
                    string text = index < items.Count ? items[index] : string.Empty;
                    var style = index == selected
                        ? PaletteStyles.InputField(_palette)
                        : PaletteStyles.DialogFill(_palette);
                    _screen.Write(
                        x + 1,
                        y + 1 + row,
                        Truncate(text, contentWidth).PadRight(contentWidth),
                        style);
                }
            });
    }

    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..Math.Max(0, width)];
}
