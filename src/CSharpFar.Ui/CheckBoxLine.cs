using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class CheckBoxLine
{
    private Rect _lastBounds;
    private bool _hasRendered;

    public CheckBoxLine(string label, bool value = false)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public bool Value { get; set; }

    public void Render(ScreenRenderer screen, int x, int y, int width, bool focused)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var palette = UiTheme.Current;
        string text = $"[{(Value ? 'x' : ' ')}] {Label}";
        screen.Write(
            x,
            y,
            Fit(text, width),
            focused ? PaletteStyles.InputField(palette) : PaletteStyles.DialogFill(palette));
        _lastBounds = new Rect(x, y, Math.Max(0, width), 1);
        _hasRendered = true;
    }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (key.Key is not (ConsoleKey.Spacebar or ConsoleKey.Enter))
            return false;

        Value = !Value;
        return true;
    }

    public bool TryHandleMouse(MouseConsoleInputEvent mouse)
    {
        if (!_hasRendered ||
            mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click) ||
            !Contains(_lastBounds, mouse.X, mouse.Y))
        {
            return false;
        }

        Value = !Value;
        return true;
    }

    private static bool Contains(Rect bounds, int x, int y) =>
        x >= bounds.X && x < bounds.Right && y >= bounds.Y && y < bounds.Bottom;

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
