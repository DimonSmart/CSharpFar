using System.Runtime.InteropServices;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class FileMetadataServiceTests : IDisposable
{
    private readonly string _root;

    public FileMetadataServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarMetadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (string path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ApplyMetadata_SetsAndClearsReadOnly()
    {
        string path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "content");
        var service = new FileMetadataService();

        service.ApplyMetadata([path], Change(FileAttributeId.ReadOnly, AttributeEditState.Checked));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));

        service.ApplyMetadata([path], Change(FileAttributeId.ReadOnly, AttributeEditState.Unchecked));
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void GetMetadata_ReadsTimes()
    {
        string path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "content");
        var service = new FileMetadataService();

        FileMetadataSnapshot snapshot = service.GetMetadata(path);

        Assert.NotNull(snapshot.CreationTime);
        Assert.NotNull(snapshot.LastWriteTime);
        Assert.NotNull(snapshot.LastAccessTime);
    }

    [Fact]
    public void WindowsDescriptors_ShowExpectedEditableFlags()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "content");

        var editable = new FileMetadataService()
            .GetMetadata(path)
            .AttributesDescriptors
            .Where(static descriptor => descriptor.IsEditable)
            .Select(static descriptor => descriptor.Id)
            .ToHashSet();

        Assert.Contains(FileAttributeId.ReadOnly, editable);
        Assert.Contains(FileAttributeId.Hidden, editable);
        Assert.Contains(FileAttributeId.System, editable);
        Assert.Contains(FileAttributeId.Archive, editable);
        Assert.Contains(FileAttributeId.Temporary, editable);
        Assert.Contains(FileAttributeId.NotContentIndexed, editable);
    }

    [Fact]
    public void UnixDescriptors_DoNotEditHiddenDotfile()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string path = Path.Combine(_root, ".hidden");
        File.WriteAllText(path, "content");

        var hidden = new FileMetadataService()
            .GetMetadata(path)
            .AttributesDescriptors
            .FirstOrDefault(static descriptor => descriptor.Id == FileAttributeId.Hidden);

        if (hidden is not null)
            Assert.False(hidden.IsEditable);
    }

    [Fact]
    public void GetMergedMetadata_DifferentReadOnlyStatesAreIndeterminate()
    {
        string first = Path.Combine(_root, "first.txt");
        string second = Path.Combine(_root, "second.txt");
        File.WriteAllText(first, "1");
        File.WriteAllText(second, "2");
        File.SetAttributes(first, File.GetAttributes(first) | FileAttributes.ReadOnly);
        var service = new FileMetadataService();

        FileMetadataSnapshot snapshot = service.GetMergedMetadata([first, second]);

        Assert.Equal(AttributeEditState.Indeterminate, snapshot.AttributeStates[FileAttributeId.ReadOnly]);
    }

    private static FileMetadataChangeSet Change(FileAttributeId id, AttributeEditState state) =>
        new(new Dictionary<FileAttributeId, AttributeEditState> { [id] = state }, null, null, null);
}
