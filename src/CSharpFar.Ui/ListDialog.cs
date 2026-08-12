namespace CSharpFar.Ui;

public sealed class ListDialogOptions<T, TResult>
{
    public required string Title { get; init; }
    public required Func<IReadOnlyList<T>> Items { get; init; }
    public required Func<T, string> ItemText { get; init; }
    public required IReadOnlyList<DialogButton> Actions { get; init; }
    public required Func<ListDialogActionContext<T>, DialogOutcome<TResult>> HandleAction { get; init; }
    public Func<TResult>? Cancel { get; init; }
    public string DefaultItemActionId { get; init; } = "default";
    public string CancelActionId { get; init; } = "cancel";
    public string? DeleteActionId { get; init; }
    public string EmptyText { get; init; } = string.Empty;
    public int DialogWidth { get; init; } = 68;
    public int MinDialogWidth { get; init; } = 40;
    public int MaxVisibleRows { get; init; } = 12;
}

public sealed record ListDialogActionContext<T>(string ActionId, T? SelectedItem, int SelectedIndex);

public abstract record DialogOutcome<TResult>
{
    private DialogOutcome() { }
    public sealed record Continue : DialogOutcome<TResult>;
    public sealed record Refresh : DialogOutcome<TResult>;
    public sealed record Close(TResult Result) : DialogOutcome<TResult>;

    public static DialogOutcome<TResult> ContinueOpen() => new Continue();
    public static DialogOutcome<TResult> RefreshOpen() => new Refresh();
    public static DialogOutcome<TResult> Complete(TResult result) => new Close(result);
}

internal sealed class ListDialog<T, TResult>
{
    private readonly ModalDialogHost _modalDialogs;

    public ListDialog(ModalDialogHost modalDialogs) =>
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));

    public TResult? Show(ListDialogOptions<T, TResult> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Items);
        ArgumentNullException.ThrowIfNull(options.ItemText);
        ArgumentNullException.ThrowIfNull(options.Actions);
        ArgumentNullException.ThrowIfNull(options.HandleAction);

        int selectedIndex = 0;
        while (true)
        {
            IReadOnlyList<T> items = options.Items();
            var dialog = new ListWithButtonsDialog<T>(items, options.ItemText, options.Actions, options.Title)
            {
                DialogWidth = options.DialogWidth,
                MinDialogWidth = options.MinDialogWidth,
                MaxVisibleRows = options.MaxVisibleRows,
                EmptyText = options.EmptyText,
                DefaultListActionId = options.DefaultItemActionId,
                CancelActionId = options.CancelActionId,
                DeleteActionId = options.DeleteActionId,
            };
            if (items.Count > 0)
                dialog.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);

            ListWithButtonsDialogResult<T>? action = dialog.Show(_modalDialogs);
            if (action is null)
                return options.Cancel is null ? default : options.Cancel();

            selectedIndex = action.SelectedIndex;
            DialogOutcome<TResult> outcome = options.HandleAction(
                new ListDialogActionContext<T>(action.ActionId, action.SelectedItem, action.SelectedIndex));
            switch (outcome)
            {
                case DialogOutcome<TResult>.Close close:
                    return close.Result;
                case DialogOutcome<TResult>.Continue:
                case DialogOutcome<TResult>.Refresh:
                    continue;
                default:
                    throw new InvalidOperationException("Unknown list-dialog outcome.");
            }
        }
    }
}
