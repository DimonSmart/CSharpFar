using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.App.Editor;
using CSharpFar.App.Search;
using CSharpFar.App.UserMenu;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App;

public sealed class Application
{
    private readonly ScreenRenderer _screen;
    private readonly PanelController _ctrl;
    private readonly IShellService _shell;
    private readonly IFileOperationService _fileOps;
    private readonly IHistoryStore _history;
    private readonly AppSettingsAlias _settings;
    private readonly UserMenuStore _userMenu;
    private readonly Action? _saveSettings;

    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private readonly CommandLineState _cmdLine = new();

    private PanelSide     _active        = PanelSide.Left;
    private bool          _running       = true;
    private bool          _panelsVisible = true;
    private bool          _quickView     = false;
    private ConsoleSize?  _lastRenderSize;
    private ScreenSnapshot? _underlay;          // last known screen content before panels
    private ConsolePalette  _palette;
    private PanelViewMode   _leftViewMode;
    private PanelViewMode   _rightViewMode;

    public Application(
        ScreenRenderer         screen,
        IFileSystemService     fs,
        IShellService          shell,
        IFileOperationService  fileOps,
        IHistoryStore?         history      = null,
        AppSettingsAlias?      settings     = null,
        UserMenuStore?         userMenu     = null,
        Action?                saveSettings = null)
    {
        _screen       = screen;
        _ctrl         = new PanelController(fs);
        _shell        = shell;
        _fileOps      = fileOps;
        _history      = history      ?? new InMemoryHistoryStore();
        _settings     = settings     ?? new AppSettingsAlias();
        _userMenu     = userMenu     ?? new UserMenuStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CSharpFar"));
        _saveSettings = saveSettings;
        _palette      = PaletteRegistry.Resolve(_settings.Ui.Palette);
        _leftViewMode  = ResolveViewMode(_settings.Panels.LeftViewMode);
        _rightViewMode = ResolveViewMode(_settings.Panels.RightViewMode);

        string cwd        = Directory.GetCurrentDirectory();
        string leftStart  = ResolveStartDir(_settings.Panels.LeftStartDirectory,  cwd);
        string rightStart = ResolveStartDir(_settings.Panels.RightStartDirectory, cwd);
        var sortMode = ResolveSortMode(_settings.Panels.DefaultSortMode);

        _left  = new FilePanelState { CurrentDirectory = leftStart,  SortMode = sortMode };
        _right = new FilePanelState { CurrentDirectory = rightStart, SortMode = sortMode };

        _ctrl.LoadDirectory(_left,  leftStart);
        _ctrl.LoadDirectory(_right, rightStart);
    }

    private static string ResolveStartDir(string? configured, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;
        return fallback;
    }

    private static SortMode ResolveSortMode(string? configured) =>
        Enum.TryParse<SortMode>(configured, ignoreCase: true, out var mode)
            ? mode
            : SortMode.Name;

    private static PanelViewMode ResolveViewMode(string? configured) =>
        Enum.TryParse<PanelViewMode>(configured, ignoreCase: true, out var mode)
            ? mode
            : PanelViewMode.Full;

