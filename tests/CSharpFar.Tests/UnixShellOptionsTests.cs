using System.Diagnostics;
using CSharpFar.Shell;

namespace CSharpFar.Tests;

#pragma warning disable CA1416

public sealed class UnixShellOptionsTests
{
    [Theory]
    [InlineData("-c", new[] { "-c" })]
    [InlineData("-lc", new[] { "-lc" })]
    [InlineData("-lic", new[] { "-lic" })]
    [InlineData("-l -c", new[] { "-l", "-c" })]
    [InlineData("-c {0}", new[] { "-c" })]
    [InlineData("-lc {0}", new[] { "-lc" })]
    [InlineData("-l -c {0}", new[] { "-l", "-c" })]
    public void UnixOptionsArePreserved(string format, string[] expected)
    {
        const string command = "echo 'é hello'; printf '$HOME && space'\n echo done";
        var info = new UnixShellCommandLineBuilder("/bin/sh", format).CreateStartInfo(command, "/tmp");

        Assert.Equal(expected.Append(command), info.ArgumentList);
        Assert.True(string.IsNullOrEmpty(info.Arguments));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyFormatDefaultsToDashC(string? format)
    {
        var info = new UnixShellCommandLineBuilder("/bin/sh", format).CreateStartInfo("echo ok", "/tmp");
        Assert.Equal(["-c", "echo ok"], info.ArgumentList);
    }

    [Theory]
    [InlineData("-l")]
    [InlineData("-cc")]
    [InlineData("-c -c")]
    [InlineData("-c; echo pwn")]
    [InlineData("-c foo")]
    [InlineData("-c prefix{0}")]
    [InlineData("-c {0} extra")]
    [InlineData("-c -l")]
    [InlineData("-o pipefail -c")]
    [InlineData("{0}")]
    [InlineData("-c \"{0}\"")]
    public void InvalidFormatCannotSupplyAdditionalShellArguments(string format)
    {
        Assert.False(UnixShellArguments.TryParse(format, out _));
        var info = new UnixShellCommandLineBuilder("/bin/sh", format)
            .CreateStartInfo("printf safe", "/tmp");
        Assert.Equal(["-c", "printf safe"], info.ArgumentList);
    }

    [Fact]
    public void LegacyConstructorAcceptsLoginShellWithoutChoosingWindows()
    {
        var service = new ShellService("/bin/sh", "-lc");
        var field = typeof(ShellService).GetField("_commandLineBuilder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsType<UnixShellCommandLineBuilder>(field!.GetValue(service));
    }

    [Fact]
    public void LegacyWindowsConstructorKeepsWindowsBuilder()
    {
        var service = new ShellService("cmd.exe", "/c {0}");
        var field = typeof(ShellService).GetField("_commandLineBuilder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsType<WindowsShellCommandLineBuilder>(field!.GetValue(service));
    }

    [Fact]
    public void LinuxStyleBuilderDoesNotOverridePath()
    {
        var info = new UnixShellCommandLineBuilder("/bin/sh", "-lc").CreateStartInfo("true", "/tmp");
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), info.Environment["PATH"]);
    }

    [UnixFact]
    public void ShellReceivesCommandAsOneArgumentIncludingUnicodeQuotesAndMetacharacters()
    {
        const string command = "printf '%s\\n' 'é spaces && $HOME' ; printf '%s' 'done'";
        var info = new UnixShellCommandLineBuilder("/bin/sh", "-c").CreateStartInfo(command, "/tmp");
        info.RedirectStandardOutput = true;
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("é spaces && $HOME\ndone", output);
    }

    [UnixFact]
    public void ShellAndPowerShellResolveThroughSameEffectivePath()
    {
        string root = Path.Combine(Path.GetTempPath(), "CSharpFarShellPath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string tool = Path.Combine(root, "my-tool");
            string shell = Path.Combine(root, "customsh");
            string pwsh = Path.Combine(root, "pwsh");
            File.WriteAllText(tool, "#!/bin/sh\nprintf 'cli-found'");
            File.WriteAllText(shell, "#!/bin/sh\nexec /bin/sh \"$@\"");
            File.WriteAllText(pwsh, "#!/bin/sh\nprintf '%s' \"$PATH\"");
            foreach (string executable in new[] { tool, shell, pwsh })
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var provider = new MacOsExecutablePathProvider("appendStandard", () => "/csharpfar-test-nonexistent",
                Directory.Exists, [root]);
            var shellBuilder = new UnixShellCommandLineBuilder("customsh", "-c", provider.GetPath);
            var shellInfo = shellBuilder.CreateStartInfo("my-tool", root);
            Assert.Equal(shell, shellInfo.FileName);
            Assert.Equal(provider.GetPath(), shellInfo.Environment["PATH"]);
            shellInfo.RedirectStandardOutput = true;
            using (Process process = Process.Start(shellInfo)!)
            {
                Assert.Equal("cli-found", process.StandardOutput.ReadToEnd());
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }

            var powerShellBuilder = new PowerShellCommandLineBuilder(null, ["pwsh"], provider.GetPath);
            var info = powerShellBuilder.CreateStartInfo("Get-Date", root);
            Assert.Equal(pwsh, info.FileName);
            Assert.Equal(provider.GetPath(), info.Environment["PATH"]);
            info.RedirectStandardOutput = true;
            using Process pwshProcess = Process.Start(info)!;
            Assert.Equal(provider.GetPath(), pwshProcess.StandardOutput.ReadToEnd());
            pwshProcess.WaitForExit();
            Assert.Equal(0, pwshProcess.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [UnixFact]
    public void PanelExecutablePassesEffectivePathToItsSubprocess()
    {
        string root = Path.Combine(Path.GetTempPath(), "CSharpFarPanelPath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string tool = Path.Combine(root, "child-tool");
            string script = Path.Combine(root, "parent-script");
            string output = Path.Combine(root, "result.txt");
            File.WriteAllText(tool, "#!/bin/sh\nprintf child-found");
            File.WriteAllText(script, "#!/bin/sh\nchild-tool > result.txt");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var provider = new MacOsExecutablePathProvider("appendStandard",
                () => "/usr/bin:/bin", Directory.Exists, [root]);
            var launcher = new UnixShellFileLauncher(new UnixExecutableFileDetector(),
                new UnixAssociationLauncher(new UnixEnvironment()), provider.GetPath);
            launcher.OpenFile(script, root);

            Assert.Equal("child-found", File.ReadAllText(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

#pragma warning restore CA1416
