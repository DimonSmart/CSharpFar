namespace CSharpFar.Console;

/// <summary>Optional low-cost trace for terminal rendering and resize diagnostics.</summary>
public static class TerminalTrace
{
    private static readonly object s_gate = new();
    private static readonly string? s_path = Environment.GetEnvironmentVariable("CSHARPFAR_HIDDEN_RESIZE_TRACE");

    public static bool Enabled => !string.IsNullOrWhiteSpace(s_path);

    public static void Write(string source, string message)
    {
        if (s_path is null)
            return;

        try
        {
            lock (s_gate)
            {
                string? directory = Path.GetDirectoryName(s_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(
                    s_path,
                    $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} thread={Environment.CurrentManagedThreadId} {source} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never affect console rendering.
        }
    }
}
