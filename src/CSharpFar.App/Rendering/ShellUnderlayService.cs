using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ShellUnderlayService
{
    private readonly ScreenRenderer _screen;
    private ScreenSnapshot? _underlay;
    private HiddenOverlay? _hiddenOverlay;

    public ShellUnderlayService(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public ConsoleViewport? CapturedViewport => _underlay?.Viewport;

    public void Capture()
    {
        RemoveHiddenOverlay();
        var viewport = _screen.GetViewport();
        HiddenResizeTrace.Write($"ShellUnderlay.Capture viewport={HiddenResizeTrace.Viewport(viewport)}");
        _underlay = _screen.Capture(new Rect(0, 0, viewport.Width, viewport.Height));
    }

    public void ApplyLegacyConsoleScrollbackMode(bool isPanelsMode) =>
        _screen.SetConsoleScrollbackEnabled(!isPanelsMode);

    public void RestoreForHiddenScreen(bool isPanelsMode)
    {
        ApplyLegacyConsoleScrollbackMode(isPanelsMode);
        _screen.SetRenderingOutputMode(false);
        RemoveHiddenOverlay();
        RestoreOrClearVisibleArea();
    }

    public void RestoreOrClearVisibleArea()
    {
        RemoveHiddenOverlay();

        if (_underlay is null)
        {
            _screen.ClearScreen();
            return;
        }

        var underlay = CreateUnderlaySnapshotForCurrentViewport(_underlay);
        _screen.ClearScreen();
        if (underlay is not null)
            _screen.Restore(underlay);
    }

    public void PrepareHiddenOverlay(ConsoleViewport viewport, Rect bounds)
    {
        int left = Math.Clamp(bounds.X, 0, viewport.Width);
        int top = Math.Clamp(bounds.Y, 0, viewport.Height);
        int right = Math.Clamp(bounds.Right, 0, viewport.Width);
        int bottom = Math.Clamp(bounds.Bottom, 0, viewport.Height);
        bounds = new Rect(left, top, right - left, bottom - top);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        HiddenResizeTrace.Write(
            $"Overlay.Prepare requested viewport={HiddenResizeTrace.Viewport(viewport)} bounds={bounds}");

        if (_hiddenOverlay is { } current &&
            current.Viewport == viewport &&
            current.Bounds.Equals(bounds))
        {
            HiddenResizeTrace.Write("Overlay.Prepare reuse");
            return;
        }

        RemoveHiddenOverlay();

        var snapshot = _screen.Capture(bounds);
        HiddenResizeTrace.Write(
            $"Overlay.Prepare captured snapshotViewport={HiddenResizeTrace.Viewport(snapshot.Viewport)} bounds={bounds}");
        _hiddenOverlay = new HiddenOverlay(viewport, bounds, snapshot);
    }

    public void RemoveHiddenOverlay()
    {
        if (_hiddenOverlay is not { } overlay)
            return;

        _hiddenOverlay = null;

        var viewport = _screen.GetViewport();
        HiddenResizeTrace.Write(
            $"Overlay.Remove current={HiddenResizeTrace.Viewport(viewport)} overlayViewport={HiddenResizeTrace.Viewport(overlay.Viewport)} overlayBounds={overlay.Bounds}");

        if (overlay.Underlay is not null)
        {
            var underlay = CreateOverlayUnderlayForCurrentViewport(overlay, viewport);
            if (underlay is not null)
            {
                HiddenResizeTrace.Write(
                    $"Overlay.Remove restore bounds={underlay.Region}");
                _screen.Restore(underlay);
            }
            else
            {
                HiddenResizeTrace.Write("Overlay.Remove discarded stale viewport");
            }
        }
    }

    private static ScreenSnapshot? CreateOverlayUnderlayForCurrentViewport(
        HiddenOverlay overlay,
        ConsoleViewport viewport)
    {
        var underlay = overlay.Underlay;
        if (underlay is null)
            return null;

        if (overlay.Viewport.Left != viewport.Left || overlay.Viewport.Top != viewport.Top)
            return null;

        int x = Math.Max(0, underlay.Region.X);
        int y = Math.Max(0, underlay.Region.Y);
        int right = Math.Min(viewport.Width, underlay.Region.Right);
        int bottom = Math.Min(viewport.Height, underlay.Region.Bottom);
        int width = right - x;
        int height = bottom - y;
        if (width <= 0)
            return null;

        if (height <= 0)
            return null;

        var cells = new SnapshotCell[height, width];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
                cells[row, col] = underlay.Cells[y - underlay.Region.Y + row, x - underlay.Region.X + col];
        }

        return new ScreenSnapshot(viewport, new Rect(x, y, width, height), cells);
    }

    private ScreenSnapshot? CreateUnderlaySnapshotForCurrentViewport(ScreenSnapshot underlay)
    {
        var viewport = _screen.GetViewport();
        int x = Math.Max(0, underlay.Region.X);
        int y = Math.Max(0, underlay.Region.Y);
        int right = Math.Min(viewport.Width, underlay.Region.Right);
        int bottom = Math.Min(viewport.Height, underlay.Region.Bottom);
        int width = right - x;
        int height = bottom - y;
        if (width <= 0 || height <= 0)
            return null;

        var cells = new SnapshotCell[height, width];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
                cells[row, col] = underlay.Cells[y - underlay.Region.Y + row, x - underlay.Region.X + col];
        }

        return new ScreenSnapshot(viewport, new Rect(x, y, width, height), cells);
    }

    private sealed record HiddenOverlay(
        ConsoleViewport Viewport,
        Rect Bounds,
        ScreenSnapshot? Underlay);
}
