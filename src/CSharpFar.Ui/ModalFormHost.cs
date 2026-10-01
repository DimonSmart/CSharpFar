using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record ModalFormOptions(
    string Title,
    int? PreferredWidth = null,
    int? PreferredHeight = null,
    int MinWidth = 20,
    int MinHeight = 8,
    bool DoubleBorder = true,
    PopupRenderOptions? OuterRenderOptions = null,
    PopupRenderOptions? FrameRenderOptions = null,
    bool SubmitOnEnter = false)
{
    public DialogResizeMode ResizeMode { get; init; } = DialogResizeMode.None;

    public int HorizontalMargin { get; init; } = 2;

    public int VerticalMargin { get; init; } = 1;

    public bool Movable { get; init; }
}

public readonly record struct ModalFormLayout(
    Rect BodyBounds,
    Rect? FooterBounds = null)
{
    public static ModalFormLayout BodyOnly(Rect contentBounds) => new(contentBounds);

    public static ModalFormLayout WithFooter(Rect contentBounds, int footerHeight)
    {
        if (footerHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(footerHeight));

        int reservedFooterHeight = Math.Min(contentBounds.Height, footerHeight);
        return new(
            new Rect(contentBounds.X, contentBounds.Y, contentBounds.Width, contentBounds.Height - reservedFooterHeight),
            new Rect(contentBounds.X, contentBounds.Bottom - reservedFooterHeight, contentBounds.Width, reservedFooterHeight));
    }
}

internal sealed record ModalFormFrame(
    ScrollableFormFrame Form,
    ModalDialogRenderer.Layout DialogLayout,
    Rect? DragBounds);

/// <summary>
/// Composes the standard modal window and routed scrollable-form lifecycle.
/// </summary>
public sealed class ModalFormHost
{
    private const int HorizontalContentInset = 1;
    private static readonly UiTargetId ModalTitleTarget = new("modal.title");
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public ModalFormHost(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public TResult Run<TResult>(
        ScrollableFormDialog form,
        ModalFormOptions options,
        Func<ModalDialogRenderer.Layout, ModalFormLayout> calculateLayout,
        Func<FormDialogEvent, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Func<IDisposable>? beginRenderScope = null,
        CancellationToken cancellationToken = default)
    {

        ArgumentNullException.ThrowIfNull(handleInput);
        return RunInteractive(
            form,
            options,
            calculateLayout,
            (routed, result) => handleInput(ToDialogEvent(routed, result, form)),
            prepareRender,
            beginRenderScope,
            cancellationToken);
    }

    internal TResult Run<TResult>(
        ScrollableFormDialog form,
        ModalFormOptions options,
        Func<ModalDialogRenderer.Layout, ModalFormLayout> calculateLayout,
        Func<UiRoutedInput<ScrollableFormFrame>, FormInputResult, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender = null,
        Func<IDisposable>? beginRenderScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handleInput);
        return RunInteractive(form, options, calculateLayout, handleInput, prepareRender, beginRenderScope, cancellationToken);
    }

    private TResult RunInteractive<TResult>(
        ScrollableFormDialog form,
        ModalFormOptions options,
        Func<ModalDialogRenderer.Layout, ModalFormLayout> calculateLayout,
        Func<UiRoutedInput<ScrollableFormFrame>, FormInputResult, ModalDialogLoopResult<TResult>> handleInput,
        Action? prepareRender,
        Func<IDisposable>? beginRenderScope,
        CancellationToken cancellationToken)
    {

        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(calculateLayout);
        ArgumentNullException.ThrowIfNull(handleInput);

        var placement = new ModalPlacementState();

        return _modalDialogs.RunInteractive<ModalFormFrame, ModalFormInputResult, TResult>(
            (context, focusScope) => Render(context, focusScope, form, options, calculateLayout, placement, beginRenderScope),
            frame => BuildInteractionFrame(frame, form),
            (input, frame, route) => RouteInput(input, frame, route, form, options, placement),
            (routed, result) =>
            {
                if (result.WindowHandled)
                    return ModalDialogLoopResult<TResult>.ContinueNoChange;

                return handleInput(ToFormRoutedInput(routed), result.FormResult);
            },
            prepareRender,
            cancellationToken: cancellationToken);
    }

    private static UiInteractionFrame BuildInteractionFrame(
        ModalFormFrame frame,
        ScrollableFormDialog form)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFragment(form.BuildInteractionFragment(frame.Form));

        if (frame.DragBounds is Rect dragBounds)
            builder.AddHitRegion(ModalTitleTarget, dragBounds);

