using CSharpFar.Core.Controllers;
using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class PanelCommandEditorContextFactory
{
    internal static EditorFileNameInsertionContext Create(
        PanelController controller,
        ResolvedPanelCommandTarget target,
        FilePanelItem? resolvedActiveItem = null)
    {
        FilePanelItem? activeItem = resolvedActiveItem ??
            (PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
                target.State, target.ActiveCommitted, controller, out var resolvedActive)
                ? resolvedActive
                : null);
        FilePanelItem? passiveItem = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.PassiveState, target.PassiveCommitted, controller, out var resolvedPassive)
            ? resolvedPassive
            : null;

        return Create(activeItem, passiveItem);
    }

    private static EditorFileNameInsertionContext Create(
        FilePanelItem? activeItem,
        FilePanelItem? passiveItem) =>
        new(
            activeItem is { IsParentDirectory: false } ? activeItem.Name : null,
            activeItem is { IsParentDirectory: false } ? activeItem.FullPath : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
}
