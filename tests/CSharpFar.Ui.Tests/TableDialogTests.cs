using CSharpFar.Console.Input;

namespace CSharpFar.Ui.Tests;

public sealed class TableDialogTests
{
    [Fact]
    public void DefaultAction_EnterAndDoubleClickUseTheSameSemanticAction()
    {
        ListDialogActionContext<string>? enterAction = null;
        ListDialogActionContext<string>? mouseAction = null;

        var enterDriver = new FakeConsoleDriver();
        enterDriver.EnqueueKey(Key(ConsoleKey.Enter));
        string? enterResult = Show(enterDriver, ["one", "two"], action =>
        {
            enterAction = action;
            return DialogOutcome<string>.Complete(action.SelectedItem!);
        });

        var mouseDriver = new FakeConsoleDriver();
        EnqueueDoubleClickOnText(mouseDriver, "one");
        string? mouseResult = Show(mouseDriver, ["one", "two"], action =>
        {
            mouseAction = action;
            return DialogOutcome<string>.Complete(action.SelectedItem!);
        });

        Assert.Equal("one", enterResult);
        Assert.Equal(enterResult, mouseResult);
        Assert.Equal("default", enterAction?.ActionId);
        Assert.Equal(enterAction, mouseAction);
    }

    [Fact]
    public void ExplicitAction_UsesCurrentSelection()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        ListDialogActionContext<string>? observed = null;
        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => ["one", "two"],
            Definition = Definition(),
            Actions = [DialogButton.Action("extra", "Extra", 'X')],
            HandleAction = action =>
            {
                observed = action;
                return DialogOutcome<string>.Complete(action.ActionId);
            },
        });

        Assert.Equal("extra", result);
        Assert.Equal("one", observed?.SelectedItem);
        Assert.Equal(0, observed?.SelectedIndex);
    }

    [Fact]
    public void Cancel_UsesDeclaredResult()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => ["one"],
            Definition = Definition(),
            CancelKeys = [ConsoleKey.Escape],
            Cancel = () => "cancelled",
            HandleAction = _ => throw new InvalidOperationException("Cancel must not dispatch an action."),
        });

        Assert.Equal("cancelled", result);
    }

    [Fact]
    public void Refresh_RebindsItemsAndAppliesRequestedSelectionSafely()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        int itemCalls = 0;

        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => ++itemCalls == 1 ? ["one", "two", "three"] : ["zero", "last"],
            Definition = Definition(),
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F5] = "refresh" },
            HandleAction = action => action.ActionId == "refresh"
                ? DialogOutcome<string>.RefreshOpen(99)
                : DialogOutcome<string>.Complete(action.SelectedItem!),
        });

        Assert.Equal("last", result);
        Assert.Equal(2, itemCalls);
    }

    [Fact]
    public void CustomCommand_CanCycleSelectionWithoutReloadingItems()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        int itemCalls = 0;

        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => { itemCalls++; return ["one", "two", "three"]; },
            Definition = Definition(),
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F2] = "cycle" },
            HandleAction = action => action.ActionId == "cycle"
                ? DialogOutcome<string>.ChangeSelection((action.SelectedIndex + 1) % 3)
                : DialogOutcome<string>.Complete(action.SelectedItem!),
        });

        Assert.Equal("three", result);
        Assert.Equal(1, itemCalls);
    }

    [Fact]
    public void UnhandledNavigationKey_RemainsTableNavigation()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        string? result = Show(driver, ["one", "two"], action => DialogOutcome<string>.Complete(action.SelectedItem!));

        Assert.Equal("two", result);
    }

    [Fact]
    public void EmptyItems_EnterDoesNotDispatchDefaultAction()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        int actionCalls = 0;

        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => [],
            Definition = Definition(),
            Cancel = () => "cancelled",
            HandleAction = _ => { actionCalls++; return DialogOutcome<string>.Complete("unexpected"); },
        });

        Assert.Equal("cancelled", result);
        Assert.Equal(0, actionCalls);
    }

    [Fact]
    public void InvalidInitialAndRequestedSelection_AreClampedWithoutException()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => ["one", "two"],
            Definition = Definition(),
            InitialSelectedIndex = 99,
            KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F2] = "first" },
            HandleAction = action => action.ActionId == "first"
                ? DialogOutcome<string>.ChangeSelection(-99)
                : DialogOutcome<string>.Complete(action.SelectedItem!),
        });

        Assert.Equal("one", result);
    }

    [Fact]
    public void NarrowTerminal_RemainsUsable()
    {
        var driver = new FakeConsoleDriver(width: 22, height: 7);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        string? result = Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Wide table",
            Items = () => Enumerable.Range(0, 20).Select(index => $"item {index}").ToArray(),
            Definition = Definition(),
            PreferredWidth = 90,
            PreferredHeight = 22,
            MinWidth = 20,
            MinHeight = 6,
            Cancel = () => "cancelled",
            HandleAction = _ => DialogOutcome<string>.ContinueOpen(),
        });

        Assert.Equal("cancelled", result);
    }

    private static string? Show(
        FakeConsoleDriver driver,
        IReadOnlyList<string> items,
        Func<ListDialogActionContext<string>, DialogOutcome<string>> handle) =>
        Create(driver).Table(new TableDialogOptions<string, string>
        {
            Title = "Items",
            Items = () => items,
            Definition = Definition(),
            HandleAction = handle,
        });

    private static TableListDefinition<string> Definition() => new()
    {
        Columns = [TableColumn<string>.Text("Name", item => item, TableWidth.Flexible(12, 2))],
    };

    private static DialogService Create(FakeConsoleDriver driver) => new(
        ModalTestHost.Create(driver),
        new FormFieldFactory(TextFieldHistoryTestProvider.Create()));

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static void EnqueueDoubleClickOnText(FakeConsoleDriver driver, string text)
    {
        driver.BeforeReadInput = current =>
        {
            current.BeforeReadInput = null;
            FakeConsoleDriver.WriteRecord record = current.WriteRecords.Last(value => value.Text.Contains(text, StringComparison.Ordinal));
            int x = record.X + record.Text.IndexOf(text, StringComparison.Ordinal);
            current.EnqueueInput(new MouseConsoleInputEvent(x, record.Y, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None));
        };
    }
}