        return builder
            .SetDefaultFocusTarget(frame.Form.DefaultTarget)
            .Build();
    }

    private static (ModalFormInputResult Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        ModalFormFrame frame,
        UiInputRouteContext route,
        ScrollableFormDialog form,
        ModalFormOptions options,
        ModalPlacementState placement)
    {
        if (options.Movable &&
            route.Target == ModalTitleTarget &&
            input is MouseConsoleInputEvent mouse)
        {
            if (mouse is { Kind: MouseEventKind.Down, Button: MouseButton.Left })
            {
                placement.BeginDrag(mouse, frame);
                return (
                    ModalFormInputResult.Window,
                    UiInputResult.CaptureMouse(ModalTitleTarget, MouseButton.Left));
            }

            if (route.IsCapturedRoute && placement.DragActive)
            {
                if (mouse.Kind == MouseEventKind.Move)
                {
                    bool moved = placement.MoveDrag(mouse, frame);
                    return (
                        ModalFormInputResult.Window,
                        moved ? UiInputResult.HandledAndInvalidate : UiInputResult.HandledResult);
                }

                if (mouse is { Kind: MouseEventKind.Up, Button: MouseButton.Left })
                {
                    placement.EndDrag();
                    return (ModalFormInputResult.Window, UiInputResult.ReleaseMouse());
                }
            }

            return (ModalFormInputResult.Window, UiInputResult.HandledResult);
        }

        if (options.SubmitOnEnter && input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter })
            return (ModalFormInputResult.Form(FormInputResult.Submit()), UiInputResult.HandledResult);

        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F1 })
            return (ModalFormInputResult.Form(FormInputResult.Auxiliary()), UiInputResult.HandledResult);

        FormRouteResult result = form.RouteInput(input, frame.Form, route);
        if (result.FormResult.Kind is FormInputResultKind.Submit or FormInputResultKind.Cancel)
            form.CloseTransientOverlays(commit: result.FormResult.Kind == FormInputResultKind.Submit);
        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } &&
            result.FormResult.Kind == FormInputResultKind.NotHandled)
        {
            form.CloseTransientOverlays(commit: true);
            return (ModalFormInputResult.Form(result.FormResult), UiInputResult.HandledResult);
        }

        return (ModalFormInputResult.Form(result.FormResult), result.UiResult);
    }

    private static UiRoutedInput<ScrollableFormFrame> ToFormRoutedInput(
        UiRoutedInput<ModalFormFrame> routed) =>
        new(routed.Input, routed.Frame.Form, routed.Target, routed.RouteKind);

    private static FormDialogEvent ToDialogEvent(
        UiRoutedInput<ScrollableFormFrame> routed,
        FormInputResult result,
        ScrollableFormDialog form)
    {
        FormDialogEventKind kind = result.Kind switch
        {
            FormInputResultKind.ValueChanged => FormDialogEventKind.ValueChanged,
            FormInputResultKind.Submit => FormDialogEventKind.Submitted,
            FormInputResultKind.Auxiliary => FormDialogEventKind.Auxiliary,
            FormInputResultKind.Cancel => FormDialogEventKind.Cancelled,
            _ when FormDialogInput.ShouldSubmit(routed, result, form) => FormDialogEventKind.Submitted,
            FormInputResultKind.NotHandled => FormDialogEventKind.NotHandled,
            _ => FormDialogEventKind.Handled,
        };
        return new(
            kind,
            result.Command,
            result.SourceRowId,
            routed.Input is KeyConsoleInputEvent { Key.Key: var key } ? key : null,
            form.FocusedRowId,
            result.SourceTarget);
    }

    private ModalFormFrame Render(
        UiRenderContext context,
        IUiFocusState focusScope,
        ScrollableFormDialog form,
        ModalFormOptions options,
        Func<ModalDialogRenderer.Layout, ModalFormLayout> calculateLayout,
        ModalPlacementState placement,
        Func<IDisposable>? beginRenderScope)
    {
        using IDisposable? renderScope = beginRenderScope?.Invoke();
        ScrollableFormFrame? frame = null;
        (int preferredWidth, int preferredHeight) = NaturalOuterSize(form, options);
        (int width, int height) = DialogSizing.Resolve(
            context.Size,
            preferredWidth,
            preferredHeight,
            options.ResizeMode,
            options.HorizontalMargin,
            options.VerticalMargin);
        ModalDialogRenderer.Layout centeredLayout = _modalRenderer.CalculateLayout(
            context.Size,
            width,
            height,
            options.MinWidth,
            options.MinHeight);
        ModalDialogRenderer.Layout layout = ResolveLayout(context.Size, centeredLayout, placement);

        _modalRenderer.Render(
            context.Canvas,
            layout,
            options.Title,
            options.DoubleBorder,
            options.OuterRenderOptions ?? DialogStyles.OuterOptions,
            options.FrameRenderOptions ?? DialogStyles.FrameOptions,
            (_, modalLayout) =>
            {
                ModalFormLayout formLayout = InsetHorizontally(calculateLayout(modalLayout));
                frame = form.Render(
                    new FormRenderContext(context, formLayout.BodyBounds, DialogStyles.Border, formLayout.FooterBounds),
                    focusScope);
            });

        if (placement.IsUserPositioned)
        {
            Rect committedBounds = layout.OuterBounds;
            context.PublishOnStable(() => placement.CommitRenderedPosition(committedBounds.X, committedBounds.Y));
        }

        Rect? dragBounds = options.Movable && layout.FrameBounds is { Width: > 0, Height: > 0 } frameBounds
            ? new Rect(frameBounds.X, frameBounds.Y, frameBounds.Width, 1)
            : null;

        return new ModalFormFrame(
            frame ?? throw new InvalidOperationException("Modal form host did not render a form frame."),
            layout,
            dragBounds);
    }

    private ModalDialogRenderer.Layout ResolveLayout(
        ConsoleSize size,
        ModalDialogRenderer.Layout centeredLayout,
        ModalPlacementState placement)
    {
        if (!placement.IsUserPositioned)
            return centeredLayout;

        Rect effective = centeredLayout.OuterBounds;
        int x = ClampCoordinate(placement.X, size.Width - effective.Width);
        int y = ClampCoordinate(placement.Y, size.Height - effective.Height);
        return _modalRenderer.CalculateLayout(new Rect(x, y, effective.Width, effective.Height));
    }

    private static int ClampCoordinate(int value, int maximum) =>
        Math.Clamp(value, 0, Math.Max(0, maximum));

    private static ModalFormLayout InsetHorizontally(ModalFormLayout layout) => new(
        InsetHorizontally(layout.BodyBounds),
        layout.FooterBounds is Rect footerBounds ? InsetHorizontally(footerBounds) : null);

    private static Rect InsetHorizontally(Rect bounds) => bounds.Width <= HorizontalContentInset * 2
        ? new Rect(bounds.X, bounds.Y, 0, bounds.Height)
        : new Rect(bounds.X + HorizontalContentInset, bounds.Y, bounds.Width - HorizontalContentInset * 2, bounds.Height);

    private static (int Width, int Height) NaturalOuterSize(ScrollableFormDialog form, ModalFormOptions options)
    {
        // Outer padding, frame border, and the form host's horizontal inset.
        const int horizontalChrome = 6;
        const int verticalChrome = 4;
        int naturalWidth = form.NaturalContentWidth + horizontalChrome;
        int naturalHeight = form.NaturalContentHeight + verticalChrome;
        int titleWidth = ConsoleTextMetrics.GetCellWidth(options.Title ?? string.Empty) + 2 + horizontalChrome;
        return (options.PreferredWidth ?? Math.Max(naturalWidth, titleWidth), options.PreferredHeight ?? naturalHeight);
    }

    private readonly record struct ModalFormInputResult(
        bool WindowHandled,
        FormInputResult FormResult)
    {
        public static ModalFormInputResult Window { get; } =
            new(true, FormInputResult.NotHandled);

        public static ModalFormInputResult Form(FormInputResult result) =>
            new(false, result);
    }

    private sealed class ModalPlacementState
    {
        public bool IsUserPositioned { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public bool DragActive { get; private set; }
        private int DragOffsetX { get; set; }
        private int DragOffsetY { get; set; }

        public void BeginDrag(MouseConsoleInputEvent mouse, ModalFormFrame frame)
        {
            Rect outer = frame.DialogLayout.OuterBounds;
            DragOffsetX = mouse.X - outer.X;
            DragOffsetY = mouse.Y - outer.Y;
            DragActive = true;
        }

        public bool MoveDrag(MouseConsoleInputEvent mouse, ModalFormFrame frame)
        {
            if (!DragActive)
                return false;

            Rect outer = frame.DialogLayout.OuterBounds;
            int x = ClampCoordinate(mouse.X - DragOffsetX, frame.Form.Viewport.Width - outer.Width);
            int y = ClampCoordinate(mouse.Y - DragOffsetY, frame.Form.Viewport.Height - outer.Height);
            if (x == outer.X && y == outer.Y)
                return false;

            IsUserPositioned = true;
            X = x;
            Y = y;
            return true;
        }

        public void EndDrag() => DragActive = false;

        public void CommitRenderedPosition(int x, int y)
        {
            if (!IsUserPositioned)
                return;

            X = x;
            Y = y;
        }
    }
}
