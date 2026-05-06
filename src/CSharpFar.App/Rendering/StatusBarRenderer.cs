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
        _screen.FillRegion(new Rect(0, y, totalWidth, 1), Theme.KeyBarLabel);

        int x = 0;
        foreach (var (key, label) in Keys)
        {
            if (x >= totalWidth) break;

            _screen.Write(x, y, key, Theme.KeyBarNum);
            x += key.Length;

            if (x >= totalWidth) break;
            int maxLabel = Math.Min(label.Length, totalWidth - x);
            _screen.Write(x, y, label[..maxLabel], Theme.KeyBarLabel);
            x += maxLabel;
        }
    }
}
