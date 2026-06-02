using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class DirectoryShortcutBarRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle _numberStyle;
    private readonly CellStyle _nameStyle;

    public DirectoryShortcutBarRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        palette ??= PaletteRegistry.Default;
        _numberStyle = PaletteStyles.DirectoryShortcutBarNumber(palette);
        _nameStyle = PaletteStyles.DirectoryShortcutBarLabel(palette);
    }

    public void Render(
        int y,
        int totalWidth,
        AppSettings.DirectoryShortcutSettings? settings)
    {
        if (y < 0 || totalWidth <= 2)
            return;

        foreach (var slot in GetVisibleSlots(totalWidth, settings))
        {
            _screen.Write(slot.X, y, slot.NumberText, _numberStyle);
            if (slot.Name.Length > 0)
            {
                _screen.Write(slot.X + slot.NumberText.Length, y, slot.Name, _nameStyle);
            }
        }
    }

    public static bool TryGetShortcutNumberAt(
        MouseConsoleInputEvent mouse,
        int barY,
        int totalWidth,
        AppSettings.DirectoryShortcutSettings? settings,
        out int number)
    {
        number = 0;
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click) ||
            mouse.Y != barY)
        {
            return false;
        }

        foreach (var slot in GetVisibleSlots(totalWidth, settings))
        {
            if (mouse.X >= slot.X && mouse.X < slot.X + slot.Width)
            {
                number = slot.Number;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<VisibleSlot> GetVisibleSlots(
        int totalWidth,
        AppSettings.DirectoryShortcutSettings? settings)
    {
        int x = 1;
        foreach (var item in DirectoryShortcutNormalizer.Normalize(settings))
        {
            string numberText = item.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int width = numberText.Length + item.Name.Length + 1;
            if (x + width > totalWidth)
                yield break;

            yield return new VisibleSlot(x, width, item.Number, numberText, item.Name);
            x += width;
        }
    }

    private readonly record struct VisibleSlot(
        int X,
        int Width,
        int Number,
        string NumberText,
        string Name);
}
