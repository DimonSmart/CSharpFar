namespace CSharpFar.Ui;

public enum UiFocusRequestKind
{
    None,
    Set,
    Clear,
    MoveNext,
    MovePrevious,
}

public readonly record struct UiFocusRequest
{
    public UiFocusRequest(UiFocusRequestKind kind, UiTargetId? target)
    {
        if (kind == UiFocusRequestKind.Set)
        {
            if (target is null)
                throw new ArgumentException("Set focus request requires a target.", nameof(target));
        }
        else if (target is not null)
        {
            throw new ArgumentException("Only Set focus request can contain a target.", nameof(target));
        }

        Kind = kind;
        Target = target;
    }

    public UiFocusRequestKind Kind { get; }

    public UiTargetId? Target { get; }

    public static UiFocusRequest None { get; } = new(UiFocusRequestKind.None, null);

    public static UiFocusRequest Clear { get; } = new(UiFocusRequestKind.Clear, null);

    public static UiFocusRequest MoveNext { get; } = new(UiFocusRequestKind.MoveNext, null);

    public static UiFocusRequest MovePrevious { get; } = new(UiFocusRequestKind.MovePrevious, null);

    public static UiFocusRequest Set(UiTargetId target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(UiFocusRequestKind.Set, target);
    }
}
