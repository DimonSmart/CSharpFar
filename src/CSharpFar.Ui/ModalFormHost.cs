using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record ModalFormOptions(
    string Title,
    int Width,
    int Height,
    int MinWidth = 20,
    int MinHeight = 8,
    bool DoubleBorder = true,
    PopupRenderOptions? OuterRenderOptions = null,
    PopupRenderOptions? FrameRenderOptions = null,
    bool SubmitOnEnter = false);

public readonly record struct ModalFormLayout(
    Rect BodyBounds,
    Rect? FooterBounds = null);

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
        Func<UiRoutedInput<ScrollableFormFrame>, FormInputResult, ModalDialogLoopResult<TResult>> handleInput,
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

                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
            handleInput,
            prepareRender,
            cancellationToken: cancellationToken);
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
        ModalDialogRenderer.Layout layout = _modalRenderer.CalculateLayout(
            context.Size,
            options.Width,
            options.Height,
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
}
