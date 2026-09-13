using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationPointerSnapshotTests
{
    [Fact]
    public void CommittedPointerSnapshot_RemainsDistinctFromFutureCaptureUntilFrameReplacement()
    {
        var viewport = new ConsoleViewport(0, 0, 80, 25);
        var oldHit = new ApplicationFunctionKeyHit(
            new Rect(0, 24, 6, 1),
            "old-command",
            FunctionKeyLayer.Plain,
            ConsoleKey.F1);
        ApplicationUiFrame rendered = Frame(viewport, oldHit);
        ApplicationUiFrame committed = rendered with
        {
            PointerSnapshots = ApplicationPointerSnapshotBuilder.Capture(rendered),
        };

        var newHit = oldHit with { CommandId = "new-command" };
        ApplicationUiFrame futureRendered = Frame(viewport, newHit);
        ApplicationPointerFrame futureSnapshots = ApplicationPointerSnapshotBuilder.Capture(futureRendered);

        RoutedPointerSnapshot<ApplicationFunctionKeyHit> committedSnapshot =
            committed.PointerSnapshots!.FunctionKeyBar!;
        RoutedPointerSnapshot<ApplicationFunctionKeyHit> futureSnapshot =
            futureSnapshots.FunctionKeyBar!;
        UiHitRegion committedRegion = Assert.Single(committedSnapshot.InteractionFragment.HitRegions);
        UiHitRegion futureRegion = Assert.Single(futureSnapshot.InteractionFragment.HitRegions);

        Assert.Equal(ApplicationTargetIds.FunctionKeyAction(FunctionKeyLayer.Plain, ConsoleKey.F1), committedRegion.Target);
        Assert.Equal(committedRegion.Target, futureRegion.Target);
        Assert.Equal("old-command", Assert.Single(committed.FunctionKeyBar!.Actions).CommandId);
        Assert.Equal("new-command", Assert.Single(futureRendered.FunctionKeyBar!.Actions).CommandId);
        Assert.NotSame(committedSnapshot, futureSnapshot);
    }

    [Fact]
    public void Capture_PreservesPanelRetryAsAdditionalRegion()
    {
        var viewport = new ConsoleViewport(0, 0, 80, 25);
        var retryBounds = new Rect(2, 3, 10, 1);
        var panel = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 20),
            10,
            [],
            retryBounds,
            null);
        ApplicationUiFrame rendered = Frame(viewport, null) with { LeftPanel = panel };

        ApplicationPointerFrame pointers = ApplicationPointerSnapshotBuilder.Capture(rendered);
        UiTargetId retryTarget = ApplicationTargetIds.PanelRetry(PanelSide.Left);

        Assert.NotNull(pointers.LeftPanel);
        Assert.True(pointers.LeftPanel!.ContainsAdditionalTarget(retryTarget));
        Assert.Contains(
            pointers.LeftPanel.InteractionFragment.HitRegions,
            region => region.Target == retryTarget && region.Bounds.Equals(retryBounds));
    }

    private static ApplicationUiFrame Frame(ConsoleViewport viewport, ApplicationFunctionKeyHit? functionKey) =>
        new(
            viewport,
            ApplicationWorkspaceMode.Panels,
            null!,
            new ApplicationCommandLineFrame(new Rect(0, 23, 80, 1), 0, 0, 0, null),
            null,
            null,
            functionKey is null ? null : new ApplicationFunctionKeyBarFrame([functionKey]),
            null);
}
