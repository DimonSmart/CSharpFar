using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class TerminalSurfaceController
{
    private readonly ScreenRenderer _screen;
    private readonly ITerminalScreenMode? _terminalScreenMode;
    private readonly ShellUnderlayService _shellUnderlay;
    private readonly UiTransientState _ui;
    private readonly Func<bool> _hasVisiblePanels;

    public TerminalSurfaceController(
        ScreenRenderer screen,
        ITerminalScreenMode? terminalScreenMode,
        ShellUnderlayService shellUnderlay,
        UiTransientState ui,
        Func<bool> hasVisiblePanels)
    {
        _screen = screen;
        _terminalScreenMode = terminalScreenMode;
        _shellUnderlay = shellUnderlay;
        _ui = ui;
        _hasVisiblePanels = hasVisiblePanels;
    }

    public bool UsesTerminalScreenMode =>
        _terminalScreenMode?.IsSupported == true;

    public TerminalSurfaceDiagnostics GetDiagnostics() =>
        new(
            UsesTerminalScreenMode,
            _terminalScreenMode?.IsSupported,
            _terminalScreenMode?.IsApplicationScreenActive,
            UsesLegacyConsoleMode: !UsesTerminalScreenMode);

    public void CaptureUnderlay() =>
        _shellUnderlay.Capture();

    public void RestoreHiddenScreen() =>
        _shellUnderlay.RestoreForHiddenScreen(_hasVisiblePanels());

    public void PrepareHiddenCommandLineOverlay(ConsoleViewport viewport, int row, int width) =>
        _shellUnderlay.PrepareHiddenCommandLineOverlay(viewport, row, width);

    public void RemoveHiddenCommandLineOverlay() =>
        _shellUnderlay.RemoveHiddenCommandLineOverlay();

    public void RestoreOrClearVisibleArea() =>
        _shellUnderlay.RestoreOrClearVisibleArea();

    public bool HasRenderableViewportChange()
    {
        var viewportChange = GetViewportChange();
        return !AcceptHiddenViewportScroll(viewportChange) &&
            viewportChange != ConsoleViewportChange.None;
    }

    public bool ScrollHiddenViewportToBottomForInput()
    {
        if (_hasVisiblePanels())
            return false;

        bool scrolled = _screen.TryScrollViewportToBottom();
        if (!scrolled)
            return false;

        _shellUnderlay.RemoveHiddenCommandLineOverlay();

        if (UsesTerminalScreenMode)
            SyncRendererWithCurrentMainScreenViewport();
        else
        {
            _shellUnderlay.Capture();
            _ui.LastRenderViewport = _shellUnderlay.CapturedViewport ?? _screen.GetViewport();
        }

        return scrolled;
    }

    public void ApplyMode()
    {
        if (UsesTerminalScreenMode)
        {
            if (_hasVisiblePanels())
                _terminalScreenMode!.EnsureApplicationScreen();
            else
                _terminalScreenMode!.EnsureMainScreen();
            return;
        }

        _shellUnderlay.ApplyLegacyConsoleScrollbackMode(_hasVisiblePanels());
    }

    public void ScrollToBottomAndSyncViewport()
    {
        _screen.TryScrollViewportToBottom();
        _shellUnderlay.RemoveHiddenCommandLineOverlay();
        _ui.LastRenderViewport = _screen.GetViewport();
    }

    public void EnterHiddenMainScreenAtBottom()
    {
        ApplyMode();

        if (UsesTerminalScreenMode)
        {
            _screen.TryScrollViewportToBottom();
            _shellUnderlay.RemoveHiddenCommandLineOverlay();
            SyncRendererWithCurrentMainScreenViewport();
            return;
        }

        _shellUnderlay.RestoreForHiddenScreen(_hasVisiblePanels());
    }

    public void PrepareMainScreenForExternalCommand()
    {
        if (UsesTerminalScreenMode)
        {
            _terminalScreenMode!.EnsureMainScreen();
            _screen.TryScrollViewportToBottom();
            _shellUnderlay.RemoveHiddenCommandLineOverlay();
            SyncRendererWithCurrentMainScreenViewport();
            return;
        }

        _shellUnderlay.RemoveHiddenCommandLineOverlay();
        _screen.SetConsoleScrollbackEnabled(true);
    }

    public void RestoreTerminal() =>
        _terminalScreenMode?.RestoreTerminal();

    private ConsoleViewportChange GetViewportChange()
    {
        if (!_ui.LastRenderViewport.HasValue)
            return ConsoleViewportChange.None;

        var viewport = _screen.GetViewport();
        var last = _ui.LastRenderViewport.Value;
        if (viewport == last)
            return ConsoleViewportChange.None;

        return viewport.Width == last.Width && viewport.Height == last.Height
            ? ConsoleViewportChange.OriginOnly
            : ConsoleViewportChange.Size;
    }

    private bool AcceptHiddenViewportScroll(ConsoleViewportChange viewportChange)
    {
        if (_hasVisiblePanels() || viewportChange != ConsoleViewportChange.OriginOnly)
            return false;

        _ui.LastRenderViewport = _screen.GetViewport();
        return true;
    }

    private void SyncRendererWithCurrentMainScreenViewport()
    {
        _shellUnderlay.Capture();
        _ui.LastRenderViewport = _shellUnderlay.CapturedViewport ?? _screen.GetViewport();
    }

    private enum ConsoleViewportChange
    {
        None,
        OriginOnly,
        Size,
    }
}
