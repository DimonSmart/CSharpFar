using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ShellUnderlayService
{
    private readonly ScreenRenderer _screen;
    private ScreenSnapshot? _underlay;
    private HiddenCommandLineOverlay? _hiddenCommandLineOverlay;

    public ShellUnderlayService(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public ConsoleViewport? CapturedViewport => _underlay?.Viewport;

    public void Capture()
    {
        RemoveHiddenCommandLineOverlay();
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
        RemoveHiddenCommandLineOverlay();
        RestoreOrClearVisibleArea();
    }

    public void RestoreOrClearVisibleArea()
    {
        RemoveHiddenCommandLineOverlay();

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

    public void PrepareHiddenCommandLineOverlay(ConsoleViewport viewport, int row, int width)
    {
        if (width <= 0 || row < 0 || row >= viewport.Height)
            return;

        HiddenResizeTrace.Write(
            $"Overlay.Prepare requested viewport={HiddenResizeTrace.Viewport(viewport)} row={row} width={width}");

        if (_hiddenCommandLineOverlay is { } current &&
            current.Viewport == viewport &&
            current.Row == row &&
            current.Width == width)
        {
            HiddenResizeTrace.Write("Overlay.Prepare reuse");
            return;
        }

        RemoveHiddenCommandLineOverlay();

        var snapshot = _screen.Capture(new Rect(0, row, width, 1));
        HiddenResizeTrace.Write(
            $"Overlay.Prepare captured snapshotViewport={HiddenResizeTrace.Viewport(snapshot.Viewport)} row={row}");
        _hiddenCommandLineOverlay = new HiddenCommandLineOverlay(viewport, row, width, snapshot);
    }

    public void RemoveHiddenCommandLineOverlay()
    {
        if (_hiddenCommandLineOverlay is not { } overlay)
            return;

        _hiddenCommandLineOverlay = null;

        var viewport = _screen.GetViewport();
        HiddenResizeTrace.Write(
            $"Overlay.Remove current={HiddenResizeTrace.Viewport(viewport)} overlayViewport={HiddenResizeTrace.Viewport(overlay.Viewport)} overlayRow={overlay.Row}");

        if (overlay.RowUnderlay is not null)
        {
            var rowUnderlay = CreateOverlayRowUnderlayForCurrentViewport(overlay, viewport);
            if (rowUnderlay is not null)
            {
                HiddenResizeTrace.Write(
                    $"Overlay.Remove restore row={rowUnderlay.Region.Y} width={rowUnderlay.Region.Width}");
                _screen.Restore(rowUnderlay);
            }
            else
            {
                HiddenResizeTrace.Write("Overlay.Remove skipped notInCurrentViewport");
            }
            return;
        }

        int absoluteRow = overlay.Viewport.Top + overlay.Row;
        if (absoluteRow < viewport.Top || absoluteRow > viewport.Bottom)
        {
            HiddenResizeTrace.Write("Overlay.Remove clear skipped notInCurrentViewport");
            return;
        }

        int row = absoluteRow - viewport.Top;
        HiddenResizeTrace.Write($"Overlay.Remove clear row={row}");
        _screen.ClearRegion(new Rect(0, row, Math.Min(overlay.Width, viewport.Width), 1));
    }

    private static ScreenSnapshot? CreateOverlayRowUnderlayForCurrentViewport(
        HiddenCommandLineOverlay overlay,
        ConsoleViewport viewport)
    {
        var underlay = overlay.RowUnderlay;
        if (underlay is null)
            return null;

        int absoluteRow = overlay.Viewport.Top + overlay.Row;
        if (absoluteRow < viewport.Top || absoluteRow > viewport.Bottom)
            return null;

        if (overlay.Viewport.Left != viewport.Left)
            return null;

        int row = absoluteRow - viewport.Top;
        int width = Math.Min(underlay.Region.Width, viewport.Width);
        if (width <= 0)
            return null;

        var cells = new SnapshotCell[1, width];
        for (int col = 0; col < width; col++)
            cells[0, col] = underlay.Cells[0, col];

        return new ScreenSnapshot(viewport, new Rect(0, row, width, 1), cells);
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

    private sealed record HiddenCommandLineOverlay(
        ConsoleViewport Viewport,
        int Row,
        int Width,
        ScreenSnapshot? RowUnderlay);
}
