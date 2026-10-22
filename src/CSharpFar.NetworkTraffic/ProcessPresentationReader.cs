using System.ComponentModel;
using System.Diagnostics;

namespace CSharpFar.NetworkTraffic;

internal static class ProcessPresentationReader
{
    public static ObservedProcessIdentity Read(Process process, DateTime createdUtc, ObservedProcessIdentity? previous = null)
    {
        string? path = previous?.ExecutablePath;
        string? name = previous?.ApplicationName;
        string? title = null;
        if (path is null)
        {
            try { path = process.MainModule?.FileName; }
            catch (Exception e) when (Unavailable(e)) { }
        }
        if (name is null && path is not null && (previous is null || previous.ExecutablePath is null))
        {
            try { name = Clean(FileVersionInfo.GetVersionInfo(path).FileDescription, 128); }
            catch (Exception e) when (Unavailable(e)) { }
        }
        string? processName = previous?.ProcessName;
        if (path is null)
        {
            try { processName = Clean(process.ProcessName, 128); }
            catch (Exception e) when (Unavailable(e)) { }
        }
        // The Process object is fresh on each poll. Never wait for an application's
        // input loop or infer a title from its parent or another executable.
        try { title = Clean(process.MainWindowTitle, 512); }
        catch (Exception e) when (Unavailable(e)) { }
        return new(process.Id, createdUtc, path) { ApplicationName = name, ProcessName = processName, WindowTitle = title };
    }

    private static string? Clean(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var result = new string(text.Where(c => !char.IsControl(c)).Take(limit).ToArray()).Trim();
        return result.Length == 0 ? null : result;
    }
    private static bool Unavailable(Exception error) => error is Win32Exception or InvalidOperationException
        or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
}
