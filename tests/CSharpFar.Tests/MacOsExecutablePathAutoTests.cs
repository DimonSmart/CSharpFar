using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class MacOsExecutablePathAutoTests
{
    private const string UserPath = "/tools with spaces:/工具:/bin:/tools with spaces";

    private static MacOsExecutablePathProvider Provider(
        string? mode, bool finder, Func<MacOsUserShellPathResult> resolver,
        string inherited = "/bin:/usr/bin:/usr/bin") =>
        new(mode, () => inherited, _ => true,
            ["/opt/homebrew/bin", "/bin", "/opt/homebrew/bin"],
            resolveUserPath: resolver, launchedFromFinder: () => finder);

    [Fact]
    public void FinderUsesUserPriorityThenInheritedThenHomebrewWithoutDuplicates()
    {
        var provider = Provider("auto", true,
            () => new(UserPath, "/bin/zsh", true, null, 25));
        Assert.Equal("/tools with spaces:/工具:/bin:/usr/bin:/opt/homebrew/bin", provider.GetPath());
        MacOsPathDiagnostics details = provider.GetDiagnostics();
        Assert.Equal("user shell", details.Source);
        Assert.Equal("/bin/zsh", details.Shell);
        Assert.Equal(25, details.ElapsedMilliseconds);
        Assert.True(details.ShellSucceeded);
    }

    [Fact]
    public void NoProbeForDirectCliEvenInAutoMode()
    {
        var provider = Provider(null, false, () => throw new Exception("probe should not run"));
        Assert.Equal("/bin:/usr/bin:/usr/bin:/opt/homebrew/bin", provider.GetPath());
        Assert.Equal("inherited", provider.GetDiagnostics().Source);
    }

    [Theory]
    [InlineData("appendStandard")]
    [InlineData("inherited")]
    public void ExplicitOptOutDoesNotStartShell(string mode)
    {
        var provider = Provider(mode, true, () => throw new Exception("probe should not run"));
        Assert.Equal(mode == "inherited" ? null : "/bin:/usr/bin:/usr/bin:/opt/homebrew/bin",
            provider.GetPath());
    }

    [Fact]
    public void FailedProbeFallsBackToLegacyAppendStandard()
    {
        var provider = Provider("auto", true,
            () => new(null, "/bin/bash", false, "shell startup timed out", 3000));
        Assert.Equal("/bin:/usr/bin:/usr/bin:/opt/homebrew/bin", provider.GetPath());
        Assert.Equal("fallback", provider.GetDiagnostics().Source);
        Assert.Equal("shell startup timed out", provider.GetDiagnostics().FallbackReason);
        Assert.False(provider.GetDiagnostics().ShellSucceeded);
    }

    [Fact]
    public async Task ParallelCallsShareExactlyOneResolutionAndSnapshot()
    {
        int calls = 0;
        var provider = Provider("auto", true, () =>
        {
            Interlocked.Increment(ref calls);
            return new("/custom:/bin", "/bin/zsh", true, null, 10);
        });
        string?[] paths = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
            Task.Run(provider.GetPath)));
        Assert.All(paths, p => Assert.Equal("/custom:/bin:/usr/bin:/opt/homebrew/bin", p));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DiagnosticsBeforeResolutionNeverStartsShell()
    {
        int calls = 0;
        var provider = Provider("auto", true, () =>
        {
            calls++;
            return new("/bin", "/bin/zsh", true, null, 1);
        });
        Assert.Equal("not initialized", provider.GetDiagnostics().Source);
        Assert.Equal(0, calls);
        _ = provider.GetPath();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EmptyUserSegmentsAreRemovedInAutoOnly()
    {
        var provider = Provider("auto", true,
            () => new(":/custom::/bin:", "/bin/zsh", true, null, 1),
            inherited: ":/usr/bin::");
        Assert.Equal("/custom:/bin:/usr/bin:/opt/homebrew/bin", provider.GetPath());
    }

    [Fact]
    public void SamePathFlowsToShellPowerShellAndPanelLauncher()
    {
        string root = Path.Combine(Path.GetTempPath(), "CSharpFar-auto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string executable = Path.Combine(root, "pwsh");
            File.WriteAllText(executable, "fake");
            var provider = new MacOsExecutablePathProvider("auto", () => "/bin",
                _ => false, [], resolveUserPath: () =>
                    new(root + ":/bin", "/bin/zsh", true, null, 1),
                launchedFromFinder: () => true);
            var unix = new UnixShellCommandLineBuilder("/bin/sh", "-c", provider.GetPath)
                .CreateStartInfo("pwd", root);
            Assert.Equal(["-c", "pwd"], unix.ArgumentList);
            Assert.Equal(provider.GetPath(), unix.Environment["PATH"]);
            Assert.Equal("/bin/sh", unix.FileName);

            var powerShell = new PowerShellCommandLineBuilder(() => executable,
                ["pwsh"], provider.GetPath).CreateStartInfo("Get-Date", root);
            Assert.Equal(provider.GetPath(), powerShell.Environment["PATH"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
