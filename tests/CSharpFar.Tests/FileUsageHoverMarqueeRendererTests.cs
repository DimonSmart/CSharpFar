using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class FileUsageHoverMarqueeRendererTests
{
    [Fact]
    public async Task RegistersExactOwnerAndDetailValueRegionsAndPreservesTheirStyles()
    {
        const string name = "a-very-long-owner-name-with-a-visible-ending";
        const string path = "C:/a/very/long/executable/path/with/editor-ending.exe";
        using var state = await State(Owner(7, name, path));
        var driver = new FakeConsoleDriver(40, 18);
        var registrations = new List<HoverMarqueeRegistration>();

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new FileUsageRenderer(canvas, CSharpFarPaletteRegistry.Default, registration =>
            {
                registrations.Add(registration);
                int final = ConsoleTextMetrics.GetCellWidth(registration.Text) - registration.VisibleCellWidth;
                return ConsoleTextMetrics.SliceToCells(registration.Text, final, registration.VisibleCellWidth);
            }).Render(new Rect(0, 0, 40, 18), state));

        Assert.Equal(2, registrations.Count);
        HoverMarqueeRegistration owner = registrations.Single(r => ((FileUsageMarqueeIdentity)r.Identity).Detail == "Name");
        HoverMarqueeRegistration executable = registrations.Single(r => ((FileUsageMarqueeIdentity)r.Identity).Detail == "Path");
        Assert.Equal(name, owner.Text);
        Assert.Equal(new Rect(3, 7, 30, 1), owner.Bounds);
        Assert.Equal(path, executable.Text);
        Assert.Equal(new Rect(9, 9, 30, 1), executable.Bounds);
        Assert.EndsWith("visible-ending", Row(driver, owner.Bounds).TrimEnd());
        Assert.EndsWith("editor-ending.exe", Row(driver, executable.Bounds).TrimEnd());

        CellStyle selected = CSharpFarPaletteStyles.FileUsageSelectedOwner(CSharpFarPaletteRegistry.Default);
        Assert.Equal(selected.Foreground, driver.GetCell(owner.Bounds.X, owner.Bounds.Y).Foreground);
        Assert.Equal(selected.Background, driver.GetCell(owner.Bounds.X, owner.Bounds.Y).Background);
        CellStyle normal = CSharpFarPaletteStyles.FileUsageNormal(CSharpFarPaletteRegistry.Default);
        Assert.Equal(normal.Foreground, driver.GetCell(executable.Bounds.X, executable.Bounds.Y).Foreground);
    }

    [Fact]
    public async Task UntruncatedValuesReasonsLabelsAndActionsAreNotRegistered()
    {
        using var state = await State(Owner(7, "editor", "C:/editor.exe"),
            reason: "a wrapped inspection reason that occupies multiple presentation rows");
        var registrations = new List<HoverMarqueeRegistration>();
        var driver = new FakeConsoleDriver(32, 18);

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new FileUsageRenderer(canvas, CSharpFarPaletteRegistry.Default, registration =>
            {
                registrations.Add(registration);
                return registration.Text;
            }).Render(new Rect(0, 0, 32, 18), state));

        Assert.Empty(registrations);
    }

    [Fact]
    public async Task SelectionRevisionCancelsAnActiveOwnerRegistration()
    {
        using var state = await State(
            Owner(7, "first-owner-name-that-is-long", "C:/first.exe"),
            Owner(8, "second-owner-name-that-is-long", "C:/second.exe"));
        HoverMarqueeRegistration first = Registrations(state).First(r => ((FileUsageMarqueeIdentity)r.Identity).Detail == "Name");
        var clock = new ManualTimeProvider();
        var marquee = new HoverMarquee(clock);
        marquee.SetRegistrations([first]);
        marquee.SetPointer(first.Bounds.X, first.Bounds.Y);
        clock.Advance(HoverMarquee.HoverDelay);
        marquee.HandleWake();
        Assert.Equal(1, marquee.CellOffset);

        state.SelectOwner(1);
        HoverMarqueeRegistration[] changed = Registrations(state).ToArray();
        Assert.True(marquee.SetRegistrations(changed));
        Assert.Equal(0, marquee.CellOffset);
        Assert.NotEqual(first.Identity, marquee.ActiveIdentity);
        Assert.Equal(state.PresentationRevision,
            Assert.IsType<FileUsageMarqueeIdentity>(marquee.ActiveIdentity).Revision);
    }

    private static IReadOnlyList<HoverMarqueeRegistration> Registrations(FileUsagePanelController state)
    {
        var result = new List<HoverMarqueeRegistration>();
        var driver = new FakeConsoleDriver(32, 18);
        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            new FileUsageRenderer(canvas, CSharpFarPaletteRegistry.Default, registration =>
            {
                result.Add(registration);
                return registration.Text;
            }).Render(new Rect(0, 0, 32, 18), state));
        return result;
    }

    private static async Task<FileUsagePanelController> State(params FileUsageOwnerEntry[] owners) =>
        await State(owners, null);

    private static async Task<FileUsagePanelController> State(FileUsageOwnerEntry owner, string reason) =>
        await State([owner], reason);

    private static async Task<FileUsagePanelController> State(FileUsageOwnerEntry[] owners, string? reason)
    {
        var snapshot = new FileUsageSnapshot("C:/file.txt", DateTimeOffset.UnixEpoch, FileUsageState.InUse, owners,
            [new(FileUsageOperation.Read, FileUsageProbeStatus.Allowed)],
            reason is null ? null : new(FileUsageErrorKind.PlatformError, reason));
        var state = new FileUsagePanelController(new ImmediateService(snapshot), () => { });
        state.Update(true, PanelSourceId.Local,
            new FilePanelItem { Name = "file.txt", FullPath = "C:/file.txt", IsDirectory = false });
        for (int i = 0; i < 100 && state.IsInspecting; i++) await Task.Delay(1);
        Assert.False(state.IsInspecting);
        return state;
    }

    private static FileUsageOwnerEntry Owner(int pid, string name, string path) =>
        new(new ProcessSnapshot(pid, name, path, DateTimeOffset.UnixEpoch.AddSeconds(pid)));

    private static string Row(FakeConsoleDriver driver, Rect bounds) =>
        string.Concat(Enumerable.Range(bounds.X, bounds.Width).Select(x => driver.GetCell(x, bounds.Y).Character));

    private sealed class ImmediateService(FileUsageSnapshot snapshot) : IFileUsagePlatformService
    {
        public FileUsageSupportInfo Support { get; } = new(true, false);
        public FileUsageSnapshot Inspect(string path, CancellationToken cancellationToken = default) => snapshot;
        public FileUsageReleaseResult Release(FileUsageReleaseRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
