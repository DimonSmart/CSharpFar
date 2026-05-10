using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal readonly record struct FunctionKeyBarItem(int KeyNumber, string Label);

internal sealed class FunctionKeyBarRenderer
{
    private const int FunctionKeyCount = 12;
    private const string Ellipsis = "...";

    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _numStyle;
    private readonly CellStyle      _labelStyle;
    private readonly CellStyle      _gapStyle;

    internal FunctionKeyBarRenderer(ScreenRenderer screen)
        : this(screen, palette: null) { }

    public FunctionKeyBarRenderer(ScreenRenderer screen, ConsolePalette? palette)
    {
        _screen = screen;
        palette ??= PaletteRegistry.Default;
        _numStyle      = PaletteStyles.KeyBarNum(palette);
        _labelStyle    = PaletteStyles.KeyBarLabel(palette);
        _gapStyle      = PaletteStyles.CommandLine(palette);
    }

    public void Render(int y, int totalWidth, IReadOnlyList<FunctionKeyBarItem> items)
    {
        if (totalWidth <= 0)
            return;

        _screen.FillRegion(new Rect(0, y, totalWidth, 1), _gapStyle);
        int slotWidth = totalWidth / FunctionKeyCount;
        if (slotWidth <= 0)
            return;

        var labelsByKey = items
            .Where(item => item.KeyNumber is >= 1 and <= FunctionKeyCount)
            .ToDictionary(item => item.KeyNumber, item => item.Label);

        for (int keyNumber = 1; keyNumber <= FunctionKeyCount; keyNumber++)
        {
            int x = (keyNumber - 1) * slotWidth;
            if (x >= totalWidth)
                break;

            string keyText = keyNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int keyWidth = Math.Min(keyText.Length, slotWidth);
            if (keyWidth > 0)
                _screen.Write(x, y, keyText.AsSpan(0, keyWidth), _numStyle);

            int separatorWidth = keyNumber < FunctionKeyCount ? 1 : 0;
            int slotEnd = keyNumber < FunctionKeyCount ? x + slotWidth : totalWidth;
            int labelWidth = slotEnd - x - keyWidth - separatorWidth;
            if (labelWidth <= 0)
                continue;

            labelsByKey.TryGetValue(keyNumber, out string? label);
            string fittedLabel = FitLabel(label ?? string.Empty, labelWidth);
            if (fittedLabel.Length > 0)
                _screen.Write(x + keyWidth, y, fittedLabel, _labelStyle);
        }
    }

    private static string FitLabel(string label, int width)
    {
        if (width <= 0)
            return string.Empty;

        if (label.Length <= width)
            return label.PadRight(width);

        if (width <= Ellipsis.Length)
            return Ellipsis[..width];

        return label[..(width - Ellipsis.Length)] + Ellipsis;
    }
}
