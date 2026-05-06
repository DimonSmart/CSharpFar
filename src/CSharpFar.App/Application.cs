using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;

namespace CSharpFar.App;

public sealed class Application
{
    private readonly ScreenRenderer _screen;
    private readonly PanelController _ctrl;

    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private PanelSide _active = PanelSide.Left;
    private bool _running = true;

    public Application(ScreenRenderer screen, IFileSystemService fs)
    {
        _screen = screen;
        _ctrl = new PanelController(fs);

        string startDir = Directory.GetCurrentDirectory();
        _left  = new FilePanelState { CurrentDirectory = startDir };
        _right = new FilePanelState { CurrentDirectory = startDir };

        _ctrl.LoadDirectory(_left,  startDir);
        _ctrl.LoadDirectory(_right, startDir);
    }

    public void Run()
    {
        _screen.SetCursorVisible(false);
        Render();

        while (_running)
        {
            var key = _screen.ReadKey();
            HandleKey(key);
            if (_running) Render();
        }

        _screen.SetCursorVisible(true);
        _screen.ClearScreen();
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        var size      = _screen.GetSize();
        int panelH    = size.Height - 2; // row (H-2): cmd line placeholder; row (H-1): key bar
        int leftW     = size.Width / 2;
        int rightW    = size.Width - leftW;

        var leftBounds  = new Rect(0,     0, leftW,  panelH);
        var rightBounds = new Rect(leftW, 0, rightW, panelH);

        var panelRender = new PanelRenderer(_screen);
        panelRender.Render(leftBounds,  _left,  _active == PanelSide.Left);
        panelRender.Render(rightBounds, _right, _active == PanelSide.Right);

        RenderCommandLine(size, panelH);
        new StatusBarRenderer(_screen).Render(size.Height - 1, size.Width);
    }

    private void RenderCommandLine(ConsoleSize size, int row)
    {
        _screen.FillRegion(new Rect(0, row, size.Width, 1), Theme.CommandLine);
        string prompt = ActiveState.CurrentDirectory + ">";
        if (prompt.Length < size.Width)
            _screen.Write(0, row, prompt, Theme.CommandLine);
        else
            _screen.Write(0, row, prompt[^(size.Width - 1)..], Theme.CommandLine);
    }

    // ── input ─────────────────────────────────────────────────────────────────

    private FilePanelState ActiveState => _active == PanelSide.Left ? _left : _right;

    private int VisibleRows()
    {
        var size = _screen.GetSize();
        int panelH = size.Height - 2;
        return PanelRenderer.VisibleRows(new Rect(0, 0, size.Width / 2, panelH));
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        int vr = VisibleRows();

        switch (key.Key)
        {
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

            case ConsoleKey.Home:
                _ctrl.MoveToFirst(ActiveState);
                break;

            case ConsoleKey.End:
                _ctrl.MoveToLast(ActiveState, vr);
                break;

            case ConsoleKey.Enter:
                TryEnterDirectory();
                break;

            case ConsoleKey.Backspace:
                TryGoUp();
                break;

            case ConsoleKey.F10:
                _running = false;
                break;
        }
    }

    private void TryEnterDirectory()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || !item.IsDirectory) return;
        try
        {
            _ctrl.LoadDirectory(ActiveState, item.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stay at current directory — error handling dialogs come in a later stage
        }
    }

    private void TryGoUp()
    {
        try
        {
            _ctrl.GoToParent(ActiveState, VisibleRows());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stay at current directory
        }
    }
}
