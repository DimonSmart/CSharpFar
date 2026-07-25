namespace CSharpFar.Ui;

public readonly struct UiTargetScope
{
    public UiTargetScope(string prefix) => Root = new UiTargetId(prefix);

    public string Prefix => Root.Value;

    public UiTargetId Root { get; }

    public UiTargetId Child(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("UI target child name cannot be null, empty, or whitespace.", nameof(name));

        return new UiTargetId($"{Prefix}.{name}");
    }
}
