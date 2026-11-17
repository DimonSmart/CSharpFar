using System.Diagnostics;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class CommandTemplateTests
{
    [Fact]
    public void Parser_OrdersParameters_DeduplicatesAndKeepsDefaults()
    {
        CommandTemplate template = CommandTemplateParser.Parse(
            "ffmpeg -i \"{input: Source file }\" -crf {input:Quality=20} \"{input:Source file}\"");

        Assert.Equal(["Source file", "Quality"], template.Parameters.Select(p => p.Name));
        Assert.Null(template.Parameters[0].DefaultValue);
        Assert.Equal("20", template.Parameters[1].DefaultValue);

        string result = CommandTemplateRenderer.Expand(template,
            new Dictionary<string, string> { ["Source file"] = "a b.mov", ["Quality"] = "22" },
            static literal => literal, InputShellDialect.Unix);
        Assert.Equal("ffmpeg -i \"a b.mov\" -crf '22' \"a b.mov\"", result);
    }

    [Fact]
    public void Parser_EscapedLiteralsAreNotInterpreted()
    {
        CommandTemplate template = CommandTemplateParser.Parse(
            "echo {{input:URL}} {unknown} {input:Value=}");

        Assert.Equal("Value", Assert.Single(template.Parameters).Name);
        Assert.Equal("", template.Parameters[0].DefaultValue);
        Assert.Equal("echo {input:URL} {unknown} 'hello'", CommandTemplateRenderer.Expand(
            template, new Dictionary<string, string> { ["Value"] = "hello" },
            static literal => literal, InputShellDialect.Unix));
    }

    [Theory]
    [InlineData("{input:}")]
    [InlineData("{input:  =abc}")]
    [InlineData("echo {input:Missing")]
    [InlineData("echo {input:N{input:X}}")]
    [InlineData("echo {{input:Broken}")]
    [InlineData("{input:Name=one} {input:Name=two}")]
    [InlineData("{input:Name} {input:Name=two}")]
    public void Parser_RejectsMalformedTemplates(string command)
    {
        FormatException error = Assert.Throws<FormatException>(() => CommandTemplateParser.Parse(command));
        Assert.Contains("position", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("echo prefix{input:Name}")]
    [InlineData("echo \"prefix {input:Name}\"")]
    [InlineData("echo \"{input:Name}suffix\"")]
    public void Renderer_RejectsAmbiguousPlacements(string command)
    {
        CommandTemplate template = CommandTemplateParser.Parse(command);
        Assert.Throws<FormatException>(() => CommandTemplateRenderer.ValidatePlacement(template, PlatformKind.Linux));
    }

    [Fact]
    public void Rendering_NeverRecursivelyExpandsValues()
    {
        CommandTemplate template = CommandTemplateParser.Parse("echo \"{input:First}\" \"{input:Second}\" {panelDir}");
        const string first = "{input:Second} {panelDir} $HOME";
        const string second = "done";
        string result = CommandTemplateRenderer.Expand(template,
            new Dictionary<string, string> { ["First"] = first, ["Second"] = second },
            literal => literal.Replace("{panelDir}", "/project"),
            InputShellDialect.Unix);

        Assert.Equal("echo \"{input:Second} {panelDir} \\$HOME\" \"done\" /project", result);
    }

    [Fact]
    public void Quoting_UnixSupportsSpecialCharactersInSingleAndDoubleQuotedArguments()
    {
        const string value = "a b & | $HOME `touch BAD` \" ' \\";
        var parameters = new Dictionary<string, string> { ["A"] = value };
        foreach (string source in new[] { "printf '%s' {input:A}", "printf '%s' \"{input:A}\"", "printf '%s' '{input:A}'" })
        {
            CommandTemplate template = CommandTemplateParser.Parse(source);
            string command = CommandTemplateRenderer.Expand(template, parameters, static literal => literal, InputShellDialect.Unix);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(value, ExecuteShell(new UnixShellCommandLineBuilder(), command));
        }
    }

    [Fact]
    public void Quoting_UnixCannotExecuteInjectedCommands()
    {
        if (OperatingSystem.IsWindows()) return;
        string marker = Path.Combine(Path.GetTempPath(), "csharpfar-injection-" + Guid.NewGuid().ToString("N"));
        try
        {
            string input = "video; touch " + marker + "; echo $(printf BAD) &";
            CommandTemplate template = CommandTemplateParser.Parse("printf '%s' \"{input:URL}\"");
            string command = CommandTemplateRenderer.Expand(template,
                new Dictionary<string, string> { ["URL"] = input },
                static literal => literal, InputShellDialect.Unix);

            Assert.Equal(input, ExecuteShell(new UnixShellCommandLineBuilder(), command));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    [Fact]
    public void Quoting_CmdRejectsExpansionAndQuoteCharacters()
    {
        CommandTemplate template = CommandTemplateParser.Parse("echo \"{input:URL}\"");
        foreach (string input in new[] { "x%PATH%", "x!PATH!", "x^&echo BAD", "x\"&echo BAD" })
        {
            var values = new Dictionary<string, string> { ["URL"] = input };
            Assert.Throws<ArgumentException>(() => CommandTemplateRenderer.Expand(
                template, values, static literal => literal, InputShellDialect.Cmd));
        }
        CommandTemplateRenderer.ValidatePlacement(template, PlatformKind.Windows);
        Assert.Equal("echo \"a&b | c $d\"",
            CommandTemplateRenderer.Expand(template, new Dictionary<string, string> { ["URL"] = "a&b | c $d" },
                static literal => literal, InputShellDialect.Cmd));
    }

    [Fact]
    public void Quoting_PowerShellEscapesSubexpressions()
    {
        CommandTemplate template = CommandTemplateParser.Parse("ps: Write-Host \"{input:Value}\"");
        Assert.Equal(InputShellDialect.PowerShell, CommandTemplateRenderer.Dialect(template.Original, PlatformKind.Windows));
        Assert.Equal("ps: Write-Host \"`$HOME `` `\"word`\"\"",
            CommandTemplateRenderer.Expand(template, new Dictionary<string, string> { ["Value"] = "$HOME ` \"word\"" },
                static literal => literal, InputShellDialect.PowerShell));
    }

    [Fact]
    public void OrdinaryCommandsRetainContextPlaceholders()
    {
        CommandTemplate template = CommandTemplateParser.Parse("cd \"{panelDir}\" && echo {selected}");
        Assert.Empty(template.Parameters);
        Assert.Equal("cd \"/workspace\" && echo {selected}",
            CommandTemplateRenderer.Expand(template, new Dictionary<string, string>(),
                s => s.Replace("{panelDir}", "/workspace"), InputShellDialect.Unix));
    }

    private static string ExecuteShell(IShellCommandLineBuilder builder, string command)
    {
        ProcessStartInfo info = builder.CreateStartInfo(command, Path.GetTempPath());
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        using Process process = Process.Start(info)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr);
        return stdout;
    }
}
