using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Menu;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationUiLayerScopeTests
{
    [Fact]
    public void Scope_RegistersApplicationLayersAboveRootAndRoutesTopmostFirst()
    {
        using var fixture = Fixture.Create();
        fixture.Services.Session.CommandLine.Completion.Visible = true;
        fixture.Services.Session.CommandLine.Completion.List.ResetItems(["", "alpha"], 1);
        fixture.Host.Render();

        var passthrough = Key(ConsoleKey.A, 'a');
        UiInputResult passResult = fixture.Host.DispatchInput(passthrough);

        Assert.True(passResult.Handled);
        Assert.True(fixture.Root.TryTakeInput(out var packet));
        Assert.Same(passthrough, packet.Input);

        fixture.Root.Clear();
        var accepted = Key(ConsoleKey.Enter);
        UiInputResult acceptedResult = fixture.Host.DispatchInput(accepted);

        Assert.True(acceptedResult.Handled);
        Assert.False(fixture.Root.TryTakeInput(out _));
        Assert.Equal("alpha", fixture.Services.Session.CommandLine.State.Text);
    }

    [Fact]
    public void Scope_RendersRootBeforeApplicationLayersAndModalAboveApplicationLayers()
    {
        using var fixture = Fixture.Create();
        fixture.Services.Session.CommandLine.Completion.Visible = true;
        fixture.Services.Session.CommandLine.Completion.List.ResetItems(["", "alpha"]);
        fixture.Host.Render();

        Assert.Equal('R', fixture.Driver.GetCell(0, 0).Character);
        Assert.NotEqual('R', fixture.Driver.GetCell(1, 21).Character);

        using var modal = fixture.Modals.Open(context =>
            context.Canvas.Write(1, 21, "M", new CSharpFar.Console.Models.CellStyle(ConsoleColor.White, ConsoleColor.Black)));
        fixture.Host.Render();
        var input = Key(ConsoleKey.A, 'a');

        UiInputResult result = fixture.Host.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.Equal('M', fixture.Driver.GetCell(1, 21).Character);
        Assert.False(fixture.Root.TryTakeInput(out _));
        Assert.True(modal.TryReadInput(out var modalInput));
        Assert.Same(input, modalInput);
    }

    [Fact]
    public void Dispose_RemovesApplicationOverlaysInLifoOrderAndLeavesRootSurfaceRoutable()
    {
        using var fixture = Fixture.Create();
        fixture.Services.Session.CommandLine.Completion.Visible = true;
        fixture.Services.Session.CommandLine.Completion.List.ResetItems(["", "alpha"]);
        fixture.Host.Render();

        fixture.Scope.Dispose();
        fixture.Scope.Dispose();
        fixture.Host.Render();

        var input = Key(ConsoleKey.Enter);
        UiInputResult result = fixture.Host.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(fixture.Root.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
        Assert.Equal('R', fixture.Driver.GetCell(1, 21).Character);
    }

    [Fact]
    public void ConstructorRollback_SecondRegistrationConflictRemovesOnlyNewRegistrations()
    {
        var fixture = Fixture.CreateWithoutScope();
        using var conflicting = fixture.Host.RegisterOverlay(fixture.PanelQuickSearchLayer);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationUiLayerScope(
                fixture.Host,
                fixture.CommandCompletionLayer,
                fixture.PanelQuickSearchLayer,
                fixture.TopMenuLayer));

        Assert.Contains("same interactive UI layer", exception.Message, StringComparison.Ordinal);

        fixture.Host.Render();
        var input = Key(ConsoleKey.A, 'a');
        fixture.Host.DispatchInput(input);

        Assert.True(fixture.Root.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);

        conflicting.Dispose();
        using var replacement = new ApplicationUiLayerScope(
            fixture.Host,
            fixture.CommandCompletionLayer,
            fixture.PanelQuickSearchLayer,
            fixture.TopMenuLayer);
    }

    [Fact]
    public void ConstructorRollback_ThirdRegistrationConflictPreservesCompositionAndLifo()
    {
        var fixture = Fixture.CreateWithoutScope();
        using var conflicting = fixture.Host.RegisterOverlay(fixture.TopMenuLayer);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationUiLayerScope(
                fixture.Host,
                fixture.CommandCompletionLayer,
                fixture.PanelQuickSearchLayer,
                fixture.TopMenuLayer));

        Assert.Contains("same interactive UI layer", exception.Message, StringComparison.Ordinal);

        fixture.Host.Render();
        var input = Key(ConsoleKey.F9);
        UiInputResult result = fixture.Host.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.False(fixture.Root.TryTakeInput(out _));

        conflicting.Dispose();
        using var replacement = new ApplicationUiLayerScope(
            fixture.Host,
            fixture.CommandCompletionLayer,
            fixture.PanelQuickSearchLayer,
            fixture.TopMenuLayer);
    }

    [Fact]
    public void Rollback_DisposesAllRegistrationsInReverseAndPreservesEveryFailure()
    {
        var calls = new List<string>();
        var firstRollbackError = new InvalidOperationException("first rollback failure");
        var secondRollbackError = new InvalidOperationException("second rollback failure");
        var registrationError = new InvalidOperationException("registration failure");
        IReadOnlyList<IDisposable> registrations =
        [
            new RecordingDisposable("completion", calls, firstRollbackError),
            new RecordingDisposable("quick search", calls),
            new RecordingDisposable("top menu", calls, secondRollbackError),
        ];

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ApplicationUiLayerScope.RethrowRegistrationErrorAfterRollback(
                registrationError,
                registrations));

        Assert.Same(registrationError, thrown);
        Assert.Equal(["top menu", "quick search", "completion"], calls);
        Exception[] rollbackErrors = Assert.IsType<Exception[]>(
            thrown.Data["ApplicationUiLayerScope.RollbackErrors"]);
        Assert.Equal([secondRollbackError, firstRollbackError], rollbackErrors);
    }

    [Fact]
    public void Scope_RoutesMenuThenQuickSearchThenCompletionThenRoot()
    {
        using var fixture = Fixture.Create();
        fixture.Services.Session.Panels.Left.Items.Add(Item("gamma.txt"));
        fixture.Host.Render();

        fixture.Host.DispatchInput(Key(ConsoleKey.G, 'g', alt: true));
        fixture.Services.Session.CommandLine.Completion.Visible = true;
        fixture.Services.Session.CommandLine.Completion.List.ResetItems(["", "completion"], 1);
        fixture.Host.Render();

        var quickSearchInput = Key(ConsoleKey.A, 'a');
        Assert.True(fixture.Host.DispatchInput(quickSearchInput).Handled);
        Assert.Equal("ga", fixture.Services.PanelQuickSearch.State?.SearchText);
        Assert.False(fixture.Root.TryTakeInput(out _));

        var continueInput = Key(ConsoleKey.Enter);
        Assert.True(fixture.Host.DispatchInput(continueInput).Handled);
        Assert.Null(fixture.Services.PanelQuickSearch.State);
        Assert.Equal("completion", fixture.Services.Session.CommandLine.State.Text);
        Assert.False(fixture.Root.TryTakeInput(out _));

        fixture.Root.Clear();
        fixture.Host.DispatchInput(Key(ConsoleKey.F9));
        Assert.Equal(UiLayerInputPolicy.Modal, fixture.TopMenuLayer.InputPolicy);
        fixture.Host.Render();
        Assert.True(fixture.Host.DispatchInput(Key(ConsoleKey.B, 'b')).Handled);
        Assert.False(fixture.Root.TryTakeInput(out _));
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key, char keyChar = '\0', bool alt = false) =>
        new(new ConsoleKeyInfo(keyChar, key, shift: false, alt, control: false));

    private static FilePanelItem Item(string name) => new()
    {
        Name = name,
        FullPath = Path.Combine(@"C:\Root", name),
        IsDirectory = false,
        Size = 1,
        LastWriteTime = new DateTime(2026, 1, 1),
        Attributes = FileAttributes.Archive,
    };

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            FakeConsoleDriver driver,
            ApplicationServices services,
            UiCompositionHost host,
            RecordingRootSurface root,
            ModalDialogHost modals,
            CommandCompletionLayer commandCompletionLayer,
            PanelQuickSearchLayer panelQuickSearchLayer,
            TopMenuLayer topMenuLayer,
            ApplicationUiLayerScope? scope)
        {
            Driver = driver;
            Services = services;
            Host = host;
            Root = root;
            Modals = modals;
            CommandCompletionLayer = commandCompletionLayer;
            PanelQuickSearchLayer = panelQuickSearchLayer;
            TopMenuLayer = topMenuLayer;
            Scope = scope!;
        }

        public FakeConsoleDriver Driver { get; }
        public ApplicationServices Services { get; }
        public UiCompositionHost Host { get; }
        public RecordingRootSurface Root { get; }
        public ModalDialogHost Modals { get; }
        public CommandCompletionLayer CommandCompletionLayer { get; }
        public PanelQuickSearchLayer PanelQuickSearchLayer { get; }
        public TopMenuLayer TopMenuLayer { get; }
        public ApplicationUiLayerScope Scope { get; }

        public static Fixture Create()
        {
            var fixture = CreateWithoutScope();
            var scope = new ApplicationUiLayerScope(
                fixture.Host,
                fixture.CommandCompletionLayer,
                fixture.PanelQuickSearchLayer,
                fixture.TopMenuLayer);
            return fixture.WithScope(scope);
        }

        public static Fixture CreateWithoutScope()
        {
            var driver = new FakeConsoleDriver(80, 25);
            var services = CreateServices(driver);
            var host = new UiCompositionHost(new ScreenRenderer(driver));
            var root = new RecordingRootSurface(host.Screen);
            host.SetRootSurface(root);
            var modals = new ModalDialogHost(host);
            var commandCompletionLayer = new CommandCompletionLayer(
                services.RenderContext,
                services.CommandCompletionController,
                temporarily => services.CommandCompletionController.Hide(temporarily),
                services.CommandHistoryNavigator.Reset);
            var panelQuickSearchLayer = new PanelQuickSearchLayer(
                services.RenderContext,
                temporarily => services.CommandCompletionController.Hide(temporarily),
                services.CommandHistoryNavigator.Reset);
            var topMenuLayer = new TopMenuLayer(
                services.RenderContext,
                services.MenuController,
                new MenuLayoutService());

            return new Fixture(
                driver,
                services,
                host,
                root,
                modals,
                commandCompletionLayer,
                panelQuickSearchLayer,
                topMenuLayer,
                null);
        }

        private Fixture WithScope(ApplicationUiLayerScope scope) =>
            new(
                Driver,
                Services,
                Host,
                Root,
                Modals,
                CommandCompletionLayer,
                PanelQuickSearchLayer,
                TopMenuLayer,
                scope);

        public void Dispose() => Scope?.Dispose();

        private static ApplicationServices CreateServices(FakeConsoleDriver driver)
        {
            var fileSystem = new FakeFileSystemService();
            const string root = @"C:\Root";
            fileSystem.AddDirectory(root);
            var settings = new AppSettings();
            settings.Panels.LeftStartDirectory = root;
            settings.Panels.RightStartDirectory = root;
            var services = ApplicationServicesBuilder.Create(
                new ScreenRenderer(driver),
                fileSystem,
                new NoOpShellService(),
                new NoOpFileOperationService(),
                new InMemoryHistoryStore(),
                settings,
                enableBuiltInNetworkModules: false);
            _ = new Application(services);
            return services;
        }
    }

    private sealed class RecordingRootSurface(ScreenRenderer screen) : IUiSurface, IUiLayer
    {
        private readonly Queue<UiRoutedInput<Unit>> _inputs = [];

        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public IUiFocusState FocusState { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        public void Render(UiRenderContext context)
        {
            var style = new CSharpFar.Console.Models.CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
            string row = new('R', context.Size.Width);
            for (int y = 0; y < context.Size.Height; y++)
                context.Canvas.Write(0, y, row, style);
        }

        public void CompleteFrame(UiFrameCompletion completion)
        {
        }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            _inputs.Enqueue(new UiRoutedInput<Unit>(input, default, context.Target, context.RouteKind));
            return UiInputResult.HandledResult;
        }

        public bool TryTakeInput(out UiRoutedInput<Unit> packet) =>
            _inputs.TryDequeue(out packet!);

        public void Clear() => _inputs.Clear();
    }

    private sealed class RecordingDisposable(string name, List<string> calls, Exception? exception = null) : IDisposable
    {
        public void Dispose()
        {
            calls.Add(name);
            if (exception is not null)
                throw exception;
        }
    }
}
