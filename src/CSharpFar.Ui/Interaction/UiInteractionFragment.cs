namespace CSharpFar.Ui;

public sealed class UiInteractionFragment
{
    public static UiInteractionFragment Empty { get; } = new([], []);

    internal UiInteractionFragment(
        IReadOnlyList<UiHitRegion> hitRegions,
        IReadOnlyList<UiFocusEntry> focusEntries)
    {
        ArgumentNullException.ThrowIfNull(hitRegions);
        ArgumentNullException.ThrowIfNull(focusEntries);

        UiHitRegion[] hitRegionSnapshot = hitRegions.ToArray();
        UiFocusEntry[] focusEntrySnapshot = focusEntries.ToArray();
        if (hitRegionSnapshot.Any(region => region is null))
            throw new ArgumentException("Interaction fragment hit regions cannot contain null.", nameof(hitRegions));
        if (focusEntrySnapshot.Any(entry => entry is null))
            throw new ArgumentException("Interaction fragment focus entries cannot contain null.", nameof(focusEntries));

        HitRegions = Array.AsReadOnly(hitRegionSnapshot);
        FocusEntries = Array.AsReadOnly(focusEntrySnapshot);
    }

    public IReadOnlyList<UiHitRegion> HitRegions { get; }

    public IReadOnlyList<UiFocusEntry> FocusEntries { get; }
}
