using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class DirectoryShortcutApplicationIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _target;

    public DirectoryShortcutApplicationIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarShortcutApp_{Guid.NewGuid():N}");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void HandleKey_ControlDigit_NavigatesToShortcut()
    {
        var app = CreateApp(out _, out _);

        bool handled = InvokeHandleKey(
            app,
            new ConsoleKeyInfo('\0', ConsoleKey.D1, shift: false, alt: false, control: true));

        Assert.True(handled);
        Assert.Equal(_target, app.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void Render_DrawsShortcutBarOverLowerPanelBorder()
    {
        var app = CreateApp(out var driver, out _);

        typeof(Application)
            .GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, null);

        Assert.Contains("1Target", driver.GetRow(driver.GetSize().Height - 3), StringComparison.Ordinal);
    }

    [Fact]
    public void HandleMouse_ClickOnShortcutBar_NavigatesToShortcut()
    {
        var app = CreateApp(out var driver, out _);

        bool handled = InvokeHandleMouse(
            app,
            new MouseConsoleInputEvent(
                2,
                driver.GetSize().Height - 3,
                MouseButton.Left,
                MouseEventKind.Down,
                MouseKeyModifiers.None));

        Assert.True(handled);
        Assert.Equal(_target, app.ActiveState.CurrentDirectory);
    }

    [Fact]
    public void EditDialog_EmptyPathClearsPrefilledSlot()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, false, false, true));
        driver.EnqueueKey(Key(ConsoleKey.Backspace));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var screen = new ScreenRenderer(driver);
        var result = new DirectoryShortcutEditDialog(ModalTestHost.Create(screen))
            .Show(1, currentItem: null, _target);

        Assert.True(result.Accepted);
        Assert.Null(result.Item);
    }

    [Fact]
    public void EditDialog_MouseCancel_WorksWhileNameFieldHasFocus()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueInput(new MouseConsoleInputEvent(
            39,
            14,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));
        var currentItem = new AppSettings.DirectoryShortcutItem
        {
            Number = 1,
            Name = "Target",
            Path = _target,
        };

        var screen = new ScreenRenderer(driver);
        var result = new DirectoryShortcutEditDialog(ModalTestHost.Create(screen))
            .Show(1, currentItem, _root);

        Assert.False(result.Accepted);
        Assert.Same(currentItem, result.Item);
    }

    [Fact]
    public void EditDialog_MouseOk_WorksWhileNameFieldHasFocus()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueInput(new MouseConsoleInputEvent(
            32,
            14,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));

        var screen = new ScreenRenderer(driver);
        var result = new DirectoryShortcutEditDialog(ModalTestHost.Create(screen))
            .Show(1, currentItem: null, _target);

        Assert.True(result.Accepted);
        Assert.Equal(_target, result.Item?.Path);
    }

    private Application CreateApp(out FakeConsoleDriver driver, out AppSettings settings)
    {
        driver = new FakeConsoleDriver();
        settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;
        settings.DirectoryShortcuts.Items.Add(new AppSettings.DirectoryShortcutItem
        {
            Number = 1,
            Name = "Target",
            Path = _target,
        });

        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        fs.AddDirectory(_target);
        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
    }

    private static bool InvokeHandleKey(Application app, ConsoleKeyInfo key)
    {
        var router = typeof(Application)
            .GetField("_keyboardInputRouter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app)!;

        return (bool)router
            .GetType()
            .GetMethod("Handle")!
            .Invoke(router, [key])!;
    }

    private static bool InvokeHandleMouse(Application app, MouseConsoleInputEvent mouse)
    {
        var router = typeof(Application)
            .GetField("_mouseInputRouter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app)!;

        return (bool)router
            .GetType()
            .GetMethod("Handle")!
            .Invoke(router, [mouse])!;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
