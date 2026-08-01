using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class SharedFormApiTests
{
    [Fact]
    public void ModalFormLayout_WithFooterReservesRequestedHeightAndClampsSmallContent()
    {
        Rect content = new(2, 3, 20, 5);

        ModalFormLayout layout = ModalFormLayout.WithFooter(content, 2);
        ModalFormLayout smallLayout = ModalFormLayout.WithFooter(content, 9);

        Assert.Equal(new Rect(2, 3, 20, 3), layout.BodyBounds);
        Assert.Equal(new Rect(2, 6, 20, 2), layout.FooterBounds);
        Assert.Equal(new Rect(2, 3, 20, 0), smallLayout.BodyBounds);
        Assert.Equal(content, smallLayout.FooterBounds);
        Assert.Equal(content, ModalFormLayout.BodyOnly(content).BodyBounds);
        Assert.Null(ModalFormLayout.BodyOnly(content).FooterBounds);
    }

    [Fact]
    public void FormDialogInput_SubmitAndCancelPoliciesRecognizeOnlyStandardResults()
    {
        var form = new ScrollableFormDialog();

        Assert.True(FormDialogInput.ShouldSubmit(Routed(ConsoleKey.F10), FormInputResult.NotHandled, form));
        Assert.False(FormDialogInput.ShouldSubmit(Routed(ConsoleKey.F10), FormInputResult.Handled, form));
        Assert.False(FormDialogInput.ShouldSubmit(Routed(ConsoleKey.F10), FormInputResult.ValueChanged, form));
        Assert.False(FormDialogInput.ShouldSubmit(Routed(ConsoleKey.F10), FormInputResult.Cancel("cancel"), form));
        Assert.True(FormDialogInput.ShouldSubmit(Routed(ConsoleKey.Spacebar), FormInputResult.Submit("ok"), form));
        Assert.False(FormDialogInput.ShouldSubmit(Routed(ConsoleKey.Spacebar), FormInputResult.NotHandled, form));
        Assert.True(FormDialogInput.ShouldCancel(FormInputResult.Cancel("cancel")));
        Assert.False(FormDialogInput.ShouldCancel(FormInputResult.Submit("cancel")));
    }

    [Fact]
    public void SpacerRow_IsBlankNonFocusableAndInert()
    {
        var row = new SpacerRow(height: 2);
        var driver = new FakeConsoleDriver(6, 2);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas => row.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 6, 2), focused: false)));

        Assert.Equal(2, row.Height);
        Assert.False(row.IsFocusable);
        Assert.Equal("      ", driver.GetRow(0));
        Assert.Equal("      ", driver.GetRow(1));
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleKey(Key(ConsoleKey.Spacebar), new FormRowInputContext(0, false)).Kind);
        Assert.Equal(FormInputResultKind.NotHandled, row.HandleMouse(Mouse(), new FormRowMouseContext(new Rect(0, 0, 6, 2), 0, false, 2)).Kind);
    }

    [Fact]
    public void DialogButtonFactories_SetTheirDocumentedModels()
    {
        Assert.Equal(new DialogButton("default", "Default", 'D', IsDefault: true), DialogButton.Default("default", "Default", 'D'));
        Assert.Equal(new DialogButton("action", "Action", 'A'), DialogButton.Action("action", "Action", 'A'));
        Assert.Equal(new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel), DialogButton.Cancel());
    }

    [Fact]
    public void FormFooter_ErrorAndButtonsReadsErrorAtRenderTime()
    {
        string? error = "First";
        IReadOnlyList<IFormRow> footer = FormFooter.ErrorAndButtons(() => error, new ButtonRow([DialogButton.Default("ok", "OK", 'O')]));
        var driver = new FakeConsoleDriver(10, 1);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas => footer[0].Render(new FormRowRenderContext(canvas, new Rect(0, 0, 10, 1), focused: false)));
        error = "Second";
        UiTestRender.Render(screen, canvas => footer[0].Render(new FormRowRenderContext(canvas, new Rect(0, 0, 10, 1), focused: false)));

        Assert.StartsWith("Second", driver.GetRow(0));
        Assert.IsType<ButtonRow>(footer[1]);
    }

    private static UiRoutedInput<ScrollableFormFrame> Routed(ConsoleKey key) =>
        new(new KeyConsoleInputEvent(Key(key)), new ScrollableFormFrame(default, default, null, 1, 0, 0, [], null), null, UiInputRouteKind.Layer);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static MouseConsoleInputEvent Mouse() =>
        new(0, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);
}
