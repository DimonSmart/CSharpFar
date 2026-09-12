using CSharpFar.Console;

namespace CSharpFar.Ui;

public sealed class TableDialogOptions<T, TResult>
{
    public required string Title { get; init; }
    public required Func<IReadOnlyList<T>> Items { get; init; }
    public required TableListDefinition<T> Definition { get; init; }
    public required Func<ListDialogActionContext<T>, DialogOutcome<TResult>> HandleAction { get; init; }
    public IReadOnlyList<DialogButton> Actions { get; init; } = [];
    public Func<TResult>? Cancel { get; init; }
    public string? DefaultItemActionId { get; init; } = "default";
    public IReadOnlyCollection<ConsoleKey> CancelKeys { get; init; } = [ConsoleKey.Escape];
    public IReadOnlyDictionary<ConsoleKey, string>? KeyboardCommands { get; init; }
    public int InitialSelectedIndex { get; init; }
    public int PreferredWidth { get; init; } = 80;
    public int PreferredHeight { get; init; } = 20;
    public int MinWidth { get; init; } = 20;
    public int MinHeight { get; init; } = 8;
    public string? FooterText { get; init; }
    public DialogAppearance Appearance { get; init; } = DialogAppearance.Standard;
    public ListAppearance TableAppearance { get; init; } = ListAppearance.Dialog;
}

internal sealed class TableDialog<T, TResult>
{
    private const string CancelCommand = "\u001f-table-dialog-cancel";
    private readonly CompositeDialogHost _composite;

    public TableDialog(ModalDialogHost modalDialogs) =>
        _composite = new CompositeDialogHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));

    public TResult? Show(TableDialogOptions<T, TResult> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Title);
        ArgumentNullException.ThrowIfNull(options.Items);
        ArgumentNullException.ThrowIfNull(options.Definition);
        ArgumentNullException.ThrowIfNull(options.HandleAction);
        ArgumentNullException.ThrowIfNull(options.Actions);
        ArgumentNullException.ThrowIfNull(options.CancelKeys);

        IReadOnlyList<T> items = GetItems(options.Items);
        var table = new TableList<T>(items, options.Definition, options.InitialSelectedIndex, options.TableAppearance);
        var form = new ScrollableFormDialog();
        if (options.Actions.Count > 0)
            form.SetRows([], [FormControls.Buttons(options.Actions)]);

        return _composite.Run<TResult?>(
            new CompositeDialogOptions(
                options.Title,
                options.PreferredWidth,
                options.PreferredHeight,
                options.MinWidth,
                options.MinHeight,
                Appearance: options.Appearance),
            form,
            table,
            options.FooterText is null ? null : () => options.FooterText,
            BuildCommands(options),
            semantic => HandleEvent(semantic, table, options));
    }

    private static CompositeDialogOutcome<TResult?> HandleEvent(
        CompositeDialogEvent semantic,
        TableList<T> table,
        TableDialogOptions<T, TResult> options)
    {
        if (semantic.Kind == CompositeDialogEventKind.Cancelled)
        {
            if (semantic.Key is ConsoleKey key && !options.CancelKeys.Contains(key))
                return CompositeDialogOutcome<TResult?>.ContinueNoChange;
            return Cancel(options);
        }

        if (semantic is { Kind: CompositeDialogEventKind.Command, Command: CancelCommand })
            return Cancel(options);

        if (semantic.Kind == CompositeDialogEventKind.ContentSelectionChanged)
            return CompositeDialogOutcome<TResult?>.ContinueChanged;

        if (semantic.Kind == CompositeDialogEventKind.ContentConfirmed)
        {
            if (options.DefaultItemActionId is null || !table.TryGetSelectedItem(out _))
                return CompositeDialogOutcome<TResult?>.ContinueNoChange;
            return Dispatch(options.DefaultItemActionId, table, options);
        }

        if (semantic is { Kind: CompositeDialogEventKind.Command, Command: { } command })
            return Dispatch(command, table, options);

        return CompositeDialogOutcome<TResult?>.ContinueNoChange;
    }

    private static CompositeDialogOutcome<TResult?> Dispatch(
        string actionId,
        TableList<T> table,
        TableDialogOptions<T, TResult> options)
    {
        T? selectedItem = table.TryGetSelectedItem(out T selected) ? selected : default;
        DialogOutcome<TResult> outcome = options.HandleAction(
            new ListDialogActionContext<T>(actionId, selectedItem, table.SelectedIndex));

        return outcome switch
        {
            DialogOutcome<TResult>.Close close => CompositeDialogOutcome<TResult?>.Complete(close.Result),
            DialogOutcome<TResult>.Continue => CompositeDialogOutcome<TResult?>.ContinueNoChange,
            DialogOutcome<TResult>.Refresh refresh => ApplyRefresh(refresh, table, options.Items),
            _ => throw new InvalidOperationException("Unknown table-dialog outcome."),
        };
    }

    private static CompositeDialogOutcome<TResult?> ApplyRefresh(
        DialogOutcome<TResult>.Refresh outcome,
        TableList<T> table,
        Func<IReadOnlyList<T>> items)
    {
        if (outcome.ReloadItems)
            table.ReplaceItems(GetItems(items));

        if (outcome.SelectedIndex.HasValue)
            SetSelection(table, outcome.SelectedIndex.Value);

        return CompositeDialogOutcome<TResult?>.ContinueChanged;
    }

    private static void SetSelection(TableList<T> table, int requestedIndex)
    {
        if (table.Count == 0)
            return;
        table.SetSelectedIndex(Math.Clamp(requestedIndex, 0, table.Count - 1));
    }

    private static IReadOnlyList<T> GetItems(Func<IReadOnlyList<T>> items)
    {
        IReadOnlyList<T>? result = items();
        return result ?? throw new InvalidOperationException("Table dialog item source returned null.");
    }

    private static IReadOnlyDictionary<ConsoleKey, string>? BuildCommands(TableDialogOptions<T, TResult> options)
    {
        var commands = options.KeyboardCommands is null
            ? new Dictionary<ConsoleKey, string>()
            : new Dictionary<ConsoleKey, string>(options.KeyboardCommands);

        foreach (ConsoleKey key in options.CancelKeys)
        {
            if (key != ConsoleKey.Escape)
                commands[key] = CancelCommand;
        }

        return commands.Count == 0 ? null : commands;
    }

    private static CompositeDialogOutcome<TResult?> Cancel(TableDialogOptions<T, TResult> options) =>
        CompositeDialogOutcome<TResult?>.Complete(options.Cancel is null ? default : options.Cancel());
}
