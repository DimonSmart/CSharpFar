using System.Diagnostics;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class TerminalSurfaceController
{
    private static readonly TimeSpan ResizeSampleInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ResizeQuietInterval = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan ResizeStabilizationTimeout = TimeSpan.FromSeconds(2);

    private readonly ScreenRenderer _screen;
    private readonly ITerminalScreenMode? _terminalScreenMode;
    private readonly ShellUnderlayService _shellUnderlay;
    private readonly UiTransientState _ui;
    private readonly Func<ApplicationWorkspaceMode> _workspaceMode;
    private bool _hiddenViewportPinnedToBottom;
    private bool _hiddenResizeStartedPinnedToBottom;

    public TerminalSurfaceController(
        ScreenRenderer screen,
        ITerminalScreenMode? terminalScreenMode,
        ShellUnderlayService shellUnderlay,
        UiTransientState ui,
        Func<ApplicationWorkspaceMode> workspaceMode)
    {
        _screen = screen;
        _terminalScreenMode = terminalScreenMode;
        _shellUnderlay = shellUnderlay;
        _ui = ui;
        _workspaceMode = workspaceMode;
    }

    public bool UsesTerminalScreenMode =>
        _terminalScreenMode?.IsSupported == true;

    public bool IsHiddenViewportPinnedToBottom => _hiddenViewportPinnedToBottom;

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

    public TerminalSurfaceSnapshot GetSnapshot() =>
        new(_screen.GetViewport(), _screen.GetSize());

    public void CaptureUnderlay() =>
        _shellUnderlay.Capture();

    public void RestoreHiddenScreen() =>
        _shellUnderlay.RestoreForHiddenScreen(IsPanelsMode);

    public void PrepareHiddenOverlay(ConsoleViewport viewport, Rect bounds) =>
        _shellUnderlay.PrepareHiddenOverlay(viewport, bounds);

    public void RemoveHiddenOverlay() =>
        _shellUnderlay.RemoveHiddenOverlay();

    public void RestoreOrClearVisibleArea() =>
        _shellUnderlay.RestoreOrClearVisibleArea();

    public bool ScrollHiddenViewportToBottomForInput()
    {
        if (IsPanelsMode)
            return false;

        bool scrolled = _screen.TryScrollViewportToBottom();
        if (!scrolled)
        {
            RefreshHiddenViewportPinnedState();
            return false;
        }

        _shellUnderlay.RemoveHiddenOverlay();

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
            if (IsPanelsMode)
                _terminalScreenMode!.EnsureApplicationScreen();
            else
                _terminalScreenMode!.EnsureMainScreen();
            return;
        }

        _shellUnderlay.ApplyLegacyConsoleScrollbackMode(IsPanelsMode);
    }

    public void ScrollToBottomAndSyncViewport()
    {
        _screen.TryScrollViewportToBottom();
        _shellUnderlay.RemoveHiddenOverlay();
        _ui.LastRenderViewport = _screen.GetViewport();
        _hiddenViewportPinnedToBottom = true;
    }

    public void EnterHiddenMainScreenAtBottom()
    {
        ApplyMode();

        if (UsesTerminalScreenMode)
        {
            _screen.TryScrollViewportToBottom();
            _shellUnderlay.RemoveHiddenOverlay();
            SyncRendererWithCurrentMainScreenViewport();
            _hiddenViewportPinnedToBottom = true;
            return;
        }

        _shellUnderlay.RestoreForHiddenScreen(IsPanelsMode);
        RefreshHiddenViewportPinnedState();
    }

    public void PrepareHiddenResize()
    {
        if (IsPanelsMode)
            return;

        HiddenResizeTrace.Write(
            $"PrepareHiddenResize start pinned={_hiddenViewportPinnedToBottom} viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");

        HiddenResizeTrace.Write(
            $"PrepareHiddenResize stableViewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
        _shellUnderlay.RemoveHiddenOverlay();
        HiddenResizeTrace.Write(
            $"PrepareHiddenResize afterOverlayRemove viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");

        if (UsesTerminalScreenMode)
            SyncRendererWithCurrentMainScreenViewport();

        HiddenResizeTrace.Write(
            $"PrepareHiddenResize done viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())} last={(_ui.LastRenderViewport.HasValue ? HiddenResizeTrace.Viewport(_ui.LastRenderViewport.Value) : "<none>")}");
    }

    public void BeginHiddenResize()
    {
        if (IsPanelsMode)
            return;

        _hiddenResizeStartedPinnedToBottom = _hiddenViewportPinnedToBottom;
        if (_hiddenResizeStartedPinnedToBottom)
            _screen.TryScrollViewportToBottom();

        HiddenResizeTrace.Write(
            $"BeginHiddenResize remove transient overlay viewport={HiddenResizeTrace.Viewport(_screen.GetViewport())}");
        _shellUnderlay.RemoveHiddenOverlayAfterReflow();
    }

    public ConsoleViewport WaitForStableHiddenGeometry()
    {
        ConsoleViewport current = _screen.GetViewport();
        var elapsed = Stopwatch.StartNew();
        TimeSpan stableSince = elapsed.Elapsed;

        HiddenResizeTrace.Write($"Resize stabilization started viewport={HiddenResizeTrace.Viewport(current)}");
        while (elapsed.Elapsed < ResizeStabilizationTimeout)
        {
            Thread.Sleep(ResizeSampleInterval);
            ConsoleViewport sample = _screen.GetViewport();
            if (sample != current)
            {
                HiddenResizeTrace.Write(
                    $"Resize geometry changed from={HiddenResizeTrace.Viewport(current)} to={HiddenResizeTrace.Viewport(sample)}; recovery coalesced");
                current = sample;
                stableSince = elapsed.Elapsed;
                continue;
            }

            if (elapsed.Elapsed - stableSince >= ResizeQuietInterval)
            {
                HiddenResizeTrace.Write($"Resize geometry stable viewport={HiddenResizeTrace.Viewport(current)}");
                return current;
            }
        }

        HiddenResizeTrace.Write($"Resize stabilization timed out viewport={HiddenResizeTrace.Viewport(current)}");
        return current;
    }

    public void MarkHiddenCommandLineRenderCompleted()
    {
        if (IsPanelsMode)
            return;

        _shellUnderlay.CaptureRenderedHiddenOverlay();

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
            _shellUnderlay.RemoveHiddenOverlay();
            SyncRendererWithCurrentMainScreenViewport();
            _hiddenViewportPinnedToBottom = true;
            return;
        }

        _shellUnderlay.RemoveHiddenOverlay();
        _screen.SetConsoleScrollbackEnabled(true);
    }

    public void RestoreTerminal() =>
        _terminalScreenMode?.RestoreTerminal();

    public bool TryAcceptViewportChange(ConsoleViewport viewport, ConsoleViewportChange change)
    {
        if (IsPanelsMode || change != ConsoleViewportChange.OriginOnly)
            return false;

        _ui.LastRenderViewport = viewport;
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

    private bool IsPanelsMode =>
        _workspaceMode() == ApplicationWorkspaceMode.Panels;

}
