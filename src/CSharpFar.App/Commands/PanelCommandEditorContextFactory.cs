using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class PanelCommandEditorContextFactory
{
    public static EditorFileNameInsertionContext Create(
        FilePanelItem? activeItem,
        FilePanelItem? passiveItem) =>
        new(
            activeItem is { IsParentDirectory: false } ? activeItem.Name : null,
            activeItem is { IsParentDirectory: false } ? activeItem.FullPath : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
}
