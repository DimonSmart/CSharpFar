using CSharpFar.Core.Models;
using System.Runtime.Versioning;

namespace CSharpFar.FileSystem.Platform;

[SupportedOSPlatform("macos")]
internal sealed class MacOsFileMetadataProvider : UnixFileMetadataProvider
{
    public override UnixFileMetadata? GetUnixMetadata(string path, FileAttributes attributes) => null;

    public override IReadOnlyList<FileAttributeDescriptor> GetAttributeDescriptors(string path, FileAttributes attributes)
    {
        var descriptors = base.GetAttributeDescriptors(path, attributes).ToList();
        int hiddenIndex = descriptors.FindIndex(static descriptor => descriptor.Id == FileAttributeId.Hidden);
        if (hiddenIndex >= 0)
            descriptors[hiddenIndex] = Descriptor(FileAttributeId.Hidden, editable: true);
        return descriptors;
    }

    public override void ApplyAttributes(
        string path,
        FileAttributes currentAttributes,
        IReadOnlyDictionary<FileAttributeId, AttributeEditState> changes)
    {
        ApplyReadOnlyCompatibility(path, currentAttributes, changes);

        if (!changes.TryGetValue(FileAttributeId.Hidden, out var hidden) ||
            hidden == AttributeEditState.Indeterminate)
        {
            return;
        }

        var refreshed = File.GetAttributes(path);
        var next = hidden == AttributeEditState.Checked
            ? refreshed | FileAttributes.Hidden
            : refreshed & ~FileAttributes.Hidden;
        if (next != refreshed)
            File.SetAttributes(path, next);
    }

    public override void ApplyUnixPermissions(
        string path,
        UnixFileMetadata currentMetadata,
        IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> changes) =>
        throw new PlatformNotSupportedException("Unix permission editing is not enabled by the macOS metadata provider.");
}
