using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationPanelWorkspaceRenderer
{
    private readonly Func<ConsolePalette> _palette;
    private readonly PanelController _controller;
    private readonly Func<IFileHighlightService?> _highlightService;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;

    public ApplicationPanelWorkspaceRenderer(
        Func<ConsolePalette> palette,
        PanelController controller,
        Func<IFileHighlightService?> highlightService,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions)
    {
        _palette = palette;
        _controller = controller;
        _highlightService = highlightService;
        _panelOptions = panelOptions;
    }

    public ApplicationPanelWorkspaceFrame Render(
        IUiCanvas canvas,
        ConsoleSize size,
        FilePanelState left,
        FilePanelState right,
        PanelSide activeSide,
        PanelViewMode leftViewMode,
        PanelViewMode rightViewMode,
        bool quickView,
        DirectorySizeState? quickViewDirState,
        DirectorySummaryMonitor? monitor,
        long? selectedChangeId,
        bool quickViewIsBackgroundUpdating)
    {
        var bounds = ApplicationLayoutService.CalculatePanelWorkspaceBounds(size);
        int panelHeight = bounds.PanelHeight;
        var leftBounds = bounds.Left;
        var rightBounds = bounds.Right;

        var palette = _palette();
        var panelRenderer = new PanelRenderer(canvas, palette, _highlightService(), _panelOptions());
        var quickViewRenderer = new QuickViewRenderer(canvas, palette);
        ApplicationPanelFrame? leftFrame = null;
        ApplicationPanelFrame? rightFrame = null;
        ApplicationQuickViewFrame? quickViewFrame = null;

        if (quickView)
        {
            (leftFrame, rightFrame, quickViewFrame) = RenderQuickView(
                panelRenderer,
                quickViewRenderer,
                leftBounds,
                rightBounds,
                left,
                right,
                activeSide,
                leftViewMode,
                rightViewMode,
                quickViewDirState, monitor, selectedChangeId, quickViewIsBackgroundUpdating);
        }
        else
        {
            leftFrame = panelRenderer.Render(leftBounds, left, activeSide == PanelSide.Left, PanelSide.Left, leftViewMode);
            rightFrame = panelRenderer.Render(rightBounds, right, activeSide == PanelSide.Right, PanelSide.Right, rightViewMode);
        }

        return new ApplicationPanelWorkspaceFrame(leftBounds, rightBounds, panelHeight, leftFrame, rightFrame, quickViewFrame);
    }

    private (ApplicationPanelFrame? Left, ApplicationPanelFrame? Right, ApplicationQuickViewFrame? QuickView) RenderQuickView(
        PanelRenderer panelRenderer,
        QuickViewRenderer quickViewRenderer,
        Rect leftBounds,
        Rect rightBounds,
        FilePanelState left,
        FilePanelState right,
        PanelSide activeSide,
        PanelViewMode leftViewMode,
        PanelViewMode rightViewMode,
        DirectorySizeState? quickViewDirState,
        DirectorySummaryMonitor? monitor,
        long? selectedChangeId,
        bool quickViewIsBackgroundUpdating)
    {
        ApplicationPanelFrame? leftFrame = null;
        ApplicationPanelFrame? rightFrame = null;
        ApplicationQuickViewFrame? quickViewFrame = null;
        if (activeSide == PanelSide.Left)
        {
            var item = _controller.CurrentItem(left);
            leftFrame = panelRenderer.Render(leftBounds, left, true, PanelSide.Left, leftViewMode);
            quickViewFrame = quickViewRenderer.Render(
                rightBounds,
                item,
                item is { IsDirectory: true } ? quickViewDirState : null,
                item is { IsDirectory: true } ? monitor : null,
                selectedChangeId,
                quickViewIsBackgroundUpdating);
        }
        else
        {
            var item = _controller.CurrentItem(right);
            quickViewFrame = quickViewRenderer.Render(
                leftBounds,
                item,
                item is { IsDirectory: true } ? quickViewDirState : null,
                item is { IsDirectory: true } ? monitor : null,
                selectedChangeId,
                quickViewIsBackgroundUpdating);
            rightFrame = panelRenderer.Render(rightBounds, right, true, PanelSide.Right, rightViewMode);
        }

        return (leftFrame, rightFrame, quickViewFrame);
    }
}

internal readonly record struct ApplicationPanelWorkspaceFrame(
    Rect Left,
    Rect Right,
    int PanelHeight,
    ApplicationPanelFrame? LeftPanel,
    ApplicationPanelFrame? RightPanel,
    ApplicationQuickViewFrame? QuickView);
