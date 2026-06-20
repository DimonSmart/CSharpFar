using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"CSharpFar-{Guid.NewGuid():N}");

    [Fact]
    public void ReadDirectory_WhenChildMetadataIsInaccessible_SkipsOnlyThatChild()
    {
        string accessibleFile = Path.Combine(_directory, "accessible.txt");
        string inaccessibleFile = Path.Combine(_directory, "inaccessible.txt");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(accessibleFile, "visible");
        File.WriteAllText(inaccessibleFile, "hidden");

        var service = new FileSystemService(
            _ => [accessibleFile, inaccessibleFile],
            path => path == inaccessibleFile
                ? throw new UnauthorizedAccessException("Entry is inaccessible.")
                : File.GetAttributes(path));

        var items = service.ReadDirectory(_directory);

        var item = Assert.Single(items);
        Assert.Equal("accessible.txt", item.Name);
        Assert.Equal(accessibleFile, item.FullPath);
    }

    [Fact]
    public void ReadDirectory_WhenRootEnumerationFails_PropagatesSourceError()
    {
        var service = new FileSystemService(
            _ => throw new UnauthorizedAccessException("Root is inaccessible."),
            File.GetAttributes);

        var error = Assert.Throws<UnauthorizedAccessException>(
            () => service.ReadDirectory(_directory));

        Assert.Equal("Root is inaccessible.", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
