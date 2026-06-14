using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem.Platform;

internal class UnixFileMetadataProvider : IFileMetadataProvider
{
    public virtual bool CanOpenSystemProperties => false;

    public virtual IReadOnlyList<FileAttributeDescriptor> GetAttributeDescriptors(string path, FileAttributes attributes)
    {
        var descriptors = new List<FileAttributeDescriptor>
        {
            Descriptor(FileAttributeId.ReadOnly, editable: true),
        };

        if (FileAttributeMapping.Has(attributes, FileAttributeId.Hidden))
            descriptors.Add(Descriptor(FileAttributeId.Hidden, editable: false, "Unix hidden state is name-based and is not edited as an attribute."));
        if (FileAttributeMapping.Has(attributes, FileAttributeId.Directory))
            descriptors.Add(Descriptor(FileAttributeId.Directory, editable: false));
        if (FileAttributeMapping.Has(attributes, FileAttributeId.ReparsePoint))
            descriptors.Add(Descriptor(FileAttributeId.ReparsePoint, editable: false));

        return descriptors;
    }

    public virtual bool CanEditCreationTime(string path, FileAttributes attributes) => false;
    public virtual bool CanEditLastWriteTime(string path, FileAttributes attributes) => true;
    public virtual bool CanEditLastAccessTime(string path, FileAttributes attributes) => true;
    public virtual string? GetOwnerDisplayName(string path) => null;

    public virtual void ApplyAttributes(
        string path,
        FileAttributes currentAttributes,
        IReadOnlyDictionary<FileAttributeId, AttributeEditState> changes)
    {
        if (!changes.TryGetValue(FileAttributeId.ReadOnly, out var readOnly) ||
            readOnly == AttributeEditState.Indeterminate)
        {
            return;
        }

        var next = readOnly == AttributeEditState.Checked
            ? currentAttributes | FileAttributes.ReadOnly
            : currentAttributes & ~FileAttributes.ReadOnly;
        if (next != currentAttributes)
            File.SetAttributes(path, next);
    }

    public virtual void OpenSystemProperties(string path) =>
        throw new PlatformNotSupportedException("System properties are supported only on Windows.");

    protected static FileAttributeDescriptor Descriptor(
        FileAttributeId id,
        bool editable,
        string? disabledReason = null) =>
        new(id, Label(id), HotKey(id), IsVisible: true, IsEditable: editable, disabledReason);

    protected static string Label(FileAttributeId id) =>
        id switch
        {
            FileAttributeId.ReadOnly => "Read only",
            FileAttributeId.ReparsePoint => "Reparse point",
            _ => id.ToString(),
        };

    protected static char? HotKey(FileAttributeId id) =>
        id switch
        {
            FileAttributeId.ReadOnly => 'R',
            FileAttributeId.Hidden => 'H',
            _ => null,
        };
}
