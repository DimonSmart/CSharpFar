using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record CompositeDialogOptions(
    string Title,
    int PreferredWidth = 80,
    int PreferredHeight = 20,
    int MinWidth = 20,
    int MinHeight = 8,
    bool DoubleBorder = true,
    DialogAppearance Appearance = DialogAppearance.Standard)
{
    public DialogResizeMode ResizeMode { get; init; } = DialogResizeMode.None;

    public int HorizontalMargin { get; init; } = 2;

    public int VerticalMargin { get; init; } = 1;
}

public enum CompositeDialogEventKind
{
    NotHandled,
    ValueChanged,
    ContentSelectionChanged,
    ContentConfirmed,
    Command,
    Cancelled,
}

public readonly record struct CompositeDialogEvent(
    CompositeDialogEventKind Kind,
    string? Command = null,
    ConsoleKey? Key = null,
    IFormFocusTarget? SourceControl = null);

public readonly record struct CompositeDialogOutcome<TResult>(bool IsComplete, bool IsChanged, TResult Result)
{
    public static CompositeDialogOutcome<TResult> ContinueNoChange => new(false, false, default!);
    public static CompositeDialogOutcome<TResult> ContinueChanged => new(false, true, default!);
    public static CompositeDialogOutcome<TResult> Complete(TResult result) => new(true, false, result);
}

/// <summary>Composes a form, routed content, optional status, and footer actions into one modal lifecycle.</summary>
public sealed class CompositeDialogHost
{
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _renderer = new();

