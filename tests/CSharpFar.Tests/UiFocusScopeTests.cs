using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiFocusScopeTests
{
    [Fact]
    public void TargetId_ValidatesAndPreservesValueSemantics()
    {
        Assert.Throws<ArgumentException>(() => new UiTargetId(null!));
        Assert.Throws<ArgumentException>(() => new UiTargetId(string.Empty));
        Assert.Throws<ArgumentException>(() => new UiTargetId("   "));

        Assert.Equal(new UiTargetId("target"), new UiTargetId("target"));
        Assert.NotEqual(new UiTargetId("target"), new UiTargetId("TARGET"));
        Assert.Equal("target", new UiTargetId("target").ToString());
    }

    [Fact]
    public void Entries_RejectNullTargets()
    {
        Assert.Throws<ArgumentNullException>(() => new UiFocusEntry(null!, 0));
        Assert.Throws<ArgumentNullException>(() => new UiHitRegion(null!, new CSharpFar.Console.Models.Rect(0, 0, 1, 1)));
    }

    [Fact]
    public void Commit_SelectsDefaultTarget()
    {
        var scope = new UiFocusScope();

        scope.Commit(Frame(["a", "b"], defaultTarget: "b"));

        Assert.Equal(new UiTargetId("b"), scope.FocusedTarget);
    }

    [Fact]
    public void Commit_SelectsFirstEnabledWhenDefaultMissing()
    {
        var scope = new UiFocusScope();

        scope.Commit(new UiFocusFrame([
            new(new UiTargetId("a"), 2, IsEnabled: false),
            new(new UiTargetId("b"), 3),
            new(new UiTargetId("c"), 1),
        ]));

        Assert.Equal(new UiTargetId("c"), scope.FocusedTarget);
    }

    [Fact]
    public void Frame_RejectsDisabledDefault()
    {
        Assert.Throws<ArgumentException>(() => new UiFocusFrame([
            new(new UiTargetId("a"), 0, IsEnabled: false),
        ], new UiTargetId("a")));
    }

    [Fact]
    public void Frame_RejectsDuplicateTargets()
    {
        Assert.Throws<ArgumentException>(() => new UiFocusFrame([
            new(new UiTargetId("a"), 0),
            new(new UiTargetId("a"), 1),
        ]));
    }

    [Fact]
    public void Frame_RejectsNullEntry()
    {
        var entries = new UiFocusEntry?[]
        {
            new(new UiTargetId("a"), 0),
            null,
        };

        var exception = Assert.Throws<ArgumentException>(() => new UiFocusFrame(entries!));
        Assert.Equal("entries", exception.ParamName);
    }

    [Fact]
    public void Commit_PreservesCurrentTargetWhenStillEnabled()
    {
        var scope = new UiFocusScope();
        scope.Commit(Frame(["a", "b"], defaultTarget: "a"));
        scope.TryFocus(new UiTargetId("b"));

        scope.Commit(Frame(["b", "c"], defaultTarget: "c"));

        Assert.Equal(new UiTargetId("b"), scope.FocusedTarget);
    }

    [Fact]
    public void Commit_FallsBackWhenCurrentTargetDisappears()
    {
        var scope = new UiFocusScope();
        scope.Commit(Frame(["a", "b"], defaultTarget: "a"));

        scope.Commit(Frame(["b", "c"], defaultTarget: "c"));

        Assert.Equal(new UiTargetId("c"), scope.FocusedTarget);
    }

    [Fact]
    public void Commit_ClearsFocusWhenNoEnabledTargets()
    {
        var scope = new UiFocusScope();
        scope.Commit(Frame(["a"]));

        scope.Commit(new UiFocusFrame([new(new UiTargetId("a"), 0, IsEnabled: false)]));

        Assert.False(scope.HasFocus);
    }

    [Fact]
    public void TryFocus_ChangesOnlyForKnownEnabledTarget()
    {
        var scope = new UiFocusScope();
        scope.Commit(new UiFocusFrame([
            new(new UiTargetId("a"), 0),
            new(new UiTargetId("b"), 1, IsEnabled: false),
        ]));

        Assert.True(scope.TryFocus(new UiTargetId("a")));
        Assert.False(scope.TryFocus(new UiTargetId("missing")));
        Assert.False(scope.TryFocus(new UiTargetId("b")));
        Assert.Equal(new UiTargetId("a"), scope.FocusedTarget);
    }

    [Fact]
    public void Traversal_UsesTabOrderOriginalOrderAndWrapAround()
    {
        var scope = new UiFocusScope();
        scope.Commit(new UiFocusFrame([
            new(new UiTargetId("a"), 2),
            new(new UiTargetId("b"), 1),
            new(new UiTargetId("c"), 1),
        ]));

        Assert.Equal(new UiTargetId("b"), scope.FocusedTarget);
        Assert.True(scope.MoveNext());
        Assert.Equal(new UiTargetId("c"), scope.FocusedTarget);
        Assert.True(scope.MoveNext());
        Assert.Equal(new UiTargetId("a"), scope.FocusedTarget);
        Assert.True(scope.MoveNext());
        Assert.Equal(new UiTargetId("b"), scope.FocusedTarget);
        Assert.True(scope.MovePrevious());
        Assert.Equal(new UiTargetId("a"), scope.FocusedTarget);
    }

    [Fact]
    public void FocusedEntry_UsesCurrentFrameCursorMetadata()
    {
        var scope = new UiFocusScope();
        var target = new UiTargetId("a");
        scope.Commit(new UiFocusFrame([new(target, 0, Cursor: new UiCursorPlacement(1, 2))]));
        scope.Commit(new UiFocusFrame([new(target, 0, Cursor: new UiCursorPlacement(3, 4, Visible: false))]));

        Assert.True(scope.TryGetFocusedEntry(out var entry));
        Assert.Equal(new UiCursorPlacement(3, 4, Visible: false), entry.Cursor);
    }

    [Fact]
    public void EmptyFrame_ClearsFocus()
    {
        var scope = new UiFocusScope();
        scope.Commit(Frame(["a"]));

        scope.Commit(UiFocusFrame.Empty);

        Assert.False(scope.HasFocus);
    }

    private static UiFocusFrame Frame(string[] ids, string? defaultTarget = null) =>
        new(ids.Select((id, index) => new UiFocusEntry(new UiTargetId(id), index)).ToArray(),
            defaultTarget is null ? null : new UiTargetId(defaultTarget));
}
