namespace CSharpFar.Ui;

/// <summary>Declarative description of a modal form plus dynamically refreshed table and actions.</summary>
public sealed class DynamicTableDialogDefinition<TItem, TResult>
{
    public required string Title { get; init; }
    public int PreferredWidth { get; init; } = 80;
    public int PreferredHeight { get; init; } = 20;
    public int MinWidth { get; init; } = 20;
    public int MinHeight { get; init; } = 8;
    public DialogResizeMode ResizeMode { get; init; } = DialogResizeMode.Both;
    public DialogAppearance Appearance { get; init; } = DialogAppearance.Standard;
    public required TableListDefinition<TItem> TableDefinition { get; init; }
    public ListAppearance TableAppearance { get; init; } = ListAppearance.Dialog;
    public Func<TItem, object>? ItemIdentity { get; init; }
    public IReadOnlyDictionary<ConsoleKey, string>? KeyboardCommands { get; init; }
    public required Func<DynamicTableDialogContext<TItem>, DynamicTableDialogState<TItem>> Synchronize { get; init; }
    public Func<TItem, DynamicTableDialogOutcome<TResult>>? HandleItemActivation { get; init; }
    public Func<ListDialogActionContext<TItem>, DynamicTableDialogOutcome<TResult>>? HandleCommand { get; init; }
    public Func<DynamicTableDialogOutcome<TResult>>? HandleCancel { get; init; }
}

/// <summary>Current selected item exposed to semantic state synchronization.</summary>
public sealed record DynamicTableDialogContext<TItem>(TItem? SelectedItem, int SelectedIndex);

/// <summary>One semantic presentation snapshot for a dynamic table dialog.</summary>
public sealed class DynamicTableDialogState<TItem>
{
    public DynamicTableDialogState(
        IReadOnlyList<FormRow>? rows = null,
        IReadOnlyList<TItem>? items = null,
        IReadOnlyList<DialogButton>? actions = null,
        string? status = null)
    {
        Rows = rows ?? [];
        Items = items ?? [];
        Actions = actions ?? [];
        Status = status;
    }

    public IReadOnlyList<FormRow> Rows { get; }
    public IReadOnlyList<TItem> Items { get; }
    public IReadOnlyList<DialogButton> Actions { get; }
    public string? Status { get; }
}

public readonly record struct DynamicTableDialogOutcome<TResult>(bool IsComplete, bool IsChanged, TResult Result)
{
    public static DynamicTableDialogOutcome<TResult> ContinueOpen => new(false, false, default!);
    public static DynamicTableDialogOutcome<TResult> RefreshOpen => new(false, true, default!);
    public static DynamicTableDialogOutcome<TResult> Complete(TResult result) => new(true, false, result);
}

internal sealed class SemanticDynamicTableDialog<TItem, TResult>
{
    private readonly CompositeDialogHost _host;

