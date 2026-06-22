using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.FileSystem.Platform;

namespace CSharpFar.Tests;

public sealed class UnixMetadataMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarUnixMerge_{Guid.NewGuid():N}");

    public UnixMetadataMergeTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void EqualModesAndOwnersMergeWithoutIndeterminateStates()
    {
        string first = Create("first");
        string second = Create("second");
        var provider = new StubUnixProvider(new Dictionary<string, UnixFileMetadata>
        {
            [first] = Metadata(0x1a4, 1000, 1000, "dmitry", "dmitry"),
            [second] = Metadata(0x1a4, 1000, 1000, "dmitry", "dmitry"),
        });

        UnixFileMetadata merged = new FileMetadataService(provider).GetMergedMetadata([first, second]).UnixMetadata!;

        Assert.Equal(AttributeEditState.Checked, merged.PermissionStates[UnixPermissionBit.GroupRead]);
        Assert.Equal(AttributeEditState.Unchecked, merged.PermissionStates[UnixPermissionBit.GroupWrite]);
        Assert.DoesNotContain(AttributeEditState.Indeterminate, merged.PermissionStates.Values);
        Assert.Equal("dmitry", merged.OwnerName);
        Assert.Equal("dmitry", merged.GroupName);
        Assert.Equal(1000, merged.Uid);
        Assert.Equal(1000, merged.Gid);
    }

    [Fact]
    public void DifferentModesAndOwnersMergeAsMixed()
    {
        string first = Create("first");
        string second = Create("second");
        var provider = new StubUnixProvider(new Dictionary<string, UnixFileMetadata>
        {
            [first] = Metadata(0x1a4, 1000, 1000, "first-owner", "first-group"),
            [second] = Metadata(0x180, 1001, 1001, "second-owner", "second-group"),
        });

        UnixFileMetadata merged = new FileMetadataService(provider).GetMergedMetadata([first, second]).UnixMetadata!;

        Assert.Equal(AttributeEditState.Indeterminate, merged.PermissionStates[UnixPermissionBit.GroupRead]);
        Assert.Equal(AttributeEditState.Indeterminate, merged.PermissionStates[UnixPermissionBit.OthersRead]);
        Assert.Null(merged.OwnerName);
        Assert.Null(merged.GroupName);
        Assert.Null(merged.Uid);
        Assert.Null(merged.Gid);
    }

    [Fact]
    public void ApplyUnixPermissions_ReportsErrorAndContinuesWithOtherItems()
    {
        string first = Create("first");
        string second = Create("second");
        UnixFileMetadata metadata = Metadata(0x1a4, 1000, 1000, "owner", "group");
        var provider = new ApplyingUnixProvider(first, metadata);
        var changes = new FileMetadataChangeSet(
            new Dictionary<FileAttributeId, AttributeEditState>(),
            null,
            null,
            null,
            new Dictionary<UnixPermissionBit, AttributeEditState>
            {
                [UnixPermissionBit.OthersWrite] = AttributeEditState.Checked,
            });

        FileMetadataApplyResult result = new FileMetadataService(provider).ApplyMetadata([first, second], changes);

        Assert.Equal(1, result.ChangedCount);
        FileMetadataApplyError error = Assert.Single(result.Errors);
        Assert.Equal(first, error.Path);
        Assert.Equal("Set Unix permissions", error.Operation);
        Assert.Equal([second], provider.AppliedPaths);
    }

    private string Create(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, name);
        return path;
    }

    private static UnixFileMetadata Metadata(int mode, int uid, int gid, string owner, string group)
    {
        UnixPermissionBits permissions = UnixPermissionMapping.FromUnixFileMode((UnixFileMode)mode);
        return new UnixFileMetadata(permissions, UnixPermissionMapping.ToStates(permissions), uid, gid, owner, group, true, null);
    }

    private sealed class StubUnixProvider(IReadOnlyDictionary<string, UnixFileMetadata> metadata) : IFileMetadataProvider
    {
        public IReadOnlyList<FileAttributeDescriptor> GetAttributeDescriptors(string path, FileAttributes attributes) => [];
        public bool CanEditCreationTime(string path, FileAttributes attributes) => false;
        public bool CanEditLastWriteTime(string path, FileAttributes attributes) => true;
        public bool CanEditLastAccessTime(string path, FileAttributes attributes) => true;
        public string? GetOwnerDisplayName(string path) => null;
        public UnixFileMetadata? GetUnixMetadata(string path, FileAttributes attributes) => metadata[path];
        public void ApplyAttributes(string path, FileAttributes currentAttributes, IReadOnlyDictionary<FileAttributeId, AttributeEditState> changes) { }
        public void ApplyUnixPermissions(string path, UnixFileMetadata currentMetadata, IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> changes) { }
        public void OpenSystemProperties(string path) => throw new PlatformNotSupportedException();
        public bool CanOpenSystemProperties => false;
    }

    private sealed class ApplyingUnixProvider(string failingPath, UnixFileMetadata metadata) : IFileMetadataProvider
    {
        public List<string> AppliedPaths { get; } = [];
        public IReadOnlyList<FileAttributeDescriptor> GetAttributeDescriptors(string path, FileAttributes attributes) => [];
        public bool CanEditCreationTime(string path, FileAttributes attributes) => false;
        public bool CanEditLastWriteTime(string path, FileAttributes attributes) => true;
        public bool CanEditLastAccessTime(string path, FileAttributes attributes) => true;
        public string? GetOwnerDisplayName(string path) => null;
        public UnixFileMetadata? GetUnixMetadata(string path, FileAttributes attributes) => metadata;
        public void ApplyAttributes(string path, FileAttributes currentAttributes, IReadOnlyDictionary<FileAttributeId, AttributeEditState> changes) { }
        public void ApplyUnixPermissions(string path, UnixFileMetadata currentMetadata, IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> changes)
        {
            if (path == failingPath)
                throw new UnauthorizedAccessException("denied");
            AppliedPaths.Add(path);
        }
        public void OpenSystemProperties(string path) => throw new PlatformNotSupportedException();
        public bool CanOpenSystemProperties => false;
    }
}
