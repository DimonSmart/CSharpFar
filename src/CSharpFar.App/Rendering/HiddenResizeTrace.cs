using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal static class HiddenResizeTrace
{
    private static readonly object s_gate = new();
    private static readonly string? s_path = CreatePath();

    public static bool Enabled => s_path is not null;

    public static void Write(string message)
    {
        if (s_path is null)
            return;

        try
        {
            lock (s_gate)
            {
                File.AppendAllText(
                    s_path,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never affect console rendering.
        }
    }

    public static string Viewport(ConsoleViewport viewport) =>
        $"L={viewport.Left} T={viewport.Top} W={viewport.Width} H={viewport.Height} B={viewport.Bottom}";

    private static string? CreatePath()
    {
        string path = Path.Combine(Path.GetTempPath(), "CSharpFar-hidden-resize.log");

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:O} hidden resize trace started pid={Environment.ProcessId}{Environment.NewLine}");
            return path;
        }
        catch
        {
            return null;
        }
    }
}
