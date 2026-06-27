using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class TerminalSurfaceController
{
    private readonly ScreenRenderer _screen;
    private readonly ITerminalScreenMode? _terminalScreenMode;
    private readonly ShellUnderlayService _shellUnderlay;
    private readonly UiTransientState _ui;
    private readonly Func<bool> _hasVisiblePanels;
    private bool _hiddenViewportPinnedToBottom;
    private bool _hiddenResizeStartedPinnedToBottom;

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

    public TerminalSurfaceDiagnostics GetDiagnostics()
    {
        var input = _screen.GetInputDiagnostics();
        return new(
            UsesTerminalScreenMode,
            _terminalScreenMode?.IsSupported,
            _terminalScreenMode?.IsApplicationScreenActive,
            UsesLegacyConsoleMode: !UsesTerminalScreenMode,
            ConsoleDriver: _screen.ConsoleDriverName,
            InputBackend: input?.InputBackendName ?? "unknown",
            MouseTrackingEnabled: input?.MouseTrackingEnabled,
            ModifierKeyTracking: input?.ModifierKeyTracking ?? new ModifierKeyTrackingSnapshot(
                "none",
                IsPlatformSupported: false,
                IsEnabled: false,
                CanTrackShiftOnly: false,
                Status: ModifierKeyTrackingStatus.PlatformNotSupported,
                FailureReason: null,
                Devices: []));
    }

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
        HiddenResizeTrace.Write(
            $"HasRenderableViewportChange change={viewportChange} visible={_hasVisiblePanels()} current={HiddenResizeTrace.Viewport(_screen.GetViewport())} last={(_ui.LastRenderViewport.HasValue ? HiddenResizeTrace.Viewport(_ui.LastRenderViewport.Value) : "<none>")}");
        return !AcceptHiddenViewportScroll(viewportChange) &&
            viewportChange != ConsoleViewportChange.None;
    }

    public bool ScrollHiddenViewportToBottomForInput()
    {
        if (_hasVisiblePanels())
            return false;

        bool scrolled = _screen.TryScrollViewportToBottom();
        if (!scrolled)
        {
            RefreshHiddenViewportPinnedState();
            return false;
        }

        _shellUnderlay.RemoveHiddenCommandLineOverlay();

        if (UsesTerminalScreenMode)
            SyncRendererWithCurrentMainScreenViewport();
        else
        {
            _shellUnderlay.Capture();
            _ui.LastRenderViewport = _shellUnderlay.CapturedViewport ?? _screen.GetViewport();
        }

        _hiddenViewportPinnedToBottom = true;
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
        _hiddenViewportPinnedToBottom = true;
    }

    public void EnterHiddenMainScreenAtBottom()
    {
        ApplyMode();

        if (UsesTerminalScreenMode)
        {
            _screen.TryScrollViewportToBottom();
            _shellUnderlay.RemoveHiddenCommandLineOverlay();
            SyncRendererWithCurrentMainScreenViewport();
            _hiddenViewportPinnedToBottom = true;
            return;
        }

        _shellUnderlay.RestoreForHiddenScreen(_hasVisiblePanels());
        RefreshHiddenViewportPinnedState();
    }

    public void PrepareHiddenResize()
    {
        if (_hasVisiblePanels())
            return;

        HiddenResizeTrace.Write(
            $"PrepareHiddenResize start pinned={_hiddenViewportPinnedToBottom} viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");

        _hiddenResizeStartedPinnedToBottom = _hiddenViewportPinnedToBottom;
        if (_hiddenResizeStartedPinnedToBottom)
            _screen.TryScrollViewportToBottom();

        HiddenResizeTrace.Write(
            $"PrepareHiddenResize afterScroll viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
        _shellUnderlay.RemoveHiddenCommandLineOverlay();
        HiddenResizeTrace.Write(
            $"PrepareHiddenResize afterOverlayRemove viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");

        if (UsesTerminalScreenMode)
            SyncRendererWithCurrentMainScreenViewport();

        HiddenResizeTrace.Write(
            $"PrepareHiddenResize done viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())} last={(_ui.LastRenderViewport.HasValue ? HiddenResizeTrace.Viewport(_ui.LastRenderViewport.Value) : "<none>")}");
    }

    public void MarkHiddenCommandLineRenderCompleted()
    {
        if (_hasVisiblePanels())
            return;

        if (_hiddenResizeStartedPinnedToBottom)
        {
            _hiddenViewportPinnedToBottom = true;
            _hiddenResizeStartedPinnedToBottom = false;
            HiddenResizeTrace.Write(
                $"MarkHiddenCommandLineRenderCompleted preservePinned viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
            return;
        }

        RefreshHiddenViewportPinnedState();
        HiddenResizeTrace.Write(
            $"MarkHiddenCommandLineRenderCompleted refreshed pinned={_hiddenViewportPinnedToBottom} viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
    }

    public void PrepareMainScreenForExternalCommand()
    {
        if (UsesTerminalScreenMode)
        {
            _terminalScreenMode!.EnsureMainScreen();
            _screen.TryScrollViewportToBottom();
            _shellUnderlay.RemoveHiddenCommandLineOverlay();
            SyncRendererWithCurrentMainScreenViewport();
            _hiddenViewportPinnedToBottom = true;
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
        RefreshHiddenViewportPinnedState();
        HiddenResizeTrace.Write(
            $"AcceptHiddenViewportScroll pinned={_hiddenViewportPinnedToBottom} viewport={HiddenResizeTrace.Viewport(_ui.LastRenderViewport.Value)}");
        return true;
    }

    private void SyncRendererWithCurrentMainScreenViewport()
    {
        HiddenResizeTrace.Write(
            $"SyncRendererWithCurrentMainScreenViewport before={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
        _shellUnderlay.Capture();
        _ui.LastRenderViewport = _shellUnderlay.CapturedViewport ?? _screen.GetViewport();
        HiddenResizeTrace.Write(
            $"SyncRendererWithCurrentMainScreenViewport after captured={(_shellUnderlay.CapturedViewport.HasValue ? HiddenResizeTrace.Viewport(_shellUnderlay.CapturedViewport.Value) : "<none>")} current={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
    }

    private void RefreshHiddenViewportPinnedState()
    {
        if (_screen.TryIsViewportAtBottom(out bool isAtBottom))
            _hiddenViewportPinnedToBottom = isAtBottom;
    }

    private enum ConsoleViewportChange
    {
        None,
        OriginOnly,
        Size,
    }
}
