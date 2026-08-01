using CSharpFar.Ui;

namespace CSharpFar.Tests;

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
