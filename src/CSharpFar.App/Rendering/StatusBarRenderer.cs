using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class StatusBarRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _numStyle;
    private readonly CellStyle      _labelStyle;
    private readonly CellStyle      _gapStyle;

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
            _gapStyle      = new CellStyle(palette.FunctionKeyTextFg,     palette.CommandLineBg);
        }
        else
        {
            _numStyle      = Theme.KeyBarNum;
            _labelStyle    = Theme.KeyBarLabel;
            _gapStyle      = Theme.CommandLine;
        }
    }

    public void Render(int y, int totalWidth)
    {
        if (totalWidth <= 0)
            return;

        _screen.FillRegion(new Rect(0, y, totalWidth, 1), _gapStyle);

        int extraWidth = Math.Max(0, totalWidth - RequiredWidth());
        int padEach    = extraWidth / Keys.Length;
        int padRemainder = extraWidth % Keys.Length;
        int x          = 0;

        for (int i = 0; i < Keys.Length; i++)
        {
            var (key, label) = Keys[i];
            if (x >= totalWidth) break;

            x = WriteClipped(x, y, totalWidth, key, _numStyle);
            if (x >= totalWidth)
                break;

            int labelPadding = padEach + (i < padRemainder ? 1 : 0);
            x = WriteClipped(x, y, totalWidth, label, _labelStyle);
            if (x >= totalWidth)
                break;

            if (labelPadding > 0)
                x = WriteClipped(x, y, totalWidth, new string(' ', labelPadding), _labelStyle);

            if (i < Keys.Length - 1 && x < totalWidth)
                x++;
        }
    }

    private static int RequiredWidth() =>
        Keys.Sum(item => item.Key.Length + item.Label.Length) + Keys.Length - 1;

    private int WriteClipped(int x, int y, int totalWidth, string text, CellStyle style)
    {
        if (x >= totalWidth)
            return x;

        int len = Math.Min(text.Length, totalWidth - x);
        if (len > 0)
            _screen.Write(x, y, text.AsSpan(0, len), style);

        return x + len;
    }
}
