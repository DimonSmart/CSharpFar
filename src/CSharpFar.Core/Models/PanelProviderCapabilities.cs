namespace CSharpFar.Core.Models;

[Flags]
public enum PanelProviderCapabilities
{
    None = 0,
    Enumerate = 1 << 0,
    OpenRead = 1 << 1,
    OpenWrite = 1 << 2,
    CreateFile = 1 << 3,
    CreateDirectory = 1 << 4,
    Delete = 1 << 5,
    Rename = 1 << 6,
    CopyFrom = 1 << 7,
    CopyTo = 1 << 8,
    MoveFrom = 1 << 9,
    MoveTo = 1 << 10,
    Edit = 1 << 11,
    Refresh = 1 << 12,
    Watch = 1 << 13,

    LocalFileSystem =
        Enumerate |
        OpenRead |
        OpenWrite |
        CreateFile |
        CreateDirectory |
        Delete |
        Rename |
        CopyFrom |
        CopyTo |
        MoveFrom |
        MoveTo |
        Edit |
        Refresh |
        Watch,

    SearchResults =
        Enumerate |
        OpenRead |
        CopyFrom |
        Refresh,
}
