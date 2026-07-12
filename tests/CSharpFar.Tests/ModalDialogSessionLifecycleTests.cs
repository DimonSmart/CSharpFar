using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ModalDialogSessionLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadInput_AfterDisposeThrows(bool generic)
    {
        var fixture = Fixture();

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.ReadInput(out _));
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.ReadInput());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryReadInput_AfterDisposeThrows(bool generic)
    {
        var fixture = Fixture();

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.TryReadInput(out _, out _));
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.TryReadInput(out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadInput_WithPendingPacketAfterDisposeThrows(bool generic)
    {
        var fixture = Fixture();
        var key = Key(ConsoleKey.A);

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            fixture.Composition.DispatchInput(key);
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.ReadInput(out _));
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            fixture.Composition.DispatchInput(key);
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.ReadInput());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryReadInput_WithPendingPacketAfterDisposeThrows(bool generic)
    {
        var fixture = Fixture();
        var key = Key(ConsoleKey.A);

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            fixture.Composition.DispatchInput(key);
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.TryReadInput(out _, out _));
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            fixture.Composition.DispatchInput(key);
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.TryReadInput(out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_AfterDisposeThrows(bool generic)
    {
        var fixture = Fixture();

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.Render());
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.Render());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedDispose_RemainsSafe(bool generic)
    {
        var fixture = Fixture();

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            session.Dispose();
            session.Dispose();
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            session.Dispose();
            session.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOutOfOrderDispose_DoesNotInvalidateSession(bool generic)
    {
        var fixture = Fixture();

        if (generic)
        {
            var first = fixture.Modals.Open(context => context.Viewport);
            var second = fixture.Modals.Open(context => context.Viewport);
            first.Render();

            Assert.Throws<InvalidOperationException>(() => first.Dispose());

            second.Dispose();
            first.Render();
            first.Dispose();
        }
        else
        {
            var first = fixture.Modals.Open(_ => { });
            var second = fixture.Modals.Open(_ => { });
            first.Render();

            Assert.Throws<InvalidOperationException>(() => first.Dispose());

            second.Dispose();
            first.Render();
            first.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulDispose_ClearsPendingPacket(bool generic)
    {
        var fixture = Fixture();
        var key = Key(ConsoleKey.A);

        if (generic)
        {
            var session = fixture.Modals.Open(context => context.Viewport);
            session.Render();
            fixture.Composition.DispatchInput(key);
            session.Dispose();

            using var next = fixture.Modals.Open(context => context.Viewport);
            next.Render();
            fixture.Composition.DispatchInput(Key(ConsoleKey.B));
            var input = next.ReadInput(out _);

            Assert.Equal(ConsoleKey.B, Assert.IsType<KeyConsoleInputEvent>(input).Key.Key);
        }
        else
        {
            var session = fixture.Modals.Open(_ => { });
            session.Render();
            fixture.Composition.DispatchInput(key);
            session.Dispose();

            using var next = fixture.Modals.Open(_ => { });
            next.Render();
            fixture.Composition.DispatchInput(Key(ConsoleKey.B));
            var input = next.ReadInput();

            Assert.Equal(ConsoleKey.B, Assert.IsType<KeyConsoleInputEvent>(input).Key.Key);
        }
    }

    [Fact]
    public void NonGenericPollingSession_PreservesPendingPacketAndNestedIsolation()
    {
        var fixture = Fixture();
        using var parent = fixture.Modals.Open(_ => { });
        parent.Render();
        fixture.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(parent.TryReadInput(out var pending));
        Assert.Equal(ConsoleKey.A, Assert.IsType<KeyConsoleInputEvent>(pending).Key.Key);
        Assert.False(parent.TryReadInput(out _));

        fixture.Driver.SetSize(100, 35);
        Assert.False(parent.TryReadInput(out _));

        using (var nested = fixture.Modals.Open(_ => { }))
        {
            nested.Render();
            fixture.Composition.DispatchInput(Key(ConsoleKey.B));

            Assert.False(parent.TryReadInput(out _));
            Assert.True(nested.TryReadInput(out var nestedInput));
            Assert.Equal(ConsoleKey.B, Assert.IsType<KeyConsoleInputEvent>(nestedInput).Key.Key);
        }

        fixture.Composition.DispatchInput(Key(ConsoleKey.C));
        Assert.True(parent.TryReadInput(out var parentInput));
        Assert.Equal(ConsoleKey.C, Assert.IsType<KeyConsoleInputEvent>(parentInput).Key.Key);
    }

    [Fact]
    public void GenericPollingSession_PreservesFrameAndAllowsNextDispatchAfterConsume()
    {
        var fixture = Fixture();
        using var session = fixture.Modals.Open(context => context.Viewport);
        var initialFrame = session.Render();

        fixture.Composition.DispatchInput(Key(ConsoleKey.A));
        fixture.Composition.Render();

        Assert.True(session.TryReadInput(out var input, out var frame));
        Assert.Equal(ConsoleKey.A, Assert.IsType<KeyConsoleInputEvent>(input).Key.Key);
        Assert.Equal(initialFrame, frame);

        fixture.Driver.SetSize(100, 35);
        Assert.False(session.TryReadInput(out _, out _));

        fixture.Composition.DispatchInput(Key(ConsoleKey.B));
        Assert.True(session.TryReadInput(out var second, out var secondFrame));
        Assert.Equal(ConsoleKey.B, Assert.IsType<KeyConsoleInputEvent>(second).Key.Key);
        Assert.Equal(new CSharpFar.Console.Models.ConsoleViewport(0, 0, 100, 35), secondFrame);
    }

    private static (FakeConsoleDriver Driver, UiCompositionHost Composition, ModalDialogHost Modals) Fixture()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var host = new UiCompositionHost(new ScreenRenderer(driver));
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, _ => { }));
        return (driver, host, new ModalDialogHost(host));
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));
}
