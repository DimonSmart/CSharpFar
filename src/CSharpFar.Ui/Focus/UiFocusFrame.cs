namespace CSharpFar.Ui;

public sealed class UiFocusFrame
{
    public static UiFocusFrame Empty { get; } = new([]);

    public UiFocusFrame(
        IReadOnlyList<UiFocusEntry> entries,
        UiTargetId? defaultTarget = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var snapshot = entries.ToArray();
        var targets = new HashSet<UiTargetId>();
        foreach (UiFocusEntry? entry in snapshot)
        {
            if (entry is null)
                throw new ArgumentException("Focus frame entries cannot contain null.", nameof(entries));

            if (!targets.Add(entry.Target))
                throw new ArgumentException($"Duplicate UI focus target '{entry.Target}'.", nameof(entries));
        }

        if (defaultTarget is UiTargetId target)
        {
            UiFocusEntry? entry = snapshot.FirstOrDefault(value => value.Target == target);
            if (entry is null)
                throw new ArgumentException("Default focus target must be present in the focus frame.", nameof(defaultTarget));
            if (!entry.IsEnabled)
                throw new ArgumentException("Default focus target must be enabled.", nameof(defaultTarget));
        }

        Entries = Array.AsReadOnly(snapshot);
        DefaultTarget = defaultTarget;
    }

    public IReadOnlyList<UiFocusEntry> Entries { get; }

    public UiTargetId? DefaultTarget { get; }
}
