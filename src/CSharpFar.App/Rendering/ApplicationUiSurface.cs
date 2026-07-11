using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationUiSurface : IUiSurface
{
    private readonly ApplicationRenderContext _context;
    private readonly ApplicationRenderCoordinator _coordinator;
    private bool _hidden;

    public ApplicationUiSurface(ApplicationRenderContext context, ApplicationRenderCoordinator coordinator)
    {
        _context = context;
        _coordinator = coordinator;
    }

    public IDisposable BeginFrame(UiRenderRequest request)
    {
        _hidden = !_context.HasVisiblePanels();
        _context.TerminalSurface.ApplyMode();
        _context.Screen.SetRenderingOutputMode(true);

        if (!_hidden)
            return _context.Screen.BeginFrame();

        if (request.IsResizeRecovery)
        {
            if (_context.TerminalSurface.UsesTerminalScreenMode)
                _context.TerminalSurface.PrepareHiddenResize();
            else
                _context.TerminalSurface.RestoreHiddenScreen();
        }

        var viewport = _context.Screen.GetViewport();
        var row = ApplicationLayoutService.CommandLineRow(viewport.Size);
        _context.TerminalSurface.PrepareHiddenCommandLineOverlay(viewport, row, viewport.Width);
        return _context.TerminalSurface.UsesTerminalScreenMode
            ? _context.Screen.BeginFrameFromCurrentViewportCapture()
            : _context.Screen.BeginFrame();
    }

    public void Render(UiRenderContext context)
    {
        if (_hidden)
            _coordinator.RenderHiddenCommandLineContent(context);
        else
            _coordinator.RenderMainContent(context);
    }

    public void CompleteFrame(UiFrameCompletion completion)
    {
        if (_hidden && completion.WasCommitted)
            _context.TerminalSurface.MarkHiddenCommandLineRenderCompleted();
    }
}
