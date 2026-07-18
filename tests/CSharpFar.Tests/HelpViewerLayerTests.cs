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
    public void Mouse_CroppedFooterActionDoesNotPublishHitTarget()
    {
        var (_, _, layer) = Render([new HelpLine(HelpLineKind.Plain, Description: "body")], width: 6, height: 4);

        Assert.Empty(layer.CommittedFrame.FooterActionHits);
        Assert.DoesNotContain(
            layer.CommittedInteractionFrame.HitRegions,
            hit => hit.Target == HelpViewerLayer.FunctionKeys);
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
