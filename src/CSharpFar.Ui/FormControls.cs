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

    public static CheckBoxColumnsRow CheckBoxColumns(
        string id,
        IReadOnlyList<IReadOnlyList<CheckBoxRow>> columns,
        int columnGap = 2)
    {
        id = RequiredId(id);
        return new CheckBoxColumnsRow(columns, columnGap) { Id = id };
    }

    public static ButtonRow Buttons(string id, params DialogButton[] buttons)
    {
        id = RequiredId(id);
        return new ButtonRow(buttons) { Id = id };
    }

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

    public static MultiLineChoiceFormRow<T> MultiLineChoice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null)
    {
        id = RequiredId(id);
        return new MultiLineChoiceFormRow<T>(label, values, format, selectedValue, itemsPerRow, comparer) { Id = id };
    }

    public static MultiLineChoiceFormRow<T> MultiLineChoice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null)
    {
        id = RequiredId(id);
        return new MultiLineChoiceFormRow<T>(label, values, format, selectedValue, fallbackValue, itemsPerRow, comparer) { Id = id };
    }

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
        ChoiceSelection<T> selection = ChoiceSelection<T>.FromValue(items, selectedValue, comparer);
        var dropdown = new DropdownSelect<T>(items, itemText) { SelectedIndex = selection.SelectedIndex };
        return new DropdownSelectFormRow<T>(label, dropdown) { Id = RequiredId(id) };
    }

    public static LabeledValueRow Value(string id, string label, Func<string> value)
    {
        id = RequiredId(id);
        return new LabeledValueRow(label, value) { Id = id };
    }

    private static ChoiceModel<T> CreateChoice<T>(
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(format);
        return new ChoiceModel<T>(ChoiceSelection<T>.FromValue(values, selectedValue, comparer), format);
    }

    private static ChoiceModel<T> CreateChoice<T>(
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(format);
        return new ChoiceModel<T>(ChoiceSelection<T>.FromValueOrFallback(values, selectedValue, fallbackValue, comparer), format);
    }

    private static string RequiredId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }
}
