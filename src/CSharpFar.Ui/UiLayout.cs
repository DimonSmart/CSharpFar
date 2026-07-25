using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct VerticalLayoutSplit(Rect Body, Rect Footer);

public static class UiLayout
{
    public static Rect Center(ConsoleSize viewport, int width, int height)
    {
        int constrainedWidth = Math.Clamp(width, 0, Math.Max(0, viewport.Width));
        int constrainedHeight = Math.Clamp(height, 0, Math.Max(0, viewport.Height));
        return new Rect(Math.Max(0, (viewport.Width - constrainedWidth) / 2), Math.Max(0, (viewport.Height - constrainedHeight) / 2), constrainedWidth, constrainedHeight);
    }

    public static Rect Inset(Rect bounds, int horizontal, int vertical) => Inset(bounds, horizontal, vertical, horizontal, vertical);

    public static Rect Inset(Rect bounds, int left, int top, int right, int bottom)
    {
        int safeLeft = Math.Max(0, left);
        int safeTop = Math.Max(0, top);
        int safeRight = Math.Max(0, right);
        int safeBottom = Math.Max(0, bottom);
        return new Rect(bounds.X + safeLeft, bounds.Y + safeTop, Math.Max(0, bounds.Width - safeLeft - safeRight), Math.Max(0, bounds.Height - safeTop - safeBottom));
    }

    public static VerticalLayoutSplit SplitBottom(Rect bounds, int footerHeight, int gap = 0)
    {
        int footer = Math.Clamp(footerHeight, 0, Math.Max(0, bounds.Height));
        int bodyHeight = Math.Max(0, bounds.Height - footer - Math.Max(0, gap));
        return new VerticalLayoutSplit(new Rect(bounds.X, bounds.Y, bounds.Width, bodyHeight), new Rect(bounds.X, bounds.Bottom - footer, bounds.Width, footer));
    }
}
