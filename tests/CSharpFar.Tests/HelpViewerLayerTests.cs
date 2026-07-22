using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class HelpViewerLayerTests
{
    [Fact]
    public void Render_CommitsBothScrollLimitsAndInteractiveTargets()
    {
        HelpLine[] lines =
        [
            new(HelpLineKind.KeyLine, "K", new string('d', 40)),
            new(HelpLineKind.Plain, Description: "short"),
            new(HelpLineKind.Plain, Description: "tail"),
            new(HelpLineKind.Plain, Description: "4"),
            new(HelpLineKind.Plain, Description: "5"),
            new(HelpLineKind.Plain, Description: "6"),
            new(HelpLineKind.Plain, Description: "7"),
            new(HelpLineKind.Plain, Description: "8"),
        ];
        var (_, _, layer) = Render(lines, width: 20, height: 5);

        HelpViewerFrame frame = layer.CommittedFrame;

        Assert.Equal(5, frame.MaxScrollTop);
        Assert.True(frame.MaxScrollLeft > 0);
        Assert.Contains(frame.FooterActionHits, hit => hit.Action == HelpAction.Close && hit.Key == "F10");
        Assert.Contains(frame.CommittedTargets(), target => target == HelpViewerLayer.Content);
        Assert.Contains(frame.CommittedTargets(), target => target == HelpViewerLayer.Scrollbar);
        Assert.Contains(frame.CommittedTargets(), target => target == HelpViewerLayer.FunctionKeys);
    }

    [Fact]
    public void Keyboard_ClampsHorizontalScrollAndUnsupportedKeyDoesNotInvalidate()
    {
        HelpLine[] lines = [new(HelpLineKind.KeyLine, "K", new string('d', 30))];
        var (composition, _, layer) = Render(lines, width: 12, height: 4);
        int maxLeft = layer.CommittedFrame.MaxScrollLeft;

        for (int i = 0; i < maxLeft + 5; i++)
        {
            UiInputResult result = composition.DispatchInput(Key(ConsoleKey.RightArrow));
            Assert.True(layer.TryTakeInput(out _));
            if (result.Invalidate)
                composition.Render();
        }

        Assert.Equal(maxLeft, layer.CommittedFrame.ScrollLeft);

        UiInputResult unsupported = composition.DispatchInput(Key(ConsoleKey.X));
        Assert.True(layer.TryTakeInput(out var packet));

        Assert.False(unsupported.Invalidate);
        Assert.Equal(HelpAction.None, packet.Semantic);
    }

    [Fact]
    public void Keyboard_AllDocumentedKeysUseCommittedFrameAndClamp()
    {
        HelpLine[] lines = Enumerable.Range(0, 20)
            .Select(i => new HelpLine(HelpLineKind.Plain, Description: i == 0 ? new string('w', 80) : $"line {i}"))
            .ToArray();
        var (composition, _, layer) = Render(lines, width: 20, height: 5);
        int visibleRows = layer.CommittedFrame.VisibleRows;
        int maxTop = layer.CommittedFrame.MaxScrollTop;
        int maxLeft = layer.CommittedFrame.MaxScrollLeft;

        Assert.Equal(HelpAction.None, DispatchKey(composition, layer, ConsoleKey.DownArrow).Semantic);
        composition.Render();
        Assert.Equal(1, layer.CommittedFrame.ScrollTop);

        DispatchKey(composition, layer, ConsoleKey.UpArrow);
        composition.Render();
        Assert.Equal(0, layer.CommittedFrame.ScrollTop);

        DispatchKey(composition, layer, ConsoleKey.PageDown);
        composition.Render();
        Assert.Equal(visibleRows, layer.CommittedFrame.ScrollTop);

        DispatchKey(composition, layer, ConsoleKey.End);
        composition.Render();
        Assert.Equal(maxTop, layer.CommittedFrame.ScrollTop);
        Assert.Equal(0, layer.CommittedFrame.ScrollLeft);

        UiInputResult clampedDown = composition.DispatchInput(Key(ConsoleKey.PageDown));
        Assert.True(layer.TryTakeInput(out _));
        Assert.False(clampedDown.Invalidate);

        DispatchKey(composition, layer, ConsoleKey.PageUp);
        composition.Render();
        Assert.Equal(Math.Max(0, maxTop - visibleRows), layer.CommittedFrame.ScrollTop);

        DispatchKey(composition, layer, ConsoleKey.Home);
        composition.Render();
        Assert.Equal(0, layer.CommittedFrame.ScrollTop);
        Assert.Equal(0, layer.CommittedFrame.ScrollLeft);

        DispatchKey(composition, layer, ConsoleKey.RightArrow);
        composition.Render();
        Assert.Equal(1, layer.CommittedFrame.ScrollLeft);

        DispatchKey(composition, layer, ConsoleKey.LeftArrow);
        composition.Render();
        Assert.Equal(0, layer.CommittedFrame.ScrollLeft);

        for (int i = 0; i < maxLeft + 3; i++)
        {
            UiInputResult result = composition.DispatchInput(Key(ConsoleKey.RightArrow));
            Assert.True(layer.TryTakeInput(out _));
            if (result.Invalidate)
                composition.Render();
        }
        Assert.Equal(maxLeft, layer.CommittedFrame.ScrollLeft);

        foreach (ConsoleKey closeKey in new[] { ConsoleKey.F1, ConsoleKey.F10, ConsoleKey.Escape })
        {
            long version = composition.StableRenderVersion;
            HelpAction action = DispatchKey(composition, layer, closeKey).Semantic;
            Assert.Equal(HelpAction.Close, action);
            Assert.Equal(version, composition.StableRenderVersion);
        }

        HelpViewerFrame before = layer.CommittedFrame;
        UiInputResult unsupported = composition.DispatchInput(Key(ConsoleKey.X));
        Assert.True(layer.TryTakeInput(out var unsupportedPacket));
        Assert.False(unsupported.Invalidate);
        Assert.Equal(HelpAction.None, unsupportedPacket.Semantic);
        Assert.Equal(before, layer.CommittedFrame);
    }

    [Theory]
    [InlineData(ConsoleKey.F1)]
    [InlineData(ConsoleKey.F10)]
    [InlineData(ConsoleKey.Escape)]
    public void Keyboard_CloseKeysProduceCloseAction(ConsoleKey key)
    {
        var (composition, _, layer) = Render([new HelpLine(HelpLineKind.Plain, Description: "body")], 40, 5);

        composition.DispatchInput(Key(key));

        Assert.True(layer.TryTakeInput(out var packet));
        Assert.Equal(HelpAction.Close, packet.Semantic);
    }

    [Fact]
    public void Mouse_WheelDownOnlyScrollsForWheelDownButton()
    {
        HelpLine[] lines = Enumerable.Range(0, 20)
            .Select(i => new HelpLine(HelpLineKind.Plain, Description: $"line {i}"))
            .ToArray();
        var (composition, _, layer) = Render(lines, 40, 5);

        UiInputResult down = composition.DispatchInput(Mouse(1, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.True(layer.TryTakeInput(out _));
        composition.Render();

        Assert.True(down.Invalidate);
        Assert.Equal(3, layer.CommittedFrame.ScrollTop);

        UiInputResult unrelated = composition.DispatchInput(Mouse(1, 1, MouseButton.Left, MouseEventKind.Wheel));
        Assert.True(layer.TryTakeInput(out _));

        Assert.False(unrelated.Invalidate);
        Assert.Equal(3, layer.CommittedFrame.ScrollTop);
    }

    [Fact]
    public void Mouse_WheelChangesStateOnlyOverCommittedContentForSupportedWheelButtons()
    {
        HelpLine[] lines = Enumerable.Range(0, 20)
            .Select(i => new HelpLine(HelpLineKind.Plain, Description: $"line {i}"))
            .ToArray();
        var (composition, _, layer) = Render(lines, 40, 5);

        DispatchMouse(composition, layer, Mouse(1, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        composition.Render();
        Assert.Equal(3, layer.CommittedFrame.ScrollTop);

        DispatchMouse(composition, layer, Mouse(1, 1, MouseButton.WheelUp, MouseEventKind.Wheel));
        composition.Render();
        Assert.Equal(0, layer.CommittedFrame.ScrollTop);

        UiInputResult topClamp = composition.DispatchInput(Mouse(1, 1, MouseButton.WheelUp, MouseEventKind.Wheel));
        Assert.True(layer.TryTakeInput(out _));
        Assert.False(topClamp.Invalidate);

        DispatchKey(composition, layer, ConsoleKey.End);
        composition.Render();
        int bottom = layer.CommittedFrame.ScrollTop;
        UiInputResult bottomClamp = composition.DispatchInput(Mouse(1, 1, MouseButton.WheelDown, MouseEventKind.Wheel));
        Assert.True(layer.TryTakeInput(out _));
        Assert.False(bottomClamp.Invalidate);
        Assert.Equal(bottom, layer.CommittedFrame.ScrollTop);

        Assert.False(composition.DispatchInput(Mouse(1, 0, MouseButton.WheelDown, MouseEventKind.Wheel)).Invalidate);
        Assert.True(layer.TryTakeInput(out _));
        Assert.False(composition.DispatchInput(Mouse(3, 4, MouseButton.WheelDown, MouseEventKind.Wheel)).Invalidate);
        Assert.True(layer.TryTakeInput(out _));
        Assert.False(composition.DispatchInput(Mouse(1, 1, MouseButton.Left, MouseEventKind.Wheel)).Invalidate);
        Assert.True(layer.TryTakeInput(out _));
    }

    [Fact]
    public void Mouse_ScrollbarTrackClickAndDragUseCommittedScrollStateAndCapture()
    {
        HelpLine[] lines = Enumerable.Range(0, 30)
            .Select(i => new HelpLine(HelpLineKind.Plain, Description: $"line {i}"))
            .ToArray();
        var (composition, _, layer) = Render(lines, 20, 8);
        Rect bar = layer.CommittedFrame.ScrollBarBounds!.Value;

        UiInputResult belowThumb = composition.DispatchInput(Mouse(bar.X, bar.Bottom - 1, MouseButton.Left, MouseEventKind.Down));
        Assert.True(layer.TryTakeInput(out _));
        Assert.True(belowThumb.Invalidate);
        composition.Render();
        Assert.True(layer.CommittedFrame.ScrollTop > 0);

        int afterBelow = layer.CommittedFrame.ScrollTop;
        UiInputResult aboveThumb = composition.DispatchInput(Mouse(bar.X, bar.Y, MouseButton.Left, MouseEventKind.Down));
        Assert.True(layer.TryTakeInput(out _));
        if (aboveThumb.Invalidate)
            composition.Render();
        Assert.True(layer.CommittedFrame.ScrollTop <= afterBelow);

        bar = layer.CommittedFrame.ScrollBarBounds!.Value;
        int thumbY = FirstThumbY(bar, layer.CommittedFrame.VerticalScrollState!);
        UiInputResult thumbDown = composition.DispatchInput(Mouse(bar.X, thumbY, MouseButton.Left, MouseEventKind.Down));
        Assert.True(layer.TryTakeInput(out _));
        Assert.False(thumbDown.Invalidate);

        UiInputResult capturedMove = composition.DispatchInput(Mouse(bar.X, bar.Bottom + 5, MouseButton.Left, MouseEventKind.Move));
        Assert.True(layer.TryTakeInput(out var movePacket));
        Assert.Equal(UiInputRouteKind.CapturedTarget, movePacket.Routed.RouteKind);
        if (capturedMove.Invalidate)
            composition.Render();

        UiInputResult up = composition.DispatchInput(Mouse(bar.X, bar.Bottom + 5, MouseButton.Left, MouseEventKind.Up));
        Assert.True(layer.TryTakeInput(out var upPacket));
        Assert.Equal(UiInputRouteKind.CapturedTarget, upPacket.Routed.RouteKind);
        Assert.False(up.Invalidate);

        UiInputResult moveAfterRelease = composition.DispatchInput(Mouse(bar.X, bar.Bottom + 5, MouseButton.Left, MouseEventKind.Move));
        Assert.True(layer.TryTakeInput(out var afterRelease));
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, afterRelease.Routed.RouteKind);
        Assert.False(moveAfterRelease.Invalidate);
    }

    [Fact]
    public void Mouse_ScrollbarDragRebasesOnAcceptedResizeAndSurvivesRejectedDisappearance()
    {
        HelpLine[] lines = Enumerable.Range(0, 30)
            .Select(i => new HelpLine(HelpLineKind.Plain, Description: $"line {i}"))
            .ToArray();
        var (composition, driver, layer) = Render(lines, 20, 8);
        Rect bar = layer.CommittedFrame.ScrollBarBounds!.Value;
        int thumbY = FirstThumbY(bar, layer.CommittedFrame.VerticalScrollState!);
        DispatchMouse(composition, layer, Mouse(bar.X, thumbY, MouseButton.Left, MouseEventKind.Down));

        driver.SetSize(1, 1);
        driver.ResizeAfterWriteCount = driver.WriteAtCallCount + 1;
        driver.ResizeAfterWrite = current => current.SetSize(20, 8);
        composition.Render();
        Assert.NotNull(layer.CommittedFrame.ScrollBarBounds);

        UiInputResult stillCaptured = composition.DispatchInput(Mouse(19, 7, MouseButton.Left, MouseEventKind.Move));
        Assert.True(layer.TryTakeInput(out var stillCapturedPacket));
        Assert.Equal(UiInputRouteKind.CapturedTarget, stillCapturedPacket.Routed.RouteKind);
        if (stillCaptured.Invalidate)
            composition.Render();

        driver.SetSize(20, 20);
        composition.Render(isResizeRecovery: true);
        UiInputResult rebasedMove = composition.DispatchInput(Mouse(19, 18, MouseButton.Left, MouseEventKind.Move));
        Assert.True(layer.TryTakeInput(out var rebasedPacket));
        Assert.Equal(UiInputRouteKind.CapturedTarget, rebasedPacket.Routed.RouteKind);
        if (rebasedMove.Invalidate)
            composition.Render();

        driver.SetSize(1, 1);
        composition.Render(isResizeRecovery: true);
        Assert.Null(layer.CommittedFrame.ScrollBarBounds);

        UiInputResult afterDisappearance = composition.DispatchInput(Mouse(0, 0, MouseButton.Left, MouseEventKind.Move));
        Assert.True(layer.TryTakeInput(out var ordinaryPacket));
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, ordinaryPacket.Routed.RouteKind);
        Assert.False(afterDisappearance.Invalidate);
    }

    [Fact]
    public void Mouse_CroppedFooterActionDoesNotPublishHitTarget()
    {
        var (_, _, layer) = Render([new HelpLine(HelpLineKind.Plain, Description: "body")], width: 6, height: 4);

        Assert.Empty(layer.CommittedFrame.FooterActionHits);
        Assert.DoesNotContain(
            layer.CommittedInteractionFrame.HitRegions,
            hit => hit.Target == HelpViewerLayer.FunctionKeys);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(6, 4)]
    [InlineData(20, 1)]
    [InlineData(1, 8)]
    public void TinyViewport_PublishesOnlyVisibleNonEmptyTargetsAndKeepsKeyboardFocus(int width, int height)
    {
        var (_, driver, layer) = Render(
            Enumerable.Range(0, 20).Select(i => new HelpLine(HelpLineKind.Plain, Description: $"line {i}")).ToArray(),
            width,
            height);
        var viewport = new Rect(0, 0, width, height);

        Assert.Equal(HelpViewerLayer.Keyboard, layer.CommittedInteractionFrame.KeyboardTarget);
        Assert.False(driver.CursorVisible);
        foreach (UiHitRegion hit in layer.CommittedInteractionFrame.HitRegions)
        {
            Assert.True(hit.Bounds.Width > 0);
            Assert.True(hit.Bounds.Height > 0);
            Assert.True(viewport.Contains(hit.Bounds.X, hit.Bounds.Y));
            Assert.True(viewport.Contains(hit.Bounds.Right - 1, hit.Bounds.Bottom - 1));
        }

        Assert.Equal(width >= 7 && height > 0, layer.CommittedFrame.FooterActionHits.Count > 0);
        bool scrollbarInteractive = layer.CommittedFrame.ScrollBarBounds is { } bar &&
            layer.CommittedFrame.VerticalScrollState is { } state &&
            ScrollBarInteraction.IsInteractive(bar, state);
        Assert.Equal(
            scrollbarInteractive,
            layer.CommittedInteractionFrame.HitRegions.Any(hit => hit.Target == HelpViewerLayer.Scrollbar));
    }

    [Fact]
    public void FooterActivationClosesOnlyInsideCommittedActionBounds()
    {
        var (composition, _, layer) = Render([new HelpLine(HelpLineKind.Plain, Description: "body")], 20, 4);
        Rect action = layer.CommittedFrame.FooterActionHits.Single().Bounds;

        HelpAction close = DispatchMouse(composition, layer, Mouse(action.X, action.Y, MouseButton.Left, MouseEventKind.Down)).Semantic;
        Assert.Equal(HelpAction.Close, close);

        UiInputResult outside = composition.DispatchInput(Mouse(action.Right + 1, action.Y, MouseButton.Left, MouseEventKind.Down));
        Assert.True(layer.TryTakeInput(out var packet));
        Assert.False(outside.Invalidate);
        Assert.Equal(HelpAction.None, packet.Semantic);

        var (croppedComposition, _, croppedLayer) = Render([new HelpLine(HelpLineKind.Plain, Description: "body")], 6, 4);
        Assert.Empty(croppedLayer.CommittedFrame.FooterActionHits);
        UiInputResult cropped = croppedComposition.DispatchInput(Mouse(1, 3, MouseButton.Left, MouseEventKind.Down));
        Assert.True(croppedLayer.TryTakeInput(out var croppedPacket));
        Assert.Equal(HelpAction.None, croppedPacket.Semantic);
        Assert.False(cropped.Invalidate);
    }

    [Fact]
    public void Cursor_IsHiddenByCommittedHelpFrameAndRestoredAfterClose()
    {
        var driver = new FakeConsoleDriver(20, 5);
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new CursorRootSurface(screen));
        composition.Render();
        Assert.True(driver.CursorVisible);

        var layer = new HelpViewerLayer([new HelpLine(HelpLineKind.Plain, Description: "body")], PaletteRegistry.Default);
        using (composition.OpenSurface(new InteractiveSurface(screen), layer))
        {
            composition.Render();
            Assert.False(driver.CursorVisible);
            driver.SetSize(20, 4);
            composition.Render(isResizeRecovery: true);
            Assert.False(driver.CursorVisible);
        }

        Assert.True(driver.CursorVisible);
        Assert.Equal(2, driver.CursorX);
        Assert.Equal(2, driver.CursorY);
    }

    [Fact]
    public void Rendering_KeyLineUsesSeparateStylesAcrossHorizontalBoundary()
    {
        var palette = PaletteRegistry.Default;
        HelpLine[] lines = [new(HelpLineKind.KeyLine, "ABC", "description")];
        var (_, driver, layer) = Render(lines, width: 28, height: 4, palette);

        SnapshotCell keyCell = driver.GetCell(2, 1);
        SnapshotCell descriptionCell = driver.GetCell(HelpKeyColumnWidth(), 1);

        Assert.Equal(PaletteStyles.HelpKey(palette).Foreground, keyCell.Foreground);
        Assert.Equal(PaletteStyles.HelpBody(palette).Foreground, descriptionCell.Foreground);

        var composition = Open(lines, width: 8, height: 4, out driver, out layer, palette);
        composition.Render();
        for (int i = 0; i < HelpKeyColumnWidth() - 2; i++)
        {
            composition.DispatchInput(Key(ConsoleKey.RightArrow));
            Assert.True(layer.TryTakeInput(out _));
            composition.Render();
        }

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 1 &&
            record.Foreground == PaletteStyles.HelpKey(palette).Foreground);
        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 1 &&
            record.Foreground == PaletteStyles.HelpBody(palette).Foreground);
    }

    private static (UiCompositionHost Composition, FakeConsoleDriver Driver, HelpViewerLayer Layer) Render(
        HelpLine[] lines,
        int width,
        int height,
        ConsolePalette? palette = null)
    {
        var composition = Open(lines, width, height, out var driver, out var layer, palette);
        composition.Render();
        return (composition, driver, layer);
    }

    private static UiCompositionHost Open(
        HelpLine[] lines,
        int width,
        int height,
        out FakeConsoleDriver driver,
        out HelpViewerLayer layer,
        ConsolePalette? palette = null)
    {
        driver = new FakeConsoleDriver(width, height);
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        layer = new HelpViewerLayer(lines, palette ?? PaletteRegistry.Default);
        composition.OpenSurface(new InteractiveSurface(screen), layer);
        return composition;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, false, false, false));

    private static MouseConsoleInputEvent Mouse(int x, int y, MouseButton button, MouseEventKind kind) =>
        new(x, y, button, kind, MouseKeyModifiers.None);

    private static int HelpKeyColumnWidth() => 20;

    private static InteractiveSurfaceInput<HelpViewerFrame, HelpAction> DispatchKey(
        UiCompositionHost composition,
        HelpViewerLayer layer,
        ConsoleKey key)
    {
        composition.DispatchInput(Key(key));
        Assert.True(layer.TryTakeInput(out var packet));
        return packet;
    }

    private static InteractiveSurfaceInput<HelpViewerFrame, HelpAction> DispatchMouse(
        UiCompositionHost composition,
        HelpViewerLayer layer,
        MouseConsoleInputEvent mouse)
    {
        composition.DispatchInput(mouse);
        Assert.True(layer.TryTakeInput(out var packet));
        return packet;
    }

    private static int FirstThumbY(Rect bar, ScrollState state)
    {
        ScrollBarThumb thumb = ScrollBarInteraction.CalculateThumb(bar, state);
        return thumb.ThumbY;
    }

    private sealed class CursorRootSurface(ScreenRenderer screen) : UiLayer<UiFocusFrame>, IUiSurface
    {
        private static readonly UiTargetId CursorTarget = new("root.cursor");

        public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        public void CompleteFrame(UiFrameCompletion completion) { }

        protected override UiFocusFrame RenderFrame(UiRenderContext context) =>
            FocusFrame(
                [new UiFocusEntry(CursorTarget, 0, Cursor: new UiCursorPlacement(2, 2, true))],
                CursorTarget);

        protected override UiInteractionFrame BuildInteractionFrame(UiFocusFrame frame) =>
            new([], frame, CursorTarget);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            UiFocusFrame frame,
            UiInputRouteContext context) => UiInputResult.NotHandled;
    }
}

file static class HelpViewerFrameTestExtensions
{
    public static IReadOnlyList<UiTargetId> CommittedTargets(this HelpViewerFrame frame)
    {
        var targets = new List<UiTargetId> { HelpViewerLayer.Content };
        if (frame.ScrollBarBounds is { } bar &&
            frame.VerticalScrollState is { } state &&
            ScrollBarInteraction.IsInteractive(bar, state))
        {
            targets.Add(HelpViewerLayer.Scrollbar);
        }
        if (frame.FooterActionHits.Count > 0)
            targets.Add(HelpViewerLayer.FunctionKeys);

        return targets;
    }
}
