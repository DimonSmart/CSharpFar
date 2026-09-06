using CSharpFar.App.Panels;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationPanelWorkspaceRenderer
{
    private readonly Func<CSharpFarPalette> _palette;
    private readonly PanelController _controller;
    private readonly Func<IFileHighlightService?> _highlightService;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;

    public ApplicationPanelWorkspaceRenderer(
        Func<CSharpFarPalette> palette,
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
        bool fileUsage,
        FileUsagePanelController fileUsagePanel,
        DirectorySizeState? quickViewDirState,
        DirectorySummaryMonitor? monitor,
        long? selectedChangeId,
        bool quickViewIsBackgroundUpdating,
        int firstVisibleQuickViewChangeIndex,
        RoutedScrollableList<DirectoryChange>? recentChanges,
        Func<HoverMarqueeRegistration, string>? renderHoverMarquee = null)
    {
        var bounds = ApplicationLayoutService.CalculatePanelWorkspaceBounds(size);
        int panelHeight = bounds.PanelHeight;
        var leftBounds = bounds.Left;
        var rightBounds = bounds.Right;

        var palette = _palette();
        var panelRenderer = new PanelRenderer(canvas, palette, _highlightService(), _panelOptions(), renderHoverMarquee);
        var quickViewRenderer = new QuickViewRenderer(canvas, palette);
        ApplicationPanelFrame? leftFrame = null;
        ApplicationPanelFrame? rightFrame = null;
        ApplicationQuickViewFrame? quickViewFrame = null;
        ApplicationFileUsageFrame? fileUsageFrame = null;

        if (fileUsage)
        {
            var renderer = new FileUsageRenderer(canvas, palette, renderHoverMarquee);
            if (activeSide == PanelSide.Left)
            {
                leftFrame = panelRenderer.Render(leftBounds, left, true, PanelSide.Left, leftViewMode);
                fileUsageFrame = renderer.Render(rightBounds, fileUsagePanel);
            }
            else
            {
                fileUsageFrame = renderer.Render(leftBounds, fileUsagePanel);
                rightFrame = panelRenderer.Render(rightBounds, right, true, PanelSide.Right, rightViewMode);
            }
        }
        else if (quickView)
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
                quickViewDirState, monitor, selectedChangeId, quickViewIsBackgroundUpdating, firstVisibleQuickViewChangeIndex, recentChanges);
            QuickViewRecentChangesMarqueeRenderer.Render(canvas, quickViewFrame, palette, renderHoverMarquee);
        }
        else
        {
            leftFrame = panelRenderer.Render(leftBounds, left, activeSide == PanelSide.Left, PanelSide.Left, leftViewMode);
            rightFrame = panelRenderer.Render(rightBounds, right, activeSide == PanelSide.Right, PanelSide.Right, rightViewMode);
        }

        return new ApplicationPanelWorkspaceFrame(leftBounds, rightBounds, panelHeight, leftFrame, rightFrame, quickViewFrame, fileUsageFrame);
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
        bool quickViewIsBackgroundUpdating,
        int firstVisibleQuickViewChangeIndex,
        RoutedScrollableList<DirectoryChange>? recentChanges)
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
                quickViewDirState,
                monitor,
                selectedChangeId,
                quickViewIsBackgroundUpdating, firstVisibleQuickViewChangeIndex, recentChanges);
        }
        else
        {
            var item = _controller.CurrentItem(right);
            quickViewFrame = quickViewRenderer.Render(
                leftBounds,
                item,
                quickViewDirState,
                monitor,
                selectedChangeId,
                quickViewIsBackgroundUpdating, firstVisibleQuickViewChangeIndex, recentChanges);
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
    ApplicationQuickViewFrame? QuickView,
    ApplicationFileUsageFrame? FileUsage);
