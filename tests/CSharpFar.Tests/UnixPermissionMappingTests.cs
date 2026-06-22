using CSharpFar.Core.Models;
using CSharpFar.FileSystem.Platform;

namespace CSharpFar.Tests;

public sealed class UnixPermissionMappingTests
{
    [Theory]
    [InlineData(0x1ed, UnixPermissionBits.OwnerRead | UnixPermissionBits.OwnerWrite | UnixPermissionBits.OwnerExecute |
                       UnixPermissionBits.GroupRead | UnixPermissionBits.GroupExecute |
                       UnixPermissionBits.OthersRead | UnixPermissionBits.OthersExecute)]
    [InlineData(0x1a4, UnixPermissionBits.OwnerRead | UnixPermissionBits.OwnerWrite |
                       UnixPermissionBits.GroupRead | UnixPermissionBits.OthersRead)]
    [InlineData(0x3ff, UnixPermissionBits.Sticky | UnixPermissionBits.OwnerRead | UnixPermissionBits.OwnerWrite |
                       UnixPermissionBits.OwnerExecute | UnixPermissionBits.GroupRead | UnixPermissionBits.GroupWrite |
                       UnixPermissionBits.GroupExecute | UnixPermissionBits.OthersRead | UnixPermissionBits.OthersWrite |
                       UnixPermissionBits.OthersExecute)]
    [InlineData(0x9ed, UnixPermissionBits.SetUid | UnixPermissionBits.OwnerRead | UnixPermissionBits.OwnerWrite |
                       UnixPermissionBits.OwnerExecute | UnixPermissionBits.GroupRead | UnixPermissionBits.GroupExecute |
                       UnixPermissionBits.OthersRead | UnixPermissionBits.OthersExecute)]
    [InlineData(0x5ed, UnixPermissionBits.SetGid | UnixPermissionBits.OwnerRead | UnixPermissionBits.OwnerWrite |
                       UnixPermissionBits.OwnerExecute | UnixPermissionBits.GroupRead | UnixPermissionBits.GroupExecute |
                       UnixPermissionBits.OthersRead | UnixPermissionBits.OthersExecute)]
    public void Mapping_ConvertsExpectedModes(int rawMode, UnixPermissionBits expected)
    {
        UnixPermissionBits permissions = UnixPermissionMapping.FromUnixFileMode((UnixFileMode)rawMode);

        Assert.Equal(expected, permissions);
        Assert.Equal((UnixFileMode)rawMode, UnixPermissionMapping.ToUnixFileMode(permissions));
    }

    [Fact]
    public void ApplyChanges_ChangesOnlyExplicitBits()
    {
        UnixPermissionBits original = UnixPermissionMapping.FromUnixFileMode((UnixFileMode)0x1a4);
        var changes = new Dictionary<UnixPermissionBit, AttributeEditState>
        {
            [UnixPermissionBit.OthersWrite] = AttributeEditState.Checked,
            [UnixPermissionBit.OwnerWrite] = AttributeEditState.Unchecked,
            [UnixPermissionBit.GroupWrite] = AttributeEditState.Indeterminate,
        };

        UnixPermissionBits actual = UnixPermissionMapping.ApplyChanges(original, changes);

        Assert.True(actual.HasFlag(UnixPermissionBits.OthersWrite));
        Assert.False(actual.HasFlag(UnixPermissionBits.OwnerWrite));
        Assert.False(actual.HasFlag(UnixPermissionBits.GroupWrite));
        Assert.True(actual.HasFlag(UnixPermissionBits.GroupRead));
    }

    [Fact]
    public void ApplyChanges_EmptyDictionaryPreservesMode()
    {
        UnixPermissionBits original = UnixPermissionMapping.FromUnixFileMode((UnixFileMode)0x3ff);

        Assert.Equal(original, UnixPermissionMapping.ApplyChanges(original, new Dictionary<UnixPermissionBit, AttributeEditState>()));
    }
}
