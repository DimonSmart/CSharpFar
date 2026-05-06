using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App;

public sealed class Application
{
    private readonly ScreenRenderer _screen;
    private readonly PanelController _ctrl;
    private readonly IShellService _shell;
    private readonly IHistoryStore _history;

    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private readonly CommandLineState _cmdLine = new();

    private PanelSide _active  = PanelSide.Left;
    private bool      _running = true;

    public Application(
        ScreenRenderer  screen,
        IFileSystemService fs,
        IShellService   shell,
        IHistoryStore?  history = null)
    {
        _screen  = screen;
        _ctrl    = new PanelController(fs);
        _shell   = shell;
        _history = history ?? new InMemoryHistoryStore();

        string startDir = Directory.GetCurrentDirectory();
        _left  = new FilePanelState { CurrentDirectory = startDir };
        _right = new FilePanelState { CurrentDirectory = startDir };

        _ctrl.LoadDirectory(_left,  startDir);
        _ctrl.LoadDirectory(_right, startDir);
    }

    public void Run()
    {
        try
        {
            Render();
            while (_running)
            {
                var key = _screen.ReadKey();
                HandleKey(key);
                if (_running) Render();
            }
            _screen.ClearScreen();
        }
        finally
        {
            _screen.SetCursorVisible(true);
        }
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        _screen.SetCursorVisible(false);

        var size   = _screen.GetSize();
        int panelH = size.Height - 2;
        int leftW  = size.Width / 2;
        int rightW = size.Width - leftW;

        var panelRenderer = new PanelRenderer(_screen);
        panelRenderer.Render(new Rect(0,     0, leftW,  panelH), _left,  _active == PanelSide.Left);
        panelRenderer.Render(new Rect(leftW, 0, rightW, panelH), _right, _active == PanelSide.Right);

        // Command line
        var cmdRenderer = new CommandLineRenderer(_screen);
        cmdRenderer.Render(panelH, size.Width, ActiveState.CurrentDirectory, _cmdLine);

        // Key bar
        new StatusBarRenderer(_screen).Render(size.Height - 1, size.Width);

        // Position cursor in command line and show it
        int curX = cmdRenderer.GetCursorX(size.Width, ActiveState.CurrentDirectory, _cmdLine);
        if (curX >= 0 && curX < size.Width)
        {
            _screen.SetCursorPosition(curX, panelH);
            _screen.SetCursorVisible(true);
        }
    }

    // ── key handling ──────────────────────────────────────────────────────────

    private FilePanelState ActiveState => _active == PanelSide.Left ? _left : _right;

    private int VisibleRows()
    {
        var size   = _screen.GetSize();
        int panelH = size.Height - 2;
        return PanelRenderer.VisibleRows(new Rect(0, 0, 0, panelH));
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        // Printable characters always go to the command line
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _cmdLine.Insert(key.KeyChar);
            return;
        }

        int vr = VisibleRows();

