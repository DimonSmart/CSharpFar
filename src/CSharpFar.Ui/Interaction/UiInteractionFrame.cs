namespace CSharpFar.Ui;

public sealed class UiInteractionFrame
{
    public static UiInteractionFrame Empty { get; } =
        new([], UiFocusFrame.Empty);

    internal UiInteractionFrame(
        IReadOnlyList<UiHitRegion> hitRegions,
        UiFocusFrame? focus = null,
        UiTargetId? keyboardTarget = null)
    {
        ArgumentNullException.ThrowIfNull(hitRegions);

        var snapshot = hitRegions.ToArray();
        foreach (UiHitRegion? region in snapshot)
        {
            if (region is null)
                throw new ArgumentException("Interaction frame hit regions cannot contain null.", nameof(hitRegions));
        }

        HitRegions = Array.AsReadOnly(snapshot);
        Focus = focus ?? UiFocusFrame.Empty;
        KeyboardTarget = keyboardTarget;
    }

    public IReadOnlyList<UiHitRegion> HitRegions { get; }

    public UiFocusFrame Focus { get; }

    public UiTargetId? KeyboardTarget { get; }

    public bool ContainsTarget(UiTargetId target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return HitRegions.Any(region => region.Target == target) ||
            Focus.Entries.Any(entry => entry.Target == target) ||
            KeyboardTarget == target;
    }

    public bool ContainsMouseTarget(UiTargetId target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return HitRegions.Any(region => region.Target == target);
    }

    public bool TryHitTest(int x, int y, out UiHitRegion region)
    {
        for (int i = HitRegions.Count - 1; i >= 0; i--)
        {
            UiHitRegion candidate = HitRegions[i];
            if (candidate.Bounds.Width > 0 &&
                candidate.Bounds.Height > 0 &&
                candidate.Bounds.Contains(x, y))
            {
                region = candidate;
                return true;
            }
        }

        region = null!;
        return false;
    }
}
