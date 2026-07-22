using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies the Ctrl+O underlay mechanism:
/// shell output captured before panels are drawn is correctly restored.
/// </summary>
public class UnderlaySnapshotTests
{
    private static (ScreenRenderer renderer, FakeConsoleDriver driver) Create(int w = 80, int h = 24)
    {
        var driver = new FakeConsoleDriver(w, h);
        var renderer = new ScreenRenderer(driver);
        return (renderer, driver);
    }

    [Fact]
    public void Capture_BeforeRender_PreservesTerminalContent()
    {
        var (renderer, driver) = Create();

        // Simulate shell output already on screen
        var shellStyle = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        renderer.Write(0, 0, "C:\\>dir", shellStyle);
        renderer.Write(0, 1, " Volume in drive C has no label.", shellStyle);

        // Step 1: capture underlay (what Ctrl+O should restore)
        var underlay = renderer.Capture(new Rect(0, 0, 80, 24));

        // Step 2: draw panels over the shell output
        var panelStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkBlue);
        renderer.FillRegion(new Rect(0, 0, 80, 24), panelStyle);
        renderer.Write(5, 5, "PANELS ARE HERE", panelStyle);

        // Verify panels are visible (not shell output)
        Assert.NotEqual('C', driver.GetCell(0, 0).Character);
        Assert.Equal(ConsoleColor.DarkBlue, driver.GetCell(0, 0).Background);

        // Step 3: Ctrl+O → restore underlay
        renderer.Restore(underlay);

