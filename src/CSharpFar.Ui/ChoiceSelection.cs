namespace CSharpFar.Ui;

/// <summary>Valid, non-empty selection state shared by choice presentations.</summary>
public sealed class ChoiceSelection<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly IEqualityComparer<T> _comparer;

    public ChoiceSelection(IReadOnlyList<T> items, T selectedValue, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("A choice selection requires at least one item.", nameof(items));

        _items = items.ToArray();
        _comparer = comparer ?? EqualityComparer<T>.Default;
        SelectedIndex = FindIndex(selectedValue);
        if (SelectedIndex < 0)
            throw new ArgumentException("The selected value must be present in the choice items.", nameof(selectedValue));
    }

    public static ChoiceSelection<T> WithFallback(IReadOnlyList<T> items, T selectedValue, T fallbackValue, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        IEqualityComparer<T> effectiveComparer = comparer ?? EqualityComparer<T>.Default;
        bool selectedPresent = items.Any(item => effectiveComparer.Equals(item, selectedValue));
        return new ChoiceSelection<T>(items, selectedPresent ? selectedValue : fallbackValue, effectiveComparer);
    }

    public IReadOnlyList<T> Items => _items;
    public T Value => _items[SelectedIndex];
    public int SelectedIndex { get; private set; }

    public void SetValue(T value)
    {
        int index = FindIndex(value);
        if (index < 0)
            throw new ArgumentException("The selected value must be present in the choice items.", nameof(value));
        SelectedIndex = index;
    }

    public bool TrySetValue(T value)
    {
        int index = FindIndex(value);
        if (index < 0) return false;
        SelectedIndex = index;
        return true;
    }

    public bool SelectIndex(int index)
    {
        if (index < 0 || index >= _items.Count || index == SelectedIndex)
            return false;
        SelectedIndex = index;
        return true;
    }

    public bool SelectNext()
    {
        if (_items.Count == 1)
            return false;
        SelectedIndex = (SelectedIndex + 1) % _items.Count;
        return true;
    }

    public bool SelectPrevious()
    {
        if (_items.Count == 1)
            return false;
        SelectedIndex = SelectedIndex == 0 ? _items.Count - 1 : SelectedIndex - 1;
        return true;
    }

    private int FindIndex(T value)
    {
        for (int index = 0; index < _items.Count; index++)
        {
            if (_comparer.Equals(_items[index], value))
                return index;
        }
        return -1;
    }
}
