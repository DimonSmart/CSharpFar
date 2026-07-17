using CSharpFar.App.Commands;
using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

internal static class PanelCommandUserMenuOperands
{
    public static string Expand(
        string command,
        ResolvedPanelCommandTarget target,
        ApplicationCommandContext context)
    {
        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedItem)
            ? resolvedItem
            : null;
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;
        IReadOnlyList<string> selected = target.State.SelectedPaths.Count > 0
            ? [.. target.State.SelectedPaths]
            : [];
        return PlaceholderExpander.Expand(
            command,
            currentFile,
            selected,
            target.ActiveCommitted?.CurrentDirectory ?? target.State.CurrentDirectory,
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory);
    }
}
