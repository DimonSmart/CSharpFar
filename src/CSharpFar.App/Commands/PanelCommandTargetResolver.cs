using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class PanelCommandTargetResolver(PanelWorkspaceController workspace)
{
    public ResolvedPanelCommandTarget ResolvePanelTarget(object? args)
    {
        if (args is ApplicationPanelCommandInvocation invocation)
        {
            PanelSide passiveSide = OtherPanelSide(invocation.Side);
            return new ResolvedPanelCommandTarget(
                invocation.Side,
                workspace.GetPanelState(invocation.Side),
                passiveSide,
                workspace.GetPanelState(passiveSide),
                invocation.VisibleRows,
                invocation.ActivePanel,
                invocation.PassivePanel);
        }

        if (args is PanelCommandArgs panelArgs)
        {
            PanelSide passiveSide = OtherPanelSide(panelArgs.PanelSide);
            return new ResolvedPanelCommandTarget(
                panelArgs.PanelSide,
                workspace.GetPanelState(panelArgs.PanelSide),
                passiveSide,
                workspace.GetPanelState(passiveSide),
                workspace.VisibleRows(panelArgs.PanelSide),
                null,
                null);
        }

        return new ResolvedPanelCommandTarget(
            workspace.ActiveSide,
            workspace.ActiveState,
            OtherPanelSide(workspace.ActiveSide),
            workspace.PassiveState,
            workspace.VisibleRows(),
            null,
            null);
    }

    public static bool TryResolveCommittedCurrentItem(
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed,
        PanelController controller,
        out FilePanelItem item)
    {
        if (committed is null)
        {
            item = controller.CurrentItem(state)!;
            return item is not null;
        }

        item = null!;
        if (committed.CurrentItemIndex is not { } index ||
            index < 0 ||
            index >= state.Items.Count)
        {
            return false;
        }

        FilePanelItem candidate = state.Items[index];
        if (committed.CurrentItemLocation is not { } location || !state.SelectedLocations.Comparer.Equals(candidate.Location, location))
            return false;

        item = candidate;
        return true;
    }

    public static bool CommittedLocationMatches(
        FilePanelState state,
        ApplicationPanelKeyboardFrame? committed) =>
        committed is null ||
        state.SelectedLocations.Comparer.Equals(state.CurrentLocation, committed.CurrentLocation);

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
}
