using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ConsumerBootstrapTests
{
    [Fact]
    public void DefaultFormFieldFactory_CreatesOrdinaryAndConfiguredFields()
    {
        var factory = new FormFieldFactory();

        TextField ordinary = factory.Text();
        TextField configured = factory.Text(new TextFieldOptions(
            InitialText: "value",
            Width: 12,
            SubmitOnEnter: true));

        Assert.Equal(string.Empty, ordinary.Text);
        Assert.Equal("value", configured.Text);
        Assert.Equal(12, configured.Width);
        Assert.True(configured.SubmitOnEnter);
    }

    [Fact]
    public void DefaultFormFieldFactory_SharesInMemoryHistoryWithinFactory()
    {
        var factory = new FormFieldFactory();
        var historyId = new TextHistoryId("ConsumerBootstrapTests.DefaultHistory");
        TextField first = factory.Text(new TextFieldOptions("  remembered  ", historyId));

        first.AcceptHistory();
        TextField second = factory.Text(new TextFieldOptions(HistoryId: historyId));

        SingleLineTextHistoryState history = Assert.IsType<SingleLineTextHistoryState>(second.Input.History);
        Assert.Contains("remembered", history.History.Items);
    }

    [Fact]
    public void DialogService_CompositionHostConstructorRunsStandardMessage()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        UiTestHost host = UiTestHost.Create(driver);
        var dialogs = new DialogService(host.Composition, new FormFieldFactory());

        dialogs.Message("Bootstrap", "Simple path works");

        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("Simple path works", StringComparison.Ordinal));
    }
}
