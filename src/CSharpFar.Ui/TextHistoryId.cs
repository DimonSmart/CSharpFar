namespace CSharpFar.Ui;

public readonly record struct TextHistoryId
{
    public TextHistoryId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public interface ITextFieldHistoryProvider
{
    TextHistory Get(TextHistoryId id);
}

public interface ITextFieldHistoryDiagnostics
{
    void ReportPersistenceFailure(TextHistoryPersistenceOperation operation, Exception exception);
}

public enum TextHistoryPersistenceOperation
{
    Load,
    Save,
}
