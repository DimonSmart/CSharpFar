using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public FileSystemServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarFsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ReadDirectory_ThrowsWhenDirectoryDoesNotExist()
    {
        string missing = Path.Combine(_tempRoot, "missing");

        Assert.Throws<DirectoryNotFoundException>(() =>
            new FileSystemService().ReadDirectory(missing));
    }
}