        switch (key.Key)
        {
            // ── Command line editing ──────────────────────────────────────────
            case ConsoleKey.LeftArrow:
                _cmdLine.MoveCursor(-1);
                break;

            case ConsoleKey.RightArrow:
                _cmdLine.MoveCursor(+1);
                break;

            case ConsoleKey.Home:
                if (_cmdLine.HasText)
                    _cmdLine.MoveToStart();
                else
                    _ctrl.MoveToFirst(ActiveState);
                break;

            case ConsoleKey.End:
                if (_cmdLine.HasText)
                    _cmdLine.MoveToEnd();
                else
                    _ctrl.MoveToLast(ActiveState, vr);
                break;

            case ConsoleKey.Delete:
                _cmdLine.DeleteForward();
                break;

            case ConsoleKey.Backspace:
                if (_cmdLine.HasText)
                    _cmdLine.DeleteBack();
                else
                    TryGoUp();
                break;

            case ConsoleKey.Escape:
                _cmdLine.Clear();
                break;

            // ── Execution ─────────────────────────────────────────────────────
            case ConsoleKey.Enter:
                if (_cmdLine.HasText)
                    ExecuteCommand(_cmdLine.Text);
                else
                    TryEnterDirectory();
                break;

            // ── Panel navigation ──────────────────────────────────────────────
            case ConsoleKey.Tab:
                _active = _active == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
                break;

            case ConsoleKey.UpArrow:
                _ctrl.MoveCursor(ActiveState, -1, vr);
                break;

            case ConsoleKey.DownArrow:
                _ctrl.MoveCursor(ActiveState, +1, vr);
                break;

            case ConsoleKey.PageUp:
                _ctrl.MoveCursor(ActiveState, -vr, vr);
                break;

            case ConsoleKey.PageDown:
                _ctrl.MoveCursor(ActiveState, +vr, vr);
                break;

            case ConsoleKey.F10:
                _running = false;
                break;
        }
    }

    // ── shell execution ───────────────────────────────────────────────────────

    private void ExecuteCommand(string command)
    {
        string workDir = ActiveState.CurrentDirectory;
        _cmdLine.Clear();

        // Scroll UI off the visible area so shell output appears cleanly
        // TODO Stage 4: replace with proper IConsoleDriver.Capture/Restore for Ctrl+O
        ScrollPanelsOff();
        PrintShellPrompt(workDir, command);

        _shell.Execute(command, workDir);

        _history.AddCommand(new CommandHistoryItem
        {
            Command          = command,
            WorkingDirectory = workDir,
        });

        RefreshPanels();
        // Render() is called by the main loop after HandleKey returns
    }

    /// <summary>
    /// Scrolls the visible window so that all panel content moves into the scroll-back
    /// buffer and the shell command output can appear in a clean screen area.
    /// The content remains accessible via Ctrl+O once Stage 4 is implemented.
    /// </summary>
    private static void ScrollPanelsOff()
    {
        SysConsole.ResetColor();
        SysConsole.CursorVisible = true;

        int h = SysConsole.WindowHeight;
        // Move to the last row of the visible window
        SysConsole.SetCursorPosition(0, SysConsole.WindowTop + h - 1);
        // Print h blank lines — this scrolls all panel content into the scroll-back buffer
        for (int i = 0; i < h; i++)
            SysConsole.WriteLine();

        // Position at the top of the now-empty visible area
        SysConsole.SetCursorPosition(0, SysConsole.WindowTop);
    }

    private static void PrintShellPrompt(string workDir, string command)
    {
        SysConsole.ForegroundColor = ConsoleColor.White;
        SysConsole.Write(workDir + ">");
        SysConsole.ForegroundColor = ConsoleColor.Yellow;
        SysConsole.WriteLine(command);
        SysConsole.ResetColor();
    }

    // ── navigation helpers ────────────────────────────────────────────────────

    private void TryEnterDirectory()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || !item.IsDirectory) return;
        try { _ctrl.LoadDirectory(ActiveState, item.FullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void TryGoUp()
    {
        try { _ctrl.GoToParent(ActiveState, VisibleRows()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void RefreshPanels()
    {
        int vr = VisibleRows();
        SafeRefresh(_left,  vr);
        SafeRefresh(_right, vr);
    }

    private void SafeRefresh(FilePanelState state, int visibleRows)
    {
        if (!Directory.Exists(state.CurrentDirectory)) return;
        try { _ctrl.RefreshDirectory(state, visibleRows); }
        catch { }
    }

    // Alias to avoid naming conflict with CSharpFar.Console namespace
    private static class SysConsole
    {
        public static int WindowHeight
        {
            get => global::System.Console.WindowHeight;
        }
        public static int WindowTop
        {
            get => global::System.Console.WindowTop;
        }
        public static bool CursorVisible
        {
            set => global::System.Console.CursorVisible = value;
        }
        public static ConsoleColor ForegroundColor
        {
            set => global::System.Console.ForegroundColor = value;
        }
        public static void ResetColor()  => global::System.Console.ResetColor();
        public static void Write(string s) => global::System.Console.Write(s);
        public static void WriteLine(string s) => global::System.Console.WriteLine(s);
        public static void WriteLine()     => global::System.Console.WriteLine();
        public static void SetCursorPosition(int x, int y) =>
            global::System.Console.SetCursorPosition(x, y);
    }
}
