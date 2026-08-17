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
    bool SubmitOnEnter = false);

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

/// <summary>
/// Composes the standard modal window and routed scrollable-form lifecycle.
/// </summary>
public sealed class ModalFormHost
{
    private const int HorizontalContentInset = 1;
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
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(calculateLayout);
        ArgumentNullException.ThrowIfNull(handleInput);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, TResult>(
            (context, focusScope) => Render(context, focusScope, form, options, calculateLayout, beginRenderScope),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                if (options.SubmitOnEnter && input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter })
                    return (FormInputResult.Submit(), UiInputResult.HandledResult);

                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F1 })
                    return (FormInputResult.Auxiliary(), UiInputResult.HandledResult);

                FormRouteResult result = form.RouteInput(input, frame, route);
                if (result.FormResult.Kind is FormInputResultKind.Submit or FormInputResultKind.Cancel)
                    form.CloseTransientOverlays(commit: result.FormResult.Kind == FormInputResultKind.Submit);
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } &&
                    result.FormResult.Kind == FormInputResultKind.NotHandled)
                {
                    // F10 is a form-level submit shortcut. It must not bubble to the
                    // application, where the same key quits the program.
                    form.CloseTransientOverlays(commit: true);
                    return (result.FormResult, UiInputResult.HandledResult);
                }

                return (result.FormResult, result.UiResult);
            },
            (routed, result) => handleInput(ToDialogEvent(routed, result, form)),
            prepareRender,
            cancellationToken: cancellationToken);
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

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, TResult>(
            (context, focusScope) => Render(context, focusScope, form, options, calculateLayout, beginRenderScope),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                if (options.SubmitOnEnter && input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter })
                    return (FormInputResult.Submit(), UiInputResult.HandledResult);

                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F1 })
                    return (FormInputResult.Auxiliary(), UiInputResult.HandledResult);

                FormRouteResult result = form.RouteInput(input, frame, route);
                if (result.FormResult.Kind is FormInputResultKind.Submit or FormInputResultKind.Cancel)
                    form.CloseTransientOverlays(commit: result.FormResult.Kind == FormInputResultKind.Submit);
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } &&
                    result.FormResult.Kind == FormInputResultKind.NotHandled)
                {
                    form.CloseTransientOverlays(commit: true);
                    return (result.FormResult, UiInputResult.HandledResult);
                }

                return (result.FormResult, result.UiResult);
            },
            handleInput,
            prepareRender,
            cancellationToken: cancellationToken);
    }

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

    private ScrollableFormFrame Render(
        UiRenderContext context,
        IUiFocusState focusScope,
        ScrollableFormDialog form,
        ModalFormOptions options,
        Func<ModalDialogRenderer.Layout, ModalFormLayout> calculateLayout,
        Func<IDisposable>? beginRenderScope)
    {
        using IDisposable? renderScope = beginRenderScope?.Invoke();
        ScrollableFormFrame? frame = null;
        (int width, int height) = NaturalOuterSize(form, options);
        ModalDialogRenderer.Layout layout = _modalRenderer.CalculateLayout(
            context.Size,
            width,
            height,
            options.MinWidth,
            options.MinHeight);

        _modalRenderer.Render(
            context.Canvas,
            layout,
            options.Title,
            options.DoubleBorder,
            options.OuterRenderOptions ?? FarDialogStyles.OuterOptions,
            options.FrameRenderOptions ?? FarDialogStyles.FrameOptions,
            (_, modalLayout) =>
            {
                ModalFormLayout formLayout = InsetHorizontally(calculateLayout(modalLayout));
                frame = form.Render(
                    new FormRenderContext(context, formLayout.BodyBounds, FarDialogStyles.Border, formLayout.FooterBounds),
                    focusScope);
            });

        return frame ?? throw new InvalidOperationException("Modal form host did not render a form frame.");
    }

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
}
