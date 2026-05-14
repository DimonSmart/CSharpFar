using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Dialogs;

internal sealed class EncodingSelectionDialog
{
    private const int DialogWidth = 44;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public EncodingSelectionDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public TextEncodingCatalogItem? Show(
        IReadOnlyList<TextEncodingCatalogItem> items,
        TextEncodingSelection currentSelection,
        Action<TextEncodingCatalogItem>? previewSelection = null,
        Action? renderUnderlay = null)
    {
        if (items.Count == 0)
            return null;

        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            int cursor = FindInitialCursor(items, currentSelection);
            int previewCursor = -1;
            while (true)
            {
                if (cursor != previewCursor)
                {
                    previewSelection?.Invoke(items[cursor]);
                    previewCursor = cursor;
                }

                Draw(items, cursor, renderUnderlay);
                var key = _screen.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        cursor = Math.Max(0, cursor - 1);
                        break;

                    case ConsoleKey.DownArrow:
                        cursor = Math.Min(items.Count - 1, cursor + 1);
                        break;

                    case ConsoleKey.Home:
                        cursor = 0;
                        break;

                    case ConsoleKey.End:
                        cursor = items.Count - 1;
                        break;

                    case ConsoleKey.Enter:
                        return items[cursor];

                    case ConsoleKey.Escape:
                    case ConsoleKey.F10:
                        return null;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(
        IReadOnlyList<TextEncodingCatalogItem> items,
        int cursor,
        Action? renderUnderlay)
    {
        using var frame = _screen.BeginFrame();
        renderUnderlay?.Invoke();

        int height = items.Count + 4;
        Rect bounds = _modalRenderer.CenteredOuterBounds(_screen, DialogWidth, height, minHeight: height);
        _modalRenderer.Render(
            _screen,
            bounds,
            "Encoding",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, layout) =>
            {
                Rect content = layout.ContentBounds;
                for (int i = 0; i < items.Count; i++)
                {
                    string text = Fit(items[i].Label, content.Width);
                    var style = i == cursor ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
                    _screen.Write(content.X, content.Y + i, text, style);
                }

                const string hint = " Enter  Esc ";
                int hintX = layout.FrameBounds.X + Math.Max(0, (layout.FrameBounds.Width - hint.Length) / 2);
                _screen.Write(hintX, layout.FrameBounds.Bottom - 1, hint, PaletteStyles.DialogTitle(_palette));
            });

        _screen.SetCursorVisible(false);
    }

    private static int FindInitialCursor(
        IReadOnlyList<TextEncodingCatalogItem> items,
        TextEncodingSelection currentSelection)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Selection == currentSelection)
                return i;
        }

        return 0;
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
