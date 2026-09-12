using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Declarative description of a long-running modal operation.</summary>
public sealed class OperationDialogDefinition<TItem, TBackground, TResult>
{
    public required string Title { get; init; }
    public int PreferredWidth { get; init; } = 80;
    public int PreferredHeight { get; init; } = 20;
    public int MinWidth { get; init; } = 20;
    public int MinHeight { get; init; } = 8;
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public DialogAppearance Appearance { get; init; } = DialogAppearance.Standard;
    public TableListDefinition<TItem>? TableDefinition { get; init; }
    public ListAppearance TableAppearance { get; init; } = ListAppearance.Dialog;
    public Func<TItem, object>? ItemIdentity { get; init; }
    public IReadOnlyDictionary<ConsoleKey, string>? KeyboardCommands { get; init; }
    public required Func<CancellationToken, Task<TBackground>> Operation { get; init; }
    public required Func<OperationDialogState<TItem>> Synchronize { get; init; }
    public Func<TItem, OperationDialogOutcome<TResult>>? HandleItemActivation { get; init; }
    public Func<ListDialogActionContext<TItem>, OperationDialogOutcome<TResult>>? HandleCommand { get; init; }
    public Func<OperationDialogOutcome<TResult>>? HandleCancel { get; init; }
    public required Func<TBackground, TResult> Complete { get; init; }
    public Action? OnCancellationRequested { get; init; }
}

/// <summary>One semantic presentation snapshot for a long-running operation dialog.</summary>
public sealed class OperationDialogState<TItem>
{
    public OperationDialogState(
        IReadOnlyList<FormRow>? rows = null,
        IReadOnlyList<TItem>? items = null,
        IReadOnlyList<DialogButton>? buttons = null,
        string? status = null)
    {
        Rows = rows ?? [];
        Items = items ?? [];
        Buttons = buttons ?? [];
        Status = status;
    }

    public IReadOnlyList<FormRow> Rows { get; }
    public IReadOnlyList<TItem> Items { get; }
    public IReadOnlyList<DialogButton> Buttons { get; }
    public string? Status { get; }
}

internal sealed class SemanticOperationDialog<TItem, TBackground, TResult>
{
    private readonly OperationDialogHost _host;

