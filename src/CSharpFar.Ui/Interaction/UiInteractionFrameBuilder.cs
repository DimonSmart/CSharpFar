using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class UiInteractionFrameBuilder
{
    private readonly List<UiHitRegion> _hitRegions = [];
    private readonly List<UiFocusEntry> _focusEntries = [];
    private UiTargetId? _defaultFocusTarget;
    private UiTargetId? _keyboardTarget;

    public UiInteractionFrameBuilder AddFragment(UiInteractionFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        _hitRegions.AddRange(fragment.HitRegions);
        _focusEntries.AddRange(fragment.FocusEntries);
        return this;
    }

    public UiInteractionFrameBuilder AddHitRegion(UiHitRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        _hitRegions.Add(region);
        return this;
    }

    public UiInteractionFrameBuilder AddHitRegion(UiTargetId target, Rect bounds) =>
        AddHitRegion(new UiHitRegion(target, bounds));

    public UiInteractionFrameBuilder AddHitRegions(IEnumerable<UiHitRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        foreach (UiHitRegion region in regions)
            AddHitRegion(region);
        return this;
    }

    public UiInteractionFrameBuilder AddFocusEntry(UiFocusEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _focusEntries.Add(entry);
        return this;
    }

    public UiInteractionFrameBuilder AddFocusEntry(
        UiTargetId target,
        int tabOrder,
        bool isEnabled = true,
        UiCursorPlacement? cursor = null) =>
        AddFocusEntry(new UiFocusEntry(target, tabOrder, isEnabled, cursor));

    public UiInteractionFrameBuilder AddFocusEntries(IEnumerable<UiFocusEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (UiFocusEntry entry in entries)
            AddFocusEntry(entry);
        return this;
    }

    public UiInteractionFrameBuilder SetDefaultFocusTarget(UiTargetId? target)
    {
        _defaultFocusTarget = target;
        return this;
    }

    public UiInteractionFrameBuilder SetKeyboardTarget(UiTargetId? target)
    {
        _keyboardTarget = target;
        return this;
    }

    public UiInteractionFragment BuildFragment() => new(_hitRegions, _focusEntries);

    public UiInteractionFrame Build() =>
        new(_hitRegions, new UiFocusFrame(_focusEntries, _defaultFocusTarget), _keyboardTarget);
}
