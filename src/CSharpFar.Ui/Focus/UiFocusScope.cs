namespace CSharpFar.Ui;

public sealed class UiFocusScope
{
    private UiFocusRequest _nextCommitRequest = UiFocusRequest.None;

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

    internal bool HasNextCommitRequest => _nextCommitRequest.Kind != UiFocusRequestKind.None;

    internal void RequestOnNextCommit(UiFocusRequest request)
    {
        if (request.Kind == UiFocusRequestKind.None)
            return;

        _nextCommitRequest = request;
    }

    internal void Commit(UiFocusFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        CurrentFrame = frame;
        FocusedTarget = ResolveFocusedTarget(frame, _nextCommitRequest);
        _nextCommitRequest = UiFocusRequest.None;
    }

    internal UiTargetId? ResolveFocusedTarget(UiFocusFrame frame) =>
        ResolveFocusedTarget(frame, _nextCommitRequest);

    private UiTargetId? ResolveFocusedTarget(UiFocusFrame frame, UiFocusRequest request)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (request.Kind == UiFocusRequestKind.Set &&
            frame.Entries.FirstOrDefault(entry => entry.Target == request.Target) is { IsEnabled: true } requested)
        {
            return requested.Target;
        }

        if (request.Kind == UiFocusRequestKind.Clear)
            return null;

        if (request.Kind is UiFocusRequestKind.MoveNext or UiFocusRequestKind.MovePrevious)
        {
            var enabled = OrderedEnabledEntries(frame).Select(value => value.Entry.Target).ToArray();
            if (enabled.Length == 0)
                return null;
            int currentIndex = FocusedTarget is UiTargetId focused ? Array.IndexOf(enabled, focused) : -1;
            if (request.Kind == UiFocusRequestKind.MoveNext)
                return enabled[(currentIndex + 1 + enabled.Length) % enabled.Length];
            return enabled[(currentIndex <= 0 ? enabled.Length : currentIndex) - 1];
        }

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