    public void Run()
    {
        try
        {
            // Capture what was in the terminal before we draw anything.
            // This becomes the initial underlay shown by Ctrl+O.
            CaptureUnderlay();

            Render();

            while (_running)
            {
                var key = _screen.ReadKey();
                bool shouldRender = IsResizeEvent(key) || HandleKey(key);
                if (!shouldRender && HasConsoleSizeChanged())
                    shouldRender = true;
                if (_running && _panelsVisible && shouldRender)
                    Render();
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
        _screen.SetRenderingOutputMode(true);
        using var frame = _screen.BeginFrame();
        _screen.SetCursorVisible(false);

        var size   = _screen.GetSize();
        _lastRenderSize = size;
        int panelH = size.Height - 2;
        int leftW  = size.Width / 2;
        int rightW = size.Width - leftW;

        var panelRenderer = new PanelRenderer(_screen, _palette);

        if (_quickView)
        {
            if (_active == PanelSide.Left)
            {
                panelRenderer.Render(new Rect(0,     0, leftW,  panelH), _left, true, PanelViewMode.Full);
                new QuickViewRenderer(_screen, _palette).Render(
                    new Rect(leftW, 0, rightW, panelH),
                    _ctrl.CurrentItem(_left));
            }
            else
            {
                new QuickViewRenderer(_screen, _palette).Render(
                    new Rect(0,     0, leftW,  panelH),
                    _ctrl.CurrentItem(_right));
                panelRenderer.Render(new Rect(leftW, 0, rightW, panelH), _right, true, PanelViewMode.Full);
            }
        }
        else
        {
            panelRenderer.Render(new Rect(0,     0, leftW,  panelH), _left,  _active == PanelSide.Left,  _leftViewMode);
            panelRenderer.Render(new Rect(leftW, 0, rightW, panelH), _right, _active == PanelSide.Right, _rightViewMode);
        }

        RenderClock(size);

        var cmdRenderer = new CommandLineRenderer(_screen, _palette);
        cmdRenderer.Render(panelH, size.Width, ActiveState.CurrentDirectory, _cmdLine);

        new StatusBarRenderer(_screen, _palette).Render(size.Height - 1, size.Width);

        PositionCommandCursor(cmdRenderer, size, panelH);
    }

    private void RenderClock(ConsoleSize size)
    {
        string text = DateTime.Now.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length > size.Width)
            return;

        var style = new CellStyle(_palette.PanelTitleActiveFg, _palette.PanelBackground);
        _screen.Write(size.Width - text.Length, 0, text, style);
    }

    private bool HasConsoleSizeChanged()
    {
        if (!_lastRenderSize.HasValue)
            return false;

        var size = _screen.GetSize();
        return size.Width != _lastRenderSize.Value.Width ||
               size.Height != _lastRenderSize.Value.Height;
    }

    private static bool IsResizeEvent(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.NoName &&
        key.KeyChar == '\0' &&
        key.Modifiers == 0;

    private static bool IsPlainControlKey(ConsoleKeyInfo key, ConsoleKey consoleKey, char controlChar)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt     = (key.Modifiers & ConsoleModifiers.Alt)     != 0;
        bool hasShift   = (key.Modifiers & ConsoleModifiers.Shift)   != 0;

        return !hasAlt && !hasShift &&
               ((hasControl && key.Key == consoleKey) ||
                key.KeyChar == controlChar);
    }

    private void PositionCommandCursor(CommandLineRenderer cmdRenderer, ConsoleSize size, int row)
    {
        int curX = cmdRenderer.GetCursorX(size.Width, ActiveState.CurrentDirectory, _cmdLine);
        if (curX >= 0 && curX < size.Width)
        {
            _screen.SetCursorPosition(curX, row);
            _screen.SetCursorVisible(true);
        }
    }

    // ── Ctrl+O ────────────────────────────────────────────────────────────────

    /// <summary>Captures the full visible screen as the underlay snapshot.</summary>
    private void CaptureUnderlay()
    {
        var size = _screen.GetSize();
        _underlay = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
    }

    /// <summary>
    /// Toggles panel visibility.
    /// Hide: restores the last captured underlay so the user sees shell output.
    /// Show: Render() will be called by the main loop.
    /// </summary>
    private bool TogglePanels()
    {
        _panelsVisible = !_panelsVisible;

        if (!_panelsVisible)
        {
            _screen.SetCursorVisible(true);
            if (_underlay is not null && UnderlayMatchesCurrentSize())
                _screen.Restore(_underlay);
            else
                _screen.ClearScreen();
            return false;
        }

        return true;
    }

    private bool UnderlayMatchesCurrentSize()
    {
        if (_underlay is null)
            return false;

        var size = _screen.GetSize();
        return _underlay.Region.X == 0 &&
               _underlay.Region.Y == 0 &&
               _underlay.Region.Width == size.Width &&
               _underlay.Region.Height == size.Height;
    }

    // ── key handling ──────────────────────────────────────────────────────────

    private FilePanelState ActiveState => _active == PanelSide.Left ? _left : _right;

    private PanelViewMode ActiveViewMode =>
        _active == PanelSide.Left ? _leftViewMode : _rightViewMode;

    private int VisibleRows()
    {
        if (_quickView)
            return VisibleRows(PanelViewMode.Full);
        return VisibleRows(ActiveViewMode);
    }

    private int VisibleRows(PanelSide side)
    {
        if (_quickView && side == _active)
            return VisibleRows(PanelViewMode.Full);

        var mode = side == PanelSide.Left ? _leftViewMode : _rightViewMode;
        return VisibleRows(mode);
    }

    private int VisibleRows(PanelViewMode mode)
    {
        var size   = _screen.GetSize();
        int panelH = size.Height - 2;
        var bounds = new Rect(0, 0, 0, panelH);
        return mode == PanelViewMode.BriefTwoColumns
            ? BriefTwoColumnsPanelRenderer.VisibleRows(bounds)
            : PanelRenderer.VisibleRows(bounds);
    }

    private bool HandleKey(ConsoleKeyInfo key)
    {
        // Ctrl+O: toggle panels — check before printable-char routing
        if (IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return TogglePanels();

        if (!_panelsVisible)
            return false;

        // Ctrl+S: settings dialog
        if (IsPlainControlKey(key, ConsoleKey.S, '\u0013'))
        {
            HandleSettings();
            return true;
        }

        // Alt+1 / Alt+2: view mode for active panel
        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            { SetActiveViewMode(PanelViewMode.Full);            return true; }
            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            { SetActiveViewMode(PanelViewMode.BriefTwoColumns); return true; }
        }

        // Ctrl+Q: toggle quick view
        if (IsPlainControlKey(key, ConsoleKey.Q, '\u0011'))
        {
            _quickView = !_quickView;
            return true;
        }

        if (IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            _ctrl.ToggleSelectAll(ActiveState);
            return true;
        }

        // Ctrl+F3/F4/F5/F6 — sort; Ctrl+A — select all; Ctrl+* — invert selection
        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        if (isControlShortcut)
        {
            int vr0 = VisibleRows();
            switch (key.Key)
            {
                case ConsoleKey.F3: _ctrl.SetSortMode(ActiveState, SortMode.Name,          vr0); return true;
                case ConsoleKey.F4: _ctrl.SetSortMode(ActiveState, SortMode.Extension,     vr0); return true;
                case ConsoleKey.F5: _ctrl.SetSortMode(ActiveState, SortMode.LastWriteTime, vr0); return true;
                case ConsoleKey.F6: _ctrl.SetSortMode(ActiveState, SortMode.Size,          vr0); return true;
                case ConsoleKey.A:  _ctrl.ToggleSelectAll(ActiveState);                          return true;
                case ConsoleKey.Multiply:
                    _ctrl.InvertSelection(ActiveState);
                    return true;
                case ConsoleKey.D8 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                    _ctrl.InvertSelection(ActiveState);
                    return true;
            }
        }

        // Alt+F7 — search files (must come before plain F7 in switch)
        if (key.Key == ConsoleKey.F7 && (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            HandleSearchFiles();
            return true;
        }

        // Alt+F11 — file history
        if (key.Key == ConsoleKey.F11 && (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            HandleFileHistory();
            return true;
        }

        // Alt+F12 — directory history
        if (key.Key == ConsoleKey.F12 && (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            HandleDirectoryHistory();
            return true;
        }

        // Alt+F8 — command history (must come before F8 delete case in switch below)
        if (key.Key == ConsoleKey.F8 && (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            HandleCommandHistory();
            return true;
        }

        // Printable characters always go to the command line
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _cmdLine.Insert(key.KeyChar);
            return true;
        }

        int vr = VisibleRows();

        switch (key.Key)
        {
            // ── Command line editing ──────────────────────────────────────────
            case ConsoleKey.LeftArrow:
                _cmdLine.MoveCursor(-1);
                return true;

            case ConsoleKey.RightArrow:
                _cmdLine.MoveCursor(+1);
                return true;

            case ConsoleKey.Home:
                if (_cmdLine.HasText) _cmdLine.MoveToStart();
                else _ctrl.MoveToFirst(ActiveState);
                return true;

            case ConsoleKey.End:
                if (_cmdLine.HasText) _cmdLine.MoveToEnd();
                else _ctrl.MoveToLast(ActiveState, vr);
                return true;

            case ConsoleKey.Delete:
                _cmdLine.DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                bool hadCommandText = _cmdLine.HasText;
                if (hadCommandText) _cmdLine.DeleteBack();
                else TryGoUp();
                return true;

            case ConsoleKey.Escape:
                _cmdLine.Clear();
                return true;

            // ── Execution ─────────────────────────────────────────────────────
            case ConsoleKey.Enter:
                if (_cmdLine.HasText) ExecuteCommand(_cmdLine.Text);
                else TryEnterDirectory();
                return true;

            // ── Selection ────────────────────────────────────────────────────
            case ConsoleKey.Insert:
                _ctrl.ToggleSelection(ActiveState, vr);
                return true;

            // ── Panel navigation ──────────────────────────────────────────────
            case ConsoleKey.Tab:
                _active = _active == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
                return true;

            case ConsoleKey.UpArrow:
                _ctrl.MoveCursor(ActiveState, -1, vr);
                return true;

            case ConsoleKey.DownArrow:
                _ctrl.MoveCursor(ActiveState, +1, vr);
                return true;

            case ConsoleKey.PageUp:
                _ctrl.MoveCursor(ActiveState, -vr, vr);
                return true;

            case ConsoleKey.PageDown:
                _ctrl.MoveCursor(ActiveState, +vr, vr);
                return true;

            // ── Help ─────────────────────────────────────────────────────────
            case ConsoleKey.F1:
                new HelpViewer(_screen).Show();
                return true;

            // ── User menu ────────────────────────────────────────────────────
            case ConsoleKey.F2:
                HandleUserMenu();
                return true;

            // ── File operations ───────────────────────────────────────────────
            case ConsoleKey.F3:
                HandleViewFile();
                return true;

            case ConsoleKey.F4:
                HandleEditFile();
                return true;

            case ConsoleKey.F5:
                HandleCopy();
                return true;

            case ConsoleKey.F6:
                HandleMove();
                return true;

            case ConsoleKey.F7:
                HandleCreateFolder();
                return true;

            case ConsoleKey.F8:
                HandleDelete();
                return true;

            case ConsoleKey.F9:
                HandleSettings();
                return true;

            case ConsoleKey.F10:
                _running = false;
                return false;
        }

        return false;
    }

    // ── F4 — edit file ────────────────────────────────────────────────────────

    private void HandleEditFile()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || item.IsParentDirectory || item.IsDirectory) return;
        _history.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileEditor(_screen).Show(item.FullPath);
        SafeRefresh(ActiveState, VisibleRows());
    }

    // ── F3 — view file ────────────────────────────────────────────────────────

    private void HandleViewFile()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || item.IsParentDirectory || item.IsDirectory) return;
        _history.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileViewer(_screen).Show(item.FullPath);
    }

