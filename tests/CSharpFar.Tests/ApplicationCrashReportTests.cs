using CSharpFar.App.Diagnostics;

namespace CSharpFar.Tests;

public sealed class ApplicationCrashReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_CreatesTimestampedReportWithExceptionDetails()
    {
        string directory = Path.Combine(_directory, "crashes");

        string? path = ApplicationCrashReport.Write(new InvalidOperationException("Broken command"), directory);

        Assert.NotNull(path);
        Assert.StartsWith(Path.Combine(directory, "CSharpFar-crash-"), path, StringComparison.Ordinal);
        string report = File.ReadAllText(path);
        Assert.Contains("Broken command", report, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), report, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
