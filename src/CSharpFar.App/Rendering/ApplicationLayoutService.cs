using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class ApplicationLayoutService
{
    public static int PanelHeight(ConsoleSize size) => Math.Max(0, size.Height - 2);

    public static int CommandLineRow(ConsoleSize size) => Math.Max(0, size.Height - 2);
}
