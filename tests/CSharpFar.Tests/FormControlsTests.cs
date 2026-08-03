using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class FormControlsTests
{
    [Fact]
    public void Choice_UsesSelectedValueAndComparer()
    {
        ChoiceFormRow<string> row = FormControls.Choice(
            "choice",
            "Choice",
            ["first", "second"],
            static value => value,
            "SECOND",
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal("choice", row.Id);
        Assert.Equal("second", row.Value);
    }

    [Fact]
    public void Choice_RequiresSelectedValueUnlessAnExplicitFallbackIsProvided()
    {
        Assert.Throws<ArgumentException>(() => FormControls.Choice(
            "choice",
            "Choice",
            ["first", "second"],
            static value => value,
            "missing"));

        ChoiceFormRow<string> row = FormControls.Choice(
            "choice",
            "Choice",
            ["first", "second"],
            static value => value,
            "missing",
            "second");

        Assert.Equal("second", row.Value);
    }

    [Fact]
    public void Dropdown_RequiresSelectedValueAndPreservesItsId()
    {
        Assert.Throws<ArgumentException>(() => FormControls.Dropdown(
            "scope",
            "Scope",
            ["current", "all"],
            static value => value,
            "missing"));

        DropdownSelectFormRow<string> row = FormControls.Dropdown(
            "scope",
            "Scope",
            ["current", "all"],
            static value => value,
            "all");

        Assert.Equal("scope", row.Id);
        Assert.Equal("all", row.Value);
    }

    [Fact]
    public void DisabledDropdown_DoesNotAcceptInputAndCanBeReenabled()
    {
        DropdownSelectFormRow<string> row = FormControls.Dropdown(
            "scope",
            "Scope",
            ["current", "all"],
            static value => value,
            "current");
        row.Enabled = false;
        row.DisabledReason = "Unavailable";

        Assert.False(row.IsFocusable);
        Assert.IsType<DropdownCompositeController<string>>(((IFormCompositeOwner)row).CompositeController);
        Assert.Equal("current", row.Value);

        row.Enabled = true;

        Assert.True(row.IsFocusable);
    }

    [Fact]
    public void AdditionalFactories_AssignIdsAndPreserveControlValues()
    {
        CheckBoxRow left = FormControls.CheckBox("left", "Left", true);
        CheckBoxRow right = FormControls.CheckBox("right", "Right");
        CheckBoxColumnsRow columns = FormControls.CheckBoxColumns("columns", [[left], [right]]);
        MultiLineChoiceFormRow<string> choice = FormControls.MultiLineChoice(
            "choice", "Choice", ["first", "second", "third"], static value => value, "second", itemsPerRow: 2);
        ButtonRow buttons = FormControls.Buttons("actions", DialogButton.Default("save", "Save", 'S'));
        LabeledValueRow value = FormControls.Value("status", "Status:", static () => "Ready");

        Assert.Equal("columns", columns.Id);
        Assert.True(left.Value);
        Assert.False(right.Value);
        Assert.Equal("choice", choice.Id);
        Assert.Equal("second", choice.Value);
        Assert.Equal(2, choice.Height);
        Assert.Equal("actions", buttons.Id);
        Assert.Equal("status", value.Id);
    }

    [Fact]
    public void MultiLineChoice_UsesFallbackWhenSelectedValueIsMissing()
    {
        MultiLineChoiceFormRow<string> row = FormControls.MultiLineChoice(
            "choice", "Choice", ["first", "second"], static value => value, "missing", "second", itemsPerRow: 1);

        Assert.Equal("second", row.Value);
    }
}
