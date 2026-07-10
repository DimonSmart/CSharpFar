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
    private readonly List<IUiSurface> _surfaces = [];
    private readonly List<Action<UiRenderContext>> _overlays = [];
    private bool _isRendering;

    public UiCompositionHost(ScreenRenderer screen)
    {
        Screen = screen;
    }

    public ScreenRenderer Screen { get; }

    public ConsoleViewport? LastStableViewport { get; private set; }

    public void SetRootSurface(IUiSurface surface)
    {
        EnsureNotRendering();
        ArgumentNullException.ThrowIfNull(surface);
        if (_surfaces.Count > 1)
            throw new InvalidOperationException("Cannot replace the root surface while a temporary surface is active.");

        if (_surfaces.Count == 0)
            _surfaces.Add(surface);
        else
            _surfaces[0] = surface;
    }

    public UiSurfaceSession OpenSurface(Action<UiRenderContext> render) =>
        OpenSurface(new ScreenRendererSurface(Screen, render));

    public UiSurfaceSession OpenSurface(IUiSurface surface)
    {
        EnsureNotRendering();
        ArgumentNullException.ThrowIfNull(surface);
        EnsureRootSurface();
        _surfaces.Add(surface);
        return new UiSurfaceSession(this, surface);
    }

    internal UiLayerScope PushOverlay(Action<UiRenderContext> render)
    {
        EnsureNotRendering();
        ArgumentNullException.ThrowIfNull(render);
        EnsureRootSurface();
        _overlays.Add(render);
        return new UiLayerScope(this, render);
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
                var surface = _surfaces[^1];
                var overlays = _overlays.ToArray();
                ConsoleViewport viewport;

                using (surface.BeginFrame(request))
                {
                    viewport = Screen.FrameViewport;
                    var context = new UiRenderContext(Screen, viewport);
                    surface.Render(context);
                    foreach (var overlay in overlays)
                        overlay(context);
                }

                var interrupted = Screen.FrameWasInterrupted;
                surface.CompleteFrame(new UiFrameCompletion(request, viewport, interrupted));
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

    private void CloseSurface(IUiSurface surface)
    {
        EnsureNotRendering();
        if (_surfaces.Count <= 1 || !ReferenceEquals(_surfaces[^1], surface))
            throw new InvalidOperationException("Temporary surfaces must be disposed in LIFO order.");
        _surfaces.RemoveAt(_surfaces.Count - 1);
    }

    private void CloseOverlay(Action<UiRenderContext> overlay)
    {
        EnsureNotRendering();
        if (_overlays.Count == 0 || !ReferenceEquals(_overlays[^1], overlay))
            throw new InvalidOperationException("Overlays must be disposed in LIFO order.");
        _overlays.RemoveAt(_overlays.Count - 1);
    }

    private void EnsureRootSurface()
    {
        if (_surfaces.Count == 0)
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
        private readonly IUiSurface _surface;

        internal UiSurfaceSession(UiCompositionHost host, IUiSurface surface) => (_host, _surface) = (host, surface);

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
            host.CloseSurface(_surface);
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
        private readonly Action<UiRenderContext> _overlay;

        internal UiLayerScope(UiCompositionHost host, Action<UiRenderContext> overlay) => (_host, _overlay) = (host, overlay);

        public void Dispose()
        {
            var host = _host;
            if (host is null)
                return;

            host.CloseOverlay(_overlay);
            _host = null;
        }
    }
}
