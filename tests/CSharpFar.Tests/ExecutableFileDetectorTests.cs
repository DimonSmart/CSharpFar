using CSharpFar.Shell;

namespace CSharpFar.Tests;

#pragma warning disable CA1416

public sealed class ExecutableFileDetectorTests : IDisposable
{
    private readonly string _root;

    public ExecutableFileDetectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarExecutableDetector_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("tool.exe", true)]
    [InlineData("tool.EXE", true)]
    [InlineData("tool.cmd", true)]
    [InlineData("tool.bat", true)]
    [InlineData("tool.txt", false)]
    [InlineData("tool", false)]
    public void WindowsExecutableFileDetector_UsesFallbackExtensions(string fileName, bool expected)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "");
        var detector = new WindowsExecutableFileDetector(
            File.Exists,
            name => name == "PATHEXT" ? "" : null);

        Assert.Equal(expected, detector.IsExecutableFile(path));
    }

    [Fact]
    public void WindowsExecutableFileDetector_DirectoryNamedExeIsNotExecutable()
    {
        string path = Path.Combine(_root, "tool.exe");
        Directory.CreateDirectory(path);
        var detector = new WindowsExecutableFileDetector(
            File.Exists,
            name => name == "PATHEXT" ? "" : null);

        Assert.False(detector.IsExecutableFile(path));
    }

    [Fact]
    public void WindowsExecutableFileDetector_RespectsPathextOverride()
    {
        string appPath = Path.Combine(_root, "tool.app");
        string exePath = Path.Combine(_root, "tool.exe");
        File.WriteAllText(appPath, "");
        File.WriteAllText(exePath, "");
        var detector = new WindowsExecutableFileDetector(
            File.Exists,
            name => name == "PATHEXT" ? ".APP" : null);

        Assert.True(detector.IsExecutableFile(appPath));
        Assert.False(detector.IsExecutableFile(exePath));
    }

    [UnixFact]
    public void UnixExecutableFileDetector_RegularFileWithoutExecuteBitIsNotExecutable()
    {
        string path = Path.Combine(_root, "script.sh");
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        Assert.False(new UnixExecutableFileDetector().IsExecutableFile(path));
    }

    [UnixFact]
    public void UnixExecutableFileDetector_RegularFileWithExecuteBitIsExecutable()
    {
        string path = Path.Combine(_root, "tool");
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.True(new UnixExecutableFileDetector().IsExecutableFile(path));
    }

    [UnixFact]
    public void UnixExecutableFileDetector_ShExtensionWithoutExecuteBitIsNotExecutable()
    {
        string path = Path.Combine(_root, "script.sh");
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.False(new UnixExecutableFileDetector().IsExecutableFile(path));
    }

    [UnixFact]
    public void UnixExecutableFileDetector_DirectoryWithExecuteBitIsNotExecutable()
    {
        string path = Path.Combine(_root, "bin");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.False(new UnixExecutableFileDetector().IsExecutableFile(path));
    }

    [UnixFact]
    public void UnixExecutableFileDetector_BrokenSymlinkIsNotExecutable()
    {
        string path = Path.Combine(_root, "broken");
        File.CreateSymbolicLink(path, Path.Combine(_root, "missing"));

        Assert.False(new UnixExecutableFileDetector().IsExecutableFile(path));
    }

    [UnixFact]
    public void UnixExecutableFileDetector_SymlinkToExecutableFileIsExecutable()
    {
        string target = Path.Combine(_root, "target");
        string link = Path.Combine(_root, "link");
        File.WriteAllText(target, "");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.CreateSymbolicLink(link, target);

        Assert.True(new UnixExecutableFileDetector().IsExecutableFile(link));
    }

}

public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix file mode tests require a Unix-like file system.";
    }
}

#pragma warning restore CA1416
