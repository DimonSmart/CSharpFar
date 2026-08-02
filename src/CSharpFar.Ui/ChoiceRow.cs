namespace CSharpFar.Ui;

/// <summary>Value-oriented compatibility facade for a reusable choice selection.</summary>
public sealed class ChoiceRow<T>
{
    private readonly Func<T, string> _format;

    public ChoiceRow(IReadOnlyList<T> choices, Func<T, string> format, int selectedIndex = 0, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _format = format ?? throw new ArgumentNullException(nameof(format));
        if (selectedIndex < 0 || selectedIndex >= choices.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        Selection = new ChoiceSelection<T>(choices, choices[selectedIndex], comparer);
    }

    public static ChoiceRow<T> FromValue(IReadOnlyList<T> choices, Func<T, string> format, T selectedValue, IEqualityComparer<T>? comparer = null) =>
        new(choices, format, FindSelectedIndex(choices, selectedValue, comparer), comparer);

    public static ChoiceRow<T> FromValue(IReadOnlyList<T> choices, Func<T, string> format, T selectedValue, T fallbackValue, IEqualityComparer<T>? comparer = null)
    {
        ChoiceSelection<T> selection = ChoiceSelection<T>.WithFallback(choices, selectedValue, fallbackValue, comparer);
        return new(choices, format, selection.SelectedIndex, comparer);
    }

    public ChoiceSelection<T> Selection { get; }
    public Func<T, string> Format => _format;
    public T Value { get => Selection.Value; set => Selection.SetValue(value); }
    public int SelectedIndex => Selection.SelectedIndex;
    public int Count => Selection.Items.Count;
    public bool TrySelectValue(T value) => Selection.TrySetValue(value);

    private static int FindSelectedIndex(IReadOnlyList<T> items, T value, IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(items);
        IEqualityComparer<T> effectiveComparer = comparer ?? EqualityComparer<T>.Default;
        for (int index = 0; index < items.Count; index++)
            if (effectiveComparer.Equals(items[index], value)) return index;
        throw new ArgumentException("The selected value must be present in the choice items.", nameof(value));
    }
}
