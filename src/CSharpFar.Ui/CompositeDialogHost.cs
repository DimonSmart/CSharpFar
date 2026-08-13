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
    bool DoubleBorder = true);

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

/// <summary>Composes a form, a routed table, optional status, and footer actions into one modal lifecycle.</summary>
public sealed class CompositeDialogHost
{
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _renderer = new();

    public CompositeDialogHost(ModalDialogHost modalDialogs) =>
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));

    public TResult Run<T, TResult>(
        CompositeDialogOptions options,
        ScrollableFormDialog form,
        TableList<T> content,
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

        return _modalDialogs.RunInteractive<Frame<T>, CompositeDialogEvent, TResult>(
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

    private Frame<T> Render<T>(UiRenderContext context, IUiFocusState focus, CompositeDialogOptions options, ScrollableFormDialog form, TableList<T> content, Func<string?>? status)
    {
        ModalDialogRenderer.Layout modal = _renderer.CalculateLayout(context.Size, options.PreferredWidth, options.PreferredHeight, options.MinWidth, options.MinHeight);
        Rect bounds = modal.ContentBounds;
        int footerHeight = Math.Min(1, bounds.Height);
        int headerHeight = Math.Min(form.NaturalContentHeight - 1, Math.Max(0, bounds.Height - footerHeight));
        // A form with footer rows exposes the action row in NaturalContentHeight. The
        // remaining rows are the semantic header; never reserve negative geometry.
        headerHeight = Math.Max(0, headerHeight);
        Rect footer = new(bounds.X, Math.Max(bounds.Y, bounds.Bottom - footerHeight), bounds.Width, footerHeight);
        int statusHeight = status is null || bounds.Height - headerHeight - footerHeight <= 0 ? 0 : 1;
        Rect header = new(bounds.X, bounds.Y, bounds.Width, headerHeight);
        Rect contentBounds = new(bounds.X, header.Bottom, bounds.Width, Math.Max(0, bounds.Height - headerHeight - statusHeight - footerHeight));
        Rect statusBounds = new(bounds.X, contentBounds.Bottom, bounds.Width, statusHeight);
        TableListFrame contentFrame = content.CalculateFrame(contentBounds);
        ScrollableFormFrame formFrame = null!;

        _renderer.Render(context.Canvas, modal, options.Title, options.DoubleBorder, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, _) =>
        {
            formFrame = form.Render(new FormRenderContext(context, header, FarDialogStyles.Border, footer), focus);
            content.Render(context.Canvas, contentFrame);
            if (statusBounds.Height > 0)
                context.Canvas.Write(statusBounds.X, statusBounds.Y, ConsoleTextMetrics.FitToCells(status?.Invoke() ?? string.Empty, statusBounds.Width), FarDialogStyles.Fill);
        });
        return new(modal, formFrame, contentFrame);
    }

    private static UiInteractionFrame BuildInteractionFrame<T>(Frame<T> frame, ScrollableFormDialog form, TableList<T> content)
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

    private static (CompositeDialogEvent Semantic, UiInputResult UiResult) Route<T>(ConsoleInputEvent input, Frame<T> frame, UiInputRouteContext route, ScrollableFormDialog form, TableList<T> content, IReadOnlyDictionary<ConsoleKey, string>? commands)
    {
        if (content.IsTargetRoute(route))
        {
            var contentResult = content.RouteInput(input, frame.Content, route);
            if (contentResult.Semantic.IsHandled)
                return (contentResult.Semantic.Kind switch
                {
                    ScrollableListInputResultKind.SelectionChanged => new(CompositeDialogEventKind.ContentSelectionChanged),
                    ScrollableListInputResultKind.Confirmed => new(CompositeDialogEventKind.ContentConfirmed),
                    _ => new(CompositeDialogEventKind.NotHandled),
                }, contentResult.UiResult);
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

    private readonly record struct Frame<T>(ModalDialogRenderer.Layout Modal, ScrollableFormFrame Form, TableListFrame Content);
}
