using CSharpFar.App.Commands;
using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

internal readonly record struct PanelCommandUserMenuOperands(
    string CurrentFile,
    IReadOnlyList<string> SelectedPaths,
    string ActiveDirectory,
    string PassiveDirectory)
{
    public static bool TryCreate(
        ResolvedPanelCommandTarget target,
        ApplicationCommandContext context,
        out PanelCommandUserMenuOperands operands)
    {
        operands = default;
        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !ApplicationCommandContext.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
        {
            return false;
        }

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedItem)
            ? resolvedItem
            : null;
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;
        IReadOnlyList<string> selected = target.State.SelectedPaths.Count > 0
            ? [.. target.State.SelectedPaths]
            : [];
        operands = new PanelCommandUserMenuOperands(
            currentFile,
            selected,
            target.ActiveCommitted?.CurrentDirectory ?? target.State.CurrentDirectory,
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory);
        return true;
    }

    public string Expand(string command) =>
        PlaceholderExpander.Expand(
            command,
            CurrentFile,
            SelectedPaths,
            ActiveDirectory,
            PassiveDirectory);
}
