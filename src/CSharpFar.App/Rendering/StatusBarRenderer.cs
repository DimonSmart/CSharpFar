using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class StatusBarRenderer
{
    private readonly ScreenRenderer _screen;

    private static readonly (string Key, string Label)[] Keys =
    [
        ("1",  "Help"),
        ("2",  "Menu"),
        ("3",  "View"),
        ("4",  "Edit"),
        ("5",  "Copy"),
        ("6",  "RenMov"),
        ("7",  "MkDir"),
        ("8",  "Delete"),
        ("9",  "PullDn"),
        ("10", "Quit"),
    ];

    public StatusBarRenderer(ScreenRenderer screen) => _screen = screen;

    public void Render(int y, int totalWidth)
    {
        if (totalWidth <= 0)
            return;

        _screen.FillRegion(new Rect(0, y, totalWidth, 1), Theme.KeyBarLabel);

        bool overflow = RequiredWidth() > totalWidth;
        int ellipsisWidth = overflow ? Math.Min(3, totalWidth) : 0;
        int contentWidth = totalWidth - ellipsisWidth;
        int x = 0;

        foreach (var (key, label) in Keys)
        {
            if (x >= contentWidth) break;

            int keyLen = Math.Min(key.Length, contentWidth - x);
            _screen.Write(x, y, key[..keyLen], Theme.KeyBarNum);
            x += keyLen;
            if (keyLen < key.Length || x >= contentWidth)
                break;

            int maxLabel = Math.Min(label.Length, contentWidth - x);
            _screen.Write(x, y, label[..maxLabel], Theme.KeyBarLabel);
            x += maxLabel;
            if (maxLabel < label.Length)
                break;
        }

        if (overflow)
            _screen.Write(totalWidth - ellipsisWidth, y, "...".AsSpan()[..ellipsisWidth], Theme.KeyBarOverflow);
    }

    private static int RequiredWidth() =>
        Keys.Sum(item => item.Key.Length + item.Label.Length);
}
