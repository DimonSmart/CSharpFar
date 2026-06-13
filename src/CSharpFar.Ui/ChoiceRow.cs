using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ChoiceRow<T>
{
    private readonly IReadOnlyList<T> _choices;
    private readonly Func<T, string> _format;
    private Rect _lastBounds;
    private bool _hasRendered;

    public ChoiceRow(IReadOnlyList<T> choices, Func<T, string> format, int selectedIndex = 0)
    {
        if (choices.Count == 0)
            throw new ArgumentException("At least one choice is required.", nameof(choices));

        _choices = choices;
        _format = format;
        SelectedIndex = Math.Clamp(selectedIndex, 0, choices.Count - 1);
    }

    public T Value
    {
        get => _choices[SelectedIndex];
        set
        {
            for (int i = 0; i < _choices.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(_choices[i], value))
                    continue;

                SelectedIndex = i;
                break;
            }
        }
    }

    public int SelectedIndex { get; private set; }

    public void Render(ScreenRenderer screen, int x, int y, int width, string label, bool focused)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var palette = UiTheme.Current;
        string text = $"{label}: {_format(Value)}";
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
        int previous = SelectedIndex;
        SelectedIndex = key.Key switch
        {
            ConsoleKey.LeftArrow => SelectedIndex <= 0 ? _choices.Count - 1 : SelectedIndex - 1,
            ConsoleKey.RightArrow or ConsoleKey.Spacebar or ConsoleKey.Enter => (SelectedIndex + 1) % _choices.Count,
            _ => SelectedIndex,
        };
        return SelectedIndex != previous;
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

        SelectedIndex = (SelectedIndex + 1) % _choices.Count;
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
