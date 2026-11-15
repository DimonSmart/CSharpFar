using CSharpFar.App.Bootstrap;

namespace CSharpFar.Tests;

public sealed class ExternalPluginRunOptionsTests
{
    [Fact]
    public void NoExtensions_CanCombineWithDiagnostics()
    {
        Assert.True(ApplicationRunOptionsParser.TryParse(
            ["--diagnostics", "--no-extensions"], out var options, out string? error), error);
        Assert.Equal(ApplicationRunMode.Normal, options.Mode);
        Assert.True(options.DiagnosticsEnabled);
        Assert.True(options.NoExtensions);
    }

    [Fact]
    public void RepeatedNoExtensions_IsRejected()
    {
        Assert.False(ApplicationRunOptionsParser.TryParse(
            ["--no-extensions", "--no-extensions"], out _, out _));
    }
}
