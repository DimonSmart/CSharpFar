using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Canonical application-level factories for standard form controls.</summary>
public static class FormControls
{
    public static TextInputRow Text(TextField field) => new(field);

    public static LabeledTextInputRow Text(string label, TextField field) => new(label, field);

    public static TextInputWithButtonsRow Text(
        string label,
        TextField field,
        IReadOnlyList<DialogButton> buttons) =>
        new(label, field, buttons);

    public static CheckBoxRow CheckBox(string id, string label, bool value = false) =>
        new(label, value) { Id = RequiredId(id) };

    public static TriStateCheckBoxRow TriStateCheckBox(
        string id,
        string label,
        CheckState value = CheckState.Unchecked) =>
        new(RequiredId(id), label, value);

    public static ChoiceFormRow<T> Choice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null) =>
        new(CreateChoice(values, format, selectedValue, comparer), label) { Id = RequiredId(id) };

    public static ChoiceFormRow<T> Choice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null) =>
        new(CreateChoice(values, format, selectedValue, fallbackValue, comparer), label) { Id = RequiredId(id) };

    public static CompactChoiceFormRow<T> CompactChoice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null) =>
        new(CreateChoice(values, format, selectedValue, comparer), label) { Id = RequiredId(id) };

    public static DropdownSelectFormRow<T> Dropdown<T>(
        string id,
        string label,
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        T selectedValue,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemText);
        var equalityComparer = comparer ?? EqualityComparer<T>.Default;
        int selectedIndex = FindIndex(items, selectedValue, equalityComparer);
        if (selectedIndex < 0)
            throw new ArgumentException("The selected value must exist in the dropdown items.", nameof(selectedValue));

        var dropdown = new DropdownSelect<T>(items, itemText) { SelectedIndex = selectedIndex };
        return new DropdownSelectFormRow<T>(label, dropdown) { Id = RequiredId(id) };
    }

    private static ChoiceRow<T> CreateChoice<T>(
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(format);
        var equalityComparer = comparer ?? EqualityComparer<T>.Default;
        int selectedIndex = FindIndex(values, selectedValue, equalityComparer);
        if (selectedIndex < 0)
            throw new ArgumentException("The selected value must exist in the choices.", nameof(selectedValue));
        return new ChoiceRow<T>(values, format, selectedIndex, equalityComparer);
    }

    private static ChoiceRow<T> CreateChoice<T>(
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(format);
        var equalityComparer = comparer ?? EqualityComparer<T>.Default;
        int selectedIndex = FindIndex(values, selectedValue, equalityComparer);
        if (selectedIndex < 0)
            selectedIndex = FindIndex(values, fallbackValue, equalityComparer);
        if (selectedIndex < 0)
            throw new ArgumentException("The fallback value must exist in the choices.", nameof(fallbackValue));
        return new ChoiceRow<T>(values, format, selectedIndex, equalityComparer);
    }

    private static int FindIndex<T>(IReadOnlyList<T> values, T value, IEqualityComparer<T> comparer)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (comparer.Equals(values[index], value))
                return index;
        }

        return -1;
    }

    private static string RequiredId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }
}
