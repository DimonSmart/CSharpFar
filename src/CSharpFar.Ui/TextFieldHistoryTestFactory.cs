namespace CSharpFar.Ui;

public static class TextFieldHistoryTestFactory
{
    public static SingleLineTextHistoryRegistry CreateInMemory() =>
        new(new InMemorySingleLineTextHistoryStore());
}
