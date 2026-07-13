using CSharpFar.App.Bootstrap;
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

public sealed class ApplicationUiSurfaceTests
{
    [Fact]
    public void SurfaceContract_UsesApplicationSurfaceAsInteractiveRoot()
    {
        var services = Services();
        var surface = services.ApplicationSurface;

        Assert.IsAssignableFrom<IUiSurface>(surface);
        Assert.IsAssignableFrom<IUiLayer>(surface);
        Assert.Equal(UiLayerInputPolicy.Bubble, surface.InputPolicy);
        Assert.NotSame(new UiFocusScope(), surface.FocusScope);

        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(surface.TryTakeInput(out _));
    }

    [Fact]
    public void Render_CommitsApplicationFrameForVisibleAndHiddenPanels()
    {
        var services = Services();

        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var visible));
        Assert.Equal(new ConsoleViewport(0, 0, 80, 25), visible.Frame.Viewport);
        Assert.Equal(ApplicationSurfaceMode.Panels, visible.Frame.Mode);

        services.Session.App.HiddenPanels = HiddenPanels.Both;
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.B));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var hidden));

        Assert.Equal(ApplicationSurfaceMode.HiddenCommandLine, hidden.Frame.Mode);
    }

    [Fact]
    public void RejectedRenderAttempt_DoesNotBecomeCommittedFrame()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        var services = Services(driver);

        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), routed.Frame.Viewport);
    }

    [Fact]
    public void PendingPacketKeepsDispatchFrameAfterAdditionalRender()
    {
        var services = Services();
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));
        services.Driver.SetSize(100, 35);
        services.Composition.Render();

        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(new ConsoleViewport(0, 0, 80, 25), routed.Frame.Viewport);
    }

    [Theory]
    [MemberData(nameof(RoutedInputs))]
    public void SupportedSemanticInput_CreatesOneApplicationPacket(ConsoleInputEvent input, Type expectedType)
    {
        var services = Services();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.IsType(expectedType, routed.Input);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(Key(ConsoleKey.B));
        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void UnsupportedSemanticInput_DoesNotCreatePacket()
    {
        var services = Services();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(new ConsoleResizeInputEvent());

        Assert.False(result.Handled);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void SecondDispatchBeforeConsumeThrows()
    {
        var services = Services();
        services.Composition.Render();

        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.Throws<InvalidOperationException>(() => services.Composition.DispatchInput(Key(ConsoleKey.B)));
    }

    [Fact]
    public void BubbleOverlayIsolation_ControlsApplicationPacketOwnership()
    {
        var services = Services();
        services.Composition.Render();
        var handled = new TestLayer(UiLayerInputPolicy.Bubble) { Result = UiInputResult.HandledResult };
        using (services.Composition.PushOverlay(handled))
            services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        var unhandled = new TestLayer(UiLayerInputPolicy.Bubble);
        using (services.Composition.PushOverlay(unhandled))
            services.Composition.DispatchInput(Key(ConsoleKey.B));

        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void ModalAndTemporarySurface_IsolateApplicationInput()
    {
        var services = Services();
        services.Composition.Render();
        var modal = new TestLayer(UiLayerInputPolicy.Modal);
        using (services.Composition.PushOverlay(modal))
            services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        var temporary = new TestSurface(services.Composition.Screen, UiInputResult.HandledResult);
        using (services.Composition.OpenSurface(temporary))
            services.Composition.DispatchInput(Key(ConsoleKey.B));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(Key(ConsoleKey.C));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(ConsoleKey.C, Assert.IsType<KeyConsoleInputEvent>(routed.Input).Key.Key);
    }

    [Fact]
    public void RenderOnlyOverlay_DoesNotBlockApplicationInput()
    {
        var services = Services();
        services.Composition.Render();

        using var overlay = services.Composition.PushOverlay(_ => { });
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    public static TheoryData<ConsoleInputEvent, Type> RoutedInputs() => new()
    {
        { Key(ConsoleKey.A), typeof(KeyConsoleInputEvent) },
        { new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control), typeof(ModifierKeyConsoleInputEvent) },
        { Mouse(), typeof(MouseConsoleInputEvent) },
    };

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
        return new TestServices(driver, services);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private static MouseConsoleInputEvent Mouse() =>
        new(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private sealed record TestServices(FakeConsoleDriver Driver, ApplicationServices Inner)
    {
        public ApplicationUiSurface ApplicationSurface => Inner.ApplicationSurface;
        public UiCompositionHost Composition => Inner.Composition;
        public CSharpFar.App.State.ApplicationSession Session => Inner.Session;
    }

    private sealed class TestLayer(UiLayerInputPolicy policy) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => Result;
    }

    private sealed class TestSurface(ScreenRenderer screen, UiInputResult result) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) { }
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => result;
    }
}
