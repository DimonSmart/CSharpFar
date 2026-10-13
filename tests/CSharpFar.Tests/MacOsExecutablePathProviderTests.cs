using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class MacOsExecutablePathProviderTests
{
    private static readonly string[] Candidates =
    [
        "/opt/homebrew/bin", "/usr/local/bin", "/opt/homebrew/sbin", "/usr/local/sbin"
    ];

    private static MacOsExecutablePathProvider Create(
        string? path,
        Func<string, bool>? exists = null,
        string? mode = null,
        Action<string>? report = null) =>
        new(mode, () => path, exists ?? (_ => true), Candidates, report);

    [Fact]
    public void AppendsOnlyExistingDirectoriesInDefinedOrder()
    {
        var provider = Create("/usr/bin:/bin", p => p != "/opt/homebrew/sbin");

        Assert.Equal(
            "/usr/bin:/bin:/opt/homebrew/bin:/usr/local/bin:/usr/local/sbin",
            provider.GetPath());
    }

    [Fact]
    public void RetainsOriginalOrderingAndDuplicateEntries()
    {
        const string original = "/custom/bin:/usr/local/bin:/custom/bin:/bin";
        var provider = Create(original);

        Assert.Equal(original + ":/opt/homebrew/bin:/opt/homebrew/sbin:/usr/local/sbin",
            provider.GetPath());
    }

    [Fact]
    public void IgnoresTrailingSlashWhenCheckingDuplicates()
    {
        var provider = Create("/opt/homebrew/bin/:/usr/local/bin///:/bin");
        Assert.Equal("/opt/homebrew/bin/:/usr/local/bin///:/bin:/opt/homebrew/sbin:/usr/local/sbin",
            provider.GetPath());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOrEmptyPathGetsSystemFallback(string? path)
    {
        var provider = Create(path, _ => false);
        Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", provider.GetPath());
    }

    [Fact]
    public void ExplicitEmptySegmentsArePreserved()
    {
        Assert.Equal("::/opt/homebrew/bin:/usr/local/bin:/opt/homebrew/sbin:/usr/local/sbin",
            Create(":").GetPath());
    }

    [Fact]
    public void DirectoryCheckFailureDoesNotBlockOtherCandidates()
    {
        var provider = Create("/bin", path => path == "/opt/homebrew/bin"
            ? throw new IOException("Access denied") : path == "/usr/local/bin");

        Assert.Equal("/bin:/usr/local/bin", provider.GetPath());
    }

    [Fact]
    public void DoesNotRewriteOriginalRelativeSegments()
    {
        var provider = Create("./tools:/usr/bin:/usr/bin");
        Assert.StartsWith("./tools:/usr/bin:/usr/bin:", provider.GetPath(), StringComparison.Ordinal);
        Assert.DoesNotContain(":./tools:", provider.GetPath(), StringComparison.Ordinal);
    }

    [Fact]
    public void InheritedModeHasNoOverride()
    {
        var provider = Create("/bin", mode: "inherited");
        Assert.Null(provider.GetPath());
    }

    [Fact]
    public void UnknownModeWarnsExactlyOnceAndUsesInherited()
    {
        var messages = new List<string>();
        var provider = Create("/bin", mode: "unexpected", report: messages.Add);
        Assert.Null(provider.GetPath());
        Assert.Null(provider.GetPath());
        Assert.Single(messages);
        Assert.Contains("shell.macOsPathMode", messages[0]);
    }

    [Fact]
    public void ComputesPathOnEachCallWithoutChangingGlobalEnvironment()
    {
        string? current = Environment.GetEnvironmentVariable("PATH");
        string? inherited = "/bin";
        var provider = new MacOsExecutablePathProvider("appendStandard",
            () => inherited, _ => false, Candidates);
        Assert.Equal("/bin", provider.GetPath());
        inherited = "/usr/bin";
        Assert.Equal("/usr/bin", provider.GetPath());
        Assert.Equal(current, Environment.GetEnvironmentVariable("PATH"));
    }
}
