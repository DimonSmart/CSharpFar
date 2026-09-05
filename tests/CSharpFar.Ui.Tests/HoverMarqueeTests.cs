using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class HoverMarqueeTests
{
    [Fact]
    public void Viewport_ClampsOffsetAlignsEndAndKeepsUnicodeWhole()
    {
        ConsoleTextViewport viewport = ConsoleTextMetrics.GetViewport("A界🙂BC", 999, 4);

        Assert.Equal(3, viewport.CellOffset);
        Assert.Equal("🙂BC", viewport.Text);
        Assert.Equal(4, ConsoleTextMetrics.GetCellWidth(viewport.Text));
        Assert.True(System.Text.Rune.TryGetRuneAt(viewport.Text, 0, out _));
    }

    [Fact]
    public void Viewport_OffsetInsideWideScalarOmitsItRatherThanSplittingUtf16()
    {
        ConsoleTextViewport viewport = ConsoleTextMetrics.GetViewport("A🙂BC", 2, 3);

        Assert.Equal(2, viewport.CellOffset);
        Assert.Equal(3, viewport.TextStartCell);
        Assert.Equal("BC", viewport.Text);
    }

    [Fact]
    public void Timing_WaitsScrollsPausesAtEndAndReturnsToLeadingText()
    {
        var clock = new ManualTimeProvider();
        var marquee = new HoverMarquee(clock);
        HoverMarqueeRegistration item = Item("one", "abcdef", x: 0, width: 3);
        marquee.SetRegistrations([item]);
        marquee.SetPointer(1, 0);

        Assert.Equal(clock.GetUtcNow() + HoverMarquee.HoverDelay, marquee.NextWakeUtc);
        Assert.False(marquee.HandleWake());

        clock.Advance(HoverMarquee.HoverDelay);
        Assert.True(marquee.HandleWake());
        Assert.Equal("bcd", marquee.GetText(item));
        clock.Advance(HoverMarquee.StepInterval);
        Assert.True(marquee.HandleWake());
        Assert.Equal("cde", marquee.GetText(item));
        clock.Advance(HoverMarquee.StepInterval);
        Assert.True(marquee.HandleWake());
        Assert.Equal("def", marquee.GetText(item));
        Assert.Equal(clock.GetUtcNow() + HoverMarquee.FinalPause, marquee.NextWakeUtc);

        clock.Advance(HoverMarquee.FinalPause);
        Assert.True(marquee.HandleWake());
        Assert.Equal("abc", marquee.GetText(item));
        Assert.Null(marquee.NextWakeUtc);
    }

    [Fact]
    public void OneOwner_ReplacementDisappearanceAndPointerExitResetImmediately()
    {
        var clock = new ManualTimeProvider();
        var marquee = new HoverMarquee(clock);
        HoverMarqueeRegistration first = Item("first", "abcdef", x: 0, width: 3);
        HoverMarqueeRegistration second = Item("second", "uvwxyz", x: 10, width: 3);
        marquee.SetRegistrations([first, second]);
        marquee.SetPointer(1, 0);
        clock.Advance(HoverMarquee.HoverDelay);
        marquee.HandleWake();

        Assert.Equal("first", marquee.ActiveIdentity);
        Assert.Equal("bcd", marquee.GetText(first));
        Assert.True(marquee.SetPointer(11, 0));
        Assert.Equal("second", marquee.ActiveIdentity);
        Assert.Equal("abc", marquee.GetText(first));

        HoverMarqueeRegistration replacement = Item("replacement", "uvwxyz", x: 10, width: 3);
        marquee.SetRegistrations([first, replacement]);
        Assert.Equal("replacement", marquee.ActiveIdentity);
        Assert.Equal(0, marquee.CellOffset);
        Assert.True(marquee.SetPointer(null, null) is false);
        Assert.Null(marquee.ActiveIdentity);
        Assert.Null(marquee.NextWakeUtc);
    }

    [Fact]
    public void SameIdentityWithChangedCommittedContentRestartsHoverDelay()
    {
        var clock = new ManualTimeProvider();
        var marquee = new HoverMarquee(clock);
        marquee.SetPointer(1, 0);
        marquee.SetRegistrations([Item("stable", "abcdef", 0, 3)]);
        clock.Advance(HoverMarquee.HoverDelay);
        marquee.HandleWake();

        marquee.SetRegistrations([Item("stable", "changed", 0, 3)]);

        Assert.Equal(0, marquee.CellOffset);
        Assert.Equal(clock.GetUtcNow() + HoverMarquee.HoverDelay, marquee.NextWakeUtc);
    }

    [Fact]
    public void Composition_PassiveMoveDrivesDelayedRenderAndPointerExitResetsImmediately()
    {
        var clock = new ManualTimeProvider();
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()), clock);
        var rendered = new List<string>();
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, context =>
            rendered.Add(context.RenderHoverMarquee(Item("stable", "abcdef", 0, 3)))));
        host.Render();

        UiInputResult move = host.DispatchInput(Mouse(1, 0, MouseButton.None, MouseEventKind.Move));
        Assert.True(move.Handled);
        Assert.False(move.Invalidate);
        Assert.Equal(clock.GetUtcNow() + HoverMarquee.HoverDelay, host.NextHoverWakeUtc);

        clock.Advance(HoverMarquee.HoverDelay);
        Assert.True(host.HandleHoverWake());
        host.Render();
        Assert.Equal("bcd", rendered[^1]);

        UiInputResult exit = host.DispatchInput(Mouse(20, 0, MouseButton.None, MouseEventKind.Move));
        Assert.True(exit.Handled);
        Assert.True(exit.Invalidate);
        host.Render();
        Assert.Equal("abc", rendered[^1]);
        Assert.Null(host.NextHoverWakeUtc);
    }

    [Fact]
    public void Composition_WithoutHoverDeadlinePreservesOrdinaryBlockingReadBoundary()
    {
        var driver = new FakeConsoleDriver();
        bool blockingReadObserved = false;
        bool nonBlockingProbeObserved = false;
        driver.BeforeReadInput = _ => blockingReadObserved = true;
        driver.BeforeTryReadInput = _ => nonBlockingProbeObserved = true;
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        var host = new UiCompositionHost(new ScreenRenderer(driver));

        ConsoleInputEvent? input = host.ReadInputUntil(null);

        Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.True(blockingReadObserved);
        Assert.False(nonBlockingProbeObserved);
    }

    [Theory]
    [InlineData(MouseButton.Left, MouseEventKind.Down)]
    [InlineData(MouseButton.WheelDown, MouseEventKind.Wheel)]
    [InlineData(MouseButton.Left, MouseEventKind.Move)]
    public void Composition_NonPassiveMouseCancelsWithoutChangingItsNormalRouting(
        MouseButton button,
        MouseEventKind kind)
    {
        var clock = new ManualTimeProvider();
        var host = new UiCompositionHost(new ScreenRenderer(new FakeConsoleDriver()), clock);
        host.SetRootSurface(new ScreenRendererSurface(host.Screen, context =>
            _ = context.RenderHoverMarquee(Item("stable", "abcdef", 0, 3))));
        host.Render();
        host.DispatchInput(Mouse(1, 0, MouseButton.None, MouseEventKind.Move));

        UiInputResult result = host.DispatchInput(Mouse(1, 0, button, kind));

        Assert.False(result.Handled);
        Assert.Null(host.NextHoverWakeUtc);
    }

    private static MouseConsoleInputEvent Mouse(int x, int y, MouseButton button, MouseEventKind kind) =>
        new(x, y, button, kind, MouseKeyModifiers.None);

    private static HoverMarqueeRegistration Item(string identity, string text, int x, int width) =>
        new(identity, text, new Rect(x, 0, width, 1), width);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
