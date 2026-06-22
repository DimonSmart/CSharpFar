namespace CSharpFar.Core.Models;

public sealed record UnixFileMetadata(
    UnixPermissionBits Permissions,
    IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> PermissionStates,
    int? Uid,
    int? Gid,
    string? OwnerName,
    string? GroupName,
    bool CanEditPermissions,
    string? PermissionsDisabledReason);
