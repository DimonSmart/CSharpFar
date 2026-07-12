using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct UiRenderRequest(bool IsResizeRecovery);

public sealed class UiRenderContext
{
    private readonly UiCompositionHost.UiRenderAttempt _attempt;

    internal UiRenderContext(ScreenRenderer screen, ConsoleViewport viewport, UiCompositionHost.UiRenderAttempt attempt) =>
        (Screen, Viewport, _attempt) = (screen, viewport, attempt);

    public ScreenRenderer Screen { get; }
    public ConsoleViewport Viewport { get; }
    public ConsoleSize Size => Viewport.Size;

    /// <summary>
    /// Defers an observable UI state change until this render attempt commits.
    /// A render callback may be invoked more than once before a frame is stable.
    /// </summary>
    public void PublishOnStable(Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        _attempt.Register(publish);
    }

    public void PublishOnStable<T>(T value, Action<T> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        _attempt.Register(() => publish(value));
    }
}

/// <summary>
/// Holds frame-dependent state visible to input handling only after a stable
/// composition frame has committed it.
/// </summary>
public sealed class UiCommittedState<T>
{
    private T? _value;

    public bool HasValue { get; private set; }

    public T Value => HasValue
        ? _value!
        : throw new InvalidOperationException("No committed UI state is available.");

    public bool TryGet(out T value)
    {
        if (!HasValue)
        {
            value = default!;
            return false;
        }

        value = _value!;
        return true;
    }

    public void Stage(UiRenderContext context, T value)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.PublishOnStable(value, Commit);
    }

    private void Commit(T value)
    {
        _value = value;
        HasValue = true;
    }
}

public readonly record struct UiFrameCompletion(
    UiRenderRequest Request,
    ConsoleViewport Viewport,
    bool WasInterrupted,
    bool WasCommitted);

public interface IUiSurface
{
    IDisposable BeginFrame(UiRenderRequest request);

    /// <summary>
    /// Builds one attempt-local frame. This callback may run repeatedly before
    /// one attempt commits and must publish observable state through the
    /// render context rather than changing it immediately.
    /// </summary>
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
    private readonly List<UiLayerEntry> _layers = [];
    private bool _isRendering;
    private bool _isDispatching;
    private UiMouseCaptureState? _mouseCapture;

    public UiCompositionHost(ScreenRenderer screen)
    {
        Screen = screen;
    }

    public ScreenRenderer Screen { get; }

    public ConsoleViewport? LastStableViewport { get; private set; }

    public void SetRootSurface(IUiSurface surface)
    {
        EnsureCanChangeLayers();
        ArgumentNullException.ThrowIfNull(surface);
        if (_layers.Skip(1).Any(entry => entry.Kind == UiLayerKind.Surface))
            throw new InvalidOperationException("Cannot replace the root surface while a temporary surface is active.");

        if (surface is IUiLayer layer)
            EnsureLayerNotRegistered(layer, _layers.Count > 0 ? _layers[0] : null);

        if (_layers.Count > 0)
            ClearCaptureIfOwnedBy(_layers[0]);

        if (_layers.Count == 0)
            _layers.Add(UiLayerEntry.ForSurface(surface));
        else
            _layers[0] = UiLayerEntry.ForSurface(surface);

        RevalidateMouseCapture();
    }

    /// <summary>Opens a surface whose render callback participates in stable-frame commit.</summary>
    public UiSurfaceSession OpenSurface(Action<UiRenderContext> render) =>
        OpenSurface(new ScreenRendererSurface(Screen, render));

    public UiSurfaceSession OpenSurface(IUiSurface surface)
    {
        EnsureCanChangeLayers();
        ArgumentNullException.ThrowIfNull(surface);
        EnsureRootSurface();
        if (surface is IUiLayer layer)
            EnsureLayerNotRegistered(layer);

        var entry = UiLayerEntry.ForSurface(surface);
        _layers.Add(entry);
        RevalidateMouseCapture();
        return new UiSurfaceSession(this, entry);
    }

