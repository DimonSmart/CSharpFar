namespace CSharpFar.Ui;

/// <summary>The standard window appearance for an ordinary modal form.</summary>
public enum DialogAppearance
{
    Standard,
    Popup,
    Warning,
}

/// <summary>Semantic window options for an ordinary modal form.</summary>
public sealed record FormDialogOptions(
    string Title,
    int? PreferredWidth = null,
    int? PreferredHeight = null,
    int MinWidth = 20,
    int MinHeight = 8,
    bool SubmitOnEnter = false)
{
    /// <summary>Semantic form-layout preferences.</summary>
    public FormLayoutOptions Layout { get; init; } = new();

    public bool DoubleBorder { get; init; } = true;

    public DialogAppearance Appearance { get; init; } = DialogAppearance.Standard;

    public DialogResizeMode ResizeMode { get; init; } = DialogResizeMode.None;

    public int HorizontalMargin { get; init; } = 2;

    public int VerticalMargin { get; init; } = 1;

    /// <summary>Optional semantic control that receives initial form focus.</summary>
    public IFormFocusTarget? InitialFocus { get; init; }

    /// <summary>Gets the theme to use while this form is displayed.</summary>
    public Func<ConsolePalette>? Theme { get; init; }
}

/// <summary>The semantic outcome of handling an ordinary form event.</summary>
public readonly struct FormDialogOutcome<TResult>
{
    private readonly TResult? _result;
    private readonly IFormFocusTarget? _focusTarget;

    private FormDialogOutcome(bool isComplete, TResult? result, string? focusRowId, IFormFocusTarget? focusTarget)
    {
        IsComplete = isComplete;
        _result = result;
        FocusRowId = focusRowId;
        _focusTarget = focusTarget;
    }

    internal bool IsComplete { get; }
    internal TResult? Result => _result;
    internal string? FocusRowId { get; }
    internal IFormFocusTarget? FocusTarget => _focusTarget;

    /// <summary>Keeps the form open and refreshes it.</summary>
    public static FormDialogOutcome<TResult> Continue() => new(false, default, null, null);

    /// <summary>Keeps the form open, refreshes it, and focuses the named row.</summary>
    public static FormDialogOutcome<TResult> ContinueWithFocus(string rowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
        return new(false, default, rowId, null);
    }

    /// <summary>Keeps the form open, refreshes it, and focuses the specified control.</summary>
    public static FormDialogOutcome<TResult> ContinueWithFocus(IFormFocusTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(false, default, null, target);
    }

    /// <summary>Closes the form and returns its result.</summary>
    public static FormDialogOutcome<TResult> Complete(TResult result) => new(true, result, null, null);
}

/// <summary>The semantic result of attempting to submit a standard form.</summary>
public readonly struct FormSubmitResult<TResult>
{
    private FormSubmitResult(TResult? result, string? errorMessage, IFormFocusTarget? focusTarget)
    {
        Result = result;
        ErrorMessage = errorMessage;
        FocusTarget = focusTarget;
    }

    internal bool IsSuccess => ErrorMessage is null;
    internal TResult? Result { get; }
    internal string? ErrorMessage { get; }
    internal IFormFocusTarget? FocusTarget { get; }

    internal static FormSubmitResult<TResult> Succeeded(TResult result) => new(result, null, null);
    internal static FormSubmitResult<TResult> Failed(string errorMessage, IFormFocusTarget? focusTarget) =>
        new(default, errorMessage, focusTarget);
}

/// <summary>Creates semantic results for a standard form submit callback.</summary>
public static class FormSubmit
{
    public static FormSubmitResult<TResult> Success<TResult>(TResult result) =>
        FormSubmitResult<TResult>.Succeeded(result);

    public static FormSubmitResult<TResult> Invalid<TResult>(string errorMessage, IFormFocusTarget? focusTarget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return FormSubmitResult<TResult>.Failed(errorMessage, focusTarget);
    }
}

/// <summary>Internal implementation of standard modal forms exposed through <see cref="DialogService"/>.</summary>
internal sealed class FormDialogs
{
    private readonly ModalFormHost _host;

