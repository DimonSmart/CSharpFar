using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class TextFieldHistoryTestProvider
{
    public static ITextFieldHistoryProvider Create() =>
        new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());
}
