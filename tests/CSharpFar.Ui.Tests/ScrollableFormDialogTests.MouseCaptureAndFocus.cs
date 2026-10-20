using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    [Fact]
    public void RoutedScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var form = LongForm();
        var driver = new FakeConsoleDriver(20, 8);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 4), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.BodyScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        host.Composition.Render();
        host.Composition.DispatchInput(Mouse(0, 7, MouseButton.Left, MouseEventKind.Move));
        host.Composition.Render();
        host.Composition.DispatchInput(Mouse(0, 7, MouseButton.Left, MouseEventKind.Up));
        int afterDrag = form.ScrollTop;
        UiInputResult next = host.Composition.DispatchInput(Mouse(2, 1, MouseButton.WheelDown, MouseEventKind.Wheel));

        Assert.True(afterDrag > 0);
        Assert.True(next.Handled);
        Assert.True(form.ScrollTop >= afterDrag);
    }

    [Fact]
    public void RoutedHistoryScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var history = HistoryWithItems(20);
        var form = new ScrollableFormDialog([new TextInputRow(new CommandLineState(), history) { Id = "pattern" }]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 1), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Move));
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Up));
        int afterDrag = history.FirstVisibleIndex;
        UiInputResult next = host.Composition.DispatchInput(Mouse(2, 2));

        Assert.True(afterDrag > 0);
        Assert.Null(history.Scrollbar.DragState);
        Assert.True(next.Handled);
    }

    [Fact]
    public void RoutedLabeledHistoryScrollbarCapture_ContinuesOutsideBoundsAndReleasesAfterUp()
    {
        var history = HistoryWithItems(20);
        var form = new ScrollableFormDialog([new LabeledTextInputRow("Value:", new CommandLineState(), history, inputWidth: 20) { Id = "pattern" }]);
        var driver = new FakeConsoleDriver(20, 10);
        var screen = new ScreenRenderer(driver);
        var layer = new TestFormLayer(screen, form, context => new FormRenderContext(context, new Rect(0, 0, 20, 1), DialogStyles.Border));
        var host = UiTestHost.Create(screen, layer);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.HistoryScrollbar);

        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));
        UiInputResult mouseDown = layer.LastRouteResult!.Value.UiResult;
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Move));
        host.Composition.DispatchInput(Mouse(0, 9, MouseButton.Left, MouseEventKind.Up));
        int afterDrag = history.FirstVisibleIndex;
        host.Composition.DispatchInput(Mouse(2, 2, MouseButton.Left, MouseEventKind.Move));

        Assert.Equal(UiMouseCaptureRequestKind.Capture, mouseDown.MouseCaptureRequest.Kind);
        Assert.Equal(scrollbar.Target, mouseDown.MouseCaptureRequest.Target);
        Assert.True(afterDrag > 0);
        Assert.Null(history.Scrollbar.DragState);
        Assert.Equal(afterDrag, history.FirstVisibleIndex);
    }

    [Fact]
    public void RoutedMouseFocus_NotHandledRowInvalidatesFrame()
    {
        var row = new NotHandledFocusableRow("target");
        var form = new ScrollableFormDialog([
            new TextInputRow(new CommandLineState()) { Id = "first" },
            row,
        ]);
        var driver = new FakeConsoleDriver(20, 5);
        var host = CreateRoutedFormHost(form, driver, visibleRows: 2);
        host.Composition.Render();

        UiInputResult result = host.Composition.DispatchInput(Mouse(2, 1));
        host.Composition.Render();

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Equal("target", form.FocusedRowId);
        Assert.True(row.RenderedFocused);
    }

    [Fact]
    public void InitialFocus_FindsFooterRow()
    {
        ScrollableFormDialog form = FooterForm();

        form.SetInitialFocus("footerButtons");
        Assert.Equal("footerButtons", form.FocusedRowId);
        Assert.Equal(FormRowRole.ButtonBar, form.FocusedRowRole);
    }

    [Fact]
    public void DuplicateIdsAcrossBodyAndFooterThrow()
    {
        var form = new ScrollableFormDialog();

        Assert.Throws<InvalidOperationException>(() => form.SetRows(
            [new CheckBoxRow(new CheckBoxLine("body")) { Id = "same" }],
            [new ButtonRow([new DialogButton("ok", "OK", 'O')]) { Id = "same" }]));
    }

    [Fact]
    public void CursorHiddenWhenFooterButtonFocusedAndRestoredInBody()
    {
        ScrollableFormDialog form = FooterForm();
        FakeConsoleDriver driver = RenderWithFooter(form, bodyRows: 2, footerY: 3);
        Assert.True(driver.CursorVisible);
        RequestFocus(form, "footerButtons");
        Assert.False(driver.CursorVisible);

        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));
        HandleKey(form, new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false));
        RenderWithFooter(form, driver, bodyRows: 2, footerY: 3);
        Assert.True(driver.CursorVisible);
        Assert.Equal("first", form.FocusedRowId);
    }

}
