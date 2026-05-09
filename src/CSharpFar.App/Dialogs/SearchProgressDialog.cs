using System.Collections.Concurrent;
using CSharpFar.App.Rendering;
using CSharpFar.App.Search;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows a "Searching…" overlay while running the search on a background thread.
/// Press Esc to cancel and return whatever was found so far.
/// Returns all found paths (sorted), or an empty list if nothing was found.
/// </summary>
internal sealed class SearchProgressDialog
{
    private const int DialogWidth  = 54;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public SearchProgressDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public IReadOnlyList<string> Show(string rootDir, string mask)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            var cts  = new CancellationTokenSource();
            var bag  = new ConcurrentBag<string>();
            var task = Task.Run(() => FileSearcher.Collect(rootDir, mask, bag, cts.Token));

            while (!task.IsCompleted)
            {
                Draw(rootDir, bag.Count, size);
                Thread.Sleep(80);

                if (global::System.Console.KeyAvailable)
                {
                    var key = global::System.Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        cts.Cancel();
                        break;
                    }
                }
            }

            try { task.Wait(2000); } catch { }

            return bag.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private void Draw(string rootDir, int count, ConsoleSize size)
    {
        int x = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int y = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw = DialogWidth - 4;

        var bounds = new Rect(x, y, DialogWidth, DialogHeight);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, "Search", false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            string dir  = Truncate($"In: {rootDir}", fw).PadRight(fw);
            string stat = $"Found: {count} file{(count == 1 ? "" : "s")}".PadRight(fw);
            _screen.Write(x + 2, y + 1, dir,  PaletteStyles.DialogFill(_palette));
            _screen.Write(x + 2, y + 2, stat, PaletteStyles.DialogFill(_palette));

            const string hint = "Esc to cancel";
            _screen.Write(x + (DialogWidth - hint.Length) / 2, y + 3, hint, PaletteStyles.DialogFill(_palette));
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
