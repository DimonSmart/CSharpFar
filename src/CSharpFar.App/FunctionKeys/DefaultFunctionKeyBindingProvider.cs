namespace CSharpFar.App.FunctionKeys;

internal sealed class DefaultFunctionKeyBindingProvider
{
    private static readonly IReadOnlyList<FunctionKeyBinding> Bindings =
    [
        new(
            FunctionKeyCommandIds.Help,
            FunctionKeyLayer.Plain,
            ConsoleKey.F1,
            "Help"),
        new(
            FunctionKeyCommandIds.UserMenu,
            FunctionKeyLayer.Plain,
            ConsoleKey.F2,
            "UserMn"),
        new(
            FunctionKeyCommandIds.View,
            FunctionKeyLayer.Plain,
            ConsoleKey.F3,
            "View"),
        new(
            FunctionKeyCommandIds.Edit,
            FunctionKeyLayer.Plain,
            ConsoleKey.F4,
            "Edit",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.OpenCreateFile,
            FunctionKeyLayer.Shift,
            ConsoleKey.F4,
            "New",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.Copy,
            FunctionKeyLayer.Plain,
            ConsoleKey.F5,
            "Copy",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.RenameOrMove,
            FunctionKeyLayer.Plain,
            ConsoleKey.F6,
            "RenMov",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.Rename,
            FunctionKeyLayer.Shift,
            ConsoleKey.F6,
            "Rename",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.Attributes,
            FunctionKeyLayer.Shift,
            ConsoleKey.F9,
            "Attr",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.CreateFolder,
            FunctionKeyLayer.Plain,
            ConsoleKey.F7,
            "MkFold",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.Delete,
            FunctionKeyLayer.Plain,
            ConsoleKey.F8,
            "Delete",
            RunsWhenUnavailable: true),
        new(
            FunctionKeyCommandIds.TopMenu,
            FunctionKeyLayer.Plain,
            ConsoleKey.F9,
            "ConfMn"),
        new(
            FunctionKeyCommandIds.Quit,
            FunctionKeyLayer.Plain,
            ConsoleKey.F10,
            "Quit"),
        new(
            FunctionKeyCommandIds.LeftVolume,
            FunctionKeyLayer.Alt,
            ConsoleKey.F1,
            "Left"),
        new(
            FunctionKeyCommandIds.RightVolume,
            FunctionKeyLayer.Alt,
            ConsoleKey.F2,
            "Right"),
        new(
            FunctionKeyCommandIds.Search,
            FunctionKeyLayer.Alt,
            ConsoleKey.F7,
            "Search"),
        new(
            FunctionKeyCommandIds.CommandHistory,
            FunctionKeyLayer.Alt,
            ConsoleKey.F8,
            "History"),
        new(
            FunctionKeyCommandIds.FileHistory,
            FunctionKeyLayer.Alt,
            ConsoleKey.F11,
            "FHist"),
        new(
            FunctionKeyCommandIds.DirectoryHistory,
            FunctionKeyLayer.Alt,
            ConsoleKey.F12,
            "DHist"),
        new(
            FunctionKeyCommandIds.ToggleLeftPanel,
            FunctionKeyLayer.Control,
            ConsoleKey.F1,
            "LeftPn"),
        new(
            FunctionKeyCommandIds.ToggleRightPanel,
            FunctionKeyLayer.Control,
            ConsoleKey.F2,
            "RightPn"),
        new(
            FunctionKeyCommandIds.SortByName,
            FunctionKeyLayer.Control,
            ConsoleKey.F3,
            "SortNm"),
        new(
            FunctionKeyCommandIds.SortByExtension,
            FunctionKeyLayer.Control,
            ConsoleKey.F4,
            "SortExt"),
        new(
            FunctionKeyCommandIds.SortByLastWriteTime,
            FunctionKeyLayer.Control,
            ConsoleKey.F5,
            "SortTm"),
        new(
            FunctionKeyCommandIds.SortBySize,
            FunctionKeyLayer.Control,
            ConsoleKey.F6,
            "SortSz"),
    ];

    public IReadOnlyList<FunctionKeyBinding> GetBindings() => Bindings;
}
