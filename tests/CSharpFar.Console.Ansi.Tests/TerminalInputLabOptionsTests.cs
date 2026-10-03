namespace CSharpFar.Console.Ansi.Tests;

public sealed class TerminalInputLabOptionsTests
{
    [Fact]
    public void TryParse_DefaultsToProductionEnhancedProfile()
    {
        Assert.True(TerminalInputLabOptions.TryParse([], out var options, out string? error));

        Assert.Null(error);
        Assert.True(options.EnhancedKeyboard);
        Assert.Equal(50, options.EscapeTimeoutMilliseconds);
        Assert.Equal(5, options.StepSeconds);
    }

    [Fact]
    public void TryParse_SupportsExistingDiagnosticOptions()
    {
        Assert.True(TerminalInputLabOptions.TryParse(
            ["--manual", "--mouse-all-motion", "--no-enhanced-keyboard", "--escape-timeout-ms", "80", "--step-seconds", "7"],
            out var options,
            out string? error));

        Assert.Null(error);
        Assert.True(options.MouseAllMotion);
        Assert.False(options.EnhancedKeyboard);
        Assert.Equal(80, options.EscapeTimeoutMilliseconds);
        Assert.Equal(7, options.StepSeconds);
    }

    [Fact]
    public void TryParse_RejectsUnknownOption()
    {
        Assert.False(TerminalInputLabOptions.TryParse(
            ["--unknown"],
            out _,
            out string? error));

        Assert.Contains("--unknown", error, StringComparison.Ordinal);
    }
}
