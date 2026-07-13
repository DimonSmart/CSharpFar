using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiInteractionFrameTests
{
    [Fact]
    public void Empty_HasNoHitRegionsAndUsesEmptyFocus()
    {
        Assert.Empty(UiInteractionFrame.Empty.HitRegions);
        Assert.Same(UiFocusFrame.Empty, UiInteractionFrame.Empty.Focus);
        Assert.False(UiInteractionFrame.Empty.TryHitTest(0, 0, out _));
    }

    [Fact]
    public void TryHitTest_FindsRegionAndUsesHalfOpenBounds()
    {
        var region = new UiHitRegion(new UiTargetId("a"), new Rect(1, 2, 3, 4));
        var frame = new UiInteractionFrame([region]);

        Assert.True(frame.TryHitTest(1, 2, out var hit));
        Assert.Same(region, hit);
        Assert.False(frame.TryHitTest(4, 2, out _));
        Assert.False(frame.TryHitTest(1, 6, out _));
    }

    [Fact]
    public void TryHitTest_SkipsZeroSizedRegions()
    {
        var frame = new UiInteractionFrame([
            new(new UiTargetId("a"), new Rect(0, 0, 0, 1)),
            new(new UiTargetId("b"), new Rect(0, 0, 1, 0)),
        ]);

        Assert.False(frame.TryHitTest(0, 0, out _));
    }

    [Fact]
    public void TryHitTest_UsesReverseRenderOrder()
    {
        var frame = new UiInteractionFrame([
            new(new UiTargetId("bottom"), new Rect(0, 0, 5, 5)),
            new(new UiTargetId("top"), new Rect(0, 0, 5, 5)),
        ]);

        Assert.True(frame.TryHitTest(2, 2, out var hit));
        Assert.Equal(new UiTargetId("top"), hit.Target);
    }

    [Fact]
    public void Frame_CopiesMutableList()
    {
        var regions = new List<UiHitRegion> { new(new UiTargetId("a"), new Rect(0, 0, 1, 1)) };
        var frame = new UiInteractionFrame(regions);

        regions.Clear();

        Assert.True(frame.TryHitTest(0, 0, out var hit));
        Assert.Equal(new UiTargetId("a"), hit.Target);
    }

    [Fact]
    public void Frame_RejectsNullRegion()
    {
        var regions = new UiHitRegion?[]
        {
            new(new UiTargetId("a"), new Rect(0, 0, 1, 1)),
            null,
        };

        var exception = Assert.Throws<ArgumentException>(() => new UiInteractionFrame(regions!));
        Assert.Equal("hitRegions", exception.ParamName);
    }

    [Fact]
    public void OneTargetCanHaveMultipleRegions()
    {
        var target = new UiTargetId("a");
        var frame = new UiInteractionFrame([
            new(target, new Rect(0, 0, 1, 1)),
            new(target, new Rect(2, 0, 1, 1)),
        ]);

        Assert.True(frame.TryHitTest(2, 0, out var hit));
        Assert.Equal(target, hit.Target);
    }

    [Fact]
    public void ContainsTarget_FindsHitOnlyAndFocusOnlyTargets()
    {
        var hitOnly = new UiTargetId("hit");
        var focusOnly = new UiTargetId("focus");
        var frame = new UiInteractionFrame(
            [new(hitOnly, new Rect(0, 0, 1, 1))],
            new UiFocusFrame([new(focusOnly, 0)]));

        Assert.True(frame.ContainsTarget(hitOnly));
        Assert.True(frame.ContainsTarget(focusOnly));
        Assert.False(frame.ContainsTarget(new UiTargetId("missing")));
    }
}
