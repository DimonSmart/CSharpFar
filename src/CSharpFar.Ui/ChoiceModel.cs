namespace CSharpFar.Ui;

/// <summary>Selection state and presentation formatter shared by choice rows.</summary>
public sealed class ChoiceModel<T>
{
    public ChoiceModel(IReadOnlyList<T> choices, Func<T, string> format, int selectedIndex = 0, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(format);
        if (selectedIndex < 0 || selectedIndex >= choices.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        Selection = new ChoiceSelection<T>(choices, choices[selectedIndex], comparer);
        Format = format;
    }

    public ChoiceModel(ChoiceSelection<T> selection, Func<T, string> format)
    {
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    public ChoiceSelection<T> Selection { get; }
    public Func<T, string> Format { get; }
    public T Value { get => Selection.Value; set => Selection.SetValue(value); }
    public int SelectedIndex => Selection.SelectedIndex;
    public int Count => Selection.Items.Count;
}
