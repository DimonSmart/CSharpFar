using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class InteractiveSurfaceHostTests
{
    [Fact]
    public void Run_PublishesInitialFrameAndDoesNotRenderAfterCompletion()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int renders = 0;
        var layer = new InteractiveSurfaceLayer<int, string>(
            (context, _) => { context.Screen.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); return ++renders; },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (_, frame, _) => new InteractiveSurfaceRouteResult<string>($"frame:{frame}"));

        string result = new InteractiveSurfaceHost(composition).Run(
            layer,
            (routed, semantic) =>
            {
                Assert.Equal(1, routed.Frame);
                return ModalDialogLoopResult<string>.Complete(semantic);
            });

        Assert.Equal("frame:1", result);
        Assert.Equal(1, renders);
    }

    [Fact]
    public void Run_ContinuedInputCreatesExactlyOneSubsequentCompositionRender()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        int renders = 0;
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Screen.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return ++renders;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(
                ((KeyConsoleInputEvent)input).Key.Key,
                Invalidate: true));

        int handled = 0;
        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, key) =>
            {
                handled++;
                return key == ConsoleKey.Enter
                    ? ModalDialogLoopResult<bool>.Complete(true)
                    : ModalDialogLoopResult<bool>.Continue;
            });

        Assert.Equal(2, handled);
        Assert.Equal(2, renders);
    }

    [Fact]
    public void Run_DomainHandlerExecutesAfterDispatchAndCanOpenNestedModal()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var modalHost = new ModalDialogHost(composition);
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Screen.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        int modalInputs = 0;
        new InteractiveSurfaceHost(composition).Run(
            layer,
            (_, key) =>
            {
                if (key == ConsoleKey.A)
                {
                    modalHost.Run(
                        context => context.Screen.Write(0, 1, "M", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)),
                        input =>
                        {
                            modalInputs++;
                            return ModalDialogLoopResult<bool>.Complete(true);
                        });
                    return ModalDialogLoopResult<bool>.Continue;
                }

                return ModalDialogLoopResult<bool>.Complete(true);
            });

        Assert.Equal(1, modalInputs);
    }

    [Fact]
    public void Run_ExceptionClosesSurfaceAndRestoresParent()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        int rootRenders = 0;
        composition.SetRootSurface(new ScreenRendererSurface(screen, context =>
        {
            rootRenders++;
            context.Screen.Write(0, 0, "R", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Screen.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        Assert.Throws<ApplicationException>(() => new InteractiveSurfaceHost(composition).Run<int, ConsoleKey, bool>(
            layer,
            (_, _) => throw new ApplicationException("boom")));
        composition.Render();

        Assert.True(rootRenders >= 1);
        Assert.Equal('R', driver.GetCell(0, 0).Character);
    }

    [Fact]
    public void CompositionPump_RendersOverlayInvalidationWhenSurfacePacketIsNotCreated()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.A));
        driver.EnqueueKey(Key(ConsoleKey.B));
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) =>
            {
                context.Screen.Write(0, 0, "S", new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
                return 1;
            },
            _ => new UiInteractionFrame([], keyboardTarget: new UiTargetId("surface.keyboard")),
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));
        using var surface = composition.OpenSurface(new InteractiveSurface(composition.Screen), layer);
        int overlayRenders = 0;
        using var overlay = composition.PushOverlay(new InvalidatingOnceOverlay(() => overlayRenders++));
        composition.Render();

        var packet = new CompositionInputPump<InteractiveSurfaceInput<int, ConsoleKey>>(
            composition,
            layer.TryTakeInput)
            .Read();

        Assert.Equal(ConsoleKey.B, packet.Semantic);
        Assert.Equal(2, overlayRenders);
    }

    [Fact]
    public void InteractiveSurfaceLayer_CannotBeRegisteredTwiceThroughDifferentLifecycles()
    {
        var composition = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()));
        composition.SetRootSurface(new ScreenRendererSurface(composition.Screen, _ => { }));
        var layer = new InteractiveSurfaceLayer<int, ConsoleKey>(
            (context, _) => 1,
            _ => UiInteractionFrame.Empty,
            (input, _, _) => new InteractiveSurfaceRouteResult<ConsoleKey>(((KeyConsoleInputEvent)input).Key.Key));

        using var first = composition.OpenSurface(new InteractiveSurface(composition.Screen), layer);

        Assert.Throws<InvalidOperationException>(() => composition.OpenSurface(new InteractiveSurface(composition.Screen), layer));
        Assert.Throws<InvalidOperationException>(() => composition.RegisterOverlay(layer));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private sealed class InvalidatingOnceOverlay(Action onRender) : IUiLayer
    {
        private bool _handled;

        public UiLayerInputPolicy InputPolicy => _handled ? UiLayerInputPolicy.None : UiLayerInputPolicy.Bubble;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public void Render(UiRenderContext context) => onRender();

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            _handled = true;
            return UiInputResult.HandledAndInvalidate;
        }
    }
}