    public CompositeDialogHost(ModalDialogHost modalDialogs) =>
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));

    public TResult Run<TResult>(
        CompositeDialogOptions options,
        ScrollableFormDialog form,
        ICompositeDialogContent content,
        Func<string?>? status,
        IReadOnlyDictionary<ConsoleKey, string>? commands,
        Func<CompositeDialogEvent, CompositeDialogOutcome<TResult>> handle,
        Action? prepareRender = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(handle);

        return _modalDialogs.RunInteractive<Frame, CompositeDialogEvent, TResult>(
            (context, focus) => Render(context, focus, options, form, content, status),
            frame => BuildInteractionFrame(frame, form, content),
            (input, frame, route) => Route(input, frame, route, form, content, commands),
            (_, semantic) =>
            {
                CompositeDialogOutcome<TResult> outcome = handle(semantic);
                return outcome.IsComplete
                    ? ModalDialogLoopResult<TResult>.Complete(outcome.Result)
                    : outcome.IsChanged
                        ? ModalDialogLoopResult<TResult>.ContinueChanged
                        : ModalDialogLoopResult<TResult>.ContinueNoChange;
            },
            prepareRender,
            frame => content.ApplyCommittedFrame(frame.Content),
            cancellationToken);
    }

    internal TResult RunTimed<TResult>(
        CompositeDialogOptions options,
        ScrollableFormDialog form,
        ICompositeDialogContent content,
        Func<string?>? status,
        IReadOnlyDictionary<ConsoleKey, string>? commands,
        Func<CompositeDialogEvent, ModalDialogLoopResult<TResult>> handle,
        Func<DateTimeOffset?> getNextWakeUtc,
        Func<ModalDialogWakeResult<TResult>> handleWake,
        Action? prepareRender = null,
        Action? afterFrameCommitted = null,
        CancellationToken cancellationToken = default,
        CancellationToken wakeSignal = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(getNextWakeUtc);
        ArgumentNullException.ThrowIfNull(handleWake);

        return _modalDialogs.RunInteractiveTimed<Frame, CompositeDialogEvent, TResult>(
            (context, focus) => Render(context, focus, options, form, content, status),
            frame => BuildInteractionFrame(frame, form, content),
            (input, frame, route) => Route(input, frame, route, form, content, commands),
            (_, semantic) => handle(semantic),
            getNextWakeUtc,
            _ => handleWake(),
            prepareRender,
            frame =>
            {
                content.ApplyCommittedFrame(frame.Content);
                afterFrameCommitted?.Invoke();
            },
            cancellationToken,
            wakeSignal);
    }

    private Frame Render(UiRenderContext context, IUiFocusState focus, CompositeDialogOptions options, ScrollableFormDialog form, ICompositeDialogContent content, Func<string?>? status)
    {
        (int width, int height) = DialogSizing.Resolve(
            context.Size,
            options.PreferredWidth,
            options.PreferredHeight,
            options.ResizeMode,
            options.HorizontalMargin,
            options.VerticalMargin);
        ModalDialogRenderer.Layout modal = _renderer.CalculateLayout(context.Size, width, height, options.MinWidth, options.MinHeight);
        Rect bounds = modal.ContentBounds;
        int footerHeight = Math.Min(form.NaturalFooterHeight, bounds.Height);
        int headerHeight = Math.Min(form.NaturalBodyHeight, Math.Max(0, bounds.Height - footerHeight));
        Rect footer = new(bounds.X, Math.Max(bounds.Y, bounds.Bottom - footerHeight), bounds.Width, footerHeight);
        int statusHeight = status is null || bounds.Height - headerHeight - footerHeight <= 0 ? 0 : 1;
        Rect header = new(bounds.X, bounds.Y, bounds.Width, headerHeight);
        Rect contentBounds = new(bounds.X, header.Bottom, bounds.Width, Math.Max(0, bounds.Height - headerHeight - statusHeight - footerHeight));
        Rect statusBounds = new(bounds.X, contentBounds.Bottom, bounds.Width, statusHeight);
        ICompositeDialogContentFrame contentFrame = content.CalculateFrame(contentBounds);
        ScrollableFormFrame formFrame = null!;

        PopupRenderOptions? popup = options.Appearance switch
        {
            DialogAppearance.Popup => PaletteStyles.DialogPopupOptions(UiTheme.Current),
            DialogAppearance.Warning => WarningDialogStyles.OuterOptions,
            _ => null,
        };
        _renderer.Render(
            context.Canvas,
            modal,
            options.Title,
            options.DoubleBorder,
            popup is null ? FarDialogStyles.OuterOptions : popup with { DrawBorder = false },
            popup is null ? FarDialogStyles.FrameOptions : popup with { DrawShadow = false },
            (_, _) =>
            {
                formFrame = form.Render(new FormRenderContext(context, header, FarDialogStyles.Border, footer), focus);
                content.Render(context.Canvas, contentFrame);
                if (statusBounds.Height > 0)
                    context.Canvas.Write(statusBounds.X, statusBounds.Y, ConsoleTextMetrics.FitToCells(status?.Invoke() ?? string.Empty, statusBounds.Width), FarDialogStyles.Fill);
            });
        return new(modal, formFrame, contentFrame);
    }

    private static UiInteractionFrame BuildInteractionFrame(Frame frame, ScrollableFormDialog form, ICompositeDialogContent content)
    {
        UiInteractionFragment formFragment = form.BuildInteractionFragment(frame.Form);
        UiFocusEntry[] header = formFragment.FocusEntries.Where(entry => !IsFooter(frame.Form, entry.Target)).ToArray();
        UiFocusEntry[] footer = formFragment.FocusEntries.Where(entry => IsFooter(frame.Form, entry.Target)).ToArray();
        UiInteractionFragment contentFragment = content.BuildInteractionFragment(frame.Content, header.Length);
        var builder = new UiInteractionFrameBuilder()
            .AddHitRegions(formFragment.HitRegions)
            .AddFocusEntries(header.Select((entry, index) => new UiFocusEntry(entry.Target, index, entry.IsEnabled, entry.Cursor)))
            .AddFragment(contentFragment)
            .AddFocusEntries(footer.Select((entry, index) => new UiFocusEntry(entry.Target, header.Length + contentFragment.FocusEntries.Count + index, entry.IsEnabled, entry.Cursor)))
            .SetDefaultFocusTarget(header.FirstOrDefault()?.Target ?? contentFragment.FocusEntries.FirstOrDefault()?.Target ?? footer.FirstOrDefault()?.Target);
        return builder.Build();
    }

    private static bool IsFooter(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.OfType<FormRowTargetFrame>().FirstOrDefault(candidate => candidate.Target == target)?.IsFooter == true;

    private static (CompositeDialogEvent Semantic, UiInputResult UiResult) Route(ConsoleInputEvent input, Frame frame, UiInputRouteContext route, ScrollableFormDialog form, ICompositeDialogContent content, IReadOnlyDictionary<ConsoleKey, string>? commands)
    {
        CompositeDialogContentInputResult contentResult = content.RouteInput(input, frame.Content, route);
        if (contentResult.IsContentRoute)
        {
            if (contentResult.Kind != CompositeDialogContentEventKind.NotHandled)
                return (contentResult.Kind switch
                {
                    CompositeDialogContentEventKind.SelectionChanged => new(CompositeDialogEventKind.ContentSelectionChanged),
                    CompositeDialogContentEventKind.Confirmed => new(CompositeDialogEventKind.ContentConfirmed),
                    _ => new(CompositeDialogEventKind.NotHandled),
                }, contentResult.UiResult);
            if (contentResult.UiResult.Handled)
                return (new(CompositeDialogEventKind.NotHandled), contentResult.UiResult);
            if (UiFocusRouting.TryHandleTraversal(input, out UiInputResult traversal))
                return (new(CompositeDialogEventKind.NotHandled), traversal);
        }

        FormRouteResult formResult = form.RouteInput(input, frame.Form, route, allowUnfocusedButtonHotkeys: true);
        if (formResult.FormResult.Kind == FormInputResultKind.Cancel)
            return (new(CompositeDialogEventKind.Cancelled), formResult.UiResult);
        if (formResult.FormResult.Kind == FormInputResultKind.ValueChanged)
            return (new(CompositeDialogEventKind.ValueChanged, SourceControl: formResult.FormResult.SourceTarget), formResult.UiResult);
        if (formResult.FormResult.Command is { } command)
            return (new(CompositeDialogEventKind.Command, command), formResult.UiResult);
        if (!formResult.FormResult.IsHandled && input is KeyConsoleInputEvent { Key.Key: var key } && commands is not null && commands.TryGetValue(key, out string? mappedCommand))
            return (new(CompositeDialogEventKind.Command, mappedCommand, key), UiInputResult.HandledResult);
        return (new(CompositeDialogEventKind.NotHandled), formResult.UiResult);
    }

    private readonly record struct Frame(ModalDialogRenderer.Layout Modal, ScrollableFormFrame Form, ICompositeDialogContentFrame Content);
}
