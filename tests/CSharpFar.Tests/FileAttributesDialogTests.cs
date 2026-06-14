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
            CanEditLastAccessTime: true);
    }
}
