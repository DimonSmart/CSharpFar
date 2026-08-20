using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class HiddenResizeTrace
{
    public static bool Enabled => TerminalTrace.Enabled;

    public static void Write(string message)
    {
        TerminalTrace.Write("hidden-resize", message);
    }

    public static string Viewport(ConsoleViewport viewport) =>
        $"L={viewport.Left} T={viewport.Top} W={viewport.Width} H={viewport.Height} B={viewport.Bottom}";

}
