using CSharpFar.FileSystem;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
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
        PanelController controller,
        out PanelCommandUserMenuOperands operands)
    {
        operands = default;
        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !PanelCommandTargetResolver.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
        {
            return false;
        }

        FilePanelItem? item = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, controller, out var resolvedItem)
            ? resolvedItem
            : null;
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;
        IReadOnlyList<string> selected = target.State.SelectedLocations
            .Where(location => location.SourceId == PanelSourceId.Local)
            .Select(location => location.SourcePath).ToArray();
        operands = new PanelCommandUserMenuOperands(
            currentFile,
            selected,
            target.ActiveCommitted?.CurrentDirectory ?? target.State.CurrentDirectory,
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory);
        return true;
    }

    internal static bool CanUseLocalUserMenu(FilePanelSourceRegistry sourceRegistry, ResolvedPanelCommandTarget target)
    {
        FilePanelState state = target.State;
        return PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted) &&
            PanelCommandTargetResolver.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted) &&
            state.SourceId == PanelSourceId.Local &&
            state.ContentKind == PanelContentKind.Source &&
            state.SearchRequest is null &&
            state.LoadError is null &&
            Path.IsPathFullyQualified(state.SourcePath) &&
            sourceRegistry.TryGetSource(PanelSourceId.Local, out var source) &&
            source is LocalFilePanelSource &&
            state.LastSuccessfulLoadLocation is { } loaded &&
            state.SelectedLocations.Comparer.Equals(loaded, state.CurrentLocation);
    }

    public string Expand(string command, string menuDirectory = "") =>
        PlaceholderExpander.Expand(
            command,
            CurrentFile,
            SelectedPaths,
            ActiveDirectory,
            PassiveDirectory,
            menuDirectory);
}
