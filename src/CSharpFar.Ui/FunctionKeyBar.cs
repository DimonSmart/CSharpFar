using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct FunctionKeyBarItem(int KeyNumber, string Label);

public readonly record struct FunctionKeyBarMouseHit(int KeyNumber, ConsoleKey Key);

public sealed class FunctionKeyBar
{
    private const int FunctionKeyCount = 12;
    private const string Ellipsis = "...";

    public void Render(
        ScreenRenderer screen,
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarItem> items)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(items);

        if (totalWidth <= 0)
            return;

        var palette = UiTheme.Current;
        var numStyle = PaletteStyles.KeyBarNum(palette);
        var labelStyle = PaletteStyles.KeyBarLabel(palette);
        var gapStyle = PaletteStyles.CommandLine(palette);

        screen.FillRegion(new Rect(0, y, totalWidth, 1), gapStyle);
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
                screen.Write(x, y, keyText.AsSpan(0, keyWidth), numStyle);

            int slotEnd = keyNumber < FunctionKeyCount ? x + slotWidth : totalWidth;
            int labelWidth = slotEnd - x - keyWidth;
            if (labelWidth <= 0)
                continue;

            labelsByKey.TryGetValue(keyNumber, out string? label);
            string fittedLabel = FitLabel(label ?? string.Empty, labelWidth);
            if (fittedLabel.Length > 0)
                screen.Write(x + keyWidth, y, fittedLabel, labelStyle);
        }
    }

    public bool TryHitTest(
        MouseConsoleInputEvent mouse,
        int y,
        int totalWidth,
        out FunctionKeyBarMouseHit hit)
    {
        hit = default;

        if (!TryGetKeyNumberAt(mouse, y, totalWidth, out int keyNumber) ||
            !TryGetFunctionKey(keyNumber, out var key))
        {
            return false;
        }

        hit = new FunctionKeyBarMouseHit(keyNumber, key);
        return true;
    }

    public static bool TryGetKeyNumberAtX(int x, int totalWidth, out int keyNumber)
    {
        keyNumber = 0;

        if (x < 0 || x >= totalWidth)
            return false;

        int slotWidth = totalWidth / FunctionKeyCount;
        if (slotWidth <= 0)
            return false;

        keyNumber = Math.Min(x / slotWidth + 1, FunctionKeyCount);
        return true;
    }

    public static bool TryGetKeyNumberAt(
        MouseConsoleInputEvent mouse,
        int y,
        int totalWidth,
        out int keyNumber)
    {
        keyNumber = 0;

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            mouse.Y != y)
        {
            return false;
        }

        return TryGetKeyNumberAtX(mouse.X, totalWidth, out keyNumber);
    }

    public static bool TryGetFunctionKey(int keyNumber, out ConsoleKey key)
    {
        key = keyNumber switch
        {
            1 => ConsoleKey.F1,
            2 => ConsoleKey.F2,
            3 => ConsoleKey.F3,
            4 => ConsoleKey.F4,
            5 => ConsoleKey.F5,
            6 => ConsoleKey.F6,
            7 => ConsoleKey.F7,
            8 => ConsoleKey.F8,
            9 => ConsoleKey.F9,
            10 => ConsoleKey.F10,
            11 => ConsoleKey.F11,
            12 => ConsoleKey.F12,
            _ => default,
        };
        return keyNumber is >= 1 and <= FunctionKeyCount;
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
