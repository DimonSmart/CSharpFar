using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class TextFieldHistoryTestProvider
{
    public static ITextFieldHistoryProvider Create() =>
        new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());

    public static SingleLineTextHistoryState CreateState(IEnumerable<string>? items = null) =>
        new(new TextHistory(items, itemsChanged: null));
}
