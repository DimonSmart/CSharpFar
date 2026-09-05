using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ChoiceFormRowSelectionTests
{
    [Fact]
    public void ChoiceFormRow_SelectsMatchingValueWithConfiguredComparer()
    {
        var row = new ChoiceFormRow<string>(
            label: "Mode:",
            values: ["Default", "Copy", "Inherit"],
            format: static value => value,
            selectedValue: "copy",
            comparer: StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Copy", row.Value);
        Assert.Equal(1, row.Choice.SelectedIndex);
    }

    [Fact]
    public void ChoiceFormRow_UsesFallbackValueWhenSelectedValueIsUnknown()
    {
        var row = new ChoiceFormRow<string>(
            label: "Mode:",
            values: ["Default", "Copy", "Inherit"],
            format: static value => value,
            selectedValue: "Unknown",
            fallbackValue: "Inherit");

        Assert.Equal("Inherit", row.Value);
    }

    [Fact]
    public void DisabledChoice_IsNotFocusableAndDoesNotChangeValue()
    {
        var row = new ChoiceFormRow<string>(
            label: "Mode:",
            values: ["Default", "Copy"],
            format: static value => value,
            selectedValue: "Default")
        {
            Enabled = false,
            DisabledReason = "Unavailable",
        };

        Assert.False(row.IsFocusable);
        Assert.Equal(FormInputResultKind.NotHandled,
            row.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false), new FormRowInputContext(true)).Kind);
        Assert.Equal("Default", row.Value);
    }

    [Fact]
    public void MultiLineChoiceFormRow_SplitsValuesByItemsPerRow()
    {
        var row = new MultiLineChoiceFormRow<string>(
            label: string.Empty,
            values: ["One", "Two", "Three", "Four", "Five"],
            format: static value => value,
            selectedValue: "Five",
            itemsPerRow: 2);

        Assert.Equal(3, row.Height);
        Assert.Equal("Five", row.Value);
    }
}
