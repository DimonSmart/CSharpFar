using System.Diagnostics;
using CSharpFar.App.CommandLine;

namespace CSharpFar.Tests;

public sealed class EnvironmentVariableCommandExecutorTests
{
    private static readonly object EnvironmentLock = new();

    [Theory]
    [InlineData("set TEST=value", "TEST", "value")]
    [InlineData("  SET TEST=value", "TEST", "value")]
    [InlineData("set \"TEST=value with spaces\"", "TEST", "value with spaces")]
    [InlineData("set TEST=a=b=c", "TEST", "a=b=c")]
    public void TryExecuteSetsProcessEnvironmentVariable(string command, string name, string expectedValue)
    {
        if (!OperatingSystem.IsWindows())
            return;

        ExecuteWithRestoredVariable(name, () =>
        {
            var executor = new EnvironmentVariableCommandExecutor();

            Assert.True(executor.TryExecute(command));
            Assert.Equal(expectedValue, Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
        });
    }

    [Fact]
    public void TryExecuteReplacesAndRemovesProcessEnvironmentVariable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ExecuteWithRestoredVariable("TEST", () =>
        {
            var executor = new EnvironmentVariableCommandExecutor();

            Assert.True(executor.TryExecute("set TEST=value"));
            Assert.True(executor.TryExecute("set TEST=new-value"));
            Assert.Equal("new-value", Environment.GetEnvironmentVariable("TEST", EnvironmentVariableTarget.Process));

            Assert.True(executor.TryExecute("set TEST="));
            Assert.Null(Environment.GetEnvironmentVariable("TEST", EnvironmentVariableTarget.Process));
        });
    }

    [Theory]
    [InlineData("set")]
    [InlineData("set TEST")]
    [InlineData("set /a X=1")]
    [InlineData("set /p X=")]
    [InlineData("set =value")]
    [InlineData("set TEST=value & echo unexpected")]
    public void TryExecuteLeavesNonAssignmentsForTheShell(string command)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var executor = new EnvironmentVariableCommandExecutor();

        Assert.False(executor.TryExecute(command));
    }

    [Fact]
    public void AssignmentIsInheritedBySubsequentCommandProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string name = "TEST_CSHARPFAR_ENVIRONMENT";
        ExecuteWithRestoredVariable(name, () =>
        {
            var executor = new EnvironmentVariableCommandExecutor();
            Assert.True(executor.TryExecute($"set {name}=hello"));

            using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c echo %{name}%")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            })!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("hello", output.Trim());
        });
    }

    private static void ExecuteWithRestoredVariable(string name, Action action)
    {
        lock (EnvironmentLock)
        {
            string? originalValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            try
            {
                action();
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, originalValue, EnvironmentVariableTarget.Process);
            }
        }
    }
}
