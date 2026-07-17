using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class PanelCommandEditorContextFactory
{
    internal static EditorFileNameInsertionContext Create(
        ApplicationCommandContext context,
        ResolvedPanelCommandTarget target,
        FilePanelItem? resolvedActiveItem = null)
    {
        FilePanelItem? activeItem = resolvedActiveItem ??
            (ApplicationCommandContext.TryResolveCommittedCurrentItem(
                target.State, target.ActiveCommitted, context.Controller, out var resolvedActive)
                ? resolvedActive
                : null);
        FilePanelItem? passiveItem = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.PassiveState, target.PassiveCommitted, context.Controller, out var resolvedPassive)
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
