using CSharpFar.Console.Input;

namespace CSharpFar.Ui.Tests;

public sealed class OperationDialogTests
{
    [Fact]
    public void PeriodicRefresh_SynchronizesUntilOperationCompletes()
    {
        var driver = new FakeConsoleDriver();
        int synchronizeCalls = 0;

        string result = Create(driver).Operation(new OperationDialogDefinition<string, int, string>
        {
            Title = "Working",
            RefreshInterval = TimeSpan.FromMilliseconds(1),
            Operation = async cancellationToken =>
            {
                while (Volatile.Read(ref synchronizeCalls) < 3)
                    await Task.Delay(1, cancellationToken);
                return 7;
            },
            Synchronize = () =>
            {
                Interlocked.Increment(ref synchronizeCalls);
                return new OperationDialogState<string>(
                    rows: [FormControls.Label($"Refresh {synchronizeCalls}")],
                    status: $"#{synchronizeCalls}");
            },
            Complete = value => $"done:{value}",
        });

        Assert.Equal("done:7", result);
        Assert.True(synchronizeCalls >= 3);
    }

    [Fact]
    public void Refresh_ReplacesItemsAndPreservesSelectionByIdentity()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        IReadOnlyList<string> items = ["one", "two"];

        string result = Create(driver).Operation(new OperationDialogDefinition<string, int, string>
        {
            Title = "Items",
            TableDefinition = Definition(),
            ItemIdentity = static item => item,
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F2] = "refresh" },
            Operation = WaitUntilCancelled,
            Synchronize = () => new OperationDialogState<string>(items: items),
            HandleCommand = action =>
            {
                if (action.ActionId == "refresh")
                {
                    items = ["zero", "two", "three"];
                    return OperationDialogOutcome<string>.ContinueChanged;
                }

                return OperationDialogOutcome<string>.ContinueNoChange;
            },
            HandleItemActivation = item => OperationDialogOutcome<string>.Complete(item),
            Complete = _ => "background",
        });

        Assert.Equal("two", result);
    }

    [Fact]
    public void DynamicButtonState_AndCommandHandlingUseCurrentSnapshot()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        bool enabled = false;
        int runCommands = 0;

        string result = Create(driver).Operation(new OperationDialogDefinition<string, int, string>
        {
            Title = "Commands",
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F2] = "enable" },
            Operation = WaitUntilCancelled,
            Synchronize = () => new OperationDialogState<string>(
                buttons: [new DialogButton("run", "Run", 'R', IsEnabled: enabled)]),
            HandleCommand = action =>
            {
                if (action.ActionId == "enable")
                {
                    enabled = true;
                    return OperationDialogOutcome<string>.ContinueChanged;
                }

                if (action.ActionId == "run")
                {
                    runCommands++;
                    return OperationDialogOutcome<string>.Complete("ran");
                }

                return OperationDialogOutcome<string>.ContinueNoChange;
            },
            Complete = _ => "background",
        });

        Assert.Equal("ran", result);
        Assert.Equal(1, runCommands);
    }

    [Fact]
    public void CancelRequest_IsSemanticAndCancelsBackgroundOperation()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        bool cancelHandled = false;
        bool backgroundCancelled = false;

        string result = Create(driver).Operation(new OperationDialogDefinition<string, int, string>
        {
            Title = "Cancel",
            Operation = async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    backgroundCancelled = true;
                    return 1;
                }
            },
            Synchronize = () => new OperationDialogState<string>(),
            HandleCancel = () =>
            {
                cancelHandled = true;
                return OperationDialogOutcome<string>.RequestCancellation;
            },
            Complete = value => $"cancelled:{value}",
        });

        Assert.True(cancelHandled);
        Assert.True(backgroundCancelled);
        Assert.Equal("cancelled:1", result);
    }

    [Fact]
    public void CompletedOperation_UsesCompletionHandler()
    {
        var driver = new FakeConsoleDriver();

        int result = Create(driver).Operation(new OperationDialogDefinition<string, int, int>
        {
            Title = "Complete",
            Operation = _ => Task.FromResult(21),
            Synchronize = () => new OperationDialogState<string>(),
            Complete = value => value * 2,
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public void FailedOperation_PropagatesException()
    {
        var driver = new FakeConsoleDriver();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Create(driver).Operation(new OperationDialogDefinition<string, int, int>
            {
                Title = "Failure",
                Operation = _ => Task.FromException<int>(new InvalidOperationException("boom")),
                Synchronize = () => new OperationDialogState<string>(),
                Complete = value => value,
            }));

        Assert.Equal("boom", error.Message);
    }

    private static async Task<int> WaitUntilCancelled(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    private static TableListDefinition<string> Definition() => new()
    {
        Columns = [TableColumn<string>.Text("Name", static item => item, TableWidth.Flexible(20, 2))],
    };

    private static DialogService Create(FakeConsoleDriver driver) => new(
        ModalTestHost.Create(driver),
        new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
