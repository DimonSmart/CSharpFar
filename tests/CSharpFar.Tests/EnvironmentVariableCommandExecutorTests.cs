using System.Diagnostics;
using CSharpFar.App.CommandLine;

namespace CSharpFar.Tests;

public sealed class EnvironmentVariableCommandExecutorTests
{
    private static readonly object EnvironmentLock = new();

    [Theory]
    [InlineData("set TEST=gpt-5.6-luna", "TEST", "gpt-5.6-luna")]
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

    [Fact]
    public void TryExecuteExpandsVariablesFromTheCurrentProcessEnvironment()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string baseName = "CSHARPFAR_TEST_BASE";
        const string testName = "CSHARPFAR_TEST_EXPANDED";
        ExecuteWithRestoredVariables([baseName, testName], () =>
        {
            var executor = new EnvironmentVariableCommandExecutor();

            Assert.True(executor.TryExecute($"set {baseName}=C:\\Tools"));
            Assert.True(executor.TryExecute($"set {testName}=%{baseName}%\\bin"));

            Assert.Equal("C:\\Tools\\bin", Environment.GetEnvironmentVariable(testName, EnvironmentVariableTarget.Process));
        });
    }

    [Theory]
    [InlineData("USERPROFILE", ".dotnet")]
    [InlineData("PATH", ";C:\\Tools")]
    public void TryExecuteExpandsExistingProcessEnvironmentVariable(string sourceName, string suffix)
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string testName = "CSHARPFAR_TEST_EXPANDED_EXISTING";
        ExecuteWithRestoredVariable(testName, () =>
        {
            string sourceValue = Environment.GetEnvironmentVariable(sourceName, EnvironmentVariableTarget.Process)!;
            var executor = new EnvironmentVariableCommandExecutor();

            Assert.True(executor.TryExecute($"set {testName}=%{sourceName}%{suffix}"));

            Assert.Equal(sourceValue + suffix, Environment.GetEnvironmentVariable(testName, EnvironmentVariableTarget.Process));
        });
    }

    [Fact]
    public void TryExecuteExpandsThePreviousValueBeforeReplacingTheTargetVariable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string name = "CSHARPFAR_TEST_SELF_REFERENCE";
        ExecuteWithRestoredVariable(name, () =>
        {
            var executor = new EnvironmentVariableCommandExecutor();
            Assert.True(executor.TryExecute($"set {name}=old"));
            Assert.True(executor.TryExecute($"set {name}=%{name}%-new"));

            Assert.Equal("old-new", Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
        });
    }

    [Fact]
    public void TryExecuteExpandsVariablesInQuotedAssignments()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string name = "CSHARPFAR_TEST_QUOTED_EXPANSION";
        ExecuteWithRestoredVariable(name, () =>
        {
            string temp = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Process)!;
            var executor = new EnvironmentVariableCommandExecutor();

            Assert.True(executor.TryExecute($"set \"{name}=%TEMP%\\folder with spaces\""));

            Assert.Equal($"{temp}\\folder with spaces", Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
        });
    }

    [Fact]
    public void TryExecutePreservesUnknownVariableReferencesLikeCmd()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string name = "CSHARPFAR_TEST_UNKNOWN_EXPANSION";
        const string unknownName = "CSHARPFAR_VARIABLE_THAT_DOES_NOT_EXIST";
        ExecuteWithRestoredVariables([name, unknownName], () =>
        {
            Environment.SetEnvironmentVariable(unknownName, null, EnvironmentVariableTarget.Process);
            var executor = new EnvironmentVariableCommandExecutor();

            Assert.True(executor.TryExecute($"set {name}=%{unknownName}%"));

            Assert.Equal($"%{unknownName}%", Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
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
        ExecuteWithRestoredVariables([name], action);
    }

    private static void ExecuteWithRestoredVariables(string[] names, Action action)
    {
        lock (EnvironmentLock)
        {
            Dictionary<string, string?> originalValues = names.ToDictionary(
                name => name,
                name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process),
                StringComparer.OrdinalIgnoreCase);
            try
            {
                action();
            }
            finally
            {
                foreach ((string name, string? originalValue) in originalValues)
                    Environment.SetEnvironmentVariable(name, originalValue, EnvironmentVariableTarget.Process);
            }
        }
    }
}
