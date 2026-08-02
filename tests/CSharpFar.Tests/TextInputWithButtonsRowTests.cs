using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TextInputWithButtonsRowTests
{
    [Fact]
    public void TextFieldConstructor_PreservesMaskingWidthAndSharedInput()
    {
        var provider = new CountingHistoryProvider();
        TextField field = new FormFieldFactory(provider).Text(
            "write",
            "secret",
            new TextHistoryId("TextInputWithButtonsRowTests.Secret"),
            maskInput: true,
            width: 12,
            submitOnEnter: true);
        var row = new TextInputWithButtonsRow(
            "",
            field,
            [new DialogButton("keep", "Keep", 'K')]);
        var driver = new FakeConsoleDriver(width: 20, height: 2);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas =>
            row.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 20, 1), focused: true)));

        Assert.Equal("write", row.Id);
        Assert.True(row.SubmitOnEnter);
        Assert.Same(field.Input, row.Input);
        Assert.Equal("secret", row.Buffer.Text);
        Assert.DoesNotContain("secret", driver.GetRow(0));
        Assert.Contains("******", driver.GetRow(0));
        Assert.Equal(0, provider.GetCallCount);
    }

    [Fact]
    public void TextFieldConstructor_UsesFieldHistoryComposite()
    {
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var id = new TextHistoryId("TextInputWithButtonsRowTests.History");
        provider.Get(id).Add("saved");
        TextField field = new FormFieldFactory(provider).Text("value", historyId: id, width: 12);
        var row = new TextInputWithButtonsRow(
            "Value:",
            field,
            [new DialogButton("keep", "Keep", 'K')]);
        SingleLineTextHistoryState popup = Assert.IsType<SingleLineTextHistoryState>(field.Input.History);

        Assert.True(popup.OpenAll(availableContentRows: 5));
        FormCompositeFrame frame = row.BuildCompositeFrame(
            new FormCompositeFrameContext(
                new Rect(0, 0, 30, 1),
                new ConsoleViewport(0, 0, 30, 8)));

        Assert.True(row.IsCompositeOpen);
        Assert.True(frame.IsOpen);
        Assert.Contains(frame.ChildTargets, target => target.Kind == FormTargetKind.HistoryDropdown);
    }

    [Fact]
    public void MouseClickButton_ReturnsButtonId()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 5);
        var screen = new ScreenRenderer(driver);
        TextField field = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text(
            "write",
            "14.06.2026 15:03:39",
            width: 19);
        var row = new TextInputWithButtonsRow(
            "write:    ",
            field,
            [
                new DialogButton("original", "Original", 'O'),
                new DialogButton("current", "Current", 'U'),
                new DialogButton("blank", "Blank", 'B'),
            ]);

        UiTestRender.Render(screen, canvas =>
            row.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 80, 1), focused: true)));
        FakeConsoleDriver.WriteRecord button = driver.WriteRecords.Last(record => record.Text.Contains("Current", StringComparison.Ordinal));
        FormInputResult pressed = row.HandleMouse(
            new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            new FormRowMouseContext(new Rect(0, 0, 80, 1), rowIndex: 0, focused: true, screenHeight: 5));
        FormInputResult result = row.HandleMouse(
            new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            new FormRowMouseContext(new Rect(0, 0, 80, 1), rowIndex: 0, focused: true, screenHeight: 5));

        Assert.Equal(FormInputResultKind.Handled, pressed.Kind);
        Assert.Equal(UiMouseCaptureRequestKind.Capture, pressed.MouseCapture);
        Assert.Equal(FormInputResultKind.Submit, result.Kind);
        Assert.Equal("current", result.Command);
        Assert.Equal(UiMouseCaptureRequestKind.Release, result.MouseCapture);
    }

    private sealed class CountingHistoryProvider : ITextFieldHistoryProvider
    {
        public int GetCallCount { get; private set; }

        public TextHistory Get(TextHistoryId id)
        {
            GetCallCount++;
            return new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore()).Get(id);
        }
    }
}
