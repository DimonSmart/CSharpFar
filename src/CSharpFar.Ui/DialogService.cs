namespace CSharpFar.Ui;

/// <summary>Application-level façade for standard modal dialogs.</summary>
public sealed class DialogService
{
    private readonly ModalDialogHost _modalDialogs;
    private readonly FormFieldFactory _fields;

    public DialogService(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public void Message(string title, string message) =>
        new MessageDialog(_modalDialogs).Show(title, message);

    public int Message(string title, string message, IReadOnlyList<string> buttons) =>
        new MessageDialog(_modalDialogs).ShowButtons(title, message, buttons);

    public bool Confirm(string title, string question, string itemName) =>
        new ConfirmDialog(_modalDialogs).Show(title, question, itemName);

    public string? Input(SingleLineInputDialogOptions options) =>
        new SingleLineInputDialog(_modalDialogs, _fields).Show(options);

    public ChoiceDialogResult Choice(ChoiceDialogOptions options) =>
        new ChoiceDialog(_modalDialogs).Show(options);

    public SelectionListDialogResult<T> Select<T>(SelectionDialogOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Items);
        ArgumentNullException.ThrowIfNull(options.ItemText);
        ArgumentNullException.ThrowIfNull(options.Title);

        var dialog = new SelectionListDialog<T>(options.Items, options.ItemText, options.Title)
        {
            MaxVisibleRows = options.Presentation?.MaxVisibleRows ?? options.MaxVisibleRows,
            MaxWidth = options.Presentation?.MaxWidth ?? options.MaxWidth,
            DoubleBorder = options.DoubleBorder,
            SelectionChanged = options.SelectionChanged,
        };
        if (options.Items.Count > 0)
            dialog.SelectedIndex = Math.Clamp(options.SelectedIndex, 0, options.Items.Count - 1);
        return dialog.Show(_modalDialogs);
    }

    public SelectionListDialogResult<T> Select<T>(
        IReadOnlyList<T> items,
        Func<T, string> itemText,
        string title,
        int selectedIndex = 0,
        int maxVisibleRows = 15)
        => Select(new SelectionDialogOptions<T>
        {
            Items = items,
            ItemText = itemText,
            Title = title,
            SelectedIndex = selectedIndex,
            MaxVisibleRows = maxVisibleRows,
        });

    public SearchOptionsDialogResult? SearchOptions(SearchOptionsDialogOptions options) =>
        new SearchOptionsDialog(_modalDialogs, _fields).Show(options);

    public TResult? List<T, TResult>(ListDialogOptions<T, TResult> options) =>
        new ListDialog<T, TResult>(_modalDialogs).Show(options);

    public TResult Form<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Func<FormDialogEvent, FormDialogOutcome<TResult>> handle,
        CancellationToken cancellationToken = default) =>
        new FormDialogs(_modalDialogs).Show(options, rows, footer, handle, cancellationToken);

    public TResult Form<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<FormDialogEvent, FormDialogOutcome<TResult>> handle,
        CancellationToken cancellationToken = default) =>
        new FormDialogs(_modalDialogs).Show(options, rows, handle, cancellationToken);

    public TResult? Form<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<IReadOnlyList<FormRow>>? footer,
        Func<FormSubmitResult<TResult>> submit,
        CancellationToken cancellationToken = default) =>
        new FormDialogs(_modalDialogs).Show(options, rows, footer, submit, cancellationToken);

    public TResult? Form<TResult>(
        FormDialogOptions options,
        Func<IReadOnlyList<FormRow>> rows,
        Func<FormSubmitResult<TResult>> submit,
        CancellationToken cancellationToken = default) =>
        new FormDialogs(_modalDialogs).Show(options, rows, footer: null, submit, cancellationToken);
}
