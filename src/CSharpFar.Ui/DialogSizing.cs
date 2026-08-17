using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

[Flags]
public enum DialogResizeMode
{
    None = 0,
    Width = 1,
    Height = 2,
    Both = Width | Height,
}

/// <summary>Resolves a modal dialog's requested outer size for one viewport.</summary>
public static class DialogSizing
{
    public static (int Width, int Height) Resolve(
        ConsoleSize viewport,
        int preferredWidth,
        int preferredHeight,
        DialogResizeMode resizeMode = DialogResizeMode.None,
        int horizontalMargin = 2,
        int verticalMargin = 1)
    {
        int width = ResolveAxis(
            preferredWidth,
            viewport.Width,
            resizeMode.HasFlag(DialogResizeMode.Width),
            horizontalMargin);
        int height = ResolveAxis(
            preferredHeight,
            viewport.Height,
            resizeMode.HasFlag(DialogResizeMode.Height),
            verticalMargin);
        return (width, height);
    }

    private static int ResolveAxis(int preferred, int viewport, bool expands, int margin)
    {
        int safePreferred = Math.Max(0, preferred);
        if (!expands)
            return safePreferred;

        int available = Math.Max(0, viewport - Math.Max(0, margin) * 2);
        return Math.Max(safePreferred, available);
    }
}
