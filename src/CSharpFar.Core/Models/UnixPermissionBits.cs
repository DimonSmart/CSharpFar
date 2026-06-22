namespace CSharpFar.Core.Models;

[Flags]
public enum UnixPermissionBits
{
    None = 0,
    OthersExecute = 0x001,
    OthersWrite = 0x002,
    OthersRead = 0x004,
    GroupExecute = 0x008,
    GroupWrite = 0x010,
    GroupRead = 0x020,
    OwnerExecute = 0x040,
    OwnerWrite = 0x080,
    OwnerRead = 0x100,
    Sticky = 0x200,
    SetGid = 0x400,
    SetUid = 0x800,
}
