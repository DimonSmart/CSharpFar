using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ChoiceModelTests
{
    [Fact]
    public void ChoiceSelection_SelectIndexDistinguishesMissingUnchangedAndChanged()
    {
        var selection = ChoiceSelection<string>.FromValue(["one", "two"], "one");

        Assert.Equal(ChoiceSelectionResult.Missing, selection.SelectIndex(-1));
        Assert.Equal(ChoiceSelectionResult.Unchanged, selection.SelectIndex(0));
        Assert.Equal(ChoiceSelectionResult.Changed, selection.SelectIndex(1));
    }

    [Fact]
    public void Value_ReassigningCurrentValueIsANoOp()
    {
        var choice = new ChoiceModel<string>(["one", "two"], static value => value);
        choice.Value = choice.Value;
        Assert.Equal("one", choice.Value);
        Assert.Equal(ChoiceSelectionResult.Unchanged, choice.Selection.SelectValue("one"));
        Assert.Equal(ChoiceSelectionResult.Missing, choice.Selection.SelectValue("missing"));
        Assert.Throws<ArgumentException>(() => choice.Value = "missing");
    }

    [Fact]
    public void Selection_ValueResultDistinguishesMissingUnchangedAndChanged()
    {
        var selection = ChoiceSelection<string>.FromValue(["one", "two"], "one");

        Assert.Equal(ChoiceSelectionResult.Unchanged, selection.SelectValue("one"));
        Assert.Equal(ChoiceSelectionResult.Changed, selection.SelectValue("two"));
        Assert.Equal(ChoiceSelectionResult.Missing, selection.SelectValue("missing"));
    }

    [Fact]
    public void SegmentedPresentation_RendersAndHitTestsOnlyCalculatedRange()
    {
        var selection = new ChoiceSelection<string>(["one", "two", "three"], "one");
        ChoiceLayout layout = ChoiceLayoutCalculator.Segmented(selection, static value => value, new Rect(0, 0, 40, 1), "Mode", 1, 3);
        var driver = new FakeConsoleDriver(40, 2);
        UiTestRender.Render(new ScreenRenderer(driver), canvas => ChoiceRenderer.Render(canvas, layout, selection, static value => value, "Mode", new(DialogStyles.Fill, DialogStyles.FocusedInput, true)));

        Assert.DoesNotContain("one", driver.GetRow(0));
        Assert.Equal(ChoiceInputResultKind.ValueChanged, ChoiceInput.HandleMouse(selection, new MouseConsoleInputEvent(layout.Targets[0].Bounds.X, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), layout));
        Assert.Equal("two", selection.Value);
    }

    [Fact]
    public void MultilinePresentation_CentralizesRowsAndCursorTarget()
    {
        var selection = new ChoiceSelection<string>(["one", "two", "three"], "three");
        ChoiceLayout layout = ChoiceLayoutCalculator.MultilineSegmented(selection, static value => value, new Rect(0, 0, 30, 2), "Mode", [2, 3]);
        Assert.Equal(2, layout.RowBounds.Count);
        Assert.True(ChoiceRenderer.TryGetSelectedMarkerBounds(layout, selection, out Rect marker));
        Assert.Equal(1, marker.Y);
    }

    [Fact]
    public void ChoiceSelection_DoesNotExposeMutableArray()
    {
        var source = new[] { "one", "two" };
        var selection = ChoiceSelection<string>.FromValue(source, "one");

        source[0] = "changed";

        Assert.IsNotType<string[]>(selection.Items);
        Assert.Equal("one", selection.Value);
    }

    [Fact]
    public void ChoiceLayout_DoesNotExposeMutableGeometryArrays()
    {
        var selection = ChoiceSelection<string>.FromValue(["one", "two"], "one");
        ChoiceLayout layout = ChoiceLayoutCalculator.Segmented(selection, static value => value, new Rect(0, 0, 20, 1), "Mode");

        Assert.IsNotType<Rect[]>(layout.RowBounds);
        Assert.IsNotType<ChoiceLayoutHitTarget[]>(layout.Targets);
    }
}
