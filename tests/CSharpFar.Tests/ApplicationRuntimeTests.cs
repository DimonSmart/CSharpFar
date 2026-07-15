using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationRuntimeTests
{
    [Fact]
    public void KeyRoutedToApplicationSurface_CallsKeyHandlerOnceAfterDispatch()
    {
        var fixture = RuntimeFixture.Create();
        bool modalOpened = false;
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));
        fixture.Context.HandleKeyInput = _ =>
        {
            fixture.KeyCount++;
            Assert.False(fixture.Services.ApplicationSurface.TryTakeInput(out var ignoredPacket));
            using var modal = fixture.Services.ModalDialogs.Open(_ => { });
            modalOpened = true;
            fixture.Running = false;
            return ApplicationRuntimeRenderRequest.None;
        };

        fixture.Run();

        Assert.True(modalOpened);
        Assert.Equal(1, fixture.KeyCount);
        Assert.Equal(0, fixture.ModifierCount);
        Assert.Equal(0, fixture.MouseCount);
    }

    [Fact]
    public void ModifierAndMouseInputs_CallOnlyTheirHandlers()
    {
        var fixture = RuntimeFixture.Create();
        fixture.Driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        fixture.Driver.EnqueueInput(Mouse());
        fixture.StopAfterHandledInputs = 2;

        fixture.Run();

        Assert.Equal(0, fixture.KeyCount);
        Assert.Equal(1, fixture.ModifierCount);
        Assert.Equal(1, fixture.MouseCount);
    }

    [Fact]
    public void UnknownSemanticEvent_DoesNotCallLegacyHandlers()
    {
        var fixture = RuntimeFixture.Create();
        fixture.Driver.EnqueueInput(new UnknownConsoleInputEvent());
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;

        fixture.Run();

        Assert.Equal(0, fixture.KeyCount + fixture.ModifierCount + fixture.MouseCount);
    }

    [Fact]
    public void HandledAndModalOverlays_DoNotCallApplicationHandlers()
    {
        var handled = RuntimeFixture.Create();
        using (handled.Services.Composition.PushOverlay(new TestLayer(UiLayerInputPolicy.Bubble, UiInputResult.HandledResult)))
        {
            handled.Driver.EnqueueInput(Key(ConsoleKey.A));
            handled.IsRunningOverride = new SequenceRunning(true, true, false).Next;
            handled.Run();
        }

        var modal = RuntimeFixture.Create();
        using (modal.Services.Composition.PushOverlay(new TestLayer(UiLayerInputPolicy.Modal, UiInputResult.NotHandled)))
        {
            modal.Driver.EnqueueInput(Key(ConsoleKey.A));
            modal.IsRunningOverride = new SequenceRunning(true, true, false).Next;
            modal.Run();
        }

        Assert.Equal(0, handled.KeyCount);
        Assert.Equal(0, modal.KeyCount);
    }

    [Fact]
    public void UnhandledOverlay_AllowsApplicationHandler()
    {
        var fixture = RuntimeFixture.Create();
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(UiLayerInputPolicy.Bubble, UiInputResult.NotHandled));
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));

        fixture.Run();

        Assert.Equal(1, fixture.KeyCount);
    }

    [Fact]
    public void RuntimeDoesNotFallbackToLegacyWhenRoutedHandledIsFalse()
    {
        var fixture = RuntimeFixture.Create();
        using var modal = fixture.Services.Composition.PushOverlay(new TestLayer(UiLayerInputPolicy.Modal, UiInputResult.NotHandled));
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;

        fixture.Run();

        Assert.Equal(0, fixture.KeyCount);
    }

    [Fact]
    public void ModalOwnedInput_IsNotProcessedByRuntimeAndNextApplicationInputWorks()
    {
        var fixture = RuntimeFixture.Create();
        using var modal = fixture.Services.ModalDialogs.Open(_ => { });
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;
        fixture.Run();

        Assert.True(modal.TryReadInput(out var modalInput));
        Assert.Equal(ConsoleKey.A, Assert.IsType<KeyConsoleInputEvent>(modalInput).Key.Key);
        Assert.Equal(0, fixture.KeyCount);

        modal.Dispose();
        var next = RuntimeFixture.Create(fixture.Driver, fixture.Services);
        next.Driver.EnqueueInput(Key(ConsoleKey.B));
        next.Run();

        Assert.Equal(1, next.KeyCount);
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, false, 1)]
    public void RenderRequests_AggregateToAtMostOneRender(bool routedInvalidate, bool legacyRender, int expectedRenderCount)
    {
        var fixture = RuntimeFixture.Create();
        fixture.RenderCount = 0;
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(
            UiLayerInputPolicy.Bubble,
            routedInvalidate ? UiInputResult.InvalidateOnly() : UiInputResult.NotHandled,
            fixture.CountRender));
        fixture.Context.HandleKeyInput = _ =>
        {
            fixture.KeyCount++;
            return new ApplicationRuntimeRenderRequest(legacyRender);
        };
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;

        fixture.Run();

        Assert.Equal(expectedRenderCount, fixture.RenderCount);
    }

    [Fact]
    public void HandlerStoppingApplication_SuppressesAdditionalRender()
    {
        var fixture = RuntimeFixture.Create();
        fixture.RenderCount = 0;
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(
            UiLayerInputPolicy.Bubble,
            UiInputResult.NotHandled,
            fixture.CountRender));
        fixture.Context.HandleKeyInput = _ =>
        {
            fixture.KeyCount++;
            fixture.Running = false;
            return new ApplicationRuntimeRenderRequest(true);
        };
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));

        fixture.Run();

        Assert.Equal(1, fixture.RenderCount);
    }

    [Fact]
    public void OverlayInvalidateWithoutApplicationPacket_Renders()
    {
        var fixture = RuntimeFixture.Create();
        fixture.RenderCount = 0;
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(
            UiLayerInputPolicy.Bubble,
            UiInputResult.HandledAndInvalidate,
            fixture.CountRender));
        fixture.Driver.EnqueueInput(Key(ConsoleKey.A));
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;

        fixture.Run();

        Assert.Equal(2, fixture.RenderCount);
        Assert.Equal(0, fixture.KeyCount);
    }

    [Fact]
    public void PendingMenuCommand_RenderRequestAggregatesIntoOneFinalRender()
    {
        var fixture = RuntimeFixture.Create();
        fixture.RenderCount = 0;
        bool commandExecuted = false;
        var request = new MenuCommandRequest { CommandId = "copy" };
        fixture.Context.TryTakeMenuCommand = new SingleMenuCommand(request).TryTake;
        fixture.Context.ExecuteMenuCommand = command =>
        {
            commandExecuted = true;
            Assert.Same(request, command);
            Assert.Equal(1, fixture.RenderCount);
            return new ApplicationRuntimeRenderRequest(true);
        };
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(
            UiLayerInputPolicy.Bubble,
            UiInputResult.HandledResult,
            fixture.CountRender));
        fixture.Driver.EnqueueInput(Key(ConsoleKey.Enter));
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;

        fixture.Run();

        Assert.True(commandExecuted);
        Assert.Equal(2, fixture.RenderCount);
        Assert.Equal(0, fixture.KeyCount);
    }

    [Fact]
    public void PendingMenuCommand_CanOpenModalAfterDispatchWithoutPreRender()
    {
        var fixture = RuntimeFixture.Create();
        fixture.RenderCount = 0;
        fixture.Context.TryTakeMenuCommand = new SingleMenuCommand(new MenuCommandRequest { CommandId = "dialog" }).TryTake;
        fixture.Context.ExecuteMenuCommand = _ =>
        {
            Assert.Equal(1, fixture.RenderCount);
            using var modal = fixture.Services.ModalDialogs.Open(_ => { });
            return ApplicationRuntimeRenderRequest.None;
        };
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(
            UiLayerInputPolicy.Bubble,
            UiInputResult.HandledResult,
            fixture.CountRender));
        fixture.Driver.EnqueueInput(Key(ConsoleKey.Enter));
        fixture.IsRunningOverride = new SequenceRunning(true, true, false).Next;

        fixture.Run();

        Assert.Equal(2, fixture.RenderCount);
    }

    [Fact]
    public void CancellationRefresh_RendersOnlyWhileRunning()
    {
        var fixture = RuntimeFixture.Create();
        fixture.RenderCount = 0;
        using var overlay = fixture.Services.Composition.PushOverlay(new TestLayer(
            UiLayerInputPolicy.Bubble,
            UiInputResult.NotHandled,
            fixture.CountRender));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int waitCalls = 0;
        fixture.Context.WaitToken = () => waitCalls++ == 0 ? cts.Token : CancellationToken.None;
        fixture.Context.ProcessPendingRefreshes = () =>
        {
            fixture.ProcessRefreshCount++;
            fixture.Running = false;
        };

        fixture.Run();

        Assert.Equal(1, fixture.ResetCount);
        Assert.Equal(1, fixture.ProcessRefreshCount);
        Assert.Equal(1, fixture.RenderCount);
    }

    [Fact]
    public void RuntimeLifecycle_DisposesAndRestoresTerminalOnNormalExitAndException()
    {
        var normal = RuntimeFixture.Create();
        normal.Running = false;
        normal.Run();

        var failing = RuntimeFixture.Create();
        failing.Context.CaptureUnderlay = () => throw new InvalidOperationException("boom");

        Assert.Throws<InvalidOperationException>(() => failing.Run());
        Assert.Equal(1, normal.DisposeCount);
        Assert.Equal(1, normal.RestoreCount);
        Assert.True(normal.Driver.CursorVisible);
        Assert.Equal(1, failing.DisposeCount);
        Assert.Equal(1, failing.RestoreCount);
        Assert.True(failing.Driver.CursorVisible);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private static MouseConsoleInputEvent Mouse() =>
        new(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private sealed record UnknownConsoleInputEvent : ConsoleInputEvent;

    private sealed class RuntimeFixture
    {
        private RuntimeFixture(FakeConsoleDriver driver, ApplicationServices services)
        {
            Driver = driver;
            Services = services;
            Context = new MutableRuntimeContext(this);
        }

        public FakeConsoleDriver Driver { get; }
        public ApplicationServices Services { get; }
        public MutableRuntimeContext Context { get; }
        public bool Running { get; set; } = true;
        public int StopAfterHandledInputs { get; set; } = 1;
        public int KeyCount { get; set; }
        public int ModifierCount { get; set; }
        public int MouseCount { get; set; }
        public int RenderCount { get; set; }
        public int ResetCount { get; set; }
        public int ProcessRefreshCount { get; set; }
        public int DisposeCount { get; set; }
        public int RestoreCount { get; set; }
        public Func<bool>? IsRunningOverride { get; set; }

        public static RuntimeFixture Create(FakeConsoleDriver? driver = null, ApplicationServices? services = null)
        {
            driver ??= new FakeConsoleDriver(80, 25);
            services ??= CreateServices(driver);
            return new RuntimeFixture(driver, services);
        }

        public void Run() =>
            new ApplicationRuntime(
                Services.Composition,
                Services.ApplicationSurface,
                Context.ToRuntimeContext()).Run();

        public void CountRender(UiRenderContext context) => RenderCount++;

        private static ApplicationServices CreateServices(FakeConsoleDriver driver)
        {
            var fs = new FakeFileSystemService();
            const string root = @"C:\Root";
            fs.AddDirectory(root);
            var settings = new AppSettings();
            settings.Panels.LeftStartDirectory = root;
            settings.Panels.RightStartDirectory = root;
            return ApplicationServicesBuilder.Create(
                new ScreenRenderer(driver),
                fs,
                new NoOpShellService(),
                new NoOpFileOperationService(),
                new InMemoryHistoryStore(),
                settings,
                enableBuiltInNetworkModules: false);
        }
    }

    private sealed class MutableRuntimeContext(RuntimeFixture fixture)
    {
        public Func<bool> IsRunning { get; set; } = () => fixture.IsRunningOverride?.Invoke() ?? fixture.Running;
        public Func<CancellationToken> WaitToken { get; set; } = () => CancellationToken.None;
        public Action CaptureUnderlay { get; set; } = () => { };
        public Action StartWatchingInitialPanels { get; set; } = () => { };
        public Action RestoreTerminal { get; set; } = () => fixture.RestoreCount++;
        public Action ResetWaitToken { get; set; } = () => fixture.ResetCount++;
        public Action ProcessPendingRefreshes { get; set; } = () => fixture.ProcessRefreshCount++;
        public Action DisposeRuntimeState { get; set; } = () => fixture.DisposeCount++;
        public Func<ConsoleKeyInfo, ApplicationRuntimeRenderRequest> HandleKeyInput { get; set; } = _ =>
        {
            fixture.KeyCount++;
            if (fixture.KeyCount + fixture.ModifierCount + fixture.MouseCount >= fixture.StopAfterHandledInputs)
                fixture.Running = false;
            return ApplicationRuntimeRenderRequest.None;
        };
        public Func<ConsoleModifiers, ApplicationRuntimeRenderRequest> HandleModifierInput { get; set; } = _ =>
        {
            fixture.ModifierCount++;
            if (fixture.KeyCount + fixture.ModifierCount + fixture.MouseCount >= fixture.StopAfterHandledInputs)
                fixture.Running = false;
            return ApplicationRuntimeRenderRequest.None;
        };
        public Func<MouseConsoleInputEvent, ApplicationRuntimeRenderRequest> HandleMouseInput { get; set; } = _ =>
        {
            fixture.MouseCount++;
            if (fixture.KeyCount + fixture.ModifierCount + fixture.MouseCount >= fixture.StopAfterHandledInputs)
                fixture.Running = false;
            return ApplicationRuntimeRenderRequest.None;
        };
        public TryTakeMenuCommand TryTakeMenuCommand { get; set; } = static (out MenuCommandRequest request) =>
        {
            request = null!;
            return false;
        };
        public Func<MenuCommandRequest, ApplicationRuntimeRenderRequest> ExecuteMenuCommand { get; set; } =
            _ => ApplicationRuntimeRenderRequest.None;

        public ApplicationRuntimeContext ToRuntimeContext() => new()
        {
            IsRunning = IsRunning,
            WaitToken = WaitToken,
            CaptureUnderlay = CaptureUnderlay,
            StartWatchingInitialPanels = StartWatchingInitialPanels,
            RestoreTerminal = RestoreTerminal,
            ResetWaitToken = ResetWaitToken,
            ProcessPendingRefreshes = ProcessPendingRefreshes,
            DisposeRuntimeState = DisposeRuntimeState,
            HandleKeyInput = HandleKeyInput,
            HandleModifierInput = HandleModifierInput,
            HandleMouseInput = HandleMouseInput,
            TryTakeMenuCommand = TryTakeMenuCommand,
            ExecuteMenuCommand = ExecuteMenuCommand,
        };
    }

    private sealed class SequenceRunning(params bool[] values)
    {
        private int _index;

        public bool Next() =>
            _index < values.Length ? values[_index++] : false;
    }

    private sealed class TestLayer(
        UiLayerInputPolicy policy,
        UiInputResult result,
        Action<UiRenderContext>? render = null) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public void Render(UiRenderContext context) => render?.Invoke(context);
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => result;
    }

    private sealed class SingleMenuCommand(MenuCommandRequest request)
    {
        private bool _taken;

        public bool TryTake(out MenuCommandRequest pending)
        {
            if (_taken)
            {
                pending = null!;
                return false;
            }

            _taken = true;
            pending = request;
            return true;
        }
    }
}
