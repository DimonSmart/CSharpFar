using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct FunctionKeyBarItem(int KeyNumber, string Label);

public readonly record struct FunctionKeyBarSlot(int KeyNumber, Rect Bounds);

public readonly record struct FunctionKeyBarMouseHit(int KeyNumber, ConsoleKey Key);

public sealed class FunctionKeyBar
{
    private const int FunctionKeyCount = 12;
    private const string Ellipsis = "...";

    public void Render(
        IUiCanvas screen,
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
        var labelsByKey = items
            .Where(item => item.KeyNumber is >= 1 and <= FunctionKeyCount)
            .ToDictionary(item => item.KeyNumber, item => item.Label);

        foreach (var slot in BuildSlots(y, totalWidth))
        {
            string keyText = slot.KeyNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int keyWidth = Math.Min(keyText.Length, slot.Bounds.Width);
            if (keyWidth > 0)
                screen.Write(slot.Bounds.X, y, keyText.AsSpan(0, keyWidth), numStyle);

            int labelWidth = slot.Bounds.Width - keyWidth;
            if (labelWidth <= 0)
                continue;

            labelsByKey.TryGetValue(slot.KeyNumber, out string? label);
            string fittedLabel = FitLabel(label ?? string.Empty, labelWidth);
            if (fittedLabel.Length > 0)
                screen.Write(slot.Bounds.X + keyWidth, y, fittedLabel, labelStyle);
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

        foreach (var slot in BuildSlots(0, totalWidth))
        {
            if (slot.Bounds.Contains(x, 0))
            {
                keyNumber = slot.KeyNumber;
                return true;
            }
        }

        return false;
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

    public static IReadOnlyList<FunctionKeyBarSlot> BuildSlots(int y, int totalWidth)
    {
        if (totalWidth <= 0)
            return Array.Empty<FunctionKeyBarSlot>();

        int slotWidth = totalWidth / FunctionKeyCount;
        if (slotWidth <= 0)
            return Array.Empty<FunctionKeyBarSlot>();

        var slots = new List<FunctionKeyBarSlot>(FunctionKeyCount);
        for (int keyNumber = 1; keyNumber <= FunctionKeyCount; keyNumber++)
        {
            int x = (keyNumber - 1) * slotWidth;
            if (x >= totalWidth)
                break;

            int slotEnd = keyNumber < FunctionKeyCount ? x + slotWidth : totalWidth;
            int width = Math.Max(0, slotEnd - x);
            if (width > 0)
                slots.Add(new FunctionKeyBarSlot(keyNumber, new Rect(x, y, width, 1)));
        }

        return Array.AsReadOnly(slots.ToArray());
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
