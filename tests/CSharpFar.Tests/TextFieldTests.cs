using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TextFieldTests
{
    [Fact]
    public void TextField_WithoutHistory_DoesNotQueryProviderOrRetainText()
    {
        var provider = new CountingHistoryProvider();
        var field = new FormFieldFactory(provider).Text("value", "  retained  ");

        field.AcceptHistory();

        Assert.Equal(0, provider.GetCallCount);
        Assert.Empty(provider.History.Items);
    }

    [Fact]
    public void TextField_Masked_DoesNotQueryProviderOrRetainText()
    {
        var provider = new CountingHistoryProvider();
        var field = new FormFieldFactory(provider).Text(
            "password",
            "secret",
            new TextHistoryId("TextFieldTests.Password"),
            maskInput: true);

        field.AcceptHistory();

        Assert.Equal(0, provider.GetCallCount);
        Assert.Empty(provider.History.Items);
    }

    [Fact]
    public void TextField_AcceptHistory_RetainsTrimmedTextWithoutDuplicates()
    {
        var provider = new CountingHistoryProvider();
        var field = new FormFieldFactory(provider).Text(
            "value",
            "  retained  ",
            new TextHistoryId("TextFieldTests.Value"));

        field.AcceptHistory();
        field.AcceptHistory();

        Assert.Equal(1, provider.GetCallCount);
        Assert.Equal(["retained"], provider.History.Items);
    }

    private sealed class CountingHistoryProvider : ITextFieldHistoryProvider
    {
        private readonly SingleLineTextHistoryRegistry _registry =
            new(new InMemorySingleLineTextHistoryStore());

        public int GetCallCount { get; private set; }
        public TextHistory History => _registry.Get(new TextHistoryId("TextFieldTests.Stored"));

        public TextHistory Get(TextHistoryId id)
        {
            GetCallCount++;
            return History;
        }
    }
}
