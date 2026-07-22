using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class UiInteractionTestFrames
{
    public static UiFocusFrame FocusFrame(
        IReadOnlyList<UiFocusEntry> entries,
        UiTargetId? defaultTarget = null) =>
        new UiInteractionFrameBuilder()
            .AddFocusEntries(entries)
            .SetDefaultFocusTarget(defaultTarget)
            .Build()
            .Focus;

    public static UiInteractionFragment InteractionFragment(
        IReadOnlyList<UiHitRegion> hitRegions,
        IReadOnlyList<UiFocusEntry> focusEntries) =>
        new UiInteractionFrameBuilder()
            .AddHitRegions(hitRegions)
            .AddFocusEntries(focusEntries)
            .BuildFragment();

    public static UiInteractionFragment InteractionFragment(UiTargetId target, Rect bounds) =>
        new UiInteractionFrameBuilder()
            .AddHitRegion(target, bounds)
            .BuildFragment();
}