    public FormDialogs(ModalDialogHost modalDialogs) =>
        _host = new ModalFormHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));

    public TResult Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Func<FormDialogEvent, FormDialogOutcome<TResult>> handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(handle);

        var form = new ScrollableFormDialog(options.Layout.Validate());
        IReadOnlyList<FormRow> currentFooter = [];
        bool initialFocusApplied = false;

        void RefreshRows()
        {
            IReadOnlyList<FormRow> body = rows() ?? throw new InvalidOperationException("Form rows cannot be null.");
            currentFooter = footer?.Invoke() ?? [];
            form.SetRows(body, currentFooter);
            if (!initialFocusApplied && options.InitialFocus is not null)
            {
                form.SetInitialFocus(options.InitialFocus);
                initialFocusApplied = true;
            }
        }

        return _host.Run(
            form,
            CreateModalOptions(options),
            layout => currentFooter.Count == 0
                ? ModalFormLayout.BodyOnly(layout.ContentBounds)
                : ModalFormLayout.WithFooter(layout.ContentBounds, FooterHeight(currentFooter)),
            formEvent =>
            {
                FormDialogOutcome<TResult> outcome = handle(formEvent);
                if (outcome.FocusRowId is not null || outcome.FocusTarget is not null)
                    RefreshRows();
                return ToLoopResult(outcome, form);
            },
            prepareRender: RefreshRows,
            beginRenderScope: options.Theme is null ? null : () => UiTheme.UseTemporary(options.Theme()),
            cancellationToken: cancellationToken);
    }

    public TResult Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<FormDialogEvent, FormDialogOutcome<TResult>> handle,
        CancellationToken cancellationToken = default) =>
        Show(options, rows, footer: null, handle, cancellationToken);

    public TResult? Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Func<FormSubmitResult<TResult>> submit,
        CancellationToken cancellationToken = default) =>
        Show(options, rows, footer, submit, auxiliary: null, cancellationToken);

    public TResult? Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Func<FormSubmitResult<TResult>> submit,
        Func<FormDialogEvent, bool>? auxiliary,
        CancellationToken cancellationToken = default) =>
        ShowStandard(options, rows, footer, submit, valueChanged: null, auxiliary, cancellationToken);

    internal TResult? Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Action<FormDialogEvent> valueChanged,
        Func<FormSubmitResult<TResult>> submit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueChanged);
        return ShowStandard(options, rows, footer, submit, valueChanged, auxiliary: null, cancellationToken);
    }

    public TResult? Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<FormSubmitResult<TResult>> submit,
        CancellationToken cancellationToken = default) =>
        Show(options, rows, footer: null, submit, auxiliary: null, cancellationToken);

    public TResult? Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<FormSubmitResult<TResult>> submit,
        Func<FormDialogEvent, bool>? auxiliary,
        CancellationToken cancellationToken = default) =>
        Show(options, rows, footer: null, submit, auxiliary, cancellationToken);

    private TResult? ShowStandard<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Func<FormSubmitResult<TResult>> submit,
        Action<FormDialogEvent>? valueChanged,
        Func<FormDialogEvent, bool>? auxiliary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submit);

        string? error = null;
        var historyFields = new HashSet<TextField>();

        IReadOnlyList<FormRow> Observe(IReadOnlyList<FormRow> formRows)
        {
            historyFields.Clear();
            foreach (FormRow row in formRows)
                row.CollectTextFields(historyFields);
            return formRows;
        }

        return Show(
            options,
            rows: () => Observe(rows() ?? throw new InvalidOperationException("Form rows cannot be null.")),
            footer: () =>
            {
                IReadOnlyList<FormRow> supplied = footer?.Invoke() ?? [];
                foreach (FormRow row in supplied)
                    row.CollectTextFields(historyFields);
                return [FormFooter.Error(() => error), .. supplied];
            },
            handle: formEvent =>
            {
                if (formEvent.IsCancelled)
                    return FormDialogOutcome<TResult?>.Complete(default);

                if (auxiliary?.Invoke(formEvent) == true)
                    return FormDialogOutcome<TResult?>.Continue();

                if (formEvent.IsValueChanged)
                {
                    valueChanged?.Invoke(formEvent);
                    error = null;
                }

                if (!formEvent.IsSubmitted)
                    return FormDialogOutcome<TResult?>.Continue();

                FormSubmitResult<TResult> outcome = submit();
                if (outcome.IsSuccess)
                {
                    foreach (TextField field in historyFields.Where(field => field.Enabled))
                        field.AcceptHistory();
                    return FormDialogOutcome<TResult?>.Complete(outcome.Result);
                }

                error = outcome.ErrorMessage;
                return outcome.FocusTarget is null
                    ? FormDialogOutcome<TResult?>.Continue()
                    : FormDialogOutcome<TResult?>.ContinueWithFocus(outcome.FocusTarget);
            },
            cancellationToken: cancellationToken);
    }

    private static ModalDialogLoopResult<TResult> ToLoopResult<TResult>(
        FormDialogOutcome<TResult> outcome,
        ScrollableFormDialog form)
    {
        if (outcome.IsComplete)
            return ModalDialogLoopResult<TResult>.Complete(outcome.Result!);

        if (outcome.FocusRowId is { } rowId)
            return ModalDialogLoopResult<TResult>.ContinueWithFocus(form.GetFocusTarget(rowId));

        if (outcome.FocusTarget is { } target)
            return ModalDialogLoopResult<TResult>.ContinueWithFocus(form.GetFocusTarget(target));

        return ModalDialogLoopResult<TResult>.ContinueChanged;
    }

    private static int FooterHeight(IReadOnlyList<FormRow> footer) => footer.Sum(row => row.Height);

    private static ModalFormOptions CreateModalOptions(FormDialogOptions options)
    {
        PopupRenderOptions? popup = options.Appearance switch
        {
            DialogAppearance.Popup => PaletteStyles.DialogPopupOptions(UiTheme.Current),
            DialogAppearance.Warning => WarningDialogStyles.OuterOptions,
            _ => null,
        };
        return new ModalFormOptions(
            options.Title,
            options.PreferredWidth,
            options.PreferredHeight,
            options.MinWidth,
            options.MinHeight,
            DoubleBorder: options.DoubleBorder,
            OuterRenderOptions: popup,
            FrameRenderOptions: popup is null ? null : popup with { DrawShadow = false },
            SubmitOnEnter: options.SubmitOnEnter)
        {
            ResizeMode = options.ResizeMode,
            HorizontalMargin = options.HorizontalMargin,
            VerticalMargin = options.VerticalMargin,
        };
    }
}
