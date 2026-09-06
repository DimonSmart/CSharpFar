using CSharpFar.App.Rendering;
using CSharpFar.App.Viewer;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class QuickViewRecentChangesMarqueeRendererTests
{
    [Fact]
    public void Render_RegistersOnlyOverflowingPathAndKeepsPrefixFixed()
    {
        var longChange = Change(1, "folder/a-very-long-file-name-that-does-not-fit.txt");
        var shortChange = Change(2, "a.txt");
        var list = new RoutedScrollableList<DirectoryChange>(
            new ScrollableListState<DirectoryChange>([longChange, shortChange]),
            new UiTargetId("test.quick-view.changes"),
            new UiTargetId("test.quick-view.changes.scrollbar"),
            RoutedScrollableListOptions.DropdownPopup);
        ScrollableListFrame listFrame = list.CalculateFrame(new Rect(2, 6, 24, 2), null);
        var frame = new ApplicationQuickViewFrame(
            new Rect(0, 0, 30, 12),
            null,
            [],
            longChange.Id,
            [longChange.Id, shortChange.Id],
            0,
            list,
            listFrame);
        var driver = new FakeConsoleDriver(40, 16);
        var registrations = new List<HoverMarqueeRegistration>();

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            QuickViewRecentChangesMarqueeRenderer.Render(
                canvas,
                frame,
                CSharpFarPaletteRegistry.Default,
                registration =>
                {
                    registrations.Add(registration);
                    int finalOffset = ConsoleTextMetrics.GetCellWidth(registration.Text) - registration.VisibleCellWidth;
                    return ConsoleTextMetrics.SliceToCells(registration.Text, finalOffset, registration.VisibleCellWidth);
                }));

        HoverMarqueeRegistration registration = Assert.Single(registrations);
        Assert.Equal(longChange.RelativePath, registration.Text);
        Assert.Equal(longChange.Id, Assert.IsType<QuickViewRecentChangeMarqueeIdentity>(registration.Identity).ChangeId);
        Assert.Equal(new Rect(15, 6, 11, 1), registration.Bounds);
        Assert.EndsWith("not-fit.txt", Row(driver, registration.Bounds).TrimEnd());
    }

    private static DirectoryChange Change(long id, string relativePath) =>
        new(id, DirectoryChangeKind.Created, relativePath, null, relativePath, DateTimeOffset.UnixEpoch, 0, 1);

    private static string Row(FakeConsoleDriver driver, Rect bounds) =>
        string.Concat(Enumerable.Range(bounds.X, bounds.Width).Select(x => driver.GetCell(x, bounds.Y).Character));
}
