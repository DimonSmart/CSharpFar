using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class PanelHoverMarqueeRendererTests
{
    [Fact]
    public void Full_RegistersOnlyVisibleNameFieldsAndRevealsFinalCellsWithoutChangingStyles()
    {
        var driver = new FakeConsoleDriver(30, 8);
        var screen = new ScreenRenderer(driver);
        var state = State(
            Item("long-file-name-ending.txt", size: 1234),
            Item("selected-directory-ending", directory: true),
            Item("parent-directory-ending", directory: true, parent: true));
        state.CursorIndex = 0;
        state.SelectedPaths.Add(state.Items[1].FullPath);
        var registrations = new List<HoverMarqueeRegistration>();

        UiTestRender.Render(screen, canvas =>
            new PanelRenderer(canvas, PaletteRegistry.Default, new FixedHighlightService(), renderHoverMarquee: registration =>
            {
                registrations.Add(registration);
                int finalOffset = Math.Max(0,
                    ConsoleTextMetrics.GetCellWidth(registration.Text) - registration.VisibleCellWidth);
                return ConsoleTextMetrics.SliceToCells(
                    registration.Text, finalOffset, registration.VisibleCellWidth);
            }).Render(new Rect(0, 0, 30, 8), state, isActive: true, PanelSide.Left));

        Assert.Equal(3, registrations.Count);
        Assert.All(registrations, registration =>
        {
            Assert.Equal(new Rect(1, registration.Bounds.Y, 19, 1), registration.Bounds);
            var identity = Assert.IsType<ApplicationPanelMarqueeIdentity>(registration.Identity);
            Assert.Equal(ApplicationPanelMarqueeField.FullName, identity.Field);
        });
        Assert.EndsWith("ending.txt", Row(driver, 1, 1, 19).TrimEnd());
        Assert.Equal(ConsoleColor.Magenta, driver.GetCell(1, 1).Foreground);
        Assert.Equal(PaletteRegistry.Default.CursorActiveBg, driver.GetCell(1, 1).Background);
        Assert.Equal(PaletteRegistry.Default.SelectedBg, driver.GetCell(1, 2).Background);
        Assert.Equal(PaletteRegistry.Default.DirectoryFg, driver.GetCell(1, 3).Foreground);

        // The size field starts after the registered name bound and keeps its own row style.
        Assert.Equal(PaletteRegistry.Default.CursorActiveBg, driver.GetCell(21, 1).Background);
        Assert.DoesNotContain(registrations, registration => registration.Bounds.Contains(21, 1));
    }

    [Fact]
    public void Brief_RegistersIndependentVisualColumnsForTheirActualItems()
    {
        // Four content rows per column: all six items are visible, with the
        // final two occupying the independently registered right column.
        var driver = new FakeConsoleDriver(24, 10);
        var screen = new ScreenRenderer(driver);
        var state = State(Enumerable.Range(0, 6)
            .Select(index => Item($"item-{index}-long-ending"))
            .ToArray());
        var registrations = new List<HoverMarqueeRegistration>();

        UiTestRender.Render(screen, canvas =>
            new BriefTwoColumnsPanelRenderer(
                canvas,
                PaletteRegistry.Default,
                renderHoverMarquee: registration =>
                {
                    registrations.Add(registration);
                    int finalOffset = Math.Max(0,
                        ConsoleTextMetrics.GetCellWidth(registration.Text) - registration.VisibleCellWidth);
                    return ConsoleTextMetrics.SliceToCells(
                        registration.Text, finalOffset, registration.VisibleCellWidth);
                }).Render(new Rect(0, 0, 24, 10), state, isActive: false));

        Assert.Equal(6, registrations.Count);
        var identities = registrations
            .Select(r => Assert.IsType<ApplicationPanelMarqueeIdentity>(r.Identity))
            .ToArray();
        var left = identities.Where(identity => identity.Field == ApplicationPanelMarqueeField.BriefLeftName).ToArray();
        var right = identities.Where(identity => identity.Field == ApplicationPanelMarqueeField.BriefRightName).ToArray();
        Assert.All(left, identity => Assert.Equal(ApplicationPanelMarqueeField.BriefLeftName, identity.Field));
        Assert.All(right, identity => Assert.Equal(ApplicationPanelMarqueeField.BriefRightName, identity.Field));
        Assert.Equal(state.Items.Take(4).Select(item => item.Location), left.Select(identity => identity.Location));
        Assert.Equal(state.Items.Skip(4).Select(item => item.Location), right.Select(identity => identity.Location));
        Assert.All(registrations.Where(registration => registration.Bounds.X == 1),
            registration => Assert.Equal(new Rect(1, registration.Bounds.Y, 11, 1), registration.Bounds));
        Assert.All(registrations.Where(registration => registration.Bounds.X == 13),
            registration => Assert.Equal(new Rect(13, registration.Bounds.Y, 10, 1), registration.Bounds));
        Assert.EndsWith("ending", Row(driver, 13, 2, 10).TrimEnd());
        Assert.Equal(PaletteRegistry.Default.PanelBackground, driver.GetCell(13, 2).Background);
    }

    [Fact]
    public void ReplacingOrMovingAnItemChangesCommittedRegistrationAndCancelsActiveOffset()
    {
        var clock = new ManualTimeProvider();
        var marquee = new HoverMarquee(clock);
        var item = Item("a-very-long-file-name");
        HoverMarqueeRegistration full = Registration(item, ApplicationPanelMarqueeField.FullName, 1, 1, 8);
        marquee.SetRegistrations([full]);
        marquee.SetPointer(2, 1);
        clock.Advance(HoverMarquee.HoverDelay);
        marquee.HandleWake();
        Assert.Equal(1, marquee.CellOffset);

        HoverMarqueeRegistration movedToBrief = Registration(
            item, ApplicationPanelMarqueeField.BriefRightName, 13, 2, 8);
        Assert.True(marquee.SetRegistrations([movedToBrief]));
        Assert.Equal(0, marquee.CellOffset);
        Assert.Null(marquee.ActiveIdentity);

        var replacement = Item("replacement-file-name");
        marquee.SetPointer(14, 2);
        marquee.SetRegistrations([Registration(replacement, ApplicationPanelMarqueeField.BriefRightName, 13, 2, 8)]);
        Assert.Equal(replacement.Location,
            Assert.IsType<ApplicationPanelMarqueeIdentity>(marquee.ActiveIdentity).Location);
        Assert.Equal(0, marquee.CellOffset);
    }

    private static HoverMarqueeRegistration Registration(
        FilePanelItem item, ApplicationPanelMarqueeField field, int x, int y, int width) =>
        new(new ApplicationPanelMarqueeIdentity(PanelSide.Left, item.Location, field),
            item.Name, new Rect(x, y, width, 1), width);

    private static FilePanelState State(params FilePanelItem[] items)
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\Test" };
        foreach (FilePanelItem item in items)
            state.Items.Add(item);
        return state;
    }

    private static FilePanelItem Item(
        string name, bool directory = false, bool parent = false, long? size = null) =>
        new()
        {
            Name = name,
            FullPath = @"C:\Test\" + name,
            IsDirectory = directory,
            IsParentDirectory = parent,
            Size = size,
        };

    private static string Row(FakeConsoleDriver driver, int x, int y, int width) =>
        string.Concat(Enumerable.Range(x, width).Select(column => driver.GetCell(column, y).Character));

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class FixedHighlightService : IFileHighlightService
    {
        public HighlightResult GetHighlight(FilePanelItem item, FileRowState state) =>
            item.Name.StartsWith("long-file", StringComparison.Ordinal)
                ? new() { ColorOverride = new FileHighlightColor((int)ConsoleColor.Magenta, null) }
                : new();
    }
}
