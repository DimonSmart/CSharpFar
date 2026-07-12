namespace CSharpFar.Ui;

public sealed record UiFocusEntry
{
    public UiFocusEntry(
        UiTargetId Target,
        int TabOrder,
        bool IsEnabled = true,
        UiCursorPlacement? Cursor = null)
    {
        ArgumentNullException.ThrowIfNull(Target);

        this.Target = Target;
        this.TabOrder = TabOrder;
        this.IsEnabled = IsEnabled;
        this.Cursor = Cursor;
    }

    public UiTargetId Target { get; }

    public int TabOrder { get; }

    public bool IsEnabled { get; }

    public UiCursorPlacement? Cursor { get; }
}
