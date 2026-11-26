namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Optional capability for renaming an existing entry without replacing any target
/// entry, including one created concurrently. The provider must enforce this in
/// the rename itself, not with a preceding existence check.
/// </summary>
public interface ISafeEntryRenameProvider
{
    Task RenameWithoutOverwriteAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exclusive staging creation and no-overwrite publication. A successful rename
/// consumes the staging file. Open must never truncate an existing staging path.
/// </summary>
public interface ISafeNewFilePublicationProvider : ISafeEntryRenameProvider
{
    Task<Stream> OpenStagingWriteAsync(string stagingPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opt-in for backup/restore moves using reliable no-overwrite renames. A failed
/// rename must leave both entries unchanged; a lost acknowledgement is not a
/// supported failure mode for this capability.
/// </summary>
public interface IRecoverableEntryRenameProvider : ISafeEntryRenameProvider
{
}
