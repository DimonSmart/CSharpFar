using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ChoiceRow<T>
{
    private readonly IReadOnlyList<T> _choices;
    private readonly Func<T, string> _format;
    private readonly List<(int Index, Rect Bounds)> _choiceBounds = [];
    private Rect _lastBounds;
    private bool _hasRendered;
    private bool _hasChoiceBounds;

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
        _hasChoiceBounds = false;
        _choiceBounds.Clear();
    }

    public void RenderSegmented(
        ScreenRenderer screen,
        int x,
        int y,
        int width,
        string label,
        bool focused,
        CellStyle fillStyle,
        CellStyle focusedStyle,
        int startIndex = 0,
        int? endIndex = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        startIndex = Math.Clamp(startIndex, 0, _choices.Count);
        int exclusiveEnd = Math.Clamp(endIndex ?? _choices.Count, startIndex, _choices.Count);
        var style = focused ? focusedStyle : fillStyle;
        string prefix = string.IsNullOrEmpty(label) ? string.Empty : label + " ";
        var parts = new List<string>();
        _choiceBounds.Clear();

        int column = prefix.Length;
        for (int i = startIndex; i < exclusiveEnd; i++)
        {
            string optionText = $"{(i == SelectedIndex ? "(x)" : "( )")} {_format(_choices[i])}";
            parts.Add(optionText);
            _choiceBounds.Add((i, new Rect(x + column, y, optionText.Length, 1)));
            column += optionText.Length + 1;
        }

        string text = prefix + string.Join(' ', parts);
        screen.Write(x, y, Fit(text, width), style);
        _lastBounds = new Rect(x, y, Math.Max(0, width), 1);
        _hasRendered = true;
        _hasChoiceBounds = true;
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

        if (_hasChoiceBounds)
        {
            foreach (var (index, bounds) in _choiceBounds)
            {
                if (!Contains(bounds, mouse.X, mouse.Y))
                    continue;

                if (SelectedIndex == index)
                    return true;

                SelectedIndex = index;
                return true;
            }

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
