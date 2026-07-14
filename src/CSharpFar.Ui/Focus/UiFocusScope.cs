namespace CSharpFar.Ui;

public sealed class UiFocusScope
{
    public UiTargetId? FocusedTarget { get; private set; }

    public UiFocusFrame CurrentFrame { get; private set; } = UiFocusFrame.Empty;

    public bool HasFocus => FocusedTarget is not null;

    public bool TryFocus(UiTargetId target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (FindEntry(target) is not { IsEnabled: true })
            return false;

        FocusedTarget = target;
        return true;
    }

    public bool MoveNext() => Move(forward: true);

    public bool MovePrevious() => Move(forward: false);

    public bool ClearFocus()
    {
        bool hadFocus = HasFocus;
        FocusedTarget = null;
        return hadFocus;
    }

    public bool TryGetFocusedEntry(out UiFocusEntry entry)
    {
        if (FocusedTarget is UiTargetId target && FindEntry(target) is { IsEnabled: true } found)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    internal void Commit(UiFocusFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        CurrentFrame = frame;
        FocusedTarget = ResolveFocusedTarget(frame);
    }

    internal UiTargetId? ResolveFocusedTarget(UiFocusFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (FocusedTarget is UiTargetId current &&
            frame.Entries.FirstOrDefault(entry => entry.Target == current) is { IsEnabled: true })
        {
            return current;
        }

        if (frame.DefaultTarget is UiTargetId defaultTarget &&
            frame.Entries.FirstOrDefault(entry => entry.Target == defaultTarget) is { IsEnabled: true })
            return defaultTarget;

        return OrderedEnabledEntries(frame).FirstOrDefault().Entry?.Target;
    }

    private bool Move(bool forward)
    {
        var entries = OrderedEnabledEntries().ToArray();
        if (entries.Length == 0)
        {
            FocusedTarget = null;
            return false;
        }

        if (FocusedTarget is not UiTargetId current)
        {
            FocusedTarget = forward ? entries[0].Entry.Target : entries[^1].Entry.Target;
            return true;
        }

        int index = Array.FindIndex(entries, value => value.Entry.Target == current);
        if (index < 0)
        {
            FocusedTarget = forward ? entries[0].Entry.Target : entries[^1].Entry.Target;
            return true;
        }

        int next = forward
            ? (index + 1) % entries.Length
            : (index - 1 + entries.Length) % entries.Length;
        FocusedTarget = entries[next].Entry.Target;
        return true;
    }

    private UiFocusEntry? FindEntry(UiTargetId target) =>
        CurrentFrame.Entries.FirstOrDefault(entry => entry.Target == target);

    private IEnumerable<(UiFocusEntry Entry, int Index)> OrderedEnabledEntries() =>
        OrderedEnabledEntries(CurrentFrame);

    private static IEnumerable<(UiFocusEntry Entry, int Index)> OrderedEnabledEntries(UiFocusFrame frame) =>
        frame.Entries
            .Select((entry, index) => (entry, index))
            .Where(value => value.entry.IsEnabled)
            .OrderBy(value => value.entry.TabOrder)
            .ThenBy(value => value.index)
            .Select(value => (value.entry, value.index));
}
