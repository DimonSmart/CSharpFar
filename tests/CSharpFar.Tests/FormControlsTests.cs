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
        Assert.Equal(FormInputResultKind.NotHandled,
            row.HandleCompositeKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false), new FormRowInputContext(0, true), new FormCompositeFrame(false, null, [])).Kind);
        Assert.Equal("current", row.Value);

        row.Enabled = true;

        Assert.True(row.IsFocusable);
    }
}
