using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.App.Viewer;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationPanelWorkspaceRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;
    private readonly PanelController _controller;
    private readonly Func<IFileHighlightService?> _highlightService;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;

    public ApplicationPanelWorkspaceRenderer(
        ScreenRenderer screen,
        Func<ConsolePalette> palette,
        PanelController controller,
        Func<IFileHighlightService?> highlightService,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions)
    {
        _screen = screen;
        _palette = palette;
        _controller = controller;
        _highlightService = highlightService;
        _panelOptions = panelOptions;
    }

    public ApplicationPanelWorkspaceFrame Render(
        ConsoleSize size,
        FilePanelState left,
        FilePanelState right,
        PanelSide activeSide,
        PanelViewMode leftViewMode,
        PanelViewMode rightViewMode,
        bool quickView,
        DirectorySizeState? quickViewDirState)
    {
        var bounds = ApplicationLayoutService.CalculatePanelWorkspaceBounds(size);
        int panelHeight = bounds.PanelHeight;
        var leftBounds = bounds.Left;
        var rightBounds = bounds.Right;

        var palette = _palette();
        var panelRenderer = new PanelRenderer(_screen, palette, _highlightService(), _panelOptions());
        var quickViewRenderer = new QuickViewRenderer(_screen, palette);
        ApplicationPanelFrame? leftFrame = null;
        ApplicationPanelFrame? rightFrame = null;

        if (quickView)
        {
            (leftFrame, rightFrame) = RenderQuickView(
                panelRenderer,
                quickViewRenderer,
                leftBounds,
                rightBounds,
                left,
                right,
                activeSide,
                leftViewMode,
                rightViewMode,
                quickViewDirState);
        }
        else
        {
            leftFrame = panelRenderer.Render(leftBounds, left, activeSide == PanelSide.Left, PanelSide.Left, leftViewMode);
            rightFrame = panelRenderer.Render(rightBounds, right, activeSide == PanelSide.Right, PanelSide.Right, rightViewMode);
        }

        return new ApplicationPanelWorkspaceFrame(leftBounds, rightBounds, panelHeight, leftFrame, rightFrame);
    }

    private (ApplicationPanelFrame? Left, ApplicationPanelFrame? Right) RenderQuickView(
        PanelRenderer panelRenderer,
        QuickViewRenderer quickViewRenderer,
        Rect leftBounds,
        Rect rightBounds,
        FilePanelState left,
        FilePanelState right,
        PanelSide activeSide,
        PanelViewMode leftViewMode,
        PanelViewMode rightViewMode,
        DirectorySizeState? quickViewDirState)
    {
        ApplicationPanelFrame? leftFrame = null;
        ApplicationPanelFrame? rightFrame = null;
        if (activeSide == PanelSide.Left)
        {
            var item = _controller.CurrentItem(left);
            leftFrame = panelRenderer.Render(leftBounds, left, true, PanelSide.Left, leftViewMode);
            quickViewRenderer.Render(
                rightBounds,
                item,
                item is { IsDirectory: true } ? quickViewDirState : null);
        }
        else
        {
            var item = _controller.CurrentItem(right);
            quickViewRenderer.Render(
                leftBounds,
                item,
                item is { IsDirectory: true } ? quickViewDirState : null);
            rightFrame = panelRenderer.Render(rightBounds, right, true, PanelSide.Right, rightViewMode);
        }

        return (leftFrame, rightFrame);
    }
}

internal readonly record struct ApplicationPanelWorkspaceFrame(
    Rect Left,
    Rect Right,
    int PanelHeight,
    ApplicationPanelFrame? LeftPanel,
    ApplicationPanelFrame? RightPanel);
