using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed partial class ScrollableFormDialogTests
{
    [Fact]
    public void ClosedDropdownFrame_HasNoSnapshotOrChildTargets()
    {
        var dropdown = new DropdownSelect<string>(["one", "two", "three"], static item => item);
        var row = new DropdownSelectFormRow<string>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([row]);

        ScrollableFormFrame frame = RenderFrame(form, visibleRows: 1, screenHeight: 8);

        FormRowTargetFrame target = Assert.IsType<FormRowTargetFrame>(Assert.Single(frame.Targets, target => target.Kind == FormTargetKind.Row));
        FormCompositeFrame compositeFrame = Assert.IsType<FormCompositeFrame>(target.CompositeFrame);
        Assert.False(compositeFrame.IsOpen);
        Assert.IsType<DropdownCompositeSnapshot>(compositeFrame.State);
        Assert.Null(compositeFrame.Overlay);
    }

    [Fact]
    public void DropdownKeyboard_UsesCommittedListState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        DropdownSelectFrame committed = Assert.IsType<DropdownSelectFrame>(
            Assert.IsType<FormRowTargetFrame>(Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row)).DropdownFrame);
        Assert.Equal(0, committed.Popup!.List.SelectedIndex);

        dropdown.SelectedIndex = 6;
        dropdown.SetScrollTopForTesting(6);
        UiInputResult result = host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));

        Assert.True(result.Handled);
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.Equal(0, dropdown.ScrollTop);
    }

    [Fact]
    public void ClosedDropdownOpening_UsesCommittedListState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();

        dropdown.SelectedIndex = 6;
        dropdown.SetScrollTopForTesting(6);
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));

        Assert.True(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, dropdown.ScrollTop);
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownOpen_AfterFailedRenderNextInputUsesCommittedClosedState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(FormInputResultKind.Cancel, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownClose_AfterFailedRenderNextInputUsesCommittedOpenState()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        Assert.False(dropdown.IsOpen);
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownCancelBaseline_RestoresFromCommittedFrame()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));
        host.Composition.Render();

        dropdown.SelectedIndex = 6;
        dropdown.Close(commit: true);
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Escape)));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownMouse_UsesCommittedPopupGeometry()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        driver.SetSize(24, 4);

        UiInputResult result = host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));

        Assert.True(result.Handled);
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal("choice", layer.LastRouteResult.Value.FormResult.SourceRowId);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownMouse_AfterFailedRenderUsesPreviousCommittedGeometry()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        layer.ThrowOnRender = true;

        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        UiInputResult result = host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));

        Assert.True(result.Handled);
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownFocusTransition_CancelsTemporarySelectionAndRemovesTargets()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        dropdown.Open();
        var form = new ScrollableFormDialog([
            new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" },
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
        ]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));
        host.Composition.Render();

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Tab)));
        host.Composition.Render();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal("next", form.FocusedRowId);
        Assert.False(dropdown.HasScrollbarDrag);
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target =>
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar);
    }

    [Fact]
    public void DropdownMouseFocusTransition_CancelsTemporarySelection()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        dropdown.Open();
        var form = new ScrollableFormDialog([
            new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" },
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
        ]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)));
        host.Composition.Render();
        FormRowTargetFrame next = Assert.Single(layer.CommittedFrame.Targets.OfType<FormRowTargetFrame>(), target => target.Row.Id == "next");

        host.Composition.DispatchInput(Mouse(next.Bounds.X, next.Bounds.Y));

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal("next", form.FocusedRowId);
    }

    [Fact]
    public void DropdownTargets_UsePopupBeforeUnderlyingRows()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([
            new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" },
            new CheckBoxRow(new CheckBoxLine("covered")) { Id = "covered" },
        ]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        UiInteractionFrame interaction = form.BuildInteractionFrame(layer.CommittedFrame);

        Assert.True(interaction.TryHitTest(popup.Bounds.X + 1, popup.Bounds.Y + 1, out UiHitRegion hit));
        Assert.Equal(FormTargetIds.ForDropdownPopup(FormTargetIds.ForExplicitRow("choice")), hit.Target);
    }

    [Fact]
    public void DropdownMouseResults_OnlyConfirmedChangedValueReportsValueChanged()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame row = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.Row);

        FormRowTargetFrame rowTarget = Assert.IsType<FormRowTargetFrame>(row);
        host.Composition.DispatchInput(Mouse(rowTarget.Layout.ControlBounds.X, rowTarget.Layout.ControlBounds.Y));
        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);

        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        int scrollbarY = scrollbar.Bounds.Bottom - 1;
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbarY));
        Assert.Equal(FormInputResultKind.Handled, layer.LastRouteResult!.Value.FormResult.Kind);
        host.Composition.Render();
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbarY, MouseButton.Left, MouseEventKind.Up));

        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        Rect content = Assert.IsType<FormCompositeChildTargetFrame>(popup).CompositeFrame.State is DropdownCompositeSnapshot { Frame.Popup.List.ContentBounds: { } value }
            ? value
            : throw new Xunit.Sdk.XunitException("Expected dropdown content frame.");
        host.Composition.DispatchInput(Mouse(content.X, content.Y + 1));
        Assert.Equal(FormInputResultKind.ValueChanged, layer.LastRouteResult!.Value.FormResult.Kind);
    }

    [Fact]
    public void DropdownConfirmedSelectionRejectedRender_OutsideClickRestoresCommittedValueAndClosesOverlay()
    {
        var dropdown = OpenDropdown();
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([row]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());

        host.Composition.DispatchInput(Mouse(23, 9));

        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, row.Value);

        layer.ThrowOnRender = false;
        host.Composition.Render();
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target =>
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownConfirmedSelectionRejectedRender_ClickOtherRowDoesNotPublishRejectedSelection()
    {
        var dropdown = OpenDropdown();
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
            row,
        ]);
        form.SetInitialFocus("choice");
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        FormRowTargetFrame next = Assert.Single(layer.CommittedFrame.Targets.OfType<FormRowTargetFrame>(), target => target.Row.Id == "next");

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(next.Bounds.X, next.Bounds.Y));

        Assert.Equal("next", form.FocusedRowId);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, row.Value);

        layer.ThrowOnRender = false;
        host.Composition.Render();
        Assert.DoesNotContain(layer.CommittedFrame.Targets, target =>
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar);
    }

    [Fact]
    public void DropdownConfirmedSelectionRejectedRender_FooterSubmitUsesCommittedValue()
    {
        var dropdown = OpenDropdown();
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog();
        form.SetRows([row], [FooterButtons()]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFooterFormHostWithLayer(form, driver, bodyRows: 1, footerY: 5);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);
        FormRowTargetFrame footer = Assert.Single(layer.CommittedFrame.Targets.OfType<FormRowTargetFrame>(), target => target.IsFooter);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(footer.Bounds.X + 2, footer.Bounds.Y));
        host.Composition.DispatchInput(Mouse(footer.Bounds.X + 2, footer.Bounds.Y, kind: MouseEventKind.Up));

        Assert.Equal(FormInputResultKind.Submit, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal(0, row.Value);
    }

    [Fact]
    public void DropdownClosedRejectedRender_OutsideClickRestoresCommittedOpenLifecycleThenCancels()
    {
        var dropdown = OpenDropdown();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame popup = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownPopup);

        host.Composition.DispatchInput(Mouse(popup.Bounds.X + 1, popup.Bounds.Y + 2));
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(23, 9));

        Assert.Equal(FormInputResultKind.OverlayChanged, layer.LastRouteResult!.Value.FormResult.Kind);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownOpenRejectedRender_ClickOtherRowIgnoresRejectedPopup()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString());
        var row = new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" };
        var form = new ScrollableFormDialog([
            new CheckBoxRow(new CheckBoxLine("next")) { Id = "next" },
            row,
        ]);
        form.SetInitialFocus("choice");
        var driver = new FakeConsoleDriver(24, 10);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 2);
        host.Composition.Render();
        FormRowTargetFrame next = Assert.Single(layer.CommittedFrame.Targets.OfType<FormRowTargetFrame>(), target => target.Row.Id == "next");

        host.Composition.DispatchInput(new KeyConsoleInputEvent(Key(ConsoleKey.Spacebar)));
        Assert.True(dropdown.IsOpen);
        layer.ThrowOnRender = true;
        Assert.Throws<InvalidOperationException>(() => host.Composition.Render());
        host.Composition.DispatchInput(Mouse(next.Bounds.X, next.Bounds.Y));

        Assert.Equal("next", form.FocusedRowId);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownScrollbarCapture_DisappearingTargetClearsCapture()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 8).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 12);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame scrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));

        dropdown.MaxVisibleRows = 8;
        host.Composition.Render();
        host.Composition.DispatchInput(Mouse(0, 11, MouseButton.Left, MouseEventKind.Move));

        Assert.DoesNotContain(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, layer.LastRouteKind);
    }

    [Fact]
    public void DropdownScrollbarDrag_CommittedResizeKeepsCaptureWithNewScrollbarTarget()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 4,
        };
        dropdown.Open();
        var form = new ScrollableFormDialog([new DropdownSelectFormRow<int>("Value:", dropdown) { Id = "choice" }]);
        var driver = new FakeConsoleDriver(24, 12);
        var (host, layer) = CreateRoutedFormHostWithLayer(form, driver, visibleRows: 1);
        host.Composition.Render();
        FormTargetFrame oldScrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(oldScrollbar.Bounds.X, oldScrollbar.Bounds.Y + 1, MouseButton.Left, MouseEventKind.Down));

        dropdown.MaxVisibleRows = 5;
        host.Composition.Render();
        FormTargetFrame newScrollbar = Assert.Single(layer.CommittedFrame.Targets, target => target.Kind == FormTargetKind.DropdownScrollbar);
        host.Composition.DispatchInput(Mouse(0, 8, MouseButton.Left, MouseEventKind.Move));

        Assert.NotEqual(oldScrollbar.Bounds, newScrollbar.Bounds);
        Assert.Equal(UiInputRouteKind.CapturedTarget, layer.LastRouteKind);
        Assert.Equal(newScrollbar.Target, layer.LastRouteTarget);
    }

}
