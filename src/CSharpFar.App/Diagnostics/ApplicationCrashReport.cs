using System.Text;

namespace CSharpFar.App.Diagnostics;

internal static class ApplicationCrashReport
{
    private const string FileNamePrefix = "CSharpFar-crash-";

    public static string? Write(Exception exception) =>
        Write(exception, AppContext.BaseDirectory);

    internal static string? Write(Exception exception, string directory)
    {
        try
        {
            string fileName = $"{FileNamePrefix}{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}.log";
            string path = Path.Combine(directory, fileName);
            string report = $"CSharpFar unexpected error ({DateTimeOffset.UtcNow:O}){Environment.NewLine}{Environment.NewLine}{exception}";
            File.WriteAllText(path, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
