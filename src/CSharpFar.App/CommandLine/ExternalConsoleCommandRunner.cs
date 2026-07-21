using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.CommandLine;

internal sealed class ExternalConsoleCommandRunner
{
    private readonly ScreenRenderer _screen;
    private readonly TerminalSurfaceController _terminalSurface;
    private readonly ApplicationCommandLineRenderer _commandLineRenderer;
    private readonly ApplicationState _state;
    private readonly CommandLineState _commandLine;
    private readonly Action _refreshPanels;

    public ExternalConsoleCommandRunner(
        ScreenRenderer screen,
        TerminalSurfaceController terminalSurface,
        ApplicationCommandLineRenderer commandLineRenderer,
        ApplicationState state,
        CommandLineState commandLine,
        Action refreshPanels)
    {
        _screen = screen;
        _terminalSurface = terminalSurface;
        _commandLineRenderer = commandLineRenderer;
        _state = state;
        _commandLine = commandLine;
        _refreshPanels = refreshPanels;
    }

    public void Execute(string workDir, string displayCommand, Action execute)
    {
        ApplicationWorkspaceMode workspaceModeAfterCommand = _state.WorkspaceMode;

        ShowShellUnderlayForCommand();
        PrintExecutedCommandPrompt(workDir, displayCommand);

        try
        {
            using var childConsoleMode = _screen.EnterChildProcessConsoleMode();
            execute();
        }
        finally
        {
            _screen.RestoreApplicationInputMode();
            MoveShellOutputAbovePromptArea();
            PrintInputPrompt(workDir);

            _terminalSurface.CaptureUnderlay();
            _screen.InvalidatePhysicalOutput();

            _refreshPanels();
            _state.WorkspaceMode = workspaceModeAfterCommand;
            _terminalSurface.ApplyMode();
        }
    }

    private void MoveShellOutputAbovePromptArea()
    {
        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        int cursorRow = SysConsole.CursorTop - SysConsole.WindowTop;
        if (cursorRow < ApplicationLayoutService.CommandLineRow(size))
            return;

        _screen.SetRenderingOutputMode(false);
        SysConsole.ResetColor();
        SysConsole.WriteLine();
        SysConsole.WriteLine();
    }

    private void ShowShellUnderlayForCommand()
    {
        _terminalSurface.PrepareMainScreenForExternalCommand();
        _screen.SetRenderingOutputMode(false);
        if (!_terminalSurface.UsesTerminalScreenMode)
            _terminalSurface.RestoreOrClearVisibleArea();

        SysConsole.ResetColor();
        SysConsole.CursorVisible = true;
    }

    private void PrintExecutedCommandPrompt(string workDir, string command)
    {
        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        int row = ApplicationLayoutService.CommandLineRow(size);
        ClearShellPromptArea(size);

        int x = WriteShellText(0, row, workDir + ">", ConsoleColor.White);
        WriteShellText(x, row, command, ConsoleColor.Yellow);

        SysConsole.ResetColor();

        int outputRow = Math.Min(size.Height - 1, row + 1);
        SysConsole.SetCursorPosition(0, SysConsole.WindowTop + outputRow);
    }

    private void PrintInputPrompt(string workDir)
    {
        _screen.SetRenderingOutputMode(true);

        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        ClearShellPromptArea(size);

        int row = ApplicationLayoutService.CommandLineRow(size);
        _commandLineRenderer.Render(row, size, workDir, _commandLine);
    }

    private void ClearShellPromptArea(ConsoleSize size)
    {
        int commandRow = ApplicationLayoutService.CommandLineRow(size);
        _screen.FillRegion(new Rect(0, commandRow, size.Width, 1), CellStyle.Default);

        int bottomRow = size.Height - 1;
        if (bottomRow != commandRow)
            _screen.FillRegion(new Rect(0, bottomRow, size.Width, 1), CellStyle.Default);
    }

    private int WriteShellText(int x, int y, string text, ConsoleColor foreground)
    {
        var size = _screen.GetSize();
        if (x >= size.Width || y >= size.Height)
            return x;

        int len = Math.Min(text.Length, size.Width - x);
        if (len <= 0)
            return x;

        var style = new CellStyle(foreground, ConsoleColor.Black);
        _screen.Write(x, y, text.AsSpan(0, len), style);
        return x + len;
    }

    private static class SysConsole
    {
        public static int WindowTop
        {
            get
            {
                try { return global::System.Console.WindowTop; }
                catch (Exception ex) when (IsConsoleStateException(ex)) { return 0; }
            }
        }

        public static int CursorTop
        {
            get
            {
                try { return global::System.Console.CursorTop; }
                catch (Exception ex) when (IsConsoleStateException(ex)) { return 0; }
            }
        }

        public static bool CursorVisible
        {
            set
            {
                try { global::System.Console.CursorVisible = value; }
                catch (Exception ex) when (IsConsoleStateException(ex)) { }
            }
        }

        public static void ResetColor()
        {
            try { global::System.Console.ResetColor(); }
            catch (Exception ex) when (IsConsoleStateException(ex)) { }
        }

        public static void SetCursorPosition(int x, int y) =>
            TrySetCursorPosition(x, y);

        public static void WriteLine()
        {
            try { global::System.Console.WriteLine(); }
            catch (Exception ex) when (IsConsoleStateException(ex)) { }
        }

        private static void TrySetCursorPosition(int x, int y)
        {
            try { global::System.Console.SetCursorPosition(x, y); }
            catch (Exception ex) when (IsConsoleStateException(ex)) { }
        }

        private static bool IsConsoleStateException(Exception ex) =>
            ex is IOException or
                  InvalidOperationException or
                  ArgumentOutOfRangeException or
                  PlatformNotSupportedException;
    }
}