    // ── Alt+F11 — file history ────────────────────────────────────────────────

    private void HandleFileHistory()
    {
        string? path = new FileHistoryDialog(_screen).Show(_history.GetFileHistory());
        if (path is null) return;

        if (!File.Exists(path))
        {
            new MessageDialog(_screen).Show("File History", $"File not found: {path}");
            return;
        }

        var choice = new OpenFileDialog(_screen).Show(Path.GetFileName(path));
        switch (choice)
        {
            case OpenFileChoice.View:
                _history.AddFile(new FileHistoryItem { Path = path });
                new FileViewer(_screen).Show(path);
                break;
            case OpenFileChoice.Edit:
                _history.AddFile(new FileHistoryItem { Path = path });
                new FileEditor(_screen).Show(path);
                SafeRefresh(ActiveState, VisibleRows());
                break;
        }
    }

    // ── F2 — user menu ────────────────────────────────────────────────────────

    private void HandleUserMenu()
    {
        if (_userMenu.Items.Count == 0)
        {
            new MessageDialog(_screen).Show(
                "User Menu", "User menu is empty.\nEdit user-menu.json to add commands.");
            return;
        }

        string? command = new UserMenuDialog(_screen).Show(_userMenu.Items);
        if (command is null) return;

        var item = _ctrl.CurrentItem(ActiveState);
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;

        IReadOnlyList<string> selected = ActiveState.SelectedPaths.Count > 0
            ? [.. ActiveState.SelectedPaths]
            : [];

        string otherDir  = (_active == PanelSide.Left ? _right : _left).CurrentDirectory;
        string expanded  = PlaceholderExpander.Expand(
            command, currentFile, selected,
            ActiveState.CurrentDirectory, otherDir);

        ExecuteCommand(expanded);
    }

