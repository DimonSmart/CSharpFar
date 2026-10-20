using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    [Fact]
    public void RoutedWheelScroll_PersistsAfterRenderWithoutMovingFocus()
    {
        var text = new CommandLineState();
        var form = new ScrollableFormDialog([
            new TextInputRow(text) { Id = "first" },
            new CheckBoxRow(new CheckBoxLine("two")) { Id = "two" },
            new CheckBoxRow(new CheckBoxLine("three")) { Id = "three" },
            new CheckBoxRow(new CheckBoxLine("four")) { Id = "four" },
            new CheckBoxRow(new CheckBoxLine("five")) { Id = "five" },
        ]);
        var driver = new FakeConsoleDriver(20, 6);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 3);
        host.Composition.Render();

        UiInputResult result = host.Composition.DispatchInput(Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        host.Composition.Render();

        Assert.True(result.Invalidate);
        Assert.Equal(2, form.ScrollTop);
        Assert.Equal("first", form.FocusedRowId);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void RoutedWheelScroll_HidesOffscreenHistoryOverlayUntilFocusedRowReceivesKey()
    {
        var history = TextFieldHistoryTestProvider.CreateState();
        history.History.Add("alpha");
        Assert.True(history.OpenAll(availableContentRows: 3));
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new LabelRow("two", DialogStyles.Fill),
            new LabelRow("three", DialogStyles.Fill),
            new LabelRow("four", DialogStyles.Fill),
            new LabelRow("five", DialogStyles.Fill),
        ]);
        var driver = new FakeConsoleDriver(20, 8);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(
            screen,
            form,
            context => new FormRenderContext(context, new Rect(0, 0, 20, 2), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();

        host.Composition.DispatchInput(Mouse(2, 0, MouseButton.WheelDown, MouseEventKind.Wheel));
        host.Composition.Render();

        Assert.Equal("pattern", form.FocusedRowId);
        Assert.Equal(3, form.ScrollTop);
        Assert.False(driver.CursorVisible);
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target => target.Kind is FormTargetKind.HistoryDropdown or FormTargetKind.HistoryScrollbar);
        Assert.DoesNotContain("alpha", driver.GetRow(0), StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", driver.GetRow(1), StringComparison.Ordinal);

        host.Composition.DispatchInput(new KeyConsoleInputEvent(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)));
        host.Composition.Render();

        Assert.Equal(0, form.ScrollTop);
        Assert.True(driver.CursorVisible);
        Assert.Contains(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);
        Assert.Contains("alpha", string.Concat(Enumerable.Range(0, 8).Select(driver.GetRow)), StringComparison.Ordinal);
    }

    [Fact]
    public void RoutedHistoryArrow_StaysOpenAfterMouseButtonRelease()
    {
        var history = TextFieldHistoryTestProvider.CreateState();
        history.History.Add("alpha");
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
        ]);
        var driver = new FakeConsoleDriver(20, 8);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 1);
        host.Composition.Render();

        UiInputResult open = host.Composition.DispatchInput(Mouse(19, 0, MouseButton.Left, MouseEventKind.Down));
        host.Composition.Render();
        UiInputResult release = host.Composition.DispatchInput(Mouse(19, 0, MouseButton.Left, MouseEventKind.Up));

        Assert.True(open.Invalidate);
        Assert.True(release.Handled);
        Assert.True(history.IsDropdownOpen);
    }

    [Fact]
    public void RoutedHistoryCloseOnLabelClick_InvalidatesFrame()
    {
        var history = TextFieldHistoryTestProvider.CreateState();
        history.History.Add("alpha");
        Assert.True(SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY: 0, screenHeight: 8));
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new LabelRow("covered", DialogStyles.Fill) { Id = "covered" },
            new LabelRow("covered", DialogStyles.Fill),
            new LabelRow("covered", DialogStyles.Fill),
            new LabelRow("covered", DialogStyles.Fill),
            new LabelRow("label", DialogStyles.Fill) { Id = "label" },
        ]);
        var driver = new FakeConsoleDriver(20, 12);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 6);
        host.Composition.Render();

        UiInputResult result = host.Composition.DispatchInput(Mouse(2, 5));

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void RoutedWheelInsideHistoryPopup_MovesHistoryWithoutScrollingBodyOrChangingValue()
    {
        var text = new CommandLineState();
        var history = HistoryWithItems(12);
        var form = new ScrollableFormDialog([
            new TextInputRow(text, history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 4), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);

        UiInputResult down = host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        host.Composition.Render();

        Assert.True(down.Handled);
        Assert.True(down.Invalidate);
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(1, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);
        Assert.Equal(string.Empty, text.Text);
        Assert.True(history.IsDropdownOpen);
        Assert.Equal("pattern", form.FocusedRowId);

        UiInputResult up = host.Composition.DispatchInput(Mouse(popup.Bounds.X, popup.Bounds.Y, MouseButton.WheelUp, MouseEventKind.Wheel));

        Assert.True(up.Handled);
        Assert.Equal(0, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);

        UiInputResult topBoundary = host.Composition.DispatchInput(Mouse(popup.Bounds.X, popup.Bounds.Y, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.True(topBoundary.Handled);
        Assert.Equal(0, history.SelectedIndex);
    }

    [Fact]
    public void RoutedWheelOnHistoryScrollbarAndAtBoundaries_BelongsToHistory()
    {
        var history = HistoryWithItems(12);
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 3), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);

        UiInputResult onScrollbar = host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.True(onScrollbar.Handled);
        Assert.Equal(1, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.Equal(0, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);

        for (int i = 0; i < history.Matches.Count; i++)
            host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelDown, MouseEventKind.Wheel));
        int last = history.SelectedIndex;
        UiInputResult boundary = host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.True(boundary.Handled);
        Assert.Equal(last, history.SelectedIndex);
        Assert.Equal(0, form.ScrollTop);
        Assert.True(history.IsDropdownOpen);
    }

    [Fact]
    public void RoutedWheelOutsideHistoryPopup_ScrollsBodyWithoutChangingHistorySelection()
    {
        var history = HistoryWithItems(12);
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState(), history) { Id = "pattern" },
            new CheckBoxRow(new CheckBoxLine("two")),
            new CheckBoxRow(new CheckBoxLine("three")),
            new CheckBoxRow(new CheckBoxLine("four")),
            new CheckBoxRow(new CheckBoxLine("five")),
            new CheckBoxRow(new CheckBoxLine("six")),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 3);
        host.Composition.Render();

        host.Composition.DispatchInput(Mouse(2, 0, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.True(form.ScrollTop > 0);
        Assert.Equal(0, history.SelectedIndex);
    }

    [Fact]
    public void RoutedHistoryMouseResults_OnlyChangedBufferReportsValueChanged()
    {
        var history = HistoryWithItems(12);
        var text = new CommandLineState();
        text.SetText(history.Matches[0]);
        var form = new ScrollableFormDialog([
            new TextInputRow(text, history) { Id = "pattern" },
            new LabelRow("outside", DialogStyles.Fill),
        ]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 2), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 1));
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);

        Assert.True(history.OpenAll(availableContentRows: 8));
        host.Composition.Render();
        popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryDropdown);
        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);

        Assert.True(history.OpenAll(availableContentRows: 8));
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 2));
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
    }

}
