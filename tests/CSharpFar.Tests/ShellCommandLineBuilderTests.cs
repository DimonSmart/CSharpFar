using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class ShellCommandLineBuilderTests
{
    [Fact]
    public void WindowsBuilder_UsesRawArgumentsForCmd()
    {
        string command = "echo \"hello world\" && set A=$B";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal("/d /s /c \"" + command + "\"", startInfo.Arguments);
    }

    [Fact]
    public void WindowsBuilder_PreservesQuotedCommandArguments()
    {
        string command = "git commit -m \"Initial commit\"";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal("/d /s /c \"git commit -m \"Initial commit\"\"", startInfo.Arguments);
    }

    [Fact]
    public void WindowsBuilder_DoesNotWrapWholeCommandAsExecutableName()
    {
        string command = "npm run package";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal("/d /s /c \"npm run package\"", startInfo.Arguments);
    }

    [Fact]
    public void WindowsBuilder_PreservesQuotedBatchArgument()
    {
        string command = "RunCrfClassifier.bat \"PARACETAMOL CINFA 1G 40 COMPRIMIDOS EFG\"";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal(
            "/d /s /c \"RunCrfClassifier.bat \"PARACETAMOL CINFA 1G 40 COMPRIMIDOS EFG\"\"",
            startInfo.Arguments);
    }

    [Fact]
    public void WindowsBuilder_PreservesQuotedExecutableAndQuotedArguments()
    {
        string command =
            "\"C:\\Program Files\\Git\\bin\\bash.exe\" \"E:/Work/Repository/scripts/export.sh\" \"argument with spaces\"";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal("/d /s /c \"" + command + "\"", startInfo.Arguments);
    }

    [Fact]
    public void UnixBuilder_UsesArgumentList()
    {
        string command = "printf '%s\\n' \"a b\"; echo $HOME";
        var startInfo = new UnixShellCommandLineBuilder().CreateStartInfo(command, "/tmp");

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.Equal(["-c", command], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
    }

    [Fact]
    public void PowerShellBuilder_PassesEntireScriptAsOneArgument()
    {
        const string script = "Write-Host \"hello world\"\nGet-ChildItem | Where-Object { $_.Length -gt 1MB }";
        var startInfo = new PowerShellCommandLineBuilder(() => "pwsh.exe").CreateStartInfo(script, "C:\\");

        Assert.Equal("pwsh.exe", startInfo.FileName);
        Assert.Equal(["-NoLogo", "-Command", script], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
    }

    [Fact]
    public void PowerShellBuilder_WindowsCandidatesPreferPwsh()
    {
        var builder = new PowerShellCommandLineBuilder(() => "pwsh.exe", ["pwsh.exe", "powershell.exe"]);

        var startInfo = builder.CreateStartInfo("Get-Date", "C:\\");

        Assert.Equal("pwsh.exe", startInfo.FileName);
    }

    [Fact]
    public void PowerShellBuilder_WindowsCandidatesFallBackToWindowsPowerShell()
    {
        var builder = new PowerShellCommandLineBuilder(() => "powershell.exe", ["pwsh.exe", "powershell.exe"]);

        var startInfo = builder.CreateStartInfo("Get-Date", "C:\\");

        Assert.Equal("powershell.exe", startInfo.FileName);
    }

    [Fact]
    public void PowerShellBuilder_UnixCandidatesUsePwsh()
    {
        var builder = new PowerShellCommandLineBuilder(() => "pwsh", ["pwsh"]);

        var startInfo = builder.CreateStartInfo("Get-Date", "/tmp");

        Assert.Equal("pwsh", startInfo.FileName);
    }

    [Fact]
    public void PowerShellBuilder_NotFoundReportsCandidates()
    {
        var builder = new PowerShellCommandLineBuilder(() => null, ["pwsh"]);

        var exception = Assert.Throws<FileNotFoundException>(() => builder.CreateStartInfo("Get-Date", "/tmp"));

        Assert.Equal("PowerShell executable was not found. Tried: pwsh", exception.Message);
    }

    [Theory]
    [InlineData("ps: Get-Date", "powershell", "Get-Date")]
    [InlineData("PS:\tGet-Date", "powershell", "Get-Date")]
    [InlineData("pwsh:Get-Date", "powershell", "Get-Date")]
    public void ShellInvocationParser_RecognizesRegisteredAliases(string command, string shellId, string script)
    {
        var parser = new ShellInvocationParser(new ShellRegistry(new ShellProfile("powershell", ["ps", "pwsh", "powershell"], new PowerShellCommandLineBuilder(() => "pwsh.exe"))));

        Assert.True(parser.TryParse(command, out var invocation));
        Assert.Equal(shellId, invocation.ShellId);
        Assert.Equal(script, invocation.Command);
    }

    [Theory]
    [InlineData("foo: bar")]
    [InlineData("C:\\Tools\\test.exe")]
    [InlineData("https://example.com")]
    public void ShellInvocationParser_LeavesUnknownPrefixesUntouched(string command)
    {
        var parser = new ShellInvocationParser(new ShellRegistry(new ShellProfile("powershell", ["ps", "pwsh", "powershell"], new PowerShellCommandLineBuilder(() => "pwsh.exe"))));

        Assert.False(parser.TryParse(command, out _));
    }

    [Fact]
    public void ShellRegistry_ResolvesAliasesAndCanonicalIdsIndependently()
    {
        var profile = new ShellProfile("powershell", ["ps", "pwsh"], new PowerShellCommandLineBuilder(() => "pwsh"));
        var registry = new ShellRegistry(profile);

        Assert.True(registry.TryResolveAlias("PS", out var resolvedByAlias));
        Assert.True(registry.TryGetById("PowerShell", out var resolvedById));
        Assert.Same(profile, resolvedByAlias);
        Assert.Same(profile, resolvedById);
    }
}