    public SemanticDynamicTableDialog(ModalDialogHost modalDialogs) =>
        _host = new CompositeDialogHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));

    public TResult Show(
        DynamicTableDialogDefinition<TItem, TResult> definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Title);
        ArgumentNullException.ThrowIfNull(definition.TableDefinition);
        ArgumentNullException.ThrowIfNull(definition.Synchronize);

        var form = new ScrollableFormDialog();
        var table = new TableList<TItem>([], definition.TableDefinition, appearance: definition.TableAppearance);
        ButtonRow? actionRow = null;
        DynamicTableDialogState<TItem>? state = null;

        return _host.Run(
            new CompositeDialogOptions(
                definition.Title,
                definition.PreferredWidth,
                definition.PreferredHeight,
                definition.MinWidth,
                definition.MinHeight,
                Appearance: definition.Appearance)
            {
                ResizeMode = definition.ResizeMode,
            },
            form,
            table,
            status: () => state?.Status,
            commands: definition.KeyboardCommands,
            handle: Handle,
            prepareRender: Synchronize,
            cancellationToken);

        void Synchronize()
        {
            DynamicTableDialogState<TItem> next = ReadState();
            ApplyItems(next);

            // Item refresh may preserve, move, or establish selection. Re-read semantic
            // state once so action enablement/status can depend on the committed selection.
            DynamicTableDialogState<TItem> selectedState = ReadState();
            if (!next.Items.SequenceEqual(selectedState.Items))
                ApplyItems(selectedState);
            ApplyPresentation(selectedState);
            state = selectedState;
        }

        DynamicTableDialogState<TItem> ReadState()
        {
            TItem? selected = table.TryGetSelectedItem(out TItem value) ? value : default;
            DynamicTableDialogState<TItem> next = definition.Synchronize(
                new DynamicTableDialogContext<TItem>(selected, table.SelectedIndex))
                ?? throw new InvalidOperationException("Dynamic table dialog state source returned null.");
            ValidateState(next);
            return next;
        }

        void ApplyItems(DynamicTableDialogState<TItem> next)
        {
            if (definition.ItemIdentity is null)
                table.ReplaceItems(next.Items);
            else
                table.ReplaceItems(next.Items, definition.ItemIdentity);
        }

        void ApplyPresentation(DynamicTableDialogState<TItem> next)
        {
            IReadOnlyList<FormRow> footer = [];
            if (next.Actions.Count > 0)
            {
                if (actionRow is null)
                    actionRow = FormControls.Buttons(next.Actions);
                else
                    actionRow.SetButtons(next.Actions);
                footer = [actionRow];
            }
            form.SetRows(next.Rows, footer);
        }

        CompositeDialogOutcome<TResult> Handle(CompositeDialogEvent semantic) =>
            semantic.Kind switch
            {
                CompositeDialogEventKind.ContentSelectionChanged => Refresh(),
                CompositeDialogEventKind.ContentConfirmed => ActivateSelected(),
                CompositeDialogEventKind.Command when semantic.Command is { } command => HandleCommand(command),
                CompositeDialogEventKind.Cancelled => Apply(definition.HandleCancel?.Invoke() ?? DynamicTableDialogOutcome<TResult>.Complete(default!)),
                CompositeDialogEventKind.ValueChanged => Refresh(),
                _ => CompositeDialogOutcome<TResult>.ContinueNoChange,
            };

        CompositeDialogOutcome<TResult> Refresh()
        {
            Synchronize();
            return CompositeDialogOutcome<TResult>.ContinueChanged;
        }

        CompositeDialogOutcome<TResult> ActivateSelected()
        {
            if (definition.HandleItemActivation is null || !table.TryGetSelectedItem(out TItem selected))
                return CompositeDialogOutcome<TResult>.ContinueNoChange;
            return Apply(definition.HandleItemActivation(selected));
        }

        CompositeDialogOutcome<TResult> HandleCommand(string command)
        {
            if (definition.HandleCommand is null)
                return CompositeDialogOutcome<TResult>.ContinueNoChange;

            TItem? selected = table.TryGetSelectedItem(out TItem item) ? item : default;
            return Apply(definition.HandleCommand(
                new ListDialogActionContext<TItem>(command, selected, table.SelectedIndex)));
        }

        CompositeDialogOutcome<TResult> Apply(DynamicTableDialogOutcome<TResult> outcome)
        {
            if (outcome.IsComplete)
                return CompositeDialogOutcome<TResult>.Complete(outcome.Result);
            if (!outcome.IsChanged)
                return CompositeDialogOutcome<TResult>.ContinueNoChange;
            Synchronize();
            return CompositeDialogOutcome<TResult>.ContinueChanged;
        }
    }

    private static void ValidateState(DynamicTableDialogState<TItem> state)
    {
        ArgumentNullException.ThrowIfNull(state.Rows);
        ArgumentNullException.ThrowIfNull(state.Items);
        ArgumentNullException.ThrowIfNull(state.Actions);
    }
}
