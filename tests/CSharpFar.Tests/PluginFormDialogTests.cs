using CSharpFar.App.Plugins;
using CSharpFar.Console.Input;
using CSharpFar.Module.Abstractions;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class PluginFormDialogTests
{
    [Fact]
    public void ValidationKeepsFormOpenAndFocusesField()
    {
        var driver = Driver(Key(ConsoleKey.F10));
        // Inspect complete committed frames, not the driver's incremental write chunks.
        driver.BeforeReadInput = current =>
        {
            Assert.DoesNotContain("hidden-value", ScreenText(current));
            Assert.Contains("************", ScreenText(current));
            current.BeforeReadInput = next =>
            {
                Assert.Contains("Required name", ScreenText(next));
                next.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
                next.EnqueueKey(Key(ConsoleKey.F10));
            };
        };
        var name = new PluginFormText("name", "Name:");
        var secret = new PluginFormText("secret", "Password:", "hidden-value") { Masked = true };
        int submits = 0;
        var form = new PluginForm("Plugin form", [secret, name])
        {
            PreferredWidth = 65,
            OnEvent = e =>
            {
                if (e.Kind != PluginFormEventKind.Submit) return null;
                submits++;
                return name.Value.Length == 0 ? new PluginFormFeedback("Required name", name.Id) : null;
            }
        };

        Assert.Equal("ok", Show(driver, form));
        Assert.Equal(2, submits);
        Assert.Equal("x", name.Value);
        Assert.Equal("hidden-value", secret.Value);
        Assert.DoesNotContain(driver.WriteRecords, record => record.Text.Contains("hidden-value", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckboxChangeRevealsFieldAndPreservesDraft()
    {
        var driver = Driver(Key(ConsoleKey.Spacebar), Key(ConsoleKey.Tab), new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), Key(ConsoleKey.F10));
        var check = new PluginFormCheckBox("remote", "Remote");
        var endpoint = new PluginFormText("endpoint", "Endpoint:") { Visible = false };
        var events = new List<PluginFormEvent>();
        var form = new PluginForm("Conditional form", [check, endpoint])
        {
            InitialFocusFieldId = check.Id,
            OnEvent = e =>
            {
                events.Add(e);
                endpoint.Visible = check.Value;
                return null;
            }
        };

        Assert.Equal("ok", Show(driver, form));
        Assert.True(check.Value);
        Assert.Equal("x", endpoint.Value);
        Assert.Contains(new PluginFormEvent(PluginFormEventKind.ValueChanged, check.Id), events);
        Assert.Contains(new PluginFormEvent(PluginFormEventKind.ValueChanged, endpoint.Id), events);
    }

    [Fact]
    public void DropdownPreviewDoesNotChangeDraftUntilCommitted()
    {
        var driver = Driver(Key(ConsoleKey.F4), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter), Key(ConsoleKey.F10));
        var mode = new PluginFormSelect("mode", "Mode:", ["Local", "Remote"], "Local");
        var changes = new List<string>();
        var form = new PluginForm("Mode", [mode])
        {
            OnEvent = e =>
            {
                if (e.Kind == PluginFormEventKind.ValueChanged) changes.Add(mode.Value);
                return null;
            }
        };
        Assert.Equal("ok", Show(driver, form));
        Assert.Equal("Remote", mode.Value);
        Assert.Equal(["Remote"], changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineAuxiliaryButtonWorksWithoutSubmitting(bool mouse)
    {
        var driver = new FakeConsoleDriver(80, 25);
        var text = new PluginFormText("path", "Path:")
        {
            Actions = [new("browse", "Browse", 'B', PluginFormButtonRole.Auxiliary)]
        };
        var form = new PluginForm("Browse form", [text])
        {
            OnEvent = e =>
            {
                if (e.Kind == PluginFormEventKind.Action && e.Id == "browse")
                    text.Value = "selected";
                return null;
            }
        };
        if (mouse)
        {
            driver.BeforeReadInput = current =>
            {
                int y = Assert.Single(Enumerable.Range(0, current.GetSize().Height)
                    .Where(row => current.GetRow(row).Contains("[ Browse ]", StringComparison.Ordinal)));
                int x = current.GetRow(y).IndexOf("[ Browse ]", StringComparison.Ordinal) + 4;
                current.EnqueueInput(new MouseConsoleInputEvent(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
                current.EnqueueInput(new MouseConsoleInputEvent(x, y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
                current.EnqueueKey(Key(ConsoleKey.F10));
            };
        }
        else
        {
            driver.EnqueueKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, true, false));
            driver.EnqueueKey(Key(ConsoleKey.F10));
        }
        Assert.Equal("ok", Show(driver, form));
        Assert.Equal("selected", text.Value);
    }

    [Fact]
    public void EscapeReturnsNoAcceptedAction()
    {
        var form = new PluginForm("Cancel form", [new PluginFormText("name", "Name:")])
        {
            OnEvent = e => throw new InvalidOperationException("Escape must not submit.")
        };
        Assert.Null(Show(Driver(Key(ConsoleKey.Escape)), form));
    }

    [Fact]
    public void CancelledTokenAndInvalidDescriptorsAreRejectedBeforeInput()
    {
        var form = new PluginForm("Cancelled", []);
        Assert.Throws<OperationCanceledException>(() => Show(new FakeConsoleDriver(), form, new CancellationToken(true)));
        var duplicate = new PluginForm("Invalid", [new PluginFormText("id", "First"), new PluginFormCheckBox("id", "Second")]);
        Assert.Throws<ArgumentException>(() => Show(new FakeConsoleDriver(), duplicate));
    }

    private static string? Show(FakeConsoleDriver driver, PluginForm form, CancellationToken token = default)
    {
        var host = UiTestHost.Create(driver);
        var fields = new FormFieldFactory();
        IPluginUiService ui = new ApplicationPluginUi(new ModuleUiServices
        {
            Screen = host.Screen,
            ModalDialogs = host.ModalDialogs,
            Fields = fields,
            Dialogs = new DialogService(host.ModalDialogs, fields),
            Palette = () => PaletteRegistry.Default
        });
        return ui.ShowForm(form, token);
    }

    private static string ScreenText(FakeConsoleDriver driver) =>
        string.Join('\n', Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow));

    private static FakeConsoleDriver Driver(params ConsoleKeyInfo[] keys)
    {
        var driver = new FakeConsoleDriver(80, 25);
        foreach (var key in keys) driver.EnqueueKey(key);
        return driver;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new(key == ConsoleKey.Spacebar ? ' ' : '\0', key, false, false, false);
}
