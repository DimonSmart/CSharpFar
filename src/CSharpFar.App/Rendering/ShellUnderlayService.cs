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
        _underlay = _screen.Capture(new Rect(0, 0, viewport.Width, viewport.Height));
    }

    public void ApplyLegacyConsoleScrollbackMode(bool hasVisiblePanels) =>
        _screen.SetConsoleScrollbackEnabled(!hasVisiblePanels);

    public void RestoreForHiddenScreen(bool hasVisiblePanels)
    {
        ApplyLegacyConsoleScrollbackMode(hasVisiblePanels);
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

        if (_hiddenCommandLineOverlay is { } current &&
            current.Viewport == viewport &&
            current.Row == row &&
            current.Width == width)
        {
            return;
        }

        RemoveHiddenCommandLineOverlay();

        var snapshot = _screen.Capture(new Rect(0, row, width, 1));
        _hiddenCommandLineOverlay = new HiddenCommandLineOverlay(viewport, row, width, snapshot);
    }

    public void RemoveHiddenCommandLineOverlay()
    {
        if (_hiddenCommandLineOverlay is not { } overlay)
            return;

        _hiddenCommandLineOverlay = null;

        var viewport = _screen.GetViewport();
        if (overlay.Row < 0 || overlay.Row >= viewport.Height)
            return;

        bool sameOrigin = viewport.Left == overlay.Viewport.Left && viewport.Top == overlay.Viewport.Top;
        if (!sameOrigin)
            return;

        if (overlay.RowUnderlay is not null)
        {
            _screen.Restore(overlay.RowUnderlay);
            return;
        }

        _screen.ClearRegion(new Rect(0, overlay.Row, Math.Min(overlay.Width, viewport.Width), 1));
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