    public SemanticOperationDialog(ModalDialogHost modalDialogs) =>
        _host = new OperationDialogHost(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));

    public TResult Show(
        OperationDialogDefinition<TItem, TBackground, TResult> options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentNullException.ThrowIfNull(options.Operation);
        ArgumentNullException.ThrowIfNull(options.Synchronize);
        ArgumentNullException.ThrowIfNull(options.Complete);
        if (options.RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.RefreshInterval));

        var form = new ScrollableFormDialog();
        ButtonRow? buttonRow = null;
        TableList<TItem>? table = options.TableDefinition is null
            ? null
            : new TableList<TItem>([], options.TableDefinition, appearance: options.TableAppearance);
        ICompositeDialogContent content = table is null ? EmptyCompositeDialogContent.Instance : table;
        OperationDialogState<TItem>? state = null;

        return _host.Run(
            new OperationDialogOptions(
                new CompositeDialogOptions(
                    options.Title,
                    options.PreferredWidth,
                    options.PreferredHeight,
                    options.MinWidth,
                    options.MinHeight,
                    Appearance: options.Appearance),
                options.RefreshInterval),
            options.Operation,
            form,
            content,
            status: () => state?.Status,
            commands: options.KeyboardCommands,
            synchronize: Synchronize,
            handle: Handle,
            complete: options.Complete,
            onCancellationRequested: options.OnCancellationRequested,
            cancellationToken);

        bool Synchronize()
        {
            OperationDialogState<TItem> next = options.Synchronize()
                ?? throw new InvalidOperationException("Operation dialog state source returned null.");
            ValidateState(next, table is not null);

            bool changed = state is null || !Equivalent(state, next);
            IReadOnlyList<FormRow> footer = [];
            if (next.Buttons.Count > 0)
            {
                if (buttonRow is null)
                    buttonRow = FormControls.Buttons(next.Buttons);
                else
                    buttonRow.SetButtons(next.Buttons);
                footer = [buttonRow];
            }
            form.SetRows(next.Rows, footer);

            if (table is not null)
            {
                if (options.ItemIdentity is null)
                    table.ReplaceItems(next.Items);
                else
                    table.ReplaceItems(next.Items, options.ItemIdentity);
            }

            state = next;
            return changed;
        }

        OperationDialogOutcome<TResult> Handle(CompositeDialogEvent semantic) =>
            semantic.Kind switch
            {
                CompositeDialogEventKind.ContentSelectionChanged =>
                    OperationDialogOutcome<TResult>.ContinueChanged,
                CompositeDialogEventKind.ContentConfirmed =>
                    HandleActivation(),
                CompositeDialogEventKind.Command when semantic.Command is { } command =>
                    HandleCommand(command),
                CompositeDialogEventKind.Cancelled =>
                    ApplyState(options.HandleCancel?.Invoke() ?? OperationDialogOutcome<TResult>.RequestCancellation),
                _ => OperationDialogOutcome<TResult>.ContinueNoChange,
            };

        OperationDialogOutcome<TResult> HandleActivation()
        {
            if (table is null ||
                options.HandleItemActivation is null ||
                !table.TryGetSelectedItem(out TItem selected))
            {
                return OperationDialogOutcome<TResult>.ContinueNoChange;
            }

            return ApplyState(options.HandleItemActivation(selected));
        }

        OperationDialogOutcome<TResult> HandleCommand(string command)
        {
            if (options.HandleCommand is null)
                return OperationDialogOutcome<TResult>.ContinueNoChange;

            TItem? selected = table is not null && table.TryGetSelectedItem(out TItem item) ? item : default;
            int selectedIndex = table?.SelectedIndex ?? -1;
            return ApplyState(options.HandleCommand(new ListDialogActionContext<TItem>(command, selected, selectedIndex)));
        }

        OperationDialogOutcome<TResult> ApplyState(OperationDialogOutcome<TResult> outcome)
        {
            if (outcome.Action is OperationDialogAction.ContinueChanged
                or OperationDialogAction.RequestCancellation
                or OperationDialogAction.RequestImmediateCancellation)
            {
                _ = Synchronize();
            }

            return outcome;
        }
    }

    private static void ValidateState(OperationDialogState<TItem> state, bool hasTable)
    {
        ArgumentNullException.ThrowIfNull(state.Rows);
        ArgumentNullException.ThrowIfNull(state.Items);
        ArgumentNullException.ThrowIfNull(state.Buttons);
        if (!hasTable && state.Items.Count != 0)
            throw new InvalidOperationException("Operation dialog state contains items but no table definition was provided.");
    }

    private static bool Equivalent(OperationDialogState<TItem> left, OperationDialogState<TItem> right) =>
        string.Equals(left.Status, right.Status, StringComparison.Ordinal) &&
        left.Rows.SequenceEqual(right.Rows) &&
        left.Items.SequenceEqual(right.Items) &&
        left.Buttons.SequenceEqual(right.Buttons);

    private sealed class EmptyCompositeDialogContent : ICompositeDialogContent
    {
        public static readonly EmptyCompositeDialogContent Instance = new();

        private EmptyCompositeDialogContent() { }

        public ICompositeDialogContentFrame CalculateFrame(Rect bounds) => EmptyFrame.Instance;

        public void Render(IUiCanvas canvas, ICompositeDialogContentFrame frame) { }

        public UiInteractionFragment BuildInteractionFragment(ICompositeDialogContentFrame frame, int focusOrder) =>
            UiInteractionFragment.Empty;

        public CompositeDialogContentInputResult RouteInput(
            ConsoleInputEvent input,
            ICompositeDialogContentFrame frame,
            UiInputRouteContext route) =>
            CompositeDialogContentInputResult.NotHandled;

        public void ApplyCommittedFrame(ICompositeDialogContentFrame frame) { }

        private sealed class EmptyFrame : ICompositeDialogContentFrame
        {
            public static readonly EmptyFrame Instance = new();
            private EmptyFrame() { }
        }
    }
}
