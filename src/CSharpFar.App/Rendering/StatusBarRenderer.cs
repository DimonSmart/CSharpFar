using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class StatusBarRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _numStyle;
    private readonly CellStyle      _labelStyle;
    private readonly CellStyle      _overflowStyle;

    private static readonly (string Key, string Label)[] Keys =
    [
        ("1",  "Help"),
        ("2",  "UserMn"),
        ("3",  "View"),
        ("4",  "Edit"),
        ("5",  "Copy"),
        ("6",  "RenMov"),
        ("7",  "MkFold"),
        ("8",  "Delete"),
        ("9",  "ConfMn"),
        ("10", "Quit"),
    ];

    internal StatusBarRenderer(ScreenRenderer screen)
        : this(screen, palette: null) { }

    public StatusBarRenderer(ScreenRenderer screen, ConsolePalette? palette)
    {
        _screen = screen;
        if (palette is not null)
        {
            _numStyle      = new CellStyle(palette.FunctionKeyNumFg,      palette.FunctionKeyNumBg);
            _labelStyle    = new CellStyle(palette.FunctionKeyTextFg,     palette.FunctionKeyBarBg);
            _overflowStyle = new CellStyle(palette.FunctionKeyOverflowFg, palette.FunctionKeyBarBg);
        }
        else
        {
            _numStyle      = Theme.KeyBarNum;
            _labelStyle    = Theme.KeyBarLabel;
            _overflowStyle = Theme.KeyBarOverflow;
        }
    }

    public void Render(int y, int totalWidth)
    {
        if (totalWidth <= 0)
            return;

        _screen.FillRegion(new Rect(0, y, totalWidth, 1), _labelStyle);

        bool overflow     = RequiredWidth() > totalWidth;
        int ellipsisWidth = overflow ? Math.Min(3, totalWidth) : 0;
        int contentWidth  = totalWidth - ellipsisWidth;
        int x             = 0;

        foreach (var (key, label) in Keys)
        {
            if (x >= contentWidth) break;

            int keyLen = Math.Min(key.Length, contentWidth - x);
            _screen.Write(x, y, key[..keyLen], _numStyle);
            x += keyLen;
            if (keyLen < key.Length || x >= contentWidth)
                break;

            int maxLabel = Math.Min(label.Length, contentWidth - x);
            _screen.Write(x, y, label[..maxLabel], _labelStyle);
            x += maxLabel;
            if (maxLabel < label.Length)
                break;
        }

        if (overflow)
            _screen.Write(totalWidth - ellipsisWidth, y, "...".AsSpan()[..ellipsisWidth], _overflowStyle);
    }

    private static int RequiredWidth() =>
        Keys.Sum(item => item.Key.Length + item.Label.Length);
}
