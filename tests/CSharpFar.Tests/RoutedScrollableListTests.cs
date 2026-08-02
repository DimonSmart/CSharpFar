using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class RoutedScrollableListTests
{
    [Fact]
    public void Fragment_PublishesListAndScrollbarTargetsWithFocusablePolicy()
    {
        var listTarget = new UiTargetId("list");
        var scrollbarTarget = new UiTargetId("scrollbar");
        var list = new RoutedScrollableList<int>(new ScrollableListState<int>([1, 2, 3, 4]), listTarget, scrollbarTarget);
        ScrollableListFrame frame = list.CalculateFrame(new Rect(0, 0, 8, 3), new Rect(8, 0, 1, 3));

        UiInteractionFragment fragment = list.BuildInteractionFragment(frame, 3);

        Assert.Contains(fragment.HitRegions, region => region.Target == listTarget);
        Assert.Contains(fragment.HitRegions, region => region.Target == scrollbarTarget);
        Assert.Contains(fragment.FocusEntries, entry => entry.Target == listTarget && entry.TabOrder == 3);
    }

    [Fact]
    public void RouteInput_RejectsForeignTarget()
    {
        var list = new RoutedScrollableList<int>(new ScrollableListState<int>([1]), new UiTargetId("list"), new UiTargetId("scrollbar"));
        ScrollableListFrame frame = list.CalculateFrame(new Rect(0, 0, 8, 1), null);

        RoutedScrollableListInputResult result = list.RouteInput(new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)), frame, UiInputRouteContext.KeyboardTarget(new UiFocusController(), new UiTargetId("other")));

        Assert.False(result.ListResult.IsHandled);
        Assert.False(result.UiResult.Handled);
    }
}
