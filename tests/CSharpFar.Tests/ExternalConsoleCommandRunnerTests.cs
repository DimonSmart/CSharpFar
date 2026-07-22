using CSharpFar.App.CommandLine;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ExternalConsoleCommandRunnerTests
{
    [Fact]
    public void RenderReturnPromptForShell_UsesCurrentPaletteAndSelection()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 6);
        var screen = new ScreenRenderer(driver);
        var commandLine = new CommandLineState();
        commandLine.SetText("abc");
        commandLine.SelectAll();
        var palette = new ConsolePalette
        {
            Name = "Test",
            CommandLineFg = ConsoleColor.Green,
            CommandLineBg = ConsoleColor.Magenta,
        };
        var terminalSurface = new TerminalSurfaceController(
            screen,
            terminalScreenMode: null,
            new ShellUnderlayService(screen),
            new UiTransientState(),
            () => ApplicationWorkspaceMode.Panels);
        var runner = new ExternalConsoleCommandRunner(
            screen,
            terminalSurface,
            new ApplicationCommandLineRenderer(() => palette),
            new ApplicationState(palette),
            commandLine,
            refreshPanels: () => { });

        runner.RenderReturnPromptForShell("W");

        int row = ApplicationLayoutService.CommandLineRow(driver.GetSize());
        var promptCell = driver.GetCell(0, row);
        var selectedCell = driver.GetCell("W>".Length, row);

        Assert.Equal('W', promptCell.Character);
        Assert.Equal(ConsoleColor.Green, promptCell.Foreground);
        Assert.Equal(ConsoleColor.Magenta, promptCell.Background);
        Assert.Equal('a', selectedCell.Character);
        Assert.Equal(ConsoleColor.Magenta, selectedCell.Foreground);
        Assert.Equal(ConsoleColor.Green, selectedCell.Background);
    }
}
