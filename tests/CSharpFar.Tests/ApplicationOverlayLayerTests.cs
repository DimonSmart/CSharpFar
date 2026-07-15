using CSharpFar.App.Bootstrap;
using CSharpFar.App;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationOverlayLayerTests
{
    [Fact]
    public void CommandCompletion_RejectedRenderAttemptDoesNotMutateSelectionBeforeStableCommit()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "alpha", "beta", "gamma"]);
        completion.SelectedIndex = 99;
        completion.FirstVisibleIndex = 99;

        services.Driver.ResizeAfterWriteCount = 1;
        services.Driver.ResizeAfterWrite = driver => driver.SetSize(100, 35);
        services.Driver.BeforeViewportWrite = _ =>
        {
            Assert.Equal(99, completion.SelectedIndex);
            Assert.Equal(99, completion.FirstVisibleIndex);
        };

        services.Composition.Render();

        Assert.Equal(3, completion.SelectedIndex);
        Assert.Equal(0, completion.FirstVisibleIndex);
    }

    [Fact]
    public void CommandCompletion_CtrlEnterBubblesToApplicationCommandLineShortcut()
    {
        var services = Services();
        var item = Item("current.txt");
        services.Session.Panels.Left.Items.Add(item);
        services.Session.Panels.Left.CursorIndex = services.Session.Panels.Left.Items.IndexOf(item);
        services.Session.CommandLine.Completion.Visible = true;
        services.Session.CommandLine.Completion.Matches.AddRange(["", "history"]);
        services.Session.CommandLine.Completion.SelectedIndex = 1;

        services.Composition.Render();
        var input = Key(ConsoleKey.Enter, control: true);
        UiInputResult routed = services.Composition.DispatchInput(input);

        Assert.True(routed.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);

        bool shouldRender = services.KeyboardInputRouter.Handle(input.Key);

        Assert.True(shouldRender);
        Assert.Equal("current.txt", services.Session.CommandLine.State.Text);
    }

    [Fact]
    public void PanelQuickSearch_ActiveTinyViewportKeepsHandlingKeyboardWithoutPopup()
    {
        var services = Services(new FakeConsoleDriver(80, 3));
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Session.Panels.Left.Items.Add(Item("alpha.txt"));
        services.Composition.Render();

        UiInputResult activate = services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        Assert.True(activate.Handled);
        services.Composition.Render();

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
        UiInputResult refine = services.Composition.DispatchInput(Key(ConsoleKey.E, keyChar: 'e'));
        Assert.True(refine.Handled);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
        Assert.Equal("ge", services.PanelQuickSearch.State?.SearchText);
    }

    [Fact]
    public void TopMenu_HiddenPanelsDoesNotInterceptF9OrTopRowMouse()
    {
        var services = Services();
        services.Session.App.HiddenPanels = HiddenPanels.Both;
        services.Composition.Render();

        services.Composition.DispatchInput(Key(ConsoleKey.F9));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var keyPacket));
        Assert.Equal(ConsoleKey.F9, Assert.IsType<KeyConsoleInputEvent>(keyPacket.Input).Key.Key);

        services.Composition.DispatchInput(Mouse(0, 0));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var mousePacket));
        Assert.IsType<MouseConsoleInputEvent>(mousePacket.Input);
    }

    private static TestServices Services(FakeConsoleDriver? driver = null)
    {
        driver ??= new FakeConsoleDriver(80, 25);
        var fs = new FakeFileSystemService();
        const string root = @"C:\Root";
        fs.AddDirectory(root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = root;
        settings.Panels.RightStartDirectory = root;
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            enableBuiltInNetworkModules: false);
        _ = new Application(services);
        return new TestServices(driver, services);
    }

    private static KeyConsoleInputEvent Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool alt = false,
        bool control = false) =>
        new(new ConsoleKeyInfo(keyChar, key, shift: false, alt, control));

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static FilePanelItem Item(string name) => new()
    {
        Name = name,
        FullPath = Path.Combine(@"C:\Root", name),
        IsDirectory = false,
        Size = 1,
        LastWriteTime = new DateTime(2026, 1, 1),
        Attributes = FileAttributes.Archive,
    };

    private sealed record TestServices(FakeConsoleDriver Driver, ApplicationServices Inner)
    {
        public ApplicationSession Session => Inner.Session;
        public UiCompositionHost Composition => Inner.Composition;
        public ApplicationUiSurface ApplicationSurface => Inner.ApplicationSurface;
        public CSharpFar.App.Input.KeyboardInputRouter KeyboardInputRouter => Inner.KeyboardInputRouter;
        public CSharpFar.App.Panels.PanelQuickSearchController PanelQuickSearch => Inner.PanelQuickSearch;
    }
}