    /// <summary>Opens an overlay whose render callback participates in stable-frame commit.</summary>
    internal UiLayerScope PushOverlay(Action<UiRenderContext> render)
    {
        EnsureCanChangeLayers();
        ArgumentNullException.ThrowIfNull(render);
        EnsureRootSurface();
        var entry = UiLayerEntry.ForOverlay(render);
        _layers.Add(entry);
        RevalidateMouseCapture();
        return new UiLayerScope(this, entry);
    }

    internal UiLayerScope PushOverlay(IUiLayer layer)
    {
        EnsureCanChangeLayers();
        ArgumentNullException.ThrowIfNull(layer);
        EnsureRootSurface();
        EnsureLayerNotRegistered(layer);
        var entry = UiLayerEntry.ForOverlay(layer);
        _layers.Add(entry);
        RevalidateMouseCapture();
        return new UiLayerScope(this, entry);
    }

    public bool HasViewportChanged() =>
        LastStableViewport is { } viewport && Screen.GetViewport() != viewport;

    public void Render(bool isResizeRecovery = false)
    {
        EnsureRootSurface();
        if (_isDispatching)
            throw new InvalidOperationException("UI composition cannot render while input is dispatching.");
        if (_isRendering)
            throw new InvalidOperationException("UI composition cannot be rendered recursively.");

        _isRendering = true;
        try
        {
            while (true)
            {
                var request = new UiRenderRequest(isResizeRecovery);
                var composition = CaptureActiveComposition();
                var attempt = new UiRenderAttempt();
                ConsoleViewport viewport;

                using (composition.Surface.SurfaceLifecycle!.BeginFrame(request))
                {
                    viewport = Screen.FrameViewport;
                    var context = new UiRenderContext(Screen, viewport, attempt);
                    composition.Surface.Layer.Render(context);
                    foreach (var overlay in composition.Overlays)
                        overlay.Layer.Render(context);
                }

                bool interrupted = Screen.FrameWasInterrupted;
                // Draining is needed only after a resize recovery. On an ordinary
                // render, leave semantic input in the driver queue so the active
                // session observes it as its next input.
                if (!interrupted && isResizeRecovery)
                    Screen.DrainResizeEvents();

                bool rejected = interrupted || Screen.GetViewport() != viewport;
                if (rejected)
                {
                    attempt.Discard();
                    composition.Surface.SurfaceLifecycle!.CompleteFrame(new UiFrameCompletion(request, viewport, interrupted, WasCommitted: false));
                    isResizeRecovery = true;
                    continue;
                }

                attempt.Commit();
                LastStableViewport = viewport;
                composition.Surface.SurfaceLifecycle!.CompleteFrame(new UiFrameCompletion(request, viewport, WasInterrupted: false, WasCommitted: true));
                return;
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    public ConsoleInputEvent ReadInput(CancellationToken cancellationToken = default) =>
        ReadCompositionInput(cancellationToken);

    public bool TryReadInput(out ConsoleInputEvent? input) =>
        TryReadCompositionInput(out input);

    /// <summary>
    /// Routes an already-read semantic input event through the active layer
    /// composition. Returned focus and capture requests are normalized after
    /// host-side application and must not be replayed by callers.
    /// </summary>
    public UiInputResult DispatchInput(ConsoleInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_isRendering)
            throw new InvalidOperationException("UI input cannot be dispatched while composition is rendering.");
        if (_isDispatching)
            throw new InvalidOperationException("UI input cannot be dispatched recursively.");

        if (input is ConsoleResizeInputEvent)
            return UiInputResult.NotHandled;

        EnsureRootSurface();
        _isDispatching = true;
        try
        {
            var composition = CaptureActiveComposition();
            if (input is MouseConsoleInputEvent mouse && _mouseCapture is { } capture)
            {
                if (CanRouteCapturedInput(composition, capture.Owner))
                    return DispatchCapturedMouse(input, mouse, capture);

                _mouseCapture = null;
            }

            bool handled = false;
            bool invalidate = false;
            foreach (UiLayerEntry entry in composition.RoutingOrder())
            {
                if (entry.Layer.InputPolicy == UiLayerInputPolicy.None)
                    continue;

                UiInputResult result = entry.Layer.RouteInput(
                    input,
                    new UiInputRouteContext(entry.Layer.FocusScope, capturedTarget: null, isCapturedRoute: false));
                ValidateInputResult(entry, input, result);
                ApplyFocusRequest(entry.Layer.FocusScope, result.FocusRequest);
                ApplyMouseCaptureRequest(entry, input, result.MouseCaptureRequest);

                handled |= result.Handled;
                invalidate |= result.Invalidate;

                if (entry.Layer.InputPolicy == UiLayerInputPolicy.Modal || result.Handled)
                    break;
            }

            return NormalizeResult(handled, invalidate);
        }
        finally
        {
            _isDispatching = false;
        }
    }

    private ActiveComposition CaptureActiveComposition()
    {
        int surfaceIndex = _layers.FindLastIndex(entry => entry.Kind == UiLayerKind.Surface);
        if (surfaceIndex < 0)
            throw new InvalidOperationException("A root UI surface must be set before composition rendering.");

        return new ActiveComposition(
            _layers[surfaceIndex],
            _layers.Skip(surfaceIndex + 1).ToArray());
    }

    private void CloseSurface(UiLayerEntry entry)
    {
        EnsureCanChangeLayers();
        if (_layers.Count <= 1 || !ReferenceEquals(_layers[^1], entry) || entry.Kind != UiLayerKind.Surface)
            throw new InvalidOperationException("Temporary surfaces must be disposed in LIFO order.");
        ClearCaptureIfOwnedBy(entry);
        _layers.RemoveAt(_layers.Count - 1);
        RevalidateMouseCapture();
    }

    private void CloseOverlay(UiLayerEntry entry)
    {
        EnsureCanChangeLayers();
        if (_layers.Count == 0 || !ReferenceEquals(_layers[^1], entry) || entry.Kind != UiLayerKind.Overlay)
            throw new InvalidOperationException("Overlays must be disposed in LIFO order.");
        ClearCaptureIfOwnedBy(entry);
        _layers.RemoveAt(_layers.Count - 1);
        RevalidateMouseCapture();
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

    private void EnsureCanChangeLayers()
    {
        EnsureNotRendering();
        if (_isDispatching)
            throw new InvalidOperationException("UI layers cannot be changed while input is dispatching.");
    }

    private void EnsureNotDispatchingInputPump()
    {
        if (_isDispatching)
            throw new InvalidOperationException("UI input cannot be read while routed input is dispatching.");
    }

    private void EnsureLayerNotRegistered(IUiLayer layer, UiLayerEntry? allowedExistingEntry = null)
    {
        if (layer.InputPolicy == UiLayerInputPolicy.None)
            return;

        foreach (UiLayerEntry entry in _layers)
        {
            if (ReferenceEquals(entry, allowedExistingEntry))
                continue;

            if (ReferenceEquals(entry.Layer, layer))
                throw new InvalidOperationException("The same interactive UI layer instance cannot be registered more than once.");
        }
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
        EnsureNotDispatchingInputPump();

        while (true)
        {
            RecoverChangedViewport();
            var input = Screen.ReadInput(cancellationToken);
            if (input is ConsoleResizeInputEvent)
            {
                Render(isResizeRecovery: true);
                continue;
            }

            if (HasViewportChanged())
            {
                Render(isResizeRecovery: true);
            }

            return input;
        }
    }

    internal bool TryReadCompositionInput(out ConsoleInputEvent? input)
    {
        EnsureNotDispatchingInputPump();
        RecoverChangedViewport();

        while (Screen.TryReadInput(out input))
        {
            if (input is ConsoleResizeInputEvent)
            {
                Render(isResizeRecovery: true);
                continue;
            }

            if (HasViewportChanged())
            {
                Render(isResizeRecovery: true);
            }

            return true;
        }

        RecoverChangedViewport();
        input = null;
        return false;
    }

    private void RecoverChangedViewport()
    {
        if (HasViewportChanged())
            Render(isResizeRecovery: true);
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
        private UiLayerEntry(UiLayerKind kind, IUiLayer layer, IUiSurface? surfaceLifecycle) =>
            (Kind, Layer, SurfaceLifecycle) = (kind, layer, surfaceLifecycle);

        public UiLayerKind Kind { get; }
        public IUiLayer Layer { get; }
        public IUiSurface? SurfaceLifecycle { get; }

        public static UiLayerEntry ForSurface(IUiSurface surface) =>
            new(UiLayerKind.Surface, surface as IUiLayer ?? new RenderOnlySurfaceLayer(surface), surface);

        public static UiLayerEntry ForOverlay(Action<UiRenderContext> overlay) =>
            new(UiLayerKind.Overlay, new RenderOnlyOverlayLayer(overlay), null);

        public static UiLayerEntry ForOverlay(IUiLayer layer) =>
            new(UiLayerKind.Overlay, layer, null);
    }

    private sealed record ActiveComposition(UiLayerEntry Surface, UiLayerEntry[] Overlays)
    {
        public bool Contains(UiLayerEntry entry) =>
            ReferenceEquals(Surface, entry) || Overlays.Any(value => ReferenceEquals(value, entry));

        public IEnumerable<UiLayerEntry> RoutingOrder()
        {
            for (int i = Overlays.Length - 1; i >= 0; i--)
                yield return Overlays[i];

            yield return Surface;
        }
    }

    private UiInputResult DispatchCapturedMouse(
        ConsoleInputEvent input,
        MouseConsoleInputEvent mouse,
        UiMouseCaptureState capture)
    {
        UiLayerEntry entry = capture.Owner;
        UiInputResult result = entry.Layer.RouteInput(
            input,
            new UiInputRouteContext(entry.Layer.FocusScope, capture.Target, isCapturedRoute: true));

        ValidateInputResult(entry, input, result);
        ApplyFocusRequest(entry.Layer.FocusScope, result.FocusRequest);
        UiMouseCaptureState? oldCapture = _mouseCapture;
        bool explicitCapture = result.MouseCaptureRequest.Kind == UiMouseCaptureRequestKind.Capture;
        ApplyMouseCaptureRequest(entry, input, result.MouseCaptureRequest);
        if (!explicitCapture &&
            oldCapture is not null &&
            ReferenceEquals(oldCapture, capture) &&
            mouse.Kind == MouseEventKind.Up &&
            mouse.Button == capture.Button)
        {
            _mouseCapture = null;
        }

        return NormalizeResult(result.Handled, result.Invalidate);
    }

    private void ApplyFocusRequest(UiFocusScope focusScope, UiFocusRequest request)
    {
        switch (request.Kind)
        {
            case UiFocusRequestKind.None:
                break;
            case UiFocusRequestKind.Set:
                focusScope.TryFocus(request.Target!);
                break;
            case UiFocusRequestKind.Clear:
                focusScope.ClearFocus();
                break;
            case UiFocusRequestKind.MoveNext:
                focusScope.MoveNext();
                break;
            case UiFocusRequestKind.MovePrevious:
                focusScope.MovePrevious();
                break;
            default:
                throw new InvalidOperationException($"Unknown focus request '{request.Kind}'.");
        }
    }

    private void ApplyMouseCaptureRequest(
        UiLayerEntry entry,
        ConsoleInputEvent input,
        UiMouseCaptureRequest request)
    {
        switch (request.Kind)
        {
            case UiMouseCaptureRequestKind.None:
                break;
            case UiMouseCaptureRequestKind.Release:
                _mouseCapture = null;
                break;
            case UiMouseCaptureRequestKind.Capture:
                if (input is not MouseConsoleInputEvent mouse)
                    throw new InvalidOperationException("Mouse capture can only be requested for mouse input.");
                if (mouse.Button != request.Button)
                    throw new InvalidOperationException("Mouse capture button must match the current mouse input.");
                if (!UiMouseCaptureRequest.IsCapturable(mouse.Button))
                    throw new InvalidOperationException("Mouse capture supports only left, right, and middle buttons.");

                _mouseCapture = new UiMouseCaptureState(entry, request.Target!, request.Button.Value);
                break;
            default:
                throw new InvalidOperationException($"Unknown mouse capture request '{request.Kind}'.");
        }
    }

    private void ClearCaptureIfOwnedBy(UiLayerEntry entry)
    {
        if (_mouseCapture is { } capture && ReferenceEquals(capture.Owner, entry))
            _mouseCapture = null;
    }

    private bool CanRouteCapturedInput(ActiveComposition composition, UiLayerEntry owner)
    {
        foreach (UiLayerEntry entry in composition.RoutingOrder())
        {
            if (ReferenceEquals(entry, owner))
                return entry.Layer.InputPolicy != UiLayerInputPolicy.None;

            if (entry.Layer.InputPolicy == UiLayerInputPolicy.Modal)
                return false;
        }

        return false;
    }

    private void RevalidateMouseCapture()
    {
        if (_mouseCapture is null || _layers.Count == 0)
            return;

        ActiveComposition composition = CaptureActiveComposition();
        if (!CanRouteCapturedInput(composition, _mouseCapture.Owner))
            _mouseCapture = null;
    }

    private static void ValidateInputResult(
        UiLayerEntry entry,
        ConsoleInputEvent input,
        UiInputResult result)
    {
        if (!result.Handled &&
            result.MouseCaptureRequest.Kind != UiMouseCaptureRequestKind.None)
        {
            throw new InvalidOperationException("Mouse capture or release requests must handle the input event.");
        }

        if (result.MouseCaptureRequest.Kind != UiMouseCaptureRequestKind.Capture)
            return;

        if (input is not MouseConsoleInputEvent mouse)
            throw new InvalidOperationException("Mouse capture can only be requested for mouse input.");
        if (mouse.Button != result.MouseCaptureRequest.Button)
            throw new InvalidOperationException("Mouse capture button must match the current mouse input.");
        if (!UiMouseCaptureRequest.IsCapturable(mouse.Button))
            throw new InvalidOperationException("Mouse capture supports only left, right, and middle buttons.");
        if (result.MouseCaptureRequest.Target is null)
            throw new InvalidOperationException("Mouse capture requires a target.");
        if (entry.Layer.InputPolicy == UiLayerInputPolicy.None)
            throw new InvalidOperationException("Mouse capture cannot be requested by a layer with no input policy.");
    }

    private static UiInputResult NormalizeResult(bool handled, bool invalidate) =>
        new(handled, invalidate, UiFocusRequest.None, UiMouseCaptureRequest.None);

    internal sealed record UiMouseCaptureState(
        UiLayerEntry Owner,
        UiTargetId Target,
        MouseButton Button);

    internal sealed class UiRenderAttempt
    {
        private List<Action>? _commits = [];

        public void Register(Action commit)
        {
            if (_commits is null)
                throw new InvalidOperationException("Stable state cannot be registered after a render attempt has finished.");
            _commits.Add(commit);
        }

        public void Commit()
        {
            var commits = _commits ?? throw new InvalidOperationException("Render attempt has already finished.");
            _commits = null;
            foreach (var commit in commits)
                commit();
        }

        public void Discard()
        {
            if (_commits is null)
                throw new InvalidOperationException("Render attempt has already finished.");
            _commits = null;
        }
    }
}
