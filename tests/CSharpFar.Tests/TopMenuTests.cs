using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TopMenuTests
{
    [Fact]
    public void AvailabilityAndOpenState_ControlInputPolicyAndOpening()
    {
        var fixture = Fixture.Create();

        Assert.Equal(UiLayerInputPolicy.Bubble, fixture.Menu.InputPolicy);
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        Assert.Equal(UiLayerInputPolicy.Modal, fixture.Menu.InputPolicy);
        Assert.Equal(1, fixture.OpeningCount);

        fixture.Available = false;
        Assert.Equal(UiLayerInputPolicy.None, fixture.Menu.InputPolicy);
    }

    [Fact]
    public void F9UsesInitialItemAndEscapeCloses()
    {
        var fixture = Fixture.Create(initial: "Beta");
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));

        Assert.True(fixture.Menu.IsOpen);
        Assert.Equal("Beta", fixture.ActiveTopId);

        fixture.Dispatch(UiTestInput.Key(ConsoleKey.Escape));
        Assert.False(fixture.Menu.IsOpen);
    }

    [Fact]
    public void KeyboardNavigationSkipsUnavailableItemsAndExecutesAfterClosing()
    {
        var fixture = Fixture.Create();
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.DownArrow));
        Assert.Equal("alpha.run", fixture.ActiveDropdownCommand);
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.Home));
        Assert.Equal("alpha.open", fixture.ActiveDropdownCommand);
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.End));
        Assert.Equal("alpha.run", fixture.ActiveDropdownCommand);

        fixture.Dispatch(UiTestInput.Key(ConsoleKey.Enter));

        Assert.Equal(["closed:alpha.run"], fixture.Events);
        Assert.False(fixture.Menu.IsOpen);
    }

    [Fact]
    public void LeftRightAndTabUseGenericTopItemIds()
    {
        var fixture = Fixture.Create(alternate: id => id == "Alpha" ? "Beta" : "Alpha");
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.RightArrow));
        Assert.Equal("Beta", fixture.ActiveTopId);
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.LeftArrow));
        Assert.Equal("Alpha", fixture.ActiveTopId);
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.Tab));
        Assert.Equal("Beta", fixture.ActiveTopId);
    }

    [Fact]
    public void HotCharactersOpenTopLevelAndExecuteDropdownCommand()
    {
        var fixture = Fixture.Create();
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.B, 'b'));
        Assert.Equal("Beta", fixture.ActiveTopId);

        fixture.Dispatch(UiTestInput.Key(ConsoleKey.T, 't'));
        Assert.Equal(["closed:beta.toggle"], fixture.Events);
    }

    [Fact]
    public void DisabledItemDoesNotExecute()
    {
        var fixture = Fixture.Create();
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        TopMenuPointerTarget disabled = fixture.Frame.PointerTargets.Single(target =>
            target.Kind == TopMenuPointerTargetKind.DropdownItem && target.ItemIndex == 2);
        fixture.Dispatch(UiTestInput.Mouse(disabled.Bounds.X, disabled.Bounds.Y));

        Assert.Empty(fixture.Events);
        Assert.True(fixture.Menu.IsOpen);
    }

    [Fact]
    public void MouseRoutesTopAndDropdownClicksAndKeepsSurfaceOpen()
    {
        var fixture = Fixture.Create();
        fixture.Render();
        TopMenuPointerTarget beta = fixture.Frame.PointerTargets.Single(target => target.Kind == TopMenuPointerTargetKind.Top && target.ItemIndex == 1);
        fixture.Dispatch(UiTestInput.Mouse(beta.Bounds.X, beta.Bounds.Y));
        fixture.Render();
        Assert.Equal("Beta", fixture.ActiveTopId);

        TopMenuPointerTarget surface = fixture.Frame.PointerTargets.Single(target => target.Kind == TopMenuPointerTargetKind.Surface);
        fixture.Dispatch(UiTestInput.Mouse(surface.Bounds.X, surface.Bounds.Y));
        Assert.True(fixture.Menu.IsOpen);

        TopMenuPointerTarget item = fixture.Frame.PointerTargets.Single(target => target.Kind == TopMenuPointerTargetKind.DropdownItem);
        fixture.Dispatch(UiTestInput.Mouse(item.Bounds.X, item.Bounds.Y));
        Assert.Equal(["closed:beta.toggle"], fixture.Events);
    }

    [Fact]
    public void OutsideClickClosesAndOpenFramePublishesFocusTarget()
    {
        var fixture = Fixture.Create();
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        fixture.Render();
        Assert.NotNull(fixture.Menu.CommittedInteractionFrame.Focus.DefaultTarget);

        fixture.Dispatch(UiTestInput.Mouse(79, 24));
        Assert.False(fixture.Menu.IsOpen);
    }

    [Fact]
    public void LongMenuScrollsCapturesScrollbarAndRecalculatesAfterResize()
    {
        var fixture = Fixture.Create(height: 6, longMenu: true);
        fixture.Dispatch(UiTestInput.Key(ConsoleKey.F9));
        fixture.Render();
        VerticalScrollbarFrame scrollbar = Assert.IsType<VerticalScrollbarFrame>(fixture.Frame.DropdownScrollbar);
        fixture.Dispatch(UiTestInput.Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 1));
        fixture.Render();
        Assert.True(fixture.Frame.Layout.DropdownFirstVisibleItemIndex > 0);
        fixture.Dispatch(UiTestInput.Mouse(scrollbar.Bounds.X, scrollbar.Bounds.Bottom - 1, MouseEventKind.Up));

        fixture.Resize(80, 25);
        fixture.Render();
        Assert.Null(fixture.Frame.DropdownScrollbar);
    }

    private sealed class Fixture
    {
        private readonly UiLayerTestHost _host;
        private readonly List<string> _events;

        private Fixture(UiLayerTestHost host, TopMenu menu, List<string> events)
        {
            _host = host;
            Menu = menu;
            _events = events;
        }

        public TopMenu Menu { get; }
        public bool Available { get; set; } = true;
        public int OpeningCount { get; private set; }
        public IReadOnlyList<string> Events => _events;
        public TopMenuFrame Frame => Menu.CommittedFrame;
        public string ActiveTopId => Menu.CommittedInteractionFrame.Focus.DefaultTarget!.Value.Split(':')[1];
        public string ActiveDropdownCommand
        {
            get
            {
                int index = int.Parse(Menu.CommittedInteractionFrame.Focus.DefaultTarget!.Value.Split(':')[2]);
                return Frame.Definition.Items.First(item => item.Id == ActiveTopId).Children[index].CommandId!;
            }
        }
        public void Render() => _host.Render();
        public UiInputResult Dispatch(ConsoleInputEvent input)
        {
            _host.Render();
            UiInputResult result = _host.Dispatch(input);
            if (result.Invalidate)
                _host.Render();
            return result;
        }
        public void Resize(int width, int height) => _host.Resize(width, height);

        public static Fixture Create(string initial = "Alpha", Func<string?, string?>? alternate = null, int height = 25, bool longMenu = false)
        {
            Fixture? fixture = null;
            var events = new List<string>();
            MenuBarDefinition definition = Definition(longMenu);
            var menu = new TopMenu(
                () => fixture!.Available,
                () => definition,
                RenderOptions,
                () => initial,
                alternate ?? (id => id == "Alpha" ? "Beta" : "Alpha"),
                () => fixture!.OpeningCount++,
                request => { events.Add($"{(fixture!.Menu.IsOpen ? "open" : "closed")}:{request.CommandId}"); return new MenuCommandResult { Success = true }; },
                new MenuLayoutService());
            var host = new UiLayerTestHost(menu, height: height);
            fixture = new Fixture(host, menu, events);
            return fixture;
        }

        private static MenuBarDefinition Definition(bool longMenu) => new()
        {
            Items =
            [
                new TopMenuItemDefinition { Id = "Alpha", Text = "Alpha", HotChar = 'A', Children = longMenu ? LongItems() : [Item("alpha.open", 'O'), Separator(), Disabled(), Item("alpha.run", 'R')] },
                new TopMenuItemDefinition { Id = "Beta", Text = "Beta", HotChar = 'B', Children = [Item("beta.toggle", 'T')] },
            ],
        };

        private static IReadOnlyList<MenuItemDefinition> LongItems() => Enumerable.Range(0, 12).Select(index => Item($"alpha.{index}", null)).ToArray();
        private static MenuRenderOptions RenderOptions() => new()
        {
            MenuBarNormalStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkCyan),
            MenuBarActiveStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Black),
            NormalStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue),
            ActiveStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Black),
            HighlightStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkBlue),
            ActiveHighlightStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black),
            DisabledStyle = new CellStyle(ConsoleColor.DarkGray, ConsoleColor.DarkBlue),
            BorderStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue),
            ShadowStyle = new CellStyle(ConsoleColor.Black, ConsoleColor.Black),
        };
        private static MenuItemDefinition Item(string command, char? hotChar) => new() { Id = command, Text = command, HotChar = hotChar, CommandId = command };
        private static MenuItemDefinition Separator() => new() { Id = "separator", Text = string.Empty, Kind = MenuItemKind.Separator, IsEnabled = false };
        private static MenuItemDefinition Disabled() => new() { Id = "disabled", Text = "disabled", IsEnabled = false, CommandId = "disabled" };
    }
}

public sealed class ApplicationTopMenuPolicyTests
{
    [Theory]
    [InlineData(PanelSide.Left, "Left")]
    [InlineData(PanelSide.Right, "Right")]
    public void OpenTopMenuForPanel_SelectsThatPanelsSemanticMenu(PanelSide side, string expectedId)
    {
        var driver = new CSharpFar.Tests.Fakes.FakeConsoleDriver();
        var fileSystem = new CSharpFar.Tests.Fakes.FakeFileSystemService();
        const string root = @"C:\Root";
        fileSystem.AddDirectory(root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = root;
        settings.Panels.RightStartDirectory = root;
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver), fileSystem, new NoOpShellService(),
            new NoOpFileOperationService(), new InMemoryHistoryStore(), settings,
            enableBuiltInNetworkModules: false);
        var app = new Application(services);

        Assert.True(app.OpenTopMenu(side));
        services.Composition.Render();

        Assert.StartsWith($"top-menu.dropdown:{expectedId}:",
            services.TopMenu.CommittedInteractionFrame.Focus.DefaultTarget!.Value,
            StringComparison.Ordinal);
    }
}
