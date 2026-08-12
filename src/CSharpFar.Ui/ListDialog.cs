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

        return dialog.Show(_modalDialogs, action =>
        {
            if (action is null)
                return ListWithButtonsDialogLoopResult<TResult?>.Complete(options.Cancel is null ? default : options.Cancel());

            DialogOutcome<TResult> outcome = options.HandleAction(
                new ListDialogActionContext<T>(action.ActionId, action.SelectedItem, action.SelectedIndex));
            return outcome switch
            {
                DialogOutcome<TResult>.Close close => ListWithButtonsDialogLoopResult<TResult?>.Complete(close.Result),
                DialogOutcome<TResult>.Continue => ListWithButtonsDialogLoopResult<TResult?>.ContinueNoChange,
                DialogOutcome<TResult>.Refresh => Refresh(dialog, options.Items),
                _ => throw new InvalidOperationException("Unknown list-dialog outcome."),
            };
        });
    }

    private static ListWithButtonsDialogLoopResult<TResult?> Refresh(
        ListWithButtonsDialog<T> dialog,
        Func<IReadOnlyList<T>> items)
    {
        dialog.RefreshItems(items(), dialog.MaxVisibleRows);
        return ListWithButtonsDialogLoopResult<TResult?>.ContinueChanged;
    }
}
