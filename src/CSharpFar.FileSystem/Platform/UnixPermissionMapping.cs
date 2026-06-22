using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem.Platform;

internal static class UnixPermissionMapping
{
    private static readonly IReadOnlyDictionary<UnixPermissionBit, UnixPermissionBits> Bits =
        new Dictionary<UnixPermissionBit, UnixPermissionBits>
        {
            [UnixPermissionBit.OwnerRead] = UnixPermissionBits.OwnerRead,
            [UnixPermissionBit.OwnerWrite] = UnixPermissionBits.OwnerWrite,
            [UnixPermissionBit.OwnerExecute] = UnixPermissionBits.OwnerExecute,
            [UnixPermissionBit.GroupRead] = UnixPermissionBits.GroupRead,
            [UnixPermissionBit.GroupWrite] = UnixPermissionBits.GroupWrite,
            [UnixPermissionBit.GroupExecute] = UnixPermissionBits.GroupExecute,
            [UnixPermissionBit.OthersRead] = UnixPermissionBits.OthersRead,
            [UnixPermissionBit.OthersWrite] = UnixPermissionBits.OthersWrite,
            [UnixPermissionBit.OthersExecute] = UnixPermissionBits.OthersExecute,
            [UnixPermissionBit.SetUid] = UnixPermissionBits.SetUid,
            [UnixPermissionBit.SetGid] = UnixPermissionBits.SetGid,
            [UnixPermissionBit.Sticky] = UnixPermissionBits.Sticky,
        };

    internal static UnixPermissionBits FromUnixFileMode(UnixFileMode mode) =>
        (UnixPermissionBits)((int)mode & 0xfff);

    internal static UnixFileMode ToUnixFileMode(UnixPermissionBits permissions) =>
        (UnixFileMode)((int)permissions & 0xfff);

    internal static IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> ToStates(
        UnixPermissionBits permissions) =>
        Bits.ToDictionary(
            static pair => pair.Key,
            pair => permissions.HasFlag(pair.Value)
                ? AttributeEditState.Checked
                : AttributeEditState.Unchecked);

    internal static UnixPermissionBits ApplyChanges(
        UnixPermissionBits permissions,
        IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> changes)
    {
        foreach (var (bit, state) in changes)
        {
            if (state == AttributeEditState.Indeterminate)
                continue;

            UnixPermissionBits flag = Bits[bit];
            permissions = state == AttributeEditState.Checked
                ? permissions | flag
                : permissions & ~flag;
        }

        return permissions;
    }
}
