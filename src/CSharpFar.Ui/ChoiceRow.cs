using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public enum ChoiceRowLayoutKind
{
    Simple,
    Segmented,
}

public readonly record struct ChoiceHitTarget(int Index, Rect Bounds, Rect MarkerBounds);

public sealed record ChoiceRowLayout(
    ChoiceRowLayoutKind Kind,
    IReadOnlyList<Rect> RowBounds,
    IReadOnlyList<ChoiceHitTarget> Choices);

public sealed class ChoiceRow<T>
{
    private readonly IReadOnlyList<T> _choices;
    private readonly Func<T, string> _format;
    private readonly IEqualityComparer<T> _comparer;

    public ChoiceRow(
        IReadOnlyList<T> choices,
        Func<T, string> format,
        int selectedIndex = 0,
        IEqualityComparer<T>? comparer = null)
    {
        _choices = choices ?? throw new ArgumentNullException(nameof(choices));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _comparer = comparer ?? EqualityComparer<T>.Default;
        SelectedIndex = choices.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, choices.Count - 1);
    }

    public static ChoiceRow<T> FromValue(
        IReadOnlyList<T> choices,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null)
    {
        var choice = new ChoiceRow<T>(choices, format, comparer: comparer);
        choice.TrySelectValue(selectedValue);
        return choice;
    }

    public static ChoiceRow<T> FromValue(
        IReadOnlyList<T> choices,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null)
    {
        var choice = new ChoiceRow<T>(choices, format, comparer: comparer);
        if (!choice.TrySelectValue(selectedValue))
            choice.TrySelectValue(fallbackValue);
        return choice;
    }

    public T Value
    {
        get => SelectedIndex < 0 ? default! : _choices[SelectedIndex];
        set => TrySelectValue(value);
    }

    public int SelectedIndex { get; private set; }
    public int Count => _choices.Count;

    public bool TrySelectValue(T value)
    {
        for (int i = 0; i < _choices.Count; i++)
        {
            if (!_comparer.Equals(_choices[i], value))
                continue;

            SelectedIndex = i;
            return true;
        }

        return false;
    }

    public bool TryGetSelectedMarkerBounds(ChoiceRowLayout layout, out Rect bounds)
    {
        foreach (var target in layout.Choices)
        {
            if (target.Index != SelectedIndex)
                continue;

            if (target.MarkerBounds.Width <= 0)
                break;

            bounds = target.MarkerBounds;
            return true;
        }

        bounds = default;
        return false;
    }

    public ChoiceRowLayout Render(IUiCanvas screen, int x, int y, int width, string label, bool focused)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var palette = UiTheme.Current;
        string text = SelectedIndex < 0 ? $"{label}: " : $"{label}: {_format(Value)}";
        screen.Write(
            x,
            y,
            Fit(text, width),
            focused ? PaletteStyles.InputField(palette) : PaletteStyles.DialogFill(palette));
        return new ChoiceRowLayout(
            ChoiceRowLayoutKind.Simple,
            Array.AsReadOnly(new[] { new Rect(x, y, Math.Max(0, width), 1) }),
            Array.AsReadOnly(Array.Empty<ChoiceHitTarget>()));
    }

    public ChoiceRowLayout CalculateSegmentedLayout(
        int x,
        int y,
        int width,
        string label,
        int startIndex = 0,
        int? endIndex = null)
    {
        startIndex = Math.Clamp(startIndex, 0, _choices.Count);
        int exclusiveEnd = Math.Clamp(endIndex ?? _choices.Count, startIndex, _choices.Count);
        string prefix = string.IsNullOrEmpty(label) ? string.Empty : label + " ";
        var choices = new List<ChoiceHitTarget>();
        var areaBounds = new Rect(x, y, Math.Max(0, width), 1);
        int column = ConsoleTextMetrics.GetCellWidth(prefix);
        for (int i = startIndex; i < exclusiveEnd; i++)
        {
            string optionText = $"{(i == SelectedIndex ? "(x)" : "( )")} {_format(_choices[i])}";
            int optionWidth = ConsoleTextMetrics.GetCellWidth(optionText);
            var optionBounds = new Rect(x + column, y, optionWidth, 1);
            var visibleBounds = Intersect(optionBounds, areaBounds);
            if (visibleBounds.Width > 0)
            {
                var markerBounds = Intersect(new Rect(optionBounds.X, y, 3, 1), areaBounds);
                choices.Add(new ChoiceHitTarget(i, visibleBounds, markerBounds));
            }
            column += optionWidth + 1;
        }

        return new ChoiceRowLayout(
            ChoiceRowLayoutKind.Segmented,
            Array.AsReadOnly(new[] { areaBounds }),
            choices.AsReadOnly());
    }

    public ChoiceRowLayout RenderSegmented(
        IUiCanvas screen,
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

        var layout = CalculateSegmentedLayout(x, y, width, label, startIndex, endIndex);
        startIndex = Math.Clamp(startIndex, 0, _choices.Count);
        int exclusiveEnd = Math.Clamp(endIndex ?? _choices.Count, startIndex, _choices.Count);
        var style = focused ? focusedStyle : fillStyle;
        string prefix = string.IsNullOrEmpty(label) ? string.Empty : label + " ";
        var parts = new List<string>();

        for (int i = startIndex; i < exclusiveEnd; i++)
        {
            string optionText = $"{(i == SelectedIndex ? "(x)" : "( )")} {_format(_choices[i])}";
            parts.Add(optionText);
        }

        string text = prefix + string.Join(' ', parts);
        screen.Write(x, y, Fit(text, width), style);
        return layout;
    }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_choices.Count == 0)
            return false;

        int previous = SelectedIndex;
        SelectedIndex = key.Key switch
        {
            ConsoleKey.LeftArrow => SelectedIndex <= 0 ? _choices.Count - 1 : SelectedIndex - 1,
            ConsoleKey.RightArrow or ConsoleKey.Spacebar or ConsoleKey.Enter => (SelectedIndex + 1) % _choices.Count,
            _ => SelectedIndex,
        };
        return SelectedIndex != previous;
    }

    public bool TryHandleMouse(MouseConsoleInputEvent mouse, ChoiceRowLayout layout)
    {
        if (_choices.Count == 0)
            return false;

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            !layout.RowBounds.Any(bounds => Contains(bounds, mouse.X, mouse.Y)))
        {
            return false;
        }

        if (layout.Kind == ChoiceRowLayoutKind.Segmented)
        {
            foreach (var target in layout.Choices)
            {
                if (!Contains(target.Bounds, mouse.X, mouse.Y))
                    continue;

                if (SelectedIndex == target.Index)
                    return true;

                SelectedIndex = target.Index;
                return true;
            }

            return false;
        }

        if (layout.Kind != ChoiceRowLayoutKind.Simple)
            return false;

        SelectedIndex = (SelectedIndex + 1) % _choices.Count;
        return true;
    }

    private static bool Contains(Rect bounds, int x, int y) =>
        x >= bounds.X && x < bounds.Right && y >= bounds.Y && y < bounds.Bottom;

    private static Rect Intersect(Rect value, Rect bounds)
    {
        int x = Math.Max(value.X, bounds.X);
        int y = Math.Max(value.Y, bounds.Y);
        int right = Math.Min(value.Right, bounds.Right);
        int bottom = Math.Min(value.Bottom, bounds.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return ConsoleTextMetrics.FitToCells(text, width);
    }
}