    // ── Alt+F7 — search files ─────────────────────────────────────────────────

    private void HandleSearchFiles()
    {
        string? mask = new InputDialog(_screen).Show(
            "Search Files", "File mask (e.g. *.cs):", initialText: "*");
        if (mask is null) return;

        string rootDir = ActiveState.CurrentDirectory;
        var results = new SearchProgressDialog(_screen).Show(rootDir, mask);

        if (results.Count == 0)
        {
            new MessageDialog(_screen).Show("Search", "No files found.");
            return;
        }

        string? selected = new SearchResultsDialog(_screen).Show(results);
        if (selected is null) return;

        string? parentDir = Path.GetDirectoryName(selected);
        string  fileName  = Path.GetFileName(selected);
        if (parentDir is null) return;

        try
        {
            _ctrl.LoadDirectory(ActiveState, parentDir);
            _ctrl.SetCursorByName(ActiveState, fileName, VisibleRows());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen).Show("Search", ex.Message);
        }
    }

    // ── F5 — copy ─────────────────────────────────────────────────────────────

    private IReadOnlyList<string> GetOperationSources()
    {
        if (ActiveState.SelectedPaths.Count > 0)
            return [.. ActiveState.SelectedPaths];

        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || item.IsParentDirectory) return [];
        return [item.FullPath];
    }

    private void HandleCopy()
    {
        var sources = GetOperationSources();
        if (sources.Count == 0) return;

        var otherDir = (_active == PanelSide.Left ? _right : _left).CurrentDirectory;
        string label = $"Copy {sources.Count} item{(sources.Count == 1 ? "" : "s")} to:";

        string? destDir = new InputDialog(_screen).Show("Copy", label, initialText: otherDir);
        if (destDir is null) return;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            var progress  = new ProgressDialog(_screen, destDir);
            var conflicts = new ConflictDialog(_screen);

            _fileOps.CopyAsync(
                sources,
                destDir,
                onProgress: fileName => progress.Update(fileName),
                onConflict: destPath => conflicts.Show(destPath))
                .GetAwaiter().GetResult();

            ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _screen.Restore(saved);
            new MessageDialog(_screen).Show("Copy Error", ex.Message);
        }
        finally
        {
            _screen.Restore(saved);
        }

        RefreshPanels();
    }

    // ── F6 — move / rename ────────────────────────────────────────────────────

    private void HandleMove()
    {
        var sources = GetOperationSources();
        if (sources.Count == 0) return;

        // Single item: pre-fill with its name (user edits to rename or enters a path to move).
        // Multiple items: pre-fill with opposite panel dir (move destination).
        string preFill = sources.Count == 1
            ? Path.GetFileName(sources[0])
            : (_active == PanelSide.Left ? _right : _left).CurrentDirectory;

        string label = sources.Count == 1 ? "Move / Rename to:" : $"Move {sources.Count} items to:";

        string? dest = new InputDialog(_screen).Show("Move", label, initialText: preFill);
        if (dest is null) return;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            var conflicts = new ConflictDialog(_screen);
            _fileOps.MoveAsync(
                sources, dest,
                onConflict: destPath => conflicts.Show(destPath))
                .GetAwaiter().GetResult();

            ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _screen.Restore(saved);
            new MessageDialog(_screen).Show("Move Error", ex.Message);
        }
        finally
        {
            _screen.Restore(saved);
        }

        RefreshPanels();
    }

    // ── F8 — delete ───────────────────────────────────────────────────────────

    private void HandleDelete()
    {
        var sources = GetOperationSources();
        if (sources.Count == 0) return;

        string prompt = sources.Count == 1
            ? $"Delete \"{Path.GetFileName(sources[0])}\"?"
            : $"Delete {sources.Count} items?";

        if (_settings.Ui.ConfirmDelete && !new ConfirmDialog(_screen).Show("Delete", prompt))
            return;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            _fileOps.DeleteAsync(sources).GetAwaiter().GetResult();
            ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _screen.Restore(saved);
            new MessageDialog(_screen).Show("Delete Error", ex.Message);
        }
        finally
        {
            _screen.Restore(saved);
        }

        RefreshPanels();
    }

    // ── Alt+F12 — directory history ───────────────────────────────────────────

    private void HandleDirectoryHistory()
    {
        string? path = new DirectoryHistoryDialog(_screen).Show(_history.GetDirectoryHistory());
        if (path is null) return;

        if (!Directory.Exists(path))
        {
            new MessageDialog(_screen).Show("Directory History", $"Directory not found: {path}");
            return;
        }

        try { _ctrl.LoadDirectory(ActiveState, path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen).Show("Directory History", ex.Message);
        }
    }

    // ── Alt+F8 — command history ──────────────────────────────────────────────

    private void HandleCommandHistory()
    {
        string? cmd = new HistoryDialog(_screen).Show(_history.GetCommandHistory());
        if (cmd is not null)
            _cmdLine.SetText(cmd);
    }

    // ── F7 — create folder ────────────────────────────────────────────────────

    private void HandleCreateFolder()
    {
        var dialog = new InputDialog(_screen);
        string? name = dialog.Show("Make Folder", "Folder name:", validate: attempt =>
        {
            if (attempt.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "Invalid characters in folder name.";

            string newPath = Path.Combine(ActiveState.CurrentDirectory, attempt);
            try   { _fileOps.CreateDirectory(newPath); return null; }
            catch (IOException ex)              { return ex.Message; }
            catch (UnauthorizedAccessException) { return "Access denied."; }
            catch (ArgumentException ex)        { return ex.Message; }
        });

        if (name is null) return;

        int vr = VisibleRows();
        SafeRefresh(ActiveState, vr);
        _ctrl.SetCursorByName(ActiveState, name, vr);
    }

    // ── Ctrl+S — settings ─────────────────────────────────────────────────────

    private void HandleSettings()
    {
        var result = new SettingsDialog(_screen).Show(
            _leftViewMode, _rightViewMode, _settings.Ui.Palette);

        if (result is null) return;

        _leftViewMode              = result.LeftViewMode;
        _rightViewMode             = result.RightViewMode;
        _settings.Panels.LeftViewMode  = result.LeftViewMode.ToString();
        _settings.Panels.RightViewMode = result.RightViewMode.ToString();
        _settings.Ui.Palette           = result.PaletteName;
        _palette = PaletteRegistry.Resolve(result.PaletteName);
        _ctrl.MoveCursor(_left,  0, VisibleRows(PanelSide.Left));
        _ctrl.MoveCursor(_right, 0, VisibleRows(PanelSide.Right));
        _saveSettings?.Invoke();
    }

    // ── Alt+1/Alt+2 — view mode ────────────────────────────────────────────────

    private void SetActiveViewMode(PanelViewMode mode)
    {
        if (_active == PanelSide.Left)
        {
            _leftViewMode = mode;
            _settings.Panels.LeftViewMode = mode.ToString();
        }
        else
        {
            _rightViewMode = mode;
            _settings.Panels.RightViewMode = mode.ToString();
        }
        _ctrl.MoveCursor(ActiveState, 0, VisibleRows());
        _saveSettings?.Invoke();
    }

    // ── shell execution ───────────────────────────────────────────────────────

    private void ExecuteCommand(string command)
    {
        string workDir = ActiveState.CurrentDirectory;
        _cmdLine.Clear();

        // Ensure panels are visible when we return (they may have been hidden)
        _panelsVisible = true;

        ShowShellUnderlayForCommand();
        PrintShellPrompt(workDir, command);

        _shell.Execute(command, workDir);

        _history.AddCommand(new CommandHistoryItem
        {
            Command          = command,
            WorkingDirectory = workDir,
        });

        // Capture shell output NOW, before Render() paints panels over it.
        // This snapshot is what Ctrl+O will restore.
        CaptureUnderlay();

        RefreshPanels();
        // Render() is called by the main loop since _panelsVisible == true
    }

    private void ShowShellUnderlayForCommand()
    {
        _screen.SetRenderingOutputMode(false);

        if (_underlay is not null && UnderlayMatchesCurrentSize())
            _screen.Restore(_underlay);
        else
            _screen.ClearScreen();

        SysConsole.ResetColor();
        SysConsole.CursorVisible = true;

        var size = _screen.GetSize();
        SysConsole.SetCursorPosition(0, SysConsole.WindowTop + size.Height - 1);
        SysConsole.WriteLine();
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
        try
        {
            _ctrl.LoadDirectory(ActiveState, item.FullPath);
            _history.AddDirectory(new DirectoryHistoryItem { Path = ActiveState.CurrentDirectory });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen).Show("Navigation", ex.Message);
        }
    }

    private void TryGoUp()
    {
        try
        {
            _ctrl.GoToParent(ActiveState, VisibleRows());
            _history.AddDirectory(new DirectoryHistoryItem { Path = ActiveState.CurrentDirectory });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen).Show("Navigation", ex.Message);
        }
    }

    private void RefreshPanels()
    {
        SafeRefresh(_left,  VisibleRows(PanelSide.Left));
        SafeRefresh(_right, VisibleRows(PanelSide.Right));
    }

    private void SafeRefresh(FilePanelState state, int visibleRows)
    {
        if (!Directory.Exists(state.CurrentDirectory)) return;
        try { _ctrl.RefreshDirectory(state, visibleRows); }
        catch { }
    }

    // ── alias to avoid namespace conflict with CSharpFar.Console ─────────────
    private static class SysConsole
    {
        public static int  WindowTop    { get => global::System.Console.WindowTop;    }
        public static bool CursorVisible { set => global::System.Console.CursorVisible = value; }
        public static ConsoleColor ForegroundColor { set => global::System.Console.ForegroundColor = value; }
        public static void ResetColor() => global::System.Console.ResetColor();
        public static void Write(string s)    => global::System.Console.Write(s);
        public static void WriteLine(string s) => global::System.Console.WriteLine(s);
        public static void WriteLine()         => global::System.Console.WriteLine();
        public static void SetCursorPosition(int x, int y) =>
            global::System.Console.SetCursorPosition(x, y);
    }
}
