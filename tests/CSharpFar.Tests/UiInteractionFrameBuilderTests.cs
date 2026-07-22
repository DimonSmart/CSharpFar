using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiInteractionFrameBuilderTests
{
    [Fact]
    public void Build_ComposesFragmentsWithLaterHitRegionOnTop()
    {
        var bottom = new UiTargetId("bottom");
        var top = new UiTargetId("top");
        var frame = new UiInteractionFrameBuilder()
            .AddFragment(InteractionFragment([new UiHitRegion(bottom, new Rect(0, 0, 4, 4))], []))
            .AddFragment(InteractionFragment([new UiHitRegion(top, new Rect(0, 0, 4, 4))], []))
            .Build();

        Assert.True(frame.TryHitTest(1, 1, out UiHitRegion hit));
        Assert.Equal(top, hit.Target);
    }

    [Fact]
    public void Build_PreservesFocusMetadataAndLayerTargets()
    {
        var focus = new UiTargetId("focus");
        var disabled = new UiTargetId("disabled");
        var keyboard = new UiTargetId("keyboard");
        var cursor = new UiCursorPlacement(2, 3);
        var frame = new UiInteractionFrameBuilder()
            .AddFragment(InteractionFragment([], [
                new UiFocusEntry(focus, 7, true, cursor),
                new UiFocusEntry(disabled, 9, false),
            ]))
            .SetDefaultFocusTarget(focus)
            .SetKeyboardTarget(keyboard)
            .Build();

        UiFocusEntry entry = frame.Focus.Entries[0];
        Assert.Equal(7, entry.TabOrder);
        Assert.True(entry.IsEnabled);
        Assert.Equal(cursor, entry.Cursor);
        Assert.False(frame.Focus.Entries[1].IsEnabled);
        Assert.Equal(focus, frame.Focus.DefaultTarget);
        Assert.Equal(keyboard, frame.KeyboardTarget);
    }

    [Fact]
    public void FragmentAndBuiltFrame_AreSnapshotsOfMutableCollections()
    {
        var target = new UiTargetId("target");
        var regions = new List<UiHitRegion> { new(target, new Rect(0, 0, 1, 1)) };
        var entries = new List<UiFocusEntry> { new(target, 0) };
        var fragment = InteractionFragment(regions, entries);
        var builder = new UiInteractionFrameBuilder().AddFragment(fragment).SetDefaultFocusTarget(target);
        UiInteractionFrame frame = builder.Build();

        regions.Clear();
        entries.Clear();
        builder.AddHitRegion(new UiTargetId("later"), new Rect(2, 0, 1, 1));

        Assert.Single(fragment.HitRegions);
        Assert.Single(fragment.FocusEntries);
        Assert.Single(frame.HitRegions);
        Assert.Single(frame.Focus.Entries);
    }
}
