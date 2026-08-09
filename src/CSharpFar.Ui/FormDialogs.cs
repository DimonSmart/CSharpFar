namespace CSharpFar.Ui;

/// <summary>The standard window appearance for an ordinary modal form.</summary>
public enum FormDialogAppearance
{
    Standard,
    Popup,
}

/// <summary>Semantic window options for an ordinary modal form.</summary>
public sealed record FormDialogOptions(
    string Title,
    int PreferredWidth,
    int PreferredHeight,
    int MinWidth = 20,
    int MinHeight = 8,
    bool SubmitOnEnter = false)
{
    /// <summary>Semantic form-layout preferences.</summary>
    public FormLayoutOptions Layout { get; init; } = new();

    public bool DoubleBorder { get; init; } = true;

    public FormDialogAppearance Appearance { get; init; } = FormDialogAppearance.Standard;

    /// <summary>Gets the theme to use while this form is displayed.</summary>
    public Func<ConsolePalette>? Theme { get; init; }
}

/// <summary>The semantic outcome of handling an ordinary form event.</summary>
public readonly record struct FormDialogOutcome<TResult>(
    bool IsComplete,
    TResult? Result,
    string? FocusRowId)
{
    /// <summary>Keeps the form open and refreshes it.</summary>
    public static FormDialogOutcome<TResult> Continue() => new(false, default, null);

    /// <summary>Keeps the form open, refreshes it, and focuses the named row.</summary>
    public static FormDialogOutcome<TResult> ContinueWithFocus(string rowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
        return new(false, default, rowId);
    }

    /// <summary>Closes the form and returns its result.</summary>
    public static FormDialogOutcome<TResult> Complete(TResult result) => new(true, result, null);
}

/// <summary>Application-level façade for standard modal forms.</summary>
public sealed class FormDialogs
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

        return _host.Run(
            form,
            CreateModalOptions(options),
            layout => currentFooter.Count == 0
                ? ModalFormLayout.BodyOnly(layout.ContentBounds)
                : ModalFormLayout.WithFooter(layout.ContentBounds, FooterHeight(currentFooter)),
            formEvent => ToLoopResult(handle(formEvent), form),
            prepareRender: () =>
            {
                IReadOnlyList<FormRow> body = rows() ?? throw new InvalidOperationException("Form rows cannot be null.");
                currentFooter = footer?.Invoke() ?? [];
                form.SetRows(body, currentFooter);
            },
            beginRenderScope: options.Theme is null ? null : () => UiTheme.UseTemporary(options.Theme()),
            cancellationToken: cancellationToken);
    }

    public TResult Show<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<FormDialogEvent, FormDialogOutcome<TResult>> handle,
        CancellationToken cancellationToken = default) =>
        Show(options, rows, footer: null, handle, cancellationToken);

    private static ModalDialogLoopResult<TResult> ToLoopResult<TResult>(
        FormDialogOutcome<TResult> outcome,
        ScrollableFormDialog form)
    {
        if (outcome.IsComplete)
            return ModalDialogLoopResult<TResult>.Complete(outcome.Result!);

        if (outcome.FocusRowId is { } rowId)
            return ModalDialogLoopResult<TResult>.ContinueWithFocus(form.GetFocusTarget(rowId));

        return ModalDialogLoopResult<TResult>.ContinueChanged;
    }

    private static int FooterHeight(IReadOnlyList<FormRow> footer) => footer.Sum(row => row.Height);

    private static ModalFormOptions CreateModalOptions(FormDialogOptions options)
    {
        PopupRenderOptions? popup = options.Appearance == FormDialogAppearance.Popup
            ? PaletteStyles.DialogPopupOptions(UiTheme.Current)
            : null;
        return new ModalFormOptions(
            options.Title,
            options.PreferredWidth,
            options.PreferredHeight,
            options.MinWidth,
            options.MinHeight,
            DoubleBorder: options.DoubleBorder,
            OuterRenderOptions: popup,
            FrameRenderOptions: popup is null ? null : popup with { DrawShadow = false },
            SubmitOnEnter: options.SubmitOnEnter);
    }
}
