using CSharpFar.App.State;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record ApplicationPointerFrame(
    RoutedPointerSnapshot<ApplicationPanelPointerTarget>? LeftPanel,
    RoutedPointerSnapshot<ApplicationPanelPointerTarget>? RightPanel,
    RoutedPointerSnapshot<ApplicationQuickViewPointerHit>? QuickView,
    RoutedPointerSnapshot<ApplicationFileUsageOwnerHit>? FileUsage,
    RoutedPointerSnapshot<ApplicationFunctionKeyHit>? FunctionKeyBar,
    RoutedPointerSnapshot<ApplicationDirectoryShortcutHit>? DirectoryShortcuts);

internal static class ApplicationPointerSnapshotBuilder
{
    public static ApplicationPointerFrame Capture(ApplicationUiFrame frame)
    {
        if (frame.Mode != ApplicationWorkspaceMode.Panels)
            return new(null, null, null, null, null, null);

        return new ApplicationPointerFrame(
            CapturePanel(frame.LeftPanel, frame.Viewport),
            CapturePanel(frame.RightPanel, frame.Viewport),
            CaptureQuickView(frame.QuickView, frame.Viewport),
            CaptureFileUsage(frame.FileUsage, frame.Viewport),
            CaptureFunctionKeyBar(frame.FunctionKeyBar, frame.Viewport),
            CaptureDirectoryShortcuts(frame.DirectoryShortcutBar, frame.Viewport));
    }

    private static RoutedPointerSnapshot<ApplicationPanelPointerTarget>? CapturePanel(
        ApplicationPanelFrame? panel,
        ConsoleViewport viewport)
    {
        if (panel is null)
            return null;

        var items = panel.VisibleItems
            .Where(item => IsVisible(item.Bounds, viewport))
            .Select(item => new RoutedPointerItem<ApplicationPanelPointerTarget>(new(item), item.Bounds))
            .ToList();

        // The left border of the right panel is the shared separator and remains clickable as its first column.
        if (panel.Side == PanelSide.Right)
        {
            foreach (ApplicationPanelItemHit item in panel.VisibleItems.Where(item =>
                         item.Bounds.X == panel.Bounds.X + 1 && IsVisible(item.Bounds, viewport)))
            {
                items.Add(new RoutedPointerItem<ApplicationPanelPointerTarget>(
                    new(item),
                    new Rect(panel.Bounds.X, item.Bounds.Y, 1, item.Bounds.Height)));
            }
        }

        UiHitRegion[] additionalRegions = panel.RetryBounds is { } retryBounds && IsVisible(retryBounds, viewport)
            ? [new UiHitRegion(ApplicationTargetIds.PanelRetry(panel.Side), retryBounds)]
            : [];
        var collection = new RoutedPointerCollection<ApplicationPanelPointerTarget>(
            ApplicationTargetIds.Panel(panel.Side),
            target => ApplicationTargetIds.PanelItem(panel.Side, target.Item!.ItemIndex));
        return collection.Capture(
            IsVisible(panel.Bounds, viewport) ? panel.Bounds : new Rect(0, 0, 0, 0),
            items,
            additionalRegions);
    }

    private static RoutedPointerSnapshot<ApplicationQuickViewPointerHit>? CaptureQuickView(
        ApplicationQuickViewFrame? frame,
        ConsoleViewport viewport)
    {
        if (frame is null)
            return null;

        IReadOnlyList<ApplicationQuickViewPointerHit> pointerHits = frame.RecentChanges is null
            ? frame.PointerHits
            : frame.MonitorToggleBounds is { } monitorToggle
                ? [new ApplicationQuickViewPointerHit(monitorToggle, new ApplicationQuickViewMonitorToggleTarget())]
                : [];
        var collection = new RoutedPointerCollection<ApplicationQuickViewPointerHit>(
            new UiTargetId("application.quick-view"),
            hit => hit.Target switch
            {
                ApplicationQuickViewMonitorToggleTarget => ApplicationTargetIds.QuickViewMonitorToggle,
                ApplicationQuickViewChangeTarget change => ApplicationTargetIds.QuickViewChange(change.ChangeId),
                _ => throw new InvalidOperationException("Unknown Quick View pointer target."),
            });
        return collection.Capture(
            IsVisible(frame.Bounds, viewport) ? frame.Bounds : new Rect(0, 0, 0, 0),
            pointerHits
                .Where(hit => IsVisible(hit.Bounds, viewport))
                .Select(hit => new RoutedPointerItem<ApplicationQuickViewPointerHit>(hit, hit.Bounds))
                .ToArray());
    }

    private static RoutedPointerSnapshot<ApplicationFileUsageOwnerHit>? CaptureFileUsage(
        ApplicationFileUsageFrame? frame,
        ConsoleViewport viewport)
    {
        if (frame is null)
            return null;

        var collection = new RoutedPointerCollection<ApplicationFileUsageOwnerHit>(
            new UiTargetId("application.file-usage"),
            hit => ApplicationTargetIds.FileUsageOwner(hit.OwnerIndex));
        return collection.Capture(
            IsVisible(frame.Bounds, viewport) ? frame.Bounds : new Rect(0, 0, 0, 0),
            frame.OwnerHits
                .Where(hit => IsVisible(hit.Bounds, viewport))
                .Select(hit => new RoutedPointerItem<ApplicationFileUsageOwnerHit>(hit, hit.Bounds))
                .ToArray());
    }

    private static RoutedPointerSnapshot<ApplicationFunctionKeyHit>? CaptureFunctionKeyBar(
        ApplicationFunctionKeyBarFrame? frame,
        ConsoleViewport viewport)
    {
        if (frame is null)
            return null;

        var collection = new RoutedPointerCollection<ApplicationFunctionKeyHit>(
            new UiTargetId("application.function-key-bar"),
            action => ApplicationTargetIds.FunctionKeyAction(action.Layer, action.Key));
        return collection.Capture(
            new Rect(0, 0, 0, 0),
            frame.Actions
                .Where(action => IsVisible(action.Bounds, viewport))
                .Select(action => new RoutedPointerItem<ApplicationFunctionKeyHit>(action, action.Bounds))
                .ToArray());
    }

    private static RoutedPointerSnapshot<ApplicationDirectoryShortcutHit>? CaptureDirectoryShortcuts(
        ApplicationDirectoryShortcutBarFrame? frame,
        ConsoleViewport viewport)
    {
        if (frame is null)
            return null;

        var collection = new RoutedPointerCollection<ApplicationDirectoryShortcutHit>(
            new UiTargetId("application.directory-shortcut-bar"),
            shortcut => ApplicationTargetIds.DirectoryShortcut(shortcut.ShortcutNumber));
        return collection.Capture(
            new Rect(0, 0, 0, 0),
            frame.Shortcuts
                .Where(shortcut => IsVisible(shortcut.Bounds, viewport))
                .Select(shortcut => new RoutedPointerItem<ApplicationDirectoryShortcutHit>(shortcut, shortcut.Bounds))
                .ToArray());
    }

    private static bool IsVisible(Rect bounds, ConsoleViewport viewport) =>
        bounds.Width > 0 &&
        bounds.Height > 0 &&
        bounds.Right > 0 &&
        bounds.Bottom > 0 &&
        bounds.X < viewport.Width &&
        bounds.Y < viewport.Height;
}
