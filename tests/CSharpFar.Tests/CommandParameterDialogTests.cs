using CSharpFar.App.Dialogs;
using CSharpFar.App.UserMenu;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CommandParameterDialogTests
{
    [Fact]
    public void Show_ReturnsEnteredDefaultsAndLabelsInSingleForm()
    {
        var driver = Driver(Key(ConsoleKey.F10));
        CommandParameterDialogResult? result = Show(driver,
        [
            new CommandTemplateParameter("Source file", "video.mov"),
            new CommandTemplateParameter("Quality", "20"),
            new CommandTemplateParameter("Output file", "video.mp4"),
        ]);

        Assert.NotNull(result);
        Assert.Equal("video.mov", result.Values["Source file"]);
        Assert.Equal("20", result.Values["Quality"]);
        Assert.Equal("video.mp4", result.Values["Output file"]);
        string screen = string.Join('\n', Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow));
        Assert.Contains("Source file", screen);
        Assert.Contains("Quality", screen);
        Assert.Contains("Output file", screen);
        Assert.Contains("Run", screen);
    }

    [Fact]
    public void Show_EscapeExplicitlyCancels()
    {
        CommandParameterDialogResult? result = Show(Driver(Key(ConsoleKey.Escape)),
            [new CommandTemplateParameter("URL", "https://example.com")]);
        Assert.Null(result);
    }

    [Fact]
    public void Show_RefusesBlankRequiredInput()
    {
        var driver = Driver(Key(ConsoleKey.F10), Key(ConsoleKey.Escape));
        CommandParameterDialogResult? result = Show(driver,
            [new CommandTemplateParameter("URL", "")]);
        Assert.Null(result);
    }

    private static CommandParameterDialogResult? Show(
        FakeConsoleDriver driver, IReadOnlyList<CommandTemplateParameter> parameters)
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        var dialogs = new DialogService(ModalTestHost.Create(driver), fields);
        return new CommandParameterDialog(dialogs, fields).Show("Download video", parameters);
    }

    private static FakeConsoleDriver Driver(params ConsoleKeyInfo[] keys)
    {
        var driver = new FakeConsoleDriver();
        foreach (ConsoleKeyInfo key in keys) driver.EnqueueKey(key);
        return driver;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
