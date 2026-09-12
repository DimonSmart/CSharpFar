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
    public sealed record Refresh : DialogOutcome<TResult>
    {
        public Refresh() { }

        internal Refresh(int selectedIndex, bool reloadItems = true)
        {
            SelectedIndex = selectedIndex;
            ReloadItems = reloadItems;
        }

        internal int? SelectedIndex { get; }
        internal bool ReloadItems { get; } = true;
    }
    public sealed record Close(TResult Result) : DialogOutcome<TResult>;

    public static DialogOutcome<TResult> ContinueOpen() => new Continue();
    public static DialogOutcome<TResult> RefreshOpen() => new Refresh();
    public static DialogOutcome<TResult> RefreshOpen(int selectedIndex) => new Refresh(selectedIndex);
    public static DialogOutcome<TResult> ChangeSelection(int selectedIndex) => new Refresh(selectedIndex, reloadItems: false);
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

        IReadOnlyList<T> items = GetItems(options.Items);
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
            if (outcome is DialogOutcome<TResult>.Close close)
                return ListWithButtonsDialogLoopResult<TResult?>.Complete(close.Result);
            if (outcome is DialogOutcome<TResult>.Continue)
                return ListWithButtonsDialogLoopResult<TResult?>.ContinueNoChange;
            if (outcome is DialogOutcome<TResult>.Refresh refresh)
            {
                if (refresh.ReloadItems)
                {
                    items = GetItems(options.Items);
                    dialog.RefreshItems(items);
                }

                if (refresh.SelectedIndex.HasValue && items.Count > 0)
                    dialog.SelectedIndex = Math.Clamp(refresh.SelectedIndex.Value, 0, items.Count - 1);
                return ListWithButtonsDialogLoopResult<TResult?>.ContinueChanged;
            }

            throw new InvalidOperationException("Unknown list-dialog outcome.");
        });
    }

    private static IReadOnlyList<T> GetItems(Func<IReadOnlyList<T>> items) =>
        items() ?? throw new InvalidOperationException("List dialog item source returned null.");
}
