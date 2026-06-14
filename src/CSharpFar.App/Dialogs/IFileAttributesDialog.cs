using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

public interface IFileAttributesDialog
{
    FileAttributesDialogResult? Show(FileMetadataSnapshot snapshot);
}

public sealed record FileAttributesDialogResult(
    FileMetadataChangeSet ChangeSet,
    bool OpenSystemProperties);

internal interface IClock
{
    DateTime Now { get; }
}

internal sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
