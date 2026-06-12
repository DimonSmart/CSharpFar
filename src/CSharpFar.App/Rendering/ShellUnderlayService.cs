using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class ShellUnderlayService
{
    private readonly ScreenRenderer _screen;
    private ScreenSnapshot? _underlay;

    public ShellUnderlayService(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public ConsoleViewport? CapturedViewport => _underlay?.Viewport;

    public void Capture()
    {
        var viewport = _screen.GetViewport();
        _underlay = _screen.Capture(new Rect(0, 0, viewport.Width, viewport.Height));
    }

    public void ApplyLegacyConsoleScrollbackMode(bool hasVisiblePanels) =>
        _screen.SetConsoleScrollbackEnabled(!hasVisiblePanels);

    public void RestoreForHiddenScreen(bool hasVisiblePanels)
    {
        ApplyLegacyConsoleScrollbackMode(hasVisiblePanels);
        _screen.SetRenderingOutputMode(false);
        RestoreOrClearVisibleArea();
    }

    public void RestoreOrClearVisibleArea()
    {
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
}
