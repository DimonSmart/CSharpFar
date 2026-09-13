namespace CSharpFar.Ui.Tests;

public sealed class DynamicTableDialogTests
{
    [Fact]
    public void RefreshPreservesSelectionByIdentity()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        IReadOnlyList<string> items = ["one", "two"];

        string result = Create(driver).DynamicTable(new DynamicTableDialogDefinition<string, string>
        {
            Title = "Dynamic",
            TableDefinition = Definition(),
            ItemIdentity = static item => item,
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F2] = "refresh" },
            Synchronize = _ => new DynamicTableDialogState<string>(items: items),
            HandleCommand = action =>
            {
                if (action.ActionId != "refresh")
                    return DynamicTableDialogOutcome<string>.ContinueOpen;
                items = ["zero", "two", "three"];
                return DynamicTableDialogOutcome<string>.RefreshOpen;
            },
            HandleItemActivation = item => DynamicTableDialogOutcome<string>.Complete(item),
        });

        Assert.Equal("two", result);
    }

    [Fact]
    public void DynamicActionUsesCurrentState()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        bool enabled = false;

        string result = Create(driver).DynamicTable(new DynamicTableDialogDefinition<string, string>
        {
            Title = "Actions",
            TableDefinition = Definition(),
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F2] = "enable" },
            Synchronize = _ => new DynamicTableDialogState<string>(
                items: ["row"],
                actions: [new DialogButton("run", "Run", 'R', IsEnabled: enabled)]),
            HandleCommand = action =>
            {
                if (action.ActionId == "enable")
                {
                    enabled = true;
                    return DynamicTableDialogOutcome<string>.RefreshOpen;
                }
                return action.ActionId == "run"
                    ? DynamicTableDialogOutcome<string>.Complete("ran")
                    : DynamicTableDialogOutcome<string>.ContinueOpen;
            },
        });

        Assert.True(enabled);
        Assert.Equal("ran", result);
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
