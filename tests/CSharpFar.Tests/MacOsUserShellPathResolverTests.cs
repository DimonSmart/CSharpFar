using System.Diagnostics;
using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class MacOsUserShellPathResolverTests
{
    private static MacOsUserShellPathResult Probe(
        string? path, string banner = "", bool success = true, string? shell = "/bin/zsh")
    {
        var resolver = new MacOsUserShellPathResolver(
            () => shell,
            (info, start, end) =>
            {
                Assert.Equal(shell, info.FileName);
                Assert.Equal("-lic", info.ArgumentList[0]);
                Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput &&
                            info.RedirectStandardError && !info.UseShellExecute);
                return new(success,
                    banner + "\n" + start + "\n" + path + "\n" + end + "\n",
                    success ? null : "test shell failed");
            });
        return resolver.Resolve();
    }

    [Theory]
    [InlineData("/custom/bin:/usr/bin", "banner\nhello")]
    [InlineData("/tmp/space dir:/tmp/日本語", "startup plugin text")]
    public void IgnoresNoisyStartupAndPreservesPath(string path, string banner)
    {
        MacOsUserShellPathResult result = Probe(path, banner);
        Assert.True(result.Success);
        Assert.Equal(path, result.Path);
        Assert.Equal("/bin/zsh", result.Shell);
    }

    [Fact]
    public void SupportsBashLoginInteractive()
    {
        Assert.True(Probe("/custom/bash", shell: "/bin/bash").Success);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(":")]
    [InlineData("/usr/bin\nmalformed")]
    public void InvalidPathIsRejected(string? path)
    {
        Assert.False(Probe(path).Success);
    }

    [Fact]
    public void NonzeroExitDiscardsOtherwiseValidOutput()
    {
        Assert.False(Probe("/good/path", success: false).Success);
    }

    [Fact]
    public void MissingMarkersAreRejected()
    {
        var resolver = new MacOsUserShellPathResolver(() => "/bin/zsh",
            (_, _, _) => new(true, "PATH=/bin\n", null));
        Assert.False(resolver.Resolve().Success);
    }

    [Fact]
    public void UnsupportedShellDoesNotStartProcess()
    {
        var resolver = new MacOsUserShellPathResolver(() => "/bin/fish",
            (_, _, _) => throw new Exception("must not run"));
        Assert.Contains("unsupported", resolver.Resolve().FailureReason);
    }

    [Fact]
    public void ProcessRunnerFailureIsSafe()
    {
        var resolver = new MacOsUserShellPathResolver(() => "/bin/zsh",
            (_, _, _) => throw new IOException("test exception"));
        MacOsUserShellPathResult result = resolver.Resolve();
        Assert.False(result.Success);
        Assert.Contains("IOException", result.FailureReason);
    }

    [UnixFact]
    public void HungProcessIsCancelledAndDoesNotReturnPartialPath()
    {
        var info = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("printf 'partial'; sleep 30");
        MacOsShellProbeResult probe = MacOsUserShellPathResolver.RunProbe(info, "start", "end");
        Assert.False(probe.Success);
        Assert.Null(probe.Output);
        Assert.Contains("timed out", probe.FailureReason);
    }

    [Fact]
    public void LoginAndInteractiveZshReadIsolatedStartupFiles()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        string root = Path.Combine(Path.GetTempPath(), "CSharpFar-zdotdir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string file in new[] { ".zprofile", ".zshrc" })
            {
                File.WriteAllText(Path.Combine(root, file),
                    "export PATH=\"/tmp/csharpfar-" + file.TrimStart('.') + ":$PATH\"\n");
            }
            var resolver = new MacOsUserShellPathResolver(() => "/bin/zsh",
                (info, start, end) =>
                {
                    info.Environment["ZDOTDIR"] = root;
                    return MacOsUserShellPathResolver.RunProbe(info, start, end);
                });

            MacOsUserShellPathResult result = resolver.Resolve();
            Assert.True(result.Success, result.FailureReason);
            Assert.Contains("/tmp/csharpfar-zprofile", result.Path);
            Assert.Contains("/tmp/csharpfar-zshrc", result.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
