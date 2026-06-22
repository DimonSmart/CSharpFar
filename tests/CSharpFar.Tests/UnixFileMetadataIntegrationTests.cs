using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class UnixFileMetadataIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarUnixMetadata_{Guid.NewGuid():N}");

    public UnixFileMetadataIntegrationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReadsAndAppliesFilePermissions()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return;

        string path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "content");
        File.SetUnixFileMode(path, (UnixFileMode)0x1a0);
        var service = new FileMetadataService();

        UnixFileMetadata metadata = service.GetMetadata(path).UnixMetadata!;

        Assert.Equal(AttributeEditState.Checked, metadata.PermissionStates[UnixPermissionBit.OwnerRead]);
        Assert.Equal(AttributeEditState.Checked, metadata.PermissionStates[UnixPermissionBit.OwnerWrite]);
        Assert.Equal(AttributeEditState.Unchecked, metadata.PermissionStates[UnixPermissionBit.OwnerExecute]);
        Assert.Equal(AttributeEditState.Checked, metadata.PermissionStates[UnixPermissionBit.GroupRead]);
        Assert.Equal(AttributeEditState.Unchecked, metadata.PermissionStates[UnixPermissionBit.OthersRead]);
        Assert.NotNull(metadata.Uid);
        Assert.NotNull(metadata.Gid);
        Assert.NotNull(metadata.OwnerName);
        Assert.NotNull(metadata.GroupName);

        service.ApplyMetadata([path], Change(UnixPermissionBit.OthersRead, AttributeEditState.Checked));
        Assert.Equal((UnixFileMode)0x1a4, File.GetUnixFileMode(path));

        service.ApplyMetadata([path], Change(UnixPermissionBit.OwnerWrite, AttributeEditState.Unchecked));
        Assert.Equal((UnixFileMode)0x124, File.GetUnixFileMode(path));
    }

    [Fact]
    public void ReadsDirectoryExecuteBits()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return;

        string path = Path.Combine(_root, "directory");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, (UnixFileMode)0x1ed);

        UnixFileMetadata metadata = new FileMetadataService().GetMetadata(path).UnixMetadata!;

        Assert.Equal(AttributeEditState.Checked, metadata.PermissionStates[UnixPermissionBit.OwnerExecute]);
        Assert.Equal(AttributeEditState.Checked, metadata.PermissionStates[UnixPermissionBit.GroupExecute]);
        Assert.Equal(AttributeEditState.Checked, metadata.PermissionStates[UnixPermissionBit.OthersExecute]);
    }

    [Fact]
    public void EmptyPermissionChangesDoNotChangeMode()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return;

        string path = Path.Combine(_root, "unchanged.txt");
        File.WriteAllText(path, "content");
        File.SetUnixFileMode(path, (UnixFileMode)0x1a4);
        var changes = new FileMetadataChangeSet(
            new Dictionary<FileAttributeId, AttributeEditState>(),
            null,
            null,
            null,
            new Dictionary<UnixPermissionBit, AttributeEditState>());

        FileMetadataApplyResult result = new FileMetadataService().ApplyMetadata([path], changes);

        Assert.Equal(0, result.ChangedCount);
        Assert.Equal((UnixFileMode)0x1a4, File.GetUnixFileMode(path));
    }

    private static FileMetadataChangeSet Change(UnixPermissionBit bit, AttributeEditState state) =>
        new(
            new Dictionary<FileAttributeId, AttributeEditState>(),
            null,
            null,
            null,
            new Dictionary<UnixPermissionBit, AttributeEditState> { [bit] = state });
}
