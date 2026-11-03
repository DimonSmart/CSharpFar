using CSharpFar.App.Updates;

namespace CSharpFar.Tests;

public sealed class SystemProcessExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UnixChildReadingStdinReceivesEof()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new SystemProcessExecutor().ExecuteAsync(
            "/bin/cat", [], cancellation.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}
