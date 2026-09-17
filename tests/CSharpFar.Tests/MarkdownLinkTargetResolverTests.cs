using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class MarkdownLinkTargetResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public MarkdownLinkTargetResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CSharpFarLinkTarget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Theory]
    [InlineData("http://example.com/path?q=1#x")]
    [InlineData("HTTPS://example.com/path")]
    public void HttpTargetsAreExternalAndPreserveUri(string target)
    {
        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve(target, null);

        Assert.Equal(MarkdownLinkTargetKind.ExternalUri, result.Kind);
        Assert.Equal(target, result.Uri!.OriginalString);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void RelativeFileResolvesAgainstPhysicalMarkdownDirectory()
    {
        string docs = Path.Combine(_tempDirectory, "docs");
        Directory.CreateDirectory(docs);
        string source = Path.Combine(docs, "readme.md");

        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("../config.json", source);

        Assert.Equal(MarkdownLinkTargetKind.RelativeFile, result.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDirectory, "config.json")), result.FilePath);
    }

    [Theory]
    [InlineData("mailto:test@example.com")]
    [InlineData("file:readme.md")]
    [InlineData("ssh:host")]
    [InlineData("vscode:file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("scheme:broken target")]
    [InlineData("#section")]
    [InlineData("docs/readme.md#section")]
    [InlineData("docs/readme.md?x=1")]
    [InlineData("/usr/local/file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"C:\file.txt")]
    [InlineData("")]
    public void UnsupportedTargetsNeverBecomeFiles(string target)
    {
        string source = Path.Combine(_tempDirectory, "readme.md");

        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve(target, source);

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
        Assert.Null(result.Uri);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void RelativeTargetWithoutPhysicalSourceDoesNotUseCurrentDirectory()
    {
        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("docs/readme.md", null);

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
    }

    [Fact]
    public void RelativeSourcePathDoesNotUseCurrentDirectoryAsImplicitBase()
    {
        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("docs/readme.md", "README.md");

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
    }

    [Fact]
    public void DirectoryTargetIsUnsupported()
    {
        string targetDirectory = Path.Combine(_tempDirectory, "docs");
        Directory.CreateDirectory(targetDirectory);
        string source = Path.Combine(_tempDirectory, "readme.md");

        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("docs", source);

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
    }
}
