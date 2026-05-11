using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows the file history list (most recently accessed first).
/// Returns the selected file path, or null if the user pressed Escape.
/// </summary>
internal sealed class FileHistoryDialog
{
    private const int DialogWidth = 60;
    private const int MaxVisible  = 15;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public FileHistoryDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public string? Show(IReadOnlyList<FileHistoryItem> history)
    {
        if (history.Count == 0) return null;

        var paths   = history.Select(i => i.Path).Reverse().ToList();
        int visible = Math.Min(paths.Count, MaxVisible);
        int dlgH    = visible + 2;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            int cursor    = 0;
            int scrollTop = 0;
            ScrollBarDragState? scrollbarDrag = null;

            while (true)
            {
                Draw(paths, cursor, scrollTop, visible, dlgH, size);

                var input = _screen.ReadInput();
                if (input is MouseConsoleInputEvent mouse &&
                    TryHandleScrollbarMouse(mouse, paths.Count, visible, dlgH, size, ref cursor, ref scrollTop, ref scrollbarDrag))
                {
                    continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    continue;

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (cursor > 0) { cursor--; if (cursor < scrollTop) scrollTop = cursor; }
                        break;

                    case ConsoleKey.DownArrow:
                        if (cursor < paths.Count - 1)
                        {
                            cursor++;
                            if (cursor >= scrollTop + visible) scrollTop = cursor - visible + 1;
                        }
                        break;

                    case ConsoleKey.PageUp:
                        cursor    = Math.Max(0, cursor - visible);
                        scrollTop = Math.Max(0, scrollTop - visible);
                        break;

                    case ConsoleKey.PageDown:
                        cursor    = Math.Min(paths.Count - 1, cursor + visible);
                        scrollTop = Math.Max(0, cursor - visible + 1);
                        break;

                    case ConsoleKey.Enter:  return paths[cursor];
                    case ConsoleKey.Escape: return null;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(
        List<string> paths, int cursor, int scrollTop,
        int visible, int dlgH, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth) / 2);
        int dlgY = Math.Max(0, (size.Height - dlgH)        / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, dlgH);
        var scrollState = paths.Count > visible
            ? new ScrollState
            {
                TotalItems = paths.Count,
                ViewportItems = visible,
                FirstVisibleIndex = scrollTop,
            }
            : null;

        new DialogFrameRenderer().RenderFrame(_screen, bounds, "File History", false, PaletteStyles.DialogPopupOptions(_palette), scrollState, (_, _) =>
        {
            for (int i = 0; i < visible; i++)
            {
                int idx = scrollTop + i;
                if (idx >= paths.Count) break;

                string text  = Truncate(paths[idx], fw).PadRight(fw);
                var    style = idx == cursor ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
                _screen.Write(dlgX + 2, dlgY + 1 + i, text, style);
            }
        });

        _screen.SetCursorVisible(false);
    }

    private static bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        int itemCount,
        int visible,
        int dlgH,
        ConsoleSize size,
        ref int cursor,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        if (itemCount <= visible)
            return false;

        int dlgX = Math.Max(0, (size.Width  - DialogWidth) / 2);
        int dlgY = Math.Max(0, (size.Height - dlgH)        / 2);
        var scrollbarBounds = new Rect(dlgX + DialogWidth - 1, dlgY + 1, 1, visible);
        return ScrollableListMouseHandler.TryHandleScrollbarMouse(
            mouse,
            scrollbarBounds,
            itemCount,
            visible,
            ref cursor,
            ref scrollTop,
            ref scrollbarDrag);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
