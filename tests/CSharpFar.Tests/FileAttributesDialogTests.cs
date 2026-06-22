using CSharpFar.App.Dialogs;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class FileAttributesDialogTests
{
    [Fact]
    public void CreateChangeSet_BlankTimeDoesNotChangeOriginalTime()
    {
        var snapshot = Snapshot();

        FileMetadataChangeSet changeSet = FileAttributesDialog.CreateChangeSet(
            snapshot,
            snapshot.AttributeStates,
            new Dictionary<UnixPermissionBit, AttributeEditState>(),
            creationText: string.Empty,
            writeText: string.Empty,
            accessText: string.Empty,
            out string? error);

        Assert.Null(error);
        Assert.Null(changeSet.CreationTime);
        Assert.Null(changeSet.LastWriteTime);
        Assert.Null(changeSet.LastAccessTime);
    }

    [Fact]
    public void CreateChangeSet_OnlyChangedValuesAreReturned()
    {
        var snapshot = Snapshot();
        var states = snapshot.AttributeStates.ToDictionary();
        states[FileAttributeId.ReadOnly] = AttributeEditState.Checked;
        string newWrite = new DateTime(2026, 6, 14, 15, 3, 39).ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        FileMetadataChangeSet changeSet = FileAttributesDialog.CreateChangeSet(
            snapshot,
            states,
            new Dictionary<UnixPermissionBit, AttributeEditState>(),
            creationText: FileAttributesDialog.FormatTime(snapshot.CreationTime),
            writeText: newWrite,
            accessText: FileAttributesDialog.FormatTime(snapshot.LastAccessTime),
            out string? error);

        Assert.Null(error);
        Assert.Equal(AttributeEditState.Checked, changeSet.AttributeChanges[FileAttributeId.ReadOnly]);
        Assert.Null(changeSet.CreationTime);
        Assert.Equal(new DateTime(2026, 6, 14, 15, 3, 39), changeSet.LastWriteTime);
        Assert.Null(changeSet.LastAccessTime);
    }

    [Fact]
    public void CreateChangeSet_ReturnsOnlyChangedUnixPermissions()
    {
        var permissions = UnixPermissionBits.OwnerRead | UnixPermissionBits.OwnerWrite | UnixPermissionBits.GroupRead;
        var unixMetadata = new UnixFileMetadata(
            permissions,
            Enum.GetValues<UnixPermissionBit>().ToDictionary(
                static bit => bit,
                bit => bit is UnixPermissionBit.OwnerRead or UnixPermissionBit.OwnerWrite or UnixPermissionBit.GroupRead
                    ? AttributeEditState.Checked
                    : AttributeEditState.Unchecked),
            1000,
            1000,
            "owner",
            "group",
            true,
            null);
        FileMetadataSnapshot snapshot = Snapshot() with { UnixMetadata = unixMetadata };
        var current = unixMetadata.PermissionStates.ToDictionary();
        current[UnixPermissionBit.GroupRead] = AttributeEditState.Unchecked;

        FileMetadataChangeSet changeSet = FileAttributesDialog.CreateChangeSet(
            snapshot,
            snapshot.AttributeStates,
            current,
            FileAttributesDialog.FormatTime(snapshot.CreationTime),
            FileAttributesDialog.FormatTime(snapshot.LastWriteTime),
            FileAttributesDialog.FormatTime(snapshot.LastAccessTime),
            out string? error);

        Assert.Null(error);
        Assert.Equal(
            new Dictionary<UnixPermissionBit, AttributeEditState>
            {
                [UnixPermissionBit.GroupRead] = AttributeEditState.Unchecked,
            },
            changeSet.UnixPermissionChanges);
    }

    [Fact]
    public void FormatUnixMode_ReturnsDisplayModeWhenPermissionsAreUniform()
    {
        UnixFileMetadata metadata = UnixMetadata(0x1a4, AttributeEditState.Checked);

        Assert.Equal("0644  rw-r--r--", FileAttributesDialog.FormatUnixMode(metadata));
    }

    [Fact]
    public void FormatUnixMode_ReturnsMixedWhenAnyPermissionIsIndeterminate()
    {
        UnixFileMetadata metadata = UnixMetadata(0x1a4, AttributeEditState.Indeterminate);

        Assert.Equal("<mixed>", FileAttributesDialog.FormatUnixMode(metadata));
    }

    private static UnixFileMetadata UnixMetadata(int permissions, AttributeEditState ownerReadState) =>
        new(
            (UnixPermissionBits)permissions,
            Enum.GetValues<UnixPermissionBit>().ToDictionary(
                static bit => bit,
                bit => bit == UnixPermissionBit.OwnerRead
                    ? ownerReadState
                    : AttributeEditState.Unchecked),
            1000,
            1000,
            "owner",
            "group",
            true,
            null);

    private static FileMetadataSnapshot Snapshot()
    {
        var created = new DateTime(2026, 1, 1, 1, 2, 3);
        var write = new DateTime(2026, 1, 2, 1, 2, 3);
        var access = new DateTime(2026, 1, 3, 1, 2, 3);
        return new FileMetadataSnapshot(
            "file.txt",
            "file.txt",
            false,
            FileAttributes.Archive,
            created,
            write,
            access,
            "DOMAIN\\User",
            [new FileAttributeDescriptor(FileAttributeId.ReadOnly, "Read only", 'R', true, true)],
            new Dictionary<FileAttributeId, AttributeEditState>
            {
                [FileAttributeId.ReadOnly] = AttributeEditState.Unchecked,
            },
            CanEditCreationTime: true,
            CanEditLastWriteTime: true,
            CanEditLastAccessTime: true,
            UnixMetadata: null);
    }
}
