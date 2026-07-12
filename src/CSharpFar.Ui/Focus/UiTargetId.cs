namespace CSharpFar.Ui;

public readonly record struct UiTargetId
{
    public UiTargetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("UI target id cannot be null, empty, or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