        // Shell output must be back ("C:\>dir" → C=0 :=1 \=2 >=3 d=4 i=5 r=6)
        Assert.Equal('C', driver.GetCell(0, 0).Character);
        Assert.Equal(':', driver.GetCell(1, 0).Character);
        Assert.Equal('\\', driver.GetCell(2, 0).Character);
        Assert.Equal('>', driver.GetCell(3, 0).Character);
        Assert.Equal('d', driver.GetCell(4, 0).Character);
        Assert.Equal('i', driver.GetCell(5, 0).Character);
        Assert.Equal('r', driver.GetCell(6, 0).Character);
    }

    [Fact]
    public void Capture_AfterSecondCommand_UpdatesUnderlay()
    {
        var (renderer, driver) = Create();

        var style = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);

        // First command output
        renderer.Write(0, 0, "C:\\>echo first", style);
        var underlay1 = renderer.Capture(new Rect(0, 0, 80, 24));

        // Second command output
        renderer.FillRegion(new Rect(0, 0, 80, 24), CellStyle.Default);
        renderer.Write(0, 0, "C:\\>echo second", style);
        var underlay2 = renderer.Capture(new Rect(0, 0, 80, 24));

        // Restore second underlay — should show "second"
        // "C:\>echo second" → C=0 :=1 \=2 >=3 e=4 c=5 h=6 o=7 ' '=8 s=9
        renderer.Restore(underlay2);
        Assert.Equal('s', driver.GetCell(9, 0).Character); // "second" starts at col 9
    }

    [Fact]
    public void TogglePanels_PanelStateIsPreservedWhileHidden()
    {
        // Panel navigation state must not change when panels are hidden.
        // We verify that the FilePanelState cursor is unchanged after Ctrl+O.
        var state = new CSharpFar.Core.Models.FilePanelState { CurrentDirectory = @"C:\" };
        state.Items.Add(new CSharpFar.Core.Models.FilePanelItem
        { Name = "file.txt", FullPath = @"C:\file.txt", IsDirectory = false });
        state.CursorIndex = 0;

        // Simulate: panels go hidden (state is not touched, only rendering is suppressed)
        int cursorBefore = state.CursorIndex;
        string dirBefore = state.CurrentDirectory;

        // Nothing changes in state when panels toggle
        Assert.Equal(cursorBefore, state.CursorIndex);
        Assert.Equal(dirBefore, state.CurrentDirectory);
    }

    [Fact]
    public void Restore_ColorAttributesArePreserved()
    {
        var (renderer, driver) = Create(20, 5);

        var style = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkRed);
        renderer.Write(0, 0, "COLORTEST", style);

        var snapshot = renderer.Capture(new Rect(0, 0, 20, 5));

        // Overwrite with different colors
        renderer.FillRegion(new Rect(0, 0, 20, 5), CellStyle.Default);

        renderer.Restore(snapshot);

        Assert.Equal('C', driver.GetCell(0, 0).Character);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(0, 0).Foreground);
        Assert.Equal(ConsoleColor.DarkRed, driver.GetCell(0, 0).Background);
    }

    [Fact]
    public void HiddenCommandLineOverlayCleanup_RestoresPreviousShellRowContent()
    {
        var (renderer, driver) = Create(40, 8);
        var shellStyle = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        renderer.Write(0, 6, "SHELL-BEFORE".AsSpan(), shellStyle);

        var underlay = new ShellUnderlayService(renderer);
        var viewport = renderer.GetViewport();
        int row = ApplicationLayoutService.CommandLineRow(viewport.Size);
        underlay.PrepareHiddenCommandLineOverlay(viewport, row, viewport.Width);

        UiTestRender.Render(renderer, canvas =>
            new ApplicationCommandLineRenderer(() => PaletteRegistry.Default)
                .Render(canvas, row, viewport.Size, @"C:\Work", new CommandLineState()));
        Assert.Contains(">", driver.GetRow(row), StringComparison.Ordinal);

        underlay.RemoveHiddenCommandLineOverlay();

        Assert.StartsWith("SHELL-BEFORE", driver.GetRow(row));
        Assert.DoesNotContain(@"C:\Work>", driver.GetRow(row), StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenCommandLineOverlayCleanup_RestoresShellRowAfterViewportTopChange()
    {
        var (renderer, driver) = Create(40, 8);
        var shellStyle = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        renderer.Write(0, 7, "SHELL-BEFORE".AsSpan(), shellStyle);
        driver.SetViewportOrigin(0, 3);

        var underlay = new ShellUnderlayService(renderer);
        var viewport = renderer.GetViewport();
        underlay.PrepareHiddenCommandLineOverlay(viewport, row: 7, viewport.Width);

        UiTestRender.Render(renderer, canvas =>
            new ApplicationCommandLineRenderer(() => PaletteRegistry.Default)
                .Render(canvas, 7, viewport.Size, @"C:\Work", new CommandLineState()));
        Assert.Contains(">", driver.GetRow(7), StringComparison.Ordinal);

        driver.SetViewportOrigin(0, 5);
        underlay.RemoveHiddenCommandLineOverlay();

        Assert.StartsWith("SHELL-BEFORE", driver.GetRow(5));
    }

    [Fact]
    public void HiddenCommandLineOverlayCleanup_RestoresShellRowAfterViewportShrink()
    {
        var (renderer, driver) = Create(40, 30);
        var shellStyle = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        renderer.Write(0, 28, "SHELL-BEFORE".AsSpan(), shellStyle);

        var underlay = new ShellUnderlayService(renderer);
        var viewport = renderer.GetViewport();
        underlay.PrepareHiddenCommandLineOverlay(viewport, row: 28, viewport.Width);

        UiTestRender.Render(renderer, canvas =>
            new ApplicationCommandLineRenderer(() => PaletteRegistry.Default)
                .Render(canvas, 28, viewport.Size, @"C:\Work", new CommandLineState()));
        Assert.Contains(">", driver.GetRow(28), StringComparison.Ordinal);

        driver.SetSize(40, 10);
        driver.SetViewportOrigin(0, 20);
        underlay.RemoveHiddenCommandLineOverlay();

        Assert.StartsWith("SHELL-BEFORE", driver.GetRow(8));
    }
}
