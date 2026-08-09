using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Semantic visual treatment for a standard form control.</summary>
public enum FormControlTone
{
    /// <summary>Uses the ordinary dialog presentation.</summary>
    Default,

    /// <summary>Uses the warning-dialog presentation.</summary>
    Warning,
}

/// <summary>Canonical application-level factories for standard form controls.</summary>
public static class FormControls
{
    /// <summary>Creates a non-interactive line of ordinary dialog text.</summary>
    public static FormRow Label(string text) => new LabelRow(text);

    /// <summary>Creates a standard dialog separator.</summary>
    public static FormRow Separator() => new SeparatorRow(FarDialogStyles.Border);

    /// <summary>Creates blank vertical space in a form.</summary>
    public static FormRow Spacer(int height = 1) => new SpacerRow(height);

    /// <summary>Creates an error message using the theme's error presentation.</summary>
    public static FormRow Error(string text) => new LabelRow(text, FarDialogStyles.Error);

    /// <summary>Creates an error message whose text is read when the form renders.</summary>
    public static FormRow Error(Func<string?> text) => FormFooter.Error(text);

    /// <summary>Creates an unlabeled text-input row from a configured field.</summary>
    public static TextInputRow Text(TextField field) => new(field);

    /// <summary>Creates a labeled text-input row from a configured field.</summary>
    public static LabeledTextInputRow Text(string label, TextField field) => new(label, field);

    /// <summary>Creates a labeled text-input row with adjacent semantic action buttons.</summary>
    public static TextInputWithButtonsRow Text(
        string label,
        TextField field,
        IReadOnlyList<DialogButton> buttons) =>
        new(label, field, buttons);

    /// <summary>Creates a standard checkbox row with an application-owned identity.</summary>
    public static CheckBoxRow CheckBox(
        string id,
        string label,
        bool isChecked = false,
        bool enabled = true,
        string? disabledReason = null) =>
        new(label, isChecked) { Id = RequiredId(id), Enabled = enabled, DisabledReason = disabledReason };

    /// <summary>Creates a standard tri-state checkbox row with an application-owned identity.</summary>
    public static TriStateCheckBoxRow TriStateCheckBox(
        string id,
        string label,
        CheckState value = CheckState.Unchecked,
        bool enabled = true,
        string? disabledReason = null) =>
        new(RequiredId(id), label, value) { Enabled = enabled, DisabledReason = disabledReason };

    /// <summary>Creates a tri-state permission matrix row.</summary>
    public static TriStateMatrixFormRow TriStateMatrix(
        string id,
        IReadOnlyList<TriStateMatrixColumn> columns,
        IReadOnlyList<TriStateMatrixRow> rows) =>
        new(columns, rows) { Id = RequiredId(id) };

    /// <summary>Creates a grid of related checkbox rows.</summary>
    public static CheckBoxColumnsRow CheckBoxColumns(
        string id,
        IReadOnlyList<IReadOnlyList<CheckBoxRow>> columns)
    {
        id = RequiredId(id);
        return new CheckBoxColumnsRow(columns) { Id = id };
    }

    /// <summary>Creates a standard form button row using the requested semantic visual treatment.</summary>
    public static ButtonRow Buttons(
        string id,
        IReadOnlyList<DialogButton> buttons,
        FormControlTone tone = FormControlTone.Default)
    {
        id = RequiredId(id);
        ArgumentNullException.ThrowIfNull(buttons);
        return new ButtonRow(buttons, tone: tone) { Id = id };
    }

    /// <summary>Creates a standard form button row with the conventional default treatment.</summary>
    public static ButtonRow Buttons(string id, params DialogButton[] buttons) => Buttons(id, (IReadOnlyList<DialogButton>)buttons);

    /// <summary>Creates a standard button row with the conventional actions identity.</summary>
    public static ButtonRow Buttons(params DialogButton[] buttons) => Buttons("actions", buttons);

    /// <summary>Creates a segmented one-line choice row, using <paramref name="fallbackValue"/> when the selected value is absent.</summary>
    public static ChoiceFormRow<T> Choice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null,
        bool enabled = true,
        string? disabledReason = null) =>
        new(CreateChoice(values, format, selectedValue, comparer), label) { Id = RequiredId(id), Enabled = enabled, DisabledReason = disabledReason };

    public static ChoiceFormRow<T> Choice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        IEqualityComparer<T>? comparer = null,
        bool enabled = true,
        string? disabledReason = null) =>
        new(CreateChoice(values, format, selectedValue, fallbackValue, comparer), label) { Id = RequiredId(id), Enabled = enabled, DisabledReason = disabledReason };

    /// <summary>Creates the compact one-line choice presentation.</summary>
    public static CompactChoiceFormRow<T> CompactChoice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        IEqualityComparer<T>? comparer = null,
        bool enabled = true,
        string? disabledReason = null) =>
        new(CreateChoice(values, format, selectedValue, comparer), label) { Id = RequiredId(id), Enabled = enabled, DisabledReason = disabledReason };

    /// <summary>Creates a multi-line choice row with an explicit fallback value; <paramref name="itemsPerRow"/> controls its visible grouping.</summary>
    public static MultiLineChoiceFormRow<T> MultiLineChoice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null,
        bool enabled = true,
        string? disabledReason = null)
    {
        id = RequiredId(id);
        return new MultiLineChoiceFormRow<T>(label, values, format, selectedValue, itemsPerRow, comparer) { Id = id, Enabled = enabled, DisabledReason = disabledReason };
    }

    public static MultiLineChoiceFormRow<T> MultiLineChoice<T>(
        string id,
        string label,
        IReadOnlyList<T> values,
        Func<T, string> format,
        T selectedValue,
        T fallbackValue,
        int itemsPerRow,
        IEqualityComparer<T>? comparer = null,
        bool enabled = true,
        string? disabledReason = null)
    {
        id = RequiredId(id);
        return new MultiLineChoiceFormRow<T>(label, values, format, selectedValue, fallbackValue, itemsPerRow, comparer) { Id = id, Enabled = enabled, DisabledReason = disabledReason };
    }

    /// <summary>Creates a dropdown row without exposing its popup or selection model.</summary>
    public static DropdownSelectFormRow<T> Dropdown<T>(
        string id,
        string label,
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        T selectedValue,
        IEqualityComparer<T>? comparer = null,
        bool enabled = true,
        string? disabledReason = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemText);
        ChoiceSelection<T> selection = ChoiceSelection<T>.FromValue(items, selectedValue, comparer);
        var dropdown = new DropdownSelect<T>(items, itemText, comparer) { SelectedIndex = selection.SelectedIndex };
        return new DropdownSelectFormRow<T>(label, dropdown) { Id = RequiredId(id), Enabled = enabled, DisabledReason = disabledReason };
    }

    /// <summary>Creates a read-only labeled value row.</summary>
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
