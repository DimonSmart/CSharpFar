using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct UiRenderRequest(bool IsResizeRecovery);

public readonly record struct UiRenderContext(ScreenRenderer Screen, ConsoleViewport Viewport)
{
    public ConsoleSize Size => Viewport.Size;
}

public readonly record struct UiFrameCompletion(
    UiRenderRequest Request,
    ConsoleViewport Viewport,
    bool WasInterrupted);

public interface IUiSurface
{
    IDisposable BeginFrame(UiRenderRequest request);

    void Render(UiRenderContext context);

    void CompleteFrame(UiFrameCompletion completion);
}

public sealed class ScreenRendererSurface : IUiSurface
{
    private readonly ScreenRenderer _screen;
    private readonly Action<UiRenderContext> _render;

    public ScreenRendererSurface(ScreenRenderer screen, Action<UiRenderContext> render)
    {
        _screen = screen;
        _render = render;
    }

    public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();

    public void Render(UiRenderContext context) => _render(context);

    public void CompleteFrame(UiFrameCompletion completion)
    {
    }
}

public sealed class UiCompositionHost
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScreenRenderer, UiCompositionHost> Hosts = new();
    private readonly List<UiLayerEntry> _layers = [];
    private bool _isRendering;

    public UiCompositionHost(ScreenRenderer screen)
    {
        Screen = screen;
        Hosts.Add(screen, this);
    }

    public ScreenRenderer Screen { get; }

    internal static UiCompositionHost For(ScreenRenderer screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (Hosts.TryGetValue(screen, out var host))
            return host;

        host = new UiCompositionHost(screen);
        host.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        return host;
    }

    public ConsoleViewport? LastStableViewport { get; private set; }

    public void SetRootSurface(IUiSurface surface)
    {
        EnsureNotRendering();
        ArgumentNullException.ThrowIfNull(surface);
        if (_layers.Skip(1).Any(entry => entry.Kind == UiLayerKind.Surface))
            throw new InvalidOperationException("Cannot replace the root surface while a temporary surface is active.");

        if (_layers.Count == 0)
            _layers.Add(UiLayerEntry.ForSurface(surface));
        else
            _layers[0] = UiLayerEntry.ForSurface(surface);
    }

    public UiSurfaceSession OpenSurface(Action<UiRenderContext> render) =>
        OpenSurface(new ScreenRendererSurface(Screen, render));

    public UiSurfaceSession OpenSurface(IUiSurface surface)
    {
        EnsureNotRendering();
        ArgumentNullException.ThrowIfNull(surface);
        EnsureRootSurface();
        var entry = UiLayerEntry.ForSurface(surface);
        _layers.Add(entry);
        return new UiSurfaceSession(this, entry);
    }

    internal UiLayerScope PushOverlay(Action<UiRenderContext> render)
    {
        EnsureNotRendering();
        ArgumentNullException.ThrowIfNull(render);
        EnsureRootSurface();
        var entry = UiLayerEntry.ForOverlay(render);
        _layers.Add(entry);
        return new UiLayerScope(this, entry);
    }

    public bool HasViewportChanged() =>
        LastStableViewport is { } viewport && Screen.GetViewport() != viewport;

    public void Render(bool isResizeRecovery = false)
    {
        EnsureRootSurface();
        if (_isRendering)
            throw new InvalidOperationException("UI composition cannot be rendered recursively.");

        _isRendering = true;
        try
        {
            while (true)
            {
                var request = new UiRenderRequest(isResizeRecovery);
                var composition = CaptureActiveComposition();
                ConsoleViewport viewport;

                using (composition.Surface.BeginFrame(request))
                {
                    viewport = Screen.FrameViewport;
                    var context = new UiRenderContext(Screen, viewport);
                    composition.Surface.Render(context);
                    foreach (var overlay in composition.Overlays)
                        overlay(context);
                }

                var interrupted = Screen.FrameWasInterrupted;
                composition.Surface.CompleteFrame(new UiFrameCompletion(request, viewport, interrupted));
                if (interrupted)
                {
                    isResizeRecovery = true;
                    continue;
                }

                Screen.DrainResizeEvents();
                if (Screen.GetViewport() != viewport)
                {
                    isResizeRecovery = true;
                    continue;
                }

                LastStableViewport = viewport;
                return;
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    private ActiveComposition CaptureActiveComposition()
    {
        int surfaceIndex = _layers.FindLastIndex(entry => entry.Kind == UiLayerKind.Surface);
        if (surfaceIndex < 0)
            throw new InvalidOperationException("A root UI surface must be set before composition rendering.");

        return new ActiveComposition(
            _layers[surfaceIndex].SurfaceRenderer!,
            _layers.Skip(surfaceIndex + 1).Select(entry => entry.OverlayRenderer!).ToArray());
    }

    private void CloseSurface(UiLayerEntry entry)
    {
        EnsureNotRendering();
        if (_layers.Count <= 1 || !ReferenceEquals(_layers[^1], entry) || entry.Kind != UiLayerKind.Surface)
            throw new InvalidOperationException("Temporary surfaces must be disposed in LIFO order.");
        _layers.RemoveAt(_layers.Count - 1);
    }

    private void CloseOverlay(UiLayerEntry entry)
    {
        EnsureNotRendering();
        if (_layers.Count == 0 || !ReferenceEquals(_layers[^1], entry) || entry.Kind != UiLayerKind.Overlay)
            throw new InvalidOperationException("Overlays must be disposed in LIFO order.");
        _layers.RemoveAt(_layers.Count - 1);
    }

    private void EnsureRootSurface()
    {
        if (_layers.Count == 0)
            throw new InvalidOperationException("A root UI surface must be set before composition rendering.");
    }

    private void EnsureNotRendering()
    {
        if (_isRendering)
            throw new InvalidOperationException("UI layers cannot be changed while composition is rendering.");
    }

    public sealed class UiSurfaceSession : IDisposable
    {
        private UiCompositionHost? _host;
        private readonly UiLayerEntry _entry;

        internal UiSurfaceSession(UiCompositionHost host, UiLayerEntry entry) => (_host, _entry) = (host, entry);

        public void Render() => (_host ?? throw new ObjectDisposedException(nameof(UiSurfaceSession))).Render();

        public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default) =>
            (_host ?? throw new ObjectDisposedException(nameof(UiSurfaceSession))).ReadCompositionInput(cancellationToken);

        public bool TryReadInput(out ConsoleInputEvent? input) =>
            (_host ?? throw new ObjectDisposedException(nameof(UiSurfaceSession))).TryReadCompositionInput(out input);

        public void Dispose()
        {
            var host = _host;
            if (host is null)
                return;
            host.CloseSurface(_entry);
            _host = null;
            host.Render();
        }
    }

    internal ConsoleInputEvent ReadCompositionInput(CancellationToken cancellationToken)
    {
        while (true)
        {
            var input = Screen.ReadInput(cancellationToken);
            if (input is ConsoleResizeInputEvent || HasViewportChanged())
            {
                Render(isResizeRecovery: true);
                if (input is ConsoleResizeInputEvent)
                    continue;
            }
            return input;
        }
    }

    internal bool TryReadCompositionInput(out ConsoleInputEvent? input)
    {
        while (Screen.TryReadInput(out input))
        {
            if (input is ConsoleResizeInputEvent || HasViewportChanged())
            {
                Render(isResizeRecovery: true);
                if (input is ConsoleResizeInputEvent)
                    continue;
            }
            return true;
        }
        input = null;
        return false;
    }

    internal sealed class UiLayerScope : IDisposable
    {
        private UiCompositionHost? _host;
        private readonly UiLayerEntry _entry;

        internal UiLayerScope(UiCompositionHost host, UiLayerEntry entry) => (_host, _entry) = (host, entry);

        public void Dispose()
        {
            var host = _host;
            if (host is null)
                return;

            host.CloseOverlay(_entry);
            _host = null;
        }
    }

    internal enum UiLayerKind { Surface, Overlay }

    internal sealed class UiLayerEntry
    {
        private UiLayerEntry(UiLayerKind kind, IUiSurface? surface, Action<UiRenderContext>? overlay) =>
            (Kind, SurfaceRenderer, OverlayRenderer) = (kind, surface, overlay);

        public UiLayerKind Kind { get; }
        public IUiSurface? SurfaceRenderer { get; }
        public Action<UiRenderContext>? OverlayRenderer { get; }

        public static UiLayerEntry ForSurface(IUiSurface surface) => new(UiLayerKind.Surface, surface, null);
        public static UiLayerEntry ForOverlay(Action<UiRenderContext> overlay) => new(UiLayerKind.Overlay, null, overlay);
    }

    private sealed record ActiveComposition(IUiSurface Surface, Action<UiRenderContext>[] Overlays);
}
