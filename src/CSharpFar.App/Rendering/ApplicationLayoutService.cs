using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class ApplicationLayoutService
{
    public static int PanelHeight(ConsoleSize size) => Math.Max(0, size.Height - 2);

    public static int CommandLineRow(ConsoleSize size) => Math.Max(0, size.Height - 2);

    public static PanelWorkspaceRenderBounds CalculatePanelWorkspaceBounds(ConsoleSize size)
    {
        int panelHeight = PanelHeight(size);
        int leftWidth = size.Width / 2;
        int rightWidth = size.Width - leftWidth;
        return new PanelWorkspaceRenderBounds(
            new Rect(0, 0, leftWidth, panelHeight),
            new Rect(leftWidth, 0, rightWidth, panelHeight),
            panelHeight);
    }
}

internal readonly record struct PanelWorkspaceRenderBounds(
    Rect Left,
    Rect Right,
    int PanelHeight);
