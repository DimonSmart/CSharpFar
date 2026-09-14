using CSharpFar.Console.Input;

namespace CSharpFar.Ui.Tests;

public sealed class SettingsDialogTests
{
    [Fact]
    public void Show_F10SavesAndEscapeCancels()
    {
        Assert.Equal(SettingsDialogResult.Saved, ShowWith(Key(ConsoleKey.F10)));
        Assert.Equal(SettingsDialogResult.Cancelled, ShowWith(Key(ConsoleKey.Escape)));
    }

    [Fact]
    public void Show_NavigationEntersSelectedPageAndEditsStableControl()
    {
        var first = FormControls.CheckBox("first.value", "First");
        var second = FormControls.CheckBox("second.value", "Second");
        var driver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.F10));

        SettingsDialogResult result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            new SettingsDialogOptions("Settings"),
            [
                new SettingsPage("first", "First", [first]),
                new SettingsPage("second", "Second", [second]),
            ]);

        Assert.Equal(SettingsDialogResult.Saved, result);
        Assert.False(first.Value);
        Assert.True(second.Value);
    }

    [Fact]
    public void Show_ValidationCanSelectAndFocusNeverOpenedPage()
    {
        var required = FormControls.CheckBox("advanced.required", "Required");
        var driver = Driver(
            Key(ConsoleKey.F10),
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.F10));

        SettingsDialogResult result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            new SettingsDialogOptions("Settings"),
            [
                new SettingsPage("general", "General", [FormControls.CheckBox("general.enabled", "Enabled")]),
                new SettingsPage(
                    "advanced",
                    "Advanced",
                    [required],
                    () => required.Value
                        ? FormSubmit.Success(true)
                        : FormSubmit.Invalid<bool>("Required must be enabled.", required)),
            ]);

        Assert.Equal(SettingsDialogResult.Saved, result);
        Assert.True(required.Value);
    }

    [Fact]
    public void Show_UnknownInitialPageFallsBackToFirstPage()
    {
        var first = FormControls.CheckBox("first.value", "First");
        var second = FormControls.CheckBox("second.value", "Second");
        var driver = Driver(
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.F10));

        SettingsDialogResult result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            new SettingsDialogOptions("Settings") { InitialPageId = "missing" },
            [
                new SettingsPage("first", "First", [first]),
                new SettingsPage("second", "Second", [second]),
            ]);

        Assert.Equal(SettingsDialogResult.Saved, result);
        Assert.True(first.Value);
        Assert.False(second.Value);
    }

    [Fact]
    public void Show_CompactToSplitResizePreservesDraftState()
    {
        var value = FormControls.CheckBox("page.value", "Value");
        var driver = Driver(
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Spacebar),
            new ConsoleResizeInputEvent(),
            Key(ConsoleKey.F10));
        driver.SetSize(44, 20);
        ResizeBeforeRead(driver, readNumber: 3, width: 100, height: 30);

        SettingsDialogResult result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            new SettingsDialogOptions("Settings"),
            [new SettingsPage("page", "Page", [value])]);

        Assert.Equal(SettingsDialogResult.Saved, result);
        Assert.True(value.Value);
    }

    [Fact]
    public void Show_RejectsDuplicatePageIds()
    {
        var dialog = new SettingsDialog(ModalTestHost.Create(Driver()));

        Assert.Throws<InvalidOperationException>(() => dialog.Show(
            new SettingsDialogOptions("Settings"),
            [
                new SettingsPage("same", "One", []),
                new SettingsPage("same", "Two", []),
            ]));
    }

    private static SettingsDialogResult ShowWith(params ConsoleInputEvent[] input) =>
        new SettingsDialog(ModalTestHost.Create(Driver(input))).Show(
            new SettingsDialogOptions("Settings"),
            [new SettingsPage("general", "General", [FormControls.CheckBox("enabled", "Enabled")])]);

    private static FakeConsoleDriver Driver(params ConsoleInputEvent[] inputs)
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        foreach (ConsoleInputEvent input in inputs)
            driver.EnqueueInput(input);
        return driver;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private static void ResizeBeforeRead(FakeConsoleDriver driver, int readNumber, int width, int height)
    {
        int reads = 0;
        driver.BeforeReadInput = OnBeforeRead;

        void OnBeforeRead(FakeConsoleDriver current)
        {
            reads++;
            if (reads == readNumber)
                current.SetSize(width, height);
            else
                current.BeforeReadInput = OnBeforeRead;
        }
    }
}
