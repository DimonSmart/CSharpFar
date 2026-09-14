using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>
/// Reusable settings surface composed from the ordinary routed list, form, focus,
/// pointer, scrollbar, and modal primitives.
/// </summary>
internal sealed class SettingsDialog
{
    private const string BackCommand = "settings.back";
    private const string SaveCommand = "settings.save";
    private const string CancelCommand = "settings.cancel";
    private static readonly UiTargetId NavigationTarget = new("settings.navigation");
    private static readonly UiTargetId NavigationScrollbarTarget = new("settings.navigation.scrollbar");

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _renderer = new();

    public SettingsDialog(ModalDialogHost modalDialogs) =>
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));

    public SettingsDialogResult Show(
        SettingsDialogOptions options,
        IReadOnlyList<SettingsPage> pages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            throw new ArgumentException("At least one settings page is required.", nameof(pages));
        if (options.CompactBreakpoint < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Compact breakpoint must be positive.");

        SettingsPage[] snapshot = pages.ToArray();
        ValidatePages(snapshot);

        int initialIndex = 0;
        if (!string.IsNullOrEmpty(options.InitialPageId))
        {
            int requested = Array.FindIndex(snapshot, page =>
                string.Equals(page.Id, options.InitialPageId, StringComparison.Ordinal));
            if (requested >= 0)
                initialIndex = requested;
        }

        var navigationState = new ScrollableListState<SettingsPage>(snapshot, initialIndex);
        var navigation = new RoutedScrollableList<SettingsPage>(
            navigationState,
            NavigationTarget,
            NavigationScrollbarTarget,
            RoutedScrollableListOptions.SelectionDialog);

        PageRuntime[] pageRuntimes = snapshot.Select(CreatePageRuntime).ToArray();
        var footer = new ScrollableFormDialog(
            [],
            new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden));
        footer.SetRows(
            [],
            [
                FormControls.Buttons(
                    "settings.footer",
                    DialogButton.Default(SaveCommand, "Save", 'S'),
                    DialogButton.Cancel("Cancel", 'C', CancelCommand)),
            ]);

        string? errorMessage = null;
        bool compactPageOpen = false;
        bool? committedCompact = null;

        return _modalDialogs.RunInteractive<SettingsFrame, SettingsSemanticEvent, SettingsDialogResult>(
            (context, focus) =>
            {
                using IDisposable? theme = options.Theme is null ? null : UiTheme.UseTemporary(options.Theme());
                return Render(
                    context,
                    focus,
                    options,
                    navigation,
                    pageRuntimes,
                    footer,
                    errorMessage,
                    compactPageOpen,
                    committedCompact);
            },
            BuildInteractionFrame,
            (input, frame, route) => Route(
                input,
                frame,
                route,
                navigation,
                pageRuntimes,
                footer),
            (routed, semantic) =>
            {
                SettingsFrame frame = routed.Frame;
                switch (semantic.Kind)
                {
                    case SettingsSemanticKind.Cancel:
                        return ModalDialogLoopResult<SettingsDialogResult>.Complete(SettingsDialogResult.Cancelled);

                    case SettingsSemanticKind.Save:
                    {
                        for (int index = 0; index < pageRuntimes.Length; index++)
                        {
                            Func<FormSubmitResult<bool>>? validator = pageRuntimes[index].Page.Validate;
                            if (validator is null)
                                continue;

                            FormSubmitResult<bool> validation = validator();
                            if (validation.IsSuccess)
                                continue;

                            navigationState.SetSelectedIndex(index, Math.Max(1, frame.NavigationFrame?.ViewportRows ?? 1));
                            compactPageOpen = frame.Compact;
                            errorMessage = validation.ErrorMessage;

                            PageRuntime invalidPage = pageRuntimes[index];
                            UiTargetId target = validation.FocusTarget is { } focusTarget
                                ? invalidPage.Form.GetFocusTarget(focusTarget)
                                : invalidPage.FirstContentTarget;
                            if (!frame.Compact && target == invalidPage.BackTarget)
                                target = NavigationTarget;
                            else
                                invalidPage.PreferredFocusTarget = target;
                            return ModalDialogLoopResult<SettingsDialogResult>.ContinueWithFocus(target);
                        }

                        errorMessage = null;
                        return ModalDialogLoopResult<SettingsDialogResult>.Complete(SettingsDialogResult.Saved);
                    }

                    case SettingsSemanticKind.NavigationSelectionChanged:
                        errorMessage = null;
                        return ModalDialogLoopResult<SettingsDialogResult>.ContinueChanged;

                    case SettingsSemanticKind.EnterContent:
                    {
                        compactPageOpen = frame.Compact;
                        errorMessage = null;
                        PageRuntime page = pageRuntimes[navigationState.SelectedIndex];
                        if (!frame.Compact &&
                            (frame.PageFrame is null || !Targets(frame.PageFrame, page.PreferredFocusTarget)))
                        {
                            return ModalDialogLoopResult<SettingsDialogResult>.ContinueChanged;
                        }

                        return ModalDialogLoopResult<SettingsDialogResult>.ContinueWithFocus(page.PreferredFocusTarget);
                    }

                    case SettingsSemanticKind.BackToNavigation:
                        compactPageOpen = false;
                        return ModalDialogLoopResult<SettingsDialogResult>.ContinueWithFocus(NavigationTarget);

                    case SettingsSemanticKind.ValueChanged:
                        errorMessage = null;
                        return ModalDialogLoopResult<SettingsDialogResult>.ContinueChanged;

                    default:
                        return ModalDialogLoopResult<SettingsDialogResult>.ContinueNoChange;
                }
            },
            applyCommittedFrame: frame =>
            {
                if (frame.NavigationFrame is { } navigationFrame)
                    navigation.ApplyCommittedFrame(navigationFrame);
                compactPageOpen = frame.Compact && frame.CompactPageOpen;
                committedCompact = frame.Compact;
            },
            cancellationToken: cancellationToken);
    }

    private SettingsFrame Render(
        UiRenderContext context,
        IUiFocusState focus,
        SettingsDialogOptions options,
        RoutedScrollableList<SettingsPage> navigation,
        IReadOnlyList<PageRuntime> pages,
        ScrollableFormDialog footer,
        string? errorMessage,
        bool compactPageOpen,
        bool? committedCompact)
    {
        (int width, int height) = DialogSizing.Resolve(
            context.Size,
            options.PreferredWidth,
            options.PreferredHeight,
            options.ResizeMode,
            options.HorizontalMargin,
            options.VerticalMargin);
        ModalDialogRenderer.Layout modal = _renderer.CalculateLayout(
            context.Size,
            width,
            height,
            options.MinWidth,
            options.MinHeight);

        Rect content = modal.ContentBounds;
        int footerHeight = Math.Min(1, content.Height);
        int statusHeight = string.IsNullOrEmpty(errorMessage) || content.Height <= footerHeight ? 0 : 1;
        int bodyHeight = Math.Max(0, content.Height - footerHeight - statusHeight);
        Rect body = new(content.X, content.Y, content.Width, bodyHeight);
        Rect status = new(content.X, body.Bottom, content.Width, statusHeight);
        Rect footerBounds = new(content.X, status.Bottom, content.Width, footerHeight);

        bool compact = body.Width < options.CompactBreakpoint;
        Rect navigationBounds = default;
        Rect pageBounds = default;
        ScrollableListFrame? navigationFrame = null;
        ScrollableFormFrame? pageFrame = null;
        ScrollableFormFrame? footerFrame = null;
        PageRuntime selectedPage = pages[navigation.State.SelectedIndex];
        selectedPage.ConfigureLayout(compact);
        bool enteringCompact = compact && committedCompact is false;
        bool showCompactPage = compact &&
            (compactPageOpen ||
             enteringCompact && focus.FocusedTarget is UiTargetId focusedTarget && selectedPage.OwnsTarget(focusedTarget));

        if (compact)
        {
            navigationBounds = showCompactPage ? default : body;
            pageBounds = showCompactPage ? body : default;
        }
        else
        {
            int navigationWidth = Math.Clamp(body.Width / 4, 16, Math.Max(16, body.Width - 12));
            navigationWidth = Math.Min(navigationWidth, Math.Max(0, body.Width - 2));
            navigationBounds = new Rect(body.X, body.Y, navigationWidth, body.Height);
            pageBounds = new Rect(
                Math.Min(body.Right, navigationBounds.Right + 1),
                body.Y,
                Math.Max(0, body.Right - navigationBounds.Right - 1),
                body.Height);
        }

        using IDisposable appearance = DialogStyles.UseAppearance(DialogAppearance.Standard);
        _renderer.Render(
            context.Canvas,
            modal,
            options.Title,
            options.DoubleBorder,
            DialogStyles.OuterOptions,
            DialogStyles.FrameOptions,
            (_, _) =>
            {
                context.Canvas.FillRegion(content, DialogStyles.Fill);

                var previousFocus = focus.CurrentFrame.Entries.ToList();
                if (!previousFocus.Any(entry => entry.Target == NavigationTarget))
                    previousFocus.Add(new UiFocusEntry(NavigationTarget, 0));

                if (navigationBounds.Width > 0 && navigationBounds.Height > 0)
                {
                    Rect listBounds = new(
                        navigationBounds.X,
                        navigationBounds.Y,
                        Math.Max(0, navigationBounds.Width - 1),
                        navigationBounds.Height);
                    Rect scrollbarBounds = new(
                        Math.Max(navigationBounds.X, navigationBounds.Right - 1),
                        navigationBounds.Y,
                        Math.Min(1, navigationBounds.Width),
                        navigationBounds.Height);
                    navigationFrame = navigation.CalculateFrame(listBounds, scrollbarBounds);
                    navigation.Render(
                        context.Canvas,
                        navigationFrame,
                        new ScrollableListRenderOptions<SettingsPage>(
                            static page => page.Title,
                            string.Empty,
                            DialogStyles.Fill,
                            DialogStyles.FocusedInput,
                            DialogStyles.Fill));
                    navigation.RenderScrollbar(context.Canvas, navigationFrame, DialogStyles.Border);
                }

                if (!compact && navigationBounds.Width > 0 && pageBounds.Width > 0)
                {
                    for (int row = 0; row < body.Height; row++)
                        context.Canvas.Write(navigationBounds.Right, body.Y + row, "│", DialogStyles.Border);
                }

                if (pageBounds.Width > 0 && pageBounds.Height > 0)
                {
                    pageFrame = selectedPage.Form.Render(
                        new FormRenderContext(context, pageBounds, DialogStyles.Border),
                        focus,
                        previousFocus,
                        compact ? selectedPage.PreferredFocusTarget : NavigationTarget);
                    ScrollableFormFrame committedPageFrame = pageFrame;
                    context.PublishOnStable(() => selectedPage.RememberTargets(committedPageFrame));
                }

                if (status.Height > 0)
                {
                    context.Canvas.Write(
                        status.X,
                        status.Y,
                        ConsoleTextMetrics.FitToCells(errorMessage ?? string.Empty, status.Width),
                        DialogStyles.Error);
                }

                if (footerBounds.Height > 0)
                {
                    footerFrame = footer.Render(
                        new FormRenderContext(
                            context,
                            new Rect(footerBounds.X, footerBounds.Y, footerBounds.Width, 0),
                            DialogStyles.Border,
                            footerBounds),
                        focus,
                        previousFocus,
                        compact && showCompactPage ? selectedPage.PreferredFocusTarget : NavigationTarget);
                }
            });

        return new SettingsFrame(
            modal,
            navigation,
            compact,
            showCompactPage,
            navigationBounds,
            pageBounds,
            footerBounds,
            navigationFrame,
            pageFrame,
            footerFrame,
            selectedPage,
            footer);
    }

    private static UiInteractionFrame BuildInteractionFrame(SettingsFrame frame)
    {
        var builder = new UiInteractionFrameBuilder();
        int order = 0;

        if (frame.NavigationFrame is { } navigationFrame)
        {
            UiInteractionFragment navigation = frame.Navigation.BuildInteractionFragment(navigationFrame, order);
            builder.AddHitRegions(navigation.HitRegions);
            builder.AddFocusEntries(navigation.FocusEntries.Select(entry =>
                new UiFocusEntry(entry.Target, order++, entry.IsEnabled, entry.Cursor)));
        }

        if (frame.PageFrame is { } pageFrame)
        {
            UiInteractionFragment page = frame.SelectedPage.Form.BuildInteractionFragment(pageFrame);
            builder.AddHitRegions(page.HitRegions);
            foreach (UiFocusEntry entry in page.FocusEntries.OrderBy(entry => entry.TabOrder))
                builder.AddFocusEntry(entry.Target, order++, entry.IsEnabled, entry.Cursor);
        }

        if (frame.FooterFrame is { } footerFrame)
        {
            UiInteractionFragment footer = frame.Footer.BuildInteractionFragment(footerFrame);
            builder.AddHitRegions(footer.HitRegions);
            foreach (UiFocusEntry entry in footer.FocusEntries.OrderBy(entry => entry.TabOrder))
                builder.AddFocusEntry(entry.Target, order++, entry.IsEnabled, entry.Cursor);
        }

        UiTargetId? defaultTarget = frame.Compact && frame.CompactPageOpen
            ? frame.SelectedPage.PreferredFocusTarget
            : NavigationTarget;
        return builder.SetDefaultFocusTarget(defaultTarget).Build();
    }

    private static (SettingsSemanticEvent Semantic, UiInputResult UiResult) Route(
        ConsoleInputEvent input,
        SettingsFrame frame,
        UiInputRouteContext route,
        RoutedScrollableList<SettingsPage> navigation,
        IReadOnlyList<PageRuntime> pages,
        ScrollableFormDialog footer)
    {
        PageRuntime selectedPage = pages[navigation.State.SelectedIndex];

        if (frame.PageFrame is { } pageFrame &&
            (Targets(pageFrame, route.Target) ||
             input is MouseConsoleInputEvent mouse && route.RouteKind == UiInputRouteKind.Layer && frame.PageBounds.Contains(mouse.X, mouse.Y)))
        {
            if (route.FocusState.FocusedTarget is UiTargetId currentPageTarget && Targets(pageFrame, currentPageTarget))
                selectedPage.PreferredFocusTarget = currentPageTarget;

            FormRouteResult pageResult = selectedPage.Form.RouteInput(input, pageFrame, route, allowUnfocusedButtonHotkeys: true);
            if (pageResult.UiResult.FocusRequest.Kind == UiFocusRequestKind.Set &&
                pageResult.UiResult.FocusRequest.Target is UiTargetId requestedPageTarget &&
                Targets(pageFrame, requestedPageTarget))
            {
                selectedPage.PreferredFocusTarget = requestedPageTarget;
            }
            SettingsSemanticEvent semantic = FromFormResult(pageResult.FormResult);
            if (pageResult.FormResult.IsHandled)
                return (semantic, pageResult.UiResult);

            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.LeftArrow } &&
                route.FocusState.FocusedTarget is UiTargetId focused &&
                Targets(pageFrame, focused))
            {
                return (new(SettingsSemanticKind.BackToNavigation), UiInputResult.HandledResult);
            }
        }

        if (frame.FooterFrame is { } footerFrame && Targets(footerFrame, route.Target))
        {
            FormRouteResult footerResult = footer.RouteInput(input, footerFrame, route, allowUnfocusedButtonHotkeys: true);
            SettingsSemanticEvent semantic = FromFormResult(footerResult.FormResult);
            if (footerResult.FormResult.IsHandled)
                return (semantic, footerResult.UiResult);
        }

        if (frame.NavigationFrame is { } navigationFrame &&
            (navigation.IsTargetRoute(route) || route.FocusState.FocusedTarget == NavigationTarget))
        {
            RoutedScrollableListInputResult listResult = navigation.RouteInput(input, navigationFrame, route);
            if (listResult.ListResult.Kind == ScrollableListInputResultKind.SelectionChanged)
                return (new(SettingsSemanticKind.NavigationSelectionChanged), listResult.UiResult);
            if (listResult.ListResult.Kind == ScrollableListInputResultKind.Confirmed)
                return (new(SettingsSemanticKind.EnterContent), listResult.UiResult);
            if (listResult.ListResult.IsHandled)
                return (default, listResult.UiResult);

            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.RightArrow or ConsoleKey.Enter })
                return (new(SettingsSemanticKind.EnterContent), UiInputResult.HandledResult);
        }

        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
            return (new(SettingsSemanticKind.Save), UiInputResult.HandledResult);
        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
            return (new(SettingsSemanticKind.Cancel), UiInputResult.HandledResult);
        if (UiFocusRouting.TryHandleTraversal(input, out UiInputResult traversal))
            return (default, traversal);

        return (default, UiInputResult.NotHandled);
    }

    private static SettingsSemanticEvent FromFormResult(FormInputResult result) =>
        result.Kind switch
        {
            FormInputResultKind.Cancel => new(SettingsSemanticKind.Cancel),
            FormInputResultKind.ValueChanged => new(SettingsSemanticKind.ValueChanged),
            FormInputResultKind.Submit when string.Equals(result.Command, BackCommand, StringComparison.Ordinal) =>
                new(SettingsSemanticKind.BackToNavigation),
            FormInputResultKind.Submit when string.Equals(result.Command, SaveCommand, StringComparison.Ordinal) =>
                new(SettingsSemanticKind.Save),
            _ => default,
        };

    private static bool Targets(ScrollableFormFrame frame, UiTargetId? target) =>
        target is { } value && frame.Targets.Any(candidate => candidate.Target == value);

    private static PageRuntime CreatePageRuntime(SettingsPage page)
    {
        var back = FormControls.Buttons(
            $"settings.back.{page.Id}",
            DialogButton.Action(BackCommand, "< Pages", 'P'));
        FormRow[] rows = [back, .. page.Rows];
        var form = new ScrollableFormDialog(
            rows,
            new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden));

        UiTargetId backTarget = form.GetFocusTarget(back);
        FormRow? firstContent = page.Rows.FirstOrDefault(row => row.IsFocusable);
        UiTargetId firstContentTarget = firstContent switch
        {
            null => backTarget,
            { FocusTarget: { } target } => form.GetFocusTarget(target),
            IFormFocusTarget target => form.GetFocusTarget(target),
            { Id: { Length: > 0 } id } => form.GetFocusTarget(id),
            _ => backTarget,
        };

        return new PageRuntime(page, form, back, backTarget, firstContentTarget);
    }

    private static void ValidatePages(IReadOnlyList<SettingsPage> pages)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (SettingsPage page in pages)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentException.ThrowIfNullOrWhiteSpace(page.Id);
            if (!ids.Add(page.Id))
                throw new InvalidOperationException($"Duplicate settings page ID '{page.Id}'.");
        }
    }

    private sealed class PageRuntime
    {
        private bool? _compact;
        private HashSet<UiTargetId> _knownTargets = [];

        public PageRuntime(
            SettingsPage page,
            ScrollableFormDialog form,
            ButtonRow back,
            UiTargetId backTarget,
            UiTargetId firstContentTarget)
        {
            Page = page;
            Form = form;
            Back = back;
            BackTarget = backTarget;
            FirstContentTarget = firstContentTarget;
            PreferredFocusTarget = firstContentTarget;
        }

        public SettingsPage Page { get; }
        public ScrollableFormDialog Form { get; }
        public ButtonRow Back { get; }
        public UiTargetId BackTarget { get; }
        public UiTargetId FirstContentTarget { get; }
        public UiTargetId PreferredFocusTarget { get; set; }

        public bool OwnsTarget(UiTargetId target) => _knownTargets.Contains(target);

        public void ConfigureLayout(bool compact)
        {
            if (_compact == compact)
                return;

            Form.SetRows(compact ? [Back, .. Page.Rows] : Page.Rows);
            if (!compact && PreferredFocusTarget == BackTarget && FirstContentTarget != BackTarget)
                PreferredFocusTarget = FirstContentTarget;
            _compact = compact;
        }

        public void RememberTargets(ScrollableFormFrame frame)
        {
            _knownTargets = frame.Targets.Select(target => target.Target).ToHashSet();
        }
    }

    private sealed record SettingsFrame(
        ModalDialogRenderer.Layout Modal,
        RoutedScrollableList<SettingsPage> Navigation,
        bool Compact,
        bool CompactPageOpen,
        Rect NavigationBounds,
        Rect PageBounds,
        Rect FooterBounds,
        ScrollableListFrame? NavigationFrame,
        ScrollableFormFrame? PageFrame,
        ScrollableFormFrame? FooterFrame,
        PageRuntime SelectedPage,
        ScrollableFormDialog Footer);

    private enum SettingsSemanticKind
    {
        None,
        NavigationSelectionChanged,
        EnterContent,
        BackToNavigation,
        ValueChanged,
        Save,
        Cancel,
    }

    private readonly record struct SettingsSemanticEvent(SettingsSemanticKind Kind);
}
