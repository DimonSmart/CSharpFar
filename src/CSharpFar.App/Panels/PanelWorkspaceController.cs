using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelWorkspaceController
{
    private readonly ScreenRenderer _screen;
    private readonly ApplicationSession _session;
    private readonly PanelQuickSearchController _panelQuickSearch;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;

    public PanelWorkspaceController(
        ScreenRenderer screen,
        ApplicationSession session,
        PanelQuickSearchController panelQuickSearch,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions)
    {
        _screen = screen;
        _session = session;
        _panelQuickSearch = panelQuickSearch;
        _panelOptions = panelOptions;
    }

    public PanelSide ActiveSide
    {
        get => _session.Panels.ActiveSide;
        set => SetActiveSide(value);
    }

    public FilePanelState ActiveState =>
        ActiveSide == PanelSide.Left ? _session.Panels.Left : _session.Panels.Right;

    public FilePanelState PassiveState =>
        ActiveSide == PanelSide.Left ? _session.Panels.Right : _session.Panels.Left;

    public bool HasHiddenPanels =>
        _session.App.HiddenPanels != HiddenPanels.None;

    public bool HasVisiblePanels =>
        _session.App.HiddenPanels != HiddenPanels.Both;

    public bool IsPanelVisible(PanelSide side) =>
        (_session.App.HiddenPanels & HiddenPanelFlag(side)) == 0;

    public FilePanelState GetPanelState(PanelSide side) =>
        side == PanelSide.Left ? _session.Panels.Left : _session.Panels.Right;

    public PanelSide PanelSideForState(FilePanelState state) =>
        ReferenceEquals(state, _session.Panels.Left) ? PanelSide.Left : PanelSide.Right;

    public int VisibleRows() =>
        VisibleRows(ActiveViewMode);

    public int VisibleRows(PanelSide side)
    {
        var mode = side == PanelSide.Left
            ? _session.Panels.LeftViewMode
            : _session.Panels.RightViewMode;
        return VisibleRows(mode);
    }

    public void EnsureActivePanelVisible()
    {
        if (IsPanelVisible(ActiveSide))
            return;

        var otherSide = OtherPanelSide(ActiveSide);
        if (IsPanelVisible(otherSide))
            SetActiveSide(otherSide);
    }

    public void SetActiveSide(PanelSide side)
    {
        if (_session.Panels.ActiveSide == side)
            return;

        _panelQuickSearch.Close();
        _session.Panels.ActiveSide = side;
    }

    public static HiddenPanels HiddenPanelFlag(PanelSide side) =>
        side == PanelSide.Left ? HiddenPanels.Left : HiddenPanels.Right;

    private PanelViewMode ActiveViewMode =>
        ActiveSide == PanelSide.Left
            ? _session.Panels.LeftViewMode
            : _session.Panels.RightViewMode;

    private int VisibleRows(PanelViewMode mode)
    {
        var size = _screen.GetSize();
        int panelHeight = ApplicationLayoutService.PanelHeight(size);
        var bounds = new Rect(0, 0, 0, panelHeight);
        return mode == PanelViewMode.BriefTwoColumns
            ? BriefTwoColumnsPanelRenderer.VisibleRows(bounds, _panelOptions())
            : PanelRenderer.VisibleRows(bounds, _panelOptions());
    }

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
}
