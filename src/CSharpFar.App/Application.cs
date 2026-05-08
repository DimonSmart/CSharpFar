using CSharpFar.App.Dialogs;
using CSharpFar.App.HitTesting;
using CSharpFar.App.Menu;
using CSharpFar.App.Rendering;
using CSharpFar.App.Editor;
using CSharpFar.App.Search;
using CSharpFar.App.UserMenu;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App;

public sealed class Application
{
    private readonly ScreenRenderer _screen;
    private readonly IFileSystemService _fs;
    private readonly PanelController _ctrl;
    private readonly IShellService _shell;
    private readonly IFileOperationService _fileOps;
    private readonly IHistoryStore _history;
    private readonly AppSettingsAlias _settings;
    private readonly UserMenuStore _userMenu;
    private readonly Action? _saveSettings;
    private readonly IVolumeService? _volumeService;

    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private readonly CommandLineState _cmdLine = new();

    private PanelSide     _active        = PanelSide.Left;
    private bool          _running       = true;
    private bool          _panelsVisible = true;
    private bool          _quickView     = false;
    private ConsoleSize?  _lastRenderSize;
    private ScreenSnapshot? _underlay;          // last known screen content before panels
    private ConsolePalette          _palette;
    private PanelViewMode           _leftViewMode;
    private PanelViewMode           _rightViewMode;
    private IFileHighlightService?          _highlightService;
    private Rect                            _leftBounds;
    private Rect                            _rightBounds;
    private IFileSystemChangeWatcher?       _watcher;
    private IFileSystemLocationService?     _locationService;
    private CancellationTokenSource         _refreshCts = new();
    private readonly MenuState              _menuState = new();
    private readonly DefaultMenuDefinitionProvider _menuProvider = new();
    private readonly MenuLayoutService      _menuLayoutService = new();
    private readonly MenuBarRenderer        _menuBarRenderer = new();
    private readonly DropdownMenuRenderer   _dropdownMenuRenderer = new();
    private readonly TopMenuController      _menuController;

    public Application(
        ScreenRenderer         screen,
        IFileSystemService     fs,
        IShellService          shell,
        IFileOperationService  fileOps,
        IHistoryStore?         history          = null,
        AppSettingsAlias?      settings         = null,
        UserMenuStore?         userMenu         = null,
        Action?                saveSettings     = null,
        IVolumeService?              volumeService     = null,
        IVolumeInfoService?          volumeInfoService  = null,
        IFileSystemChangeWatcher?    changeWatcher     = null,
        IFileSystemLocationService?  locationService   = null,
        IVolumeMountPointService?    mountPointService = null)
    {
        _screen       = screen;
        _fs           = fs;
        var sortSvc   = new PanelSortService();
        var viewBuilder = new PanelViewBuilder(fs, sortSvc, volumeInfoService, mountPoints: mountPointService);
        _ctrl         = new PanelController(viewBuilder);
        _shell        = shell;
        _fileOps      = fileOps;
        _history      = history      ?? new InMemoryHistoryStore();
        _settings     = settings     ?? new AppSettingsAlias();
        _userMenu     = userMenu     ?? new UserMenuStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CSharpFar"));
        _saveSettings     = saveSettings;
        _volumeService    = volumeService;
        _watcher          = changeWatcher;
        _locationService  = locationService;
        _menuController   = new TopMenuController(_menuState, ExecuteMenuCommand);

        if (_watcher != null)
            _watcher.Changed += OnFileSystemChanged;
        _palette          = PaletteRegistry.Resolve(_settings.Ui.Palette);
        _leftViewMode     = ResolveViewMode(_settings.Panels.LeftViewMode);
        _rightViewMode    = ResolveViewMode(_settings.Panels.RightViewMode);
        _highlightService = CreateHighlightService(_settings);

        string cwd        = Directory.GetCurrentDirectory();
        string leftStart  = ResolveStartDir(_settings.Panels.LeftStartDirectory,  cwd);
        string rightStart = ResolveStartDir(_settings.Panels.RightStartDirectory, cwd);
        var sortMode = ResolveSortMode(_settings.Panels.DefaultSortMode);

        _left  = new FilePanelState { CurrentDirectory = leftStart,  SortMode = sortMode };
        _right = new FilePanelState { CurrentDirectory = rightStart, SortMode = sortMode };

        var opts = _settings.Panels.Options;
        _ctrl.LoadDirectory(_left,  leftStart,  opts);
        _ctrl.LoadDirectory(_right, rightStart, opts);
    }

    private AppSettingsAlias.PanelOptionsSettings PanelOptions => _settings.Panels.Options;

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

            StartWatching(_left,  PanelSide.Left);
            StartWatching(_right, PanelSide.Right);

            Render();

            while (_running)
            {
                ConsoleInputEvent evt;
                try
                {
                    evt = _screen.ReadInput(_refreshCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Woken by auto-refresh — reset CTS and refresh affected panels.
                    ResetRefreshCts();
                    ProcessPendingRefreshes();
                    if (_running && _panelsVisible)
                        Render();
                    continue;
                }

                bool isResize     = false;
                bool shouldRender = false;

                switch (evt)
                {
                    case ConsoleResizeInputEvent:
                        isResize      = true;
                        shouldRender  = true;
                        break;
                    case KeyConsoleInputEvent { Key: var key }:
                        shouldRender = HandleKey(key);
                        break;
                    case MouseConsoleInputEvent mouseEvt:
                        shouldRender = HandleMouse(mouseEvt);
                        break;
                }

                if (!shouldRender && HasConsoleSizeChanged())
                {
                    isResize     = true;
                    shouldRender = true;
                }

                if (_running && shouldRender)
                {
                    if (isResize)
                        WaitForStableConsoleSize();

                    if (_panelsVisible)
                        Render();
                    else
                    {
                        if (isResize)
                            RestoreUnderlayForHiddenScreen();
                        RenderCommandLineOnly();
                    }
                }
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
        int panelH = PanelHeight(size);
        int leftW  = size.Width / 2;
        int rightW = size.Width - leftW;

        var panelRenderer = new PanelRenderer(_screen, _palette, _highlightService, PanelOptions);

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
            var leftBounds  = new Rect(0, 0, leftW, panelH);
            var rightBounds = GetRightPanelBounds(size.Width, leftW, rightW, panelH);
            _leftBounds  = leftBounds;
            _rightBounds = rightBounds;

            panelRenderer.Render(leftBounds,  _left,  _active == PanelSide.Left,  _leftViewMode);
            panelRenderer.Render(rightBounds, _right, _active == PanelSide.Right, _rightViewMode);
            RenderPanelFrameJoin(leftBounds, rightBounds);
        }

        RenderClock(size);

        var cmdRenderer = new CommandLineRenderer(_screen, _palette);
        cmdRenderer.Render(panelH, size.Width, ActiveState.CurrentDirectory, _cmdLine);

        new StatusBarRenderer(_screen, _palette).Render(size.Height - 1, size.Width);

        RenderMenuOverlay(size);

        if (_menuState.OpenState == MenuOpenState.Closed)
            PositionCommandCursor(cmdRenderer, size, panelH);
        else
            _screen.SetCursorVisible(false);
    }

    private static Rect GetRightPanelBounds(int screenWidth, int leftWidth, int rightWidth, int panelHeight)
    {
        if (screenWidth >= 4 && leftWidth >= 2 && rightWidth >= 2)
        {
            int sharedBorderX = leftWidth - 1;
            return new Rect(sharedBorderX, 0, screenWidth - sharedBorderX, panelHeight);
        }

        return new Rect(leftWidth, 0, rightWidth, panelHeight);
    }

    private void RenderPanelFrameJoin(Rect leftBounds, Rect rightBounds)
    {
        if (leftBounds.Right - 1 != rightBounds.X || leftBounds.Height < 2)
            return;

        int sharedX = rightBounds.X;
        var style = new CellStyle(_palette.PanelBorderActiveFg, _palette.PanelBackground);

        _screen.WriteChar(sharedX, leftBounds.Y, '╦', style);
        _screen.WriteChar(sharedX, leftBounds.Bottom - 1, '╩', style);

        int separatorY = PanelStatusRenderer.SeparatorRow(leftBounds, PanelOptions);
        if (separatorY > leftBounds.Y && separatorY < leftBounds.Bottom - 1)
            _screen.WriteChar(sharedX, separatorY, '╫', style);
    }

    private void RenderCommandLineOnly()
    {
        _screen.SetRenderingOutputMode(true);
        using var frame = _screen.BeginFrame();

        var size = _screen.GetSize();
        _lastRenderSize = size;

        int row = CommandLineRow(size);
        var cmdRenderer = new CommandLineRenderer(_screen, _palette);
        cmdRenderer.Render(row, size.Width, ActiveState.CurrentDirectory, _cmdLine);
        PositionCommandCursor(cmdRenderer, size, row);
    }

    private void RenderClock(ConsoleSize size)
    {
        string text = DateTime.Now.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length > size.Width)
            return;

        var style = new CellStyle(_palette.PanelPathActiveFg, _palette.PanelPathActiveBg);
        _screen.Write(size.Width - text.Length, 0, text, style);
    }

    private void RenderMenuOverlay(ConsoleSize size)
    {
        if (_menuState.OpenState == MenuOpenState.Closed)
            return;

        var bounds = new Rect(0, 0, size.Width, size.Height);
        var definition = BuildMenuDefinition();
        var layout = _menuLayoutService.CalculateLayout(bounds, definition, _menuState);
        var options = BuildMenuRenderOptions();

        _menuBarRenderer.Render(_screen, bounds, definition, _menuState, layout, options);
        _dropdownMenuRenderer.Render(_screen, definition, _menuState, layout, options);
    }

    private bool HasConsoleSizeChanged()
    {
        if (!_lastRenderSize.HasValue)
            return false;

        var size = _screen.GetSize();
        return size.Width != _lastRenderSize.Value.Width ||
               size.Height != _lastRenderSize.Value.Height;
    }

    private static int PanelHeight(ConsoleSize size) => Math.Max(0, size.Height - 2);

    private static int CommandLineRow(ConsoleSize size) => Math.Max(0, size.Height - 2);

    private void WaitForStableConsoleSize()
    {
        var previous = _screen.GetSize();
        for (int i = 0; i < 3; i++)
        {
            Thread.Sleep(25);
            var current = _screen.GetSize();
            if (current.Width == previous.Width && current.Height == previous.Height)
                return;
            previous = current;
        }
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

    private static bool IsPlainFunctionKey(ConsoleKeyInfo key, ConsoleKey consoleKey) =>
        key.Key == consoleKey && key.Modifiers == 0;

    private static bool IsTopMenuActivationMouse(MouseConsoleInputEvent evt) =>
        evt.Y == 0 &&
        evt.Button == MouseButton.Left &&
        (evt.Kind == MouseEventKind.Down || evt.Kind == MouseEventKind.Click);

    private MenuBarDefinition BuildMenuDefinition() =>
        _menuProvider.BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = _active,
            LeftPanel = _left,
            RightPanel = _right,
            LeftViewMode = _leftViewMode,
            RightViewMode = _rightViewMode,
            Settings = _settings,
            CanSaveSettings = _saveSettings is not null,
        });

    private MenuRenderOptions BuildMenuRenderOptions() =>
        new()
        {
            NormalStyle = new CellStyle(_palette.MenuNormalFg, _palette.MenuNormalBg),
            ActiveStyle = new CellStyle(_palette.MenuActiveFg, _palette.MenuActiveBg),
            DisabledStyle = new CellStyle(_palette.MenuDisabledFg, _palette.MenuDisabledBg),
            BorderStyle = new CellStyle(_palette.MenuBorderFg, _palette.MenuBorderBg),
            ShadowStyle = new CellStyle(_palette.MenuShadowFg, _palette.MenuShadowBg),
        };

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
            RestoreUnderlayForHiddenScreen();
            RenderCommandLineOnly();
            return false;
        }

        return true;
    }

    private void RestoreUnderlayForHiddenScreen()
    {
        _screen.SetRenderingOutputMode(false);
        RestoreOrClearUnderlay();
    }

    private void RestoreOrClearUnderlay()
    {
        if (_underlay is not null && UnderlayMatchesCurrentSize())
            _screen.Restore(_underlay);
        else
            _screen.ClearScreen();
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
        int panelH = PanelHeight(size);
        var bounds = new Rect(0, 0, 0, panelH);
        return mode == PanelViewMode.BriefTwoColumns
            ? BriefTwoColumnsPanelRenderer.VisibleRows(bounds, PanelOptions)
            : PanelRenderer.VisibleRows(bounds, PanelOptions);
    }

    private (int RowsPerColumn, int ColumnCount, int VisibleRows) ActiveColumnGeometry()
    {
        var mode = _quickView ? PanelViewMode.Full : ActiveViewMode;
        int visibleRows = VisibleRows(mode);

        if (mode != PanelViewMode.BriefTwoColumns)
            return (Math.Max(1, visibleRows), 1, visibleRows);

        var size = _screen.GetSize();
        var bounds = new Rect(0, 0, 0, PanelHeight(size));
        int rowsPerColumn = BriefTwoColumnsPanelRenderer.RowsPerColumn(bounds, PanelOptions);
        return (rowsPerColumn, 2, visibleRows);
    }

    // ── mouse handling ────────────────────────────────────────────────────────

    private bool HandleMouse(MouseConsoleInputEvent evt)
    {
        if (!_panelsVisible)
            return false;

        if (_menuState.OpenState != MenuOpenState.Closed || IsTopMenuActivationMouse(evt))
        {
            var definition = BuildMenuDefinition();
            var size = _screen.GetSize();
            var layout = _menuLayoutService.CalculateLayout(
                new Rect(0, 0, size.Width, size.Height),
                definition,
                _menuState);
            return _menuController.HandleMouse(evt, definition, layout, _active);
        }

        if (_quickView) return false;

        // Identify which panel was hit
        bool inLeft  = _leftBounds.Contains(evt.X,  evt.Y);
        bool inRight = _rightBounds.Contains(evt.X, evt.Y);
        if (!inLeft && !inRight) return false;

        var side  = inLeft ? PanelSide.Left : PanelSide.Right;
        var state = inLeft ? _left : _right;
        var mode  = inLeft ? _leftViewMode : _rightViewMode;
        var bounds = inLeft ? _leftBounds : _rightBounds;
        int visRows = VisibleRows(side);

        // Mouse wheel: scroll the panel under cursor
        if (evt.Kind == MouseEventKind.Wheel)
        {
            _active = side;
            int delta = evt.Button == MouseButton.WheelUp ? -3 : 3;
            _ctrl.ScrollView(state, delta, visRows);
            return true;
        }

        // Right click: activate panel, move cursor, optionally toggle selection
        if (evt.Button == MouseButton.Right && evt.Kind == MouseEventKind.Down)
        {
            _active = side;
            int? itemIdx = PanelHitTester.HitTestItem(evt.X, evt.Y, bounds, state, mode, PanelOptions);
            if (itemIdx.HasValue)
            {
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);
                if (PanelOptions.RightClickSelectsFiles)
                {
                    var item = state.Items[itemIdx.Value];
                    if (PanelController.CanSelect(item, PanelOptions))
                        _ctrl.ToggleCurrentSelection(state, PanelOptions);
                }
            }
            return true;
        }

        // Left click: activate panel and move cursor
        if (evt.Button == MouseButton.Left && evt.Kind == MouseEventKind.Down)
        {
            _active = side;
            int? itemIdx = PanelHitTester.HitTestItem(evt.X, evt.Y, bounds, state, mode, PanelOptions);
            if (itemIdx.HasValue)
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);
            return true;
        }

        return false;
    }

    private bool HandleKey(ConsoleKeyInfo key)
    {
        if (_menuState.OpenState != MenuOpenState.Closed)
        {
            if (!_panelsVisible)
            {
                _menuController.Close();
                return true;
            }

            return _menuController.HandleKey(key, BuildMenuDefinition(), _active);
        }

        // Ctrl+O: toggle panels — check before printable-char routing
        if (IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return TogglePanels();

        if (!_panelsVisible)
            return HandleHiddenCommandLineKey(key);

        if (IsPlainFunctionKey(key, ConsoleKey.F9))
            return _menuController.HandleKey(key, BuildMenuDefinition(), _active);

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
            _ctrl.ToggleSelectAll(ActiveState, PanelOptions);
            return true;
        }

        if ((key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.LeftArrow)
            {
                _cmdLine.MoveCursor(-1);
                return true;
            }

            if (key.Key == ConsoleKey.RightArrow)
            {
                _cmdLine.MoveCursor(+1);
                return true;
            }
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
                case ConsoleKey.F3: _ctrl.SetSortMode(ActiveState, SortMode.Name,          vr0, PanelOptions); return true;
                case ConsoleKey.F4: _ctrl.SetSortMode(ActiveState, SortMode.Extension,     vr0, PanelOptions); return true;
                case ConsoleKey.F5: _ctrl.SetSortMode(ActiveState, SortMode.LastWriteTime, vr0, PanelOptions); return true;
                case ConsoleKey.F6: _ctrl.SetSortMode(ActiveState, SortMode.Size,          vr0, PanelOptions); return true;
                case ConsoleKey.A:  _ctrl.ToggleSelectAll(ActiveState, PanelOptions);                          return true;
                case ConsoleKey.Multiply:
                    _ctrl.InvertSelection(ActiveState, PanelOptions);
                    return true;
                case ConsoleKey.D8 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                    _ctrl.InvertSelection(ActiveState, PanelOptions);
                    return true;
            }
        }

        // Alt+F1 / Alt+F2 — drive/volume selection
        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.F1) { HandleDriveSelect(PanelSide.Left);  return true; }
            if (key.Key == ConsoleKey.F2) { HandleDriveSelect(PanelSide.Right); return true; }
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

        int vr = VisibleRows();

        switch (key.Key)
        {
            // ── Horizontal navigation / command line editing ──────────────────
            case ConsoleKey.LeftArrow:
                MovePanelColumn(-1);
                return true;

            case ConsoleKey.RightArrow:
                MovePanelColumn(+1);
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
                _ctrl.ToggleSelection(ActiveState, vr, PanelOptions);
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

            case ConsoleKey.F10:
                _running = false;
                return false;
        }

        // Printable characters always go to the command line. This must run
        // after special keys so malformed function-key chars cannot be inserted.
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _cmdLine.Insert(key.KeyChar);
            return true;
        }

        return false;
    }

    private bool HandleHiddenCommandLineKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _cmdLine.MoveCursor(-1);
                return true;

            case ConsoleKey.RightArrow:
                _cmdLine.MoveCursor(+1);
                return true;

            case ConsoleKey.Home:
                _cmdLine.MoveToStart();
                return true;

            case ConsoleKey.End:
                _cmdLine.MoveToEnd();
                return true;

            case ConsoleKey.Delete:
                _cmdLine.DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                _cmdLine.DeleteBack();
                return true;

            case ConsoleKey.Escape:
                _cmdLine.Clear();
                return true;

            case ConsoleKey.Enter:
                if (_cmdLine.HasText)
                    ExecuteCommand(_cmdLine.Text);
                return true;

            case ConsoleKey.F10:
                _running = false;
                return false;
        }

        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _cmdLine.Insert(key.KeyChar);
            return true;
        }

        return false;
    }

    private void MovePanelColumn(int direction)
    {
        var geometry = ActiveColumnGeometry();
        _ctrl.MoveCursorByColumn(
            ActiveState,
            direction,
            geometry.RowsPerColumn,
            geometry.ColumnCount,
            geometry.VisibleRows);
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
            _ctrl.LoadDirectory(ActiveState, parentDir, PanelOptions);
            _ctrl.SetCursorByName(ActiveState, fileName, VisibleRows());
            StartWatching(ActiveState, _active);
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

        try
        {
            _ctrl.LoadDirectory(ActiveState, path, PanelOptions);
            StartWatching(ActiveState, _active);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen).Show("Directory History", ex.Message);
        }
    }

    // ── Alt+F1 / Alt+F2 — drive selection ────────────────────────────────────

    private void HandleDriveSelect(PanelSide side)
    {
        var targetState = side == PanelSide.Left ? _left : _right;

        var volumes = _volumeService?.GetVolumes() ?? [];
        var items   = volumes
            .Select(v => new VolumeSelectionItem
            {
                Label    = v.DisplayName,
                Shortcut = v.Shortcut,
                Volume   = v,
                Action   = VolumeSelectionAction.OpenVolume,
            })
            .ToList();

        int initialCursor = FindInitialCursor(items, targetState.CurrentDirectory);

        var selected = new DriveDialog(_screen).Show(items, initialCursor);
        if (selected is null) return;

        var vol = selected.Volume!;

        try
        {
            _ctrl.LoadDirectory(targetState, vol.RootPath, PanelOptions);
            _history.AddDirectory(new DirectoryHistoryItem { Path = vol.RootPath });
            _quickView = false;
            _active    = side;
            StartWatching(targetState, side);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen).Show("Change drive", ex.Message);
        }
    }

    /// <summary>
    /// Returns the index of the item whose RootPath is the longest prefix of
    /// <paramref name="currentDirectory"/>. Falls back to 0 if there is no match.
    /// </summary>
    private static int FindInitialCursor(List<VolumeSelectionItem> items, string currentDirectory)
    {
        int bestIdx = 0;
        int bestLen = -1;

        for (int i = 0; i < items.Count; i++)
        {
            string? root = items[i].Volume?.RootPath;
            if (root is null) continue;

            if (currentDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                root.Length > bestLen)
            {
                bestLen = root.Length;
                bestIdx = i;
            }
        }

        return bestIdx;
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
            _leftViewMode, _rightViewMode,
            _settings.Ui.Palette,
            _settings.Panels.FileHighlighting.Enabled);

        if (result is null) return;

        _leftViewMode                              = result.LeftViewMode;
        _rightViewMode                             = result.RightViewMode;
        _settings.Panels.LeftViewMode              = result.LeftViewMode.ToString();
        _settings.Panels.RightViewMode             = result.RightViewMode.ToString();
        _settings.Ui.Palette                       = result.PaletteName;
        _settings.Panels.FileHighlighting.Enabled  = result.FileHighlightingEnabled;
        _palette          = PaletteRegistry.Resolve(result.PaletteName);
        _highlightService = CreateHighlightService(_settings);
        _ctrl.MoveCursor(_left,  0, VisibleRows(PanelSide.Left));
        _ctrl.MoveCursor(_right, 0, VisibleRows(PanelSide.Right));
        _saveSettings?.Invoke();
    }

    private MenuCommandResult ExecuteMenuCommand(MenuCommandRequest request)
    {
        switch (request.CommandId)
        {
            case MenuCommandIds.PanelSetViewMode:
                if (request.Args is not SetPanelViewModeArgs viewArgs)
                    return MenuCommandFailure("Missing panel view mode arguments.");
                SetPanelViewMode(viewArgs.PanelSide, viewArgs.ViewMode);
                return MenuCommandSuccess();

            case MenuCommandIds.PanelSetSortMode:
                if (request.Args is not SetPanelSortModeArgs sortArgs)
                    return MenuCommandFailure("Missing panel sort arguments.");
                _ctrl.SetSortMode(
                    GetPanelState(sortArgs.PanelSide),
                    sortArgs.SortMode,
                    VisibleRows(sortArgs.PanelSide),
                    PanelOptions);
                return MenuCommandSuccess();

            case MenuCommandIds.PanelToggleReverseSort:
                if (request.Args is not PanelCommandArgs reverseArgs)
                    return MenuCommandFailure("Missing panel arguments.");
                ToggleReverseSort(reverseArgs.PanelSide);
                return MenuCommandSuccess();

            case MenuCommandIds.PanelRefresh:
                if (request.Args is not PanelCommandArgs refreshArgs)
                    return MenuCommandFailure("Missing panel arguments.");
                SafeRefresh(GetPanelState(refreshArgs.PanelSide), VisibleRows(refreshArgs.PanelSide));
                return MenuCommandSuccess();

            case MenuCommandIds.SettingsOpenPanelSettings:
                HandleSettings();
                return MenuCommandSuccess();

            case MenuCommandIds.SettingsToggleShowHiddenAndSystemFiles:
                return ToggleSetting(() => PanelOptions.ShowHiddenAndSystemFiles = !PanelOptions.ShowHiddenAndSystemFiles);
            case MenuCommandIds.SettingsToggleHighlightFiles:
                return ToggleSetting(() =>
                {
                    _settings.Panels.FileHighlighting.Enabled = !_settings.Panels.FileHighlighting.Enabled;
                    _highlightService = CreateHighlightService(_settings);
                });
            case MenuCommandIds.SettingsToggleSelectFolders:
                return ToggleSetting(() => PanelOptions.SelectFolders = !PanelOptions.SelectFolders);
            case MenuCommandIds.SettingsToggleRightClickSelectsFiles:
                return ToggleSetting(() => PanelOptions.RightClickSelectsFiles = !PanelOptions.RightClickSelectsFiles);
            case MenuCommandIds.SettingsToggleSortFoldersByExtension:
                return ToggleSetting(() => PanelOptions.SortFoldersByExtension = !PanelOptions.SortFoldersByExtension);
            case MenuCommandIds.SettingsToggleShowStatusLine:
                return ToggleSetting(() => PanelOptions.ShowStatusLine = !PanelOptions.ShowStatusLine);
            case MenuCommandIds.SettingsToggleShowFilesTotalInformation:
                return ToggleSetting(() => PanelOptions.ShowFilesTotalInformation = !PanelOptions.ShowFilesTotalInformation);
            case MenuCommandIds.SettingsToggleShowFreeSize:
                return ToggleSetting(() => PanelOptions.ShowFreeSize = !PanelOptions.ShowFreeSize);
            case MenuCommandIds.SettingsToggleShowScrollbar:
                return ToggleSetting(() => PanelOptions.ShowScrollbar = !PanelOptions.ShowScrollbar);
            case MenuCommandIds.SettingsToggleShowSortModeLetter:
                return ToggleSetting(() => PanelOptions.ShowSortModeLetter = !PanelOptions.ShowSortModeLetter);
            case MenuCommandIds.SettingsToggleShowParentDirectoryInRootFolders:
                return ToggleSetting(() => PanelOptions.ShowParentDirectoryInRootFolders = !PanelOptions.ShowParentDirectoryInRootFolders);
            case MenuCommandIds.SettingsSave:
                if (_saveSettings is null)
                    return MenuCommandFailure("Settings save callback is not available.");
                _saveSettings();
                return MenuCommandSuccess();
            default:
                return MenuCommandFailure($"Unsupported menu command: {request.CommandId}");
        }
    }

    private void SetPanelViewMode(PanelSide side, PanelViewMode mode)
    {
        if (side == PanelSide.Left)
        {
            _leftViewMode = mode;
            _settings.Panels.LeftViewMode = mode.ToString();
        }
        else
        {
            _rightViewMode = mode;
            _settings.Panels.RightViewMode = mode.ToString();
        }

        _ctrl.MoveCursor(GetPanelState(side), 0, VisibleRows(side));
        _saveSettings?.Invoke();
    }

    private void ToggleReverseSort(PanelSide side)
    {
        var state = GetPanelState(side);
        state.SortDescending = !state.SortDescending;
        SafeRefresh(state, VisibleRows(side));
    }

    private MenuCommandResult ToggleSetting(Action toggle)
    {
        toggle();
        RefreshPanels();
        _saveSettings?.Invoke();
        return MenuCommandSuccess();
    }

    private FilePanelState GetPanelState(PanelSide side) =>
        side == PanelSide.Left ? _left : _right;

    private static MenuCommandResult MenuCommandSuccess() => new() { Success = true };

    private static MenuCommandResult MenuCommandFailure(string message) =>
        new() { Success = false, ErrorMessage = message };

    // ── Alt+1/Alt+2 — view mode ────────────────────────────────────────────────

    private void SetActiveViewMode(PanelViewMode mode)
    {
        SetPanelViewMode(_active, mode);
    }

    // ── file highlighting ─────────────────────────────────────────────────────

    private static IFileHighlightService? CreateHighlightService(AppSettingsAlias settings)
    {
        var hs = settings.Panels.FileHighlighting;
        if (!hs.Enabled) return null;

        var (rules, groups) = ResolveHighlightRules(hs);
        return rules.Count == 0 ? null : new FileHighlightService(rules, groups);
    }

    private static (IReadOnlyList<FileHighlightRule> Rules,
                    IReadOnlyDictionary<string, MaskGroup> Groups)
        ResolveHighlightRules(AppSettingsAlias.FileHighlightingSettings hs)
    {
        if (!string.Equals(hs.Preset, "FarDefault", StringComparison.OrdinalIgnoreCase))
            return (hs.Rules,
                    hs.MaskGroups.ToDictionary(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase));

        return hs.Mode switch
        {
            "UserRulesOnly" => (
                hs.Rules,
                hs.MaskGroups.ToDictionary(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase)),

            "PresetOnly" => (
                FarDefaultHighlightPreset.Rules,
                FarDefaultHighlightPreset.GroupsByName),

            _ => ( // PresetPlusUserRules (default)
                [.. FarDefaultHighlightPreset.Rules, .. hs.Rules],
                MergeHighlightGroups(FarDefaultHighlightPreset.Groups, hs.MaskGroups)),
        };
    }

    private static IReadOnlyDictionary<string, MaskGroup> MergeHighlightGroups(
        IReadOnlyList<MaskGroup> preset,
        IReadOnlyList<MaskGroup> user)
    {
        var dict = preset.ToDictionary(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase);
        foreach (var g in user) dict[g.Name] = g; // user group replaces same preset name
        return dict;
    }

    // ── shell execution ───────────────────────────────────────────────────────

    private void ExecuteCommand(string command)
    {
        string workDir = ActiveState.CurrentDirectory;
        bool showPanelsAfterCommand = _panelsVisible;
        _cmdLine.Clear();

        ShowShellUnderlayForCommand();
        PrintExecutedCommandPrompt(workDir, command);

        _shell.Execute(command, workDir);
        PrintInputPrompt(workDir);

        _history.AddCommand(new CommandHistoryItem
        {
            Command          = command,
            WorkingDirectory = workDir,
        });

        // Capture shell output NOW, before Render() paints panels over it.
        // This snapshot is what Ctrl+O will restore.
        CaptureUnderlay();

        RefreshPanels();
        _panelsVisible = showPanelsAfterCommand;
        // Render() or RenderCommandLineOnly() is called by the main loop.
    }

    private void ShowShellUnderlayForCommand()
    {
        _screen.SetRenderingOutputMode(false);
        RestoreOrClearUnderlay();

        SysConsole.ResetColor();
        SysConsole.CursorVisible = true;
    }

    private void PrintExecutedCommandPrompt(string workDir, string command)
    {
        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        int row = CommandLineRow(size);
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

        int row = CommandLineRow(size);
        var cmdRenderer = new CommandLineRenderer(_screen, _palette);
        cmdRenderer.Render(row, size.Width, workDir, _cmdLine);
        PositionCommandCursor(cmdRenderer, size, row);
    }

    private void ClearShellPromptArea(ConsoleSize size)
    {
        int commandRow = CommandLineRow(size);
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

    // ── navigation helpers ────────────────────────────────────────────────────

    private void TryEnterDirectory()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || !item.IsDirectory) return;
        try
        {
            if (item.IsParentDirectory)
                _ctrl.GoToParent(ActiveState, VisibleRows(), PanelOptions);
            else
                _ctrl.LoadDirectory(ActiveState, item.FullPath, PanelOptions);

            _history.AddDirectory(new DirectoryHistoryItem { Path = ActiveState.CurrentDirectory });
            StartWatching(ActiveState, _active);
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
            _ctrl.GoToParent(ActiveState, VisibleRows(), PanelOptions);
            _history.AddDirectory(new DirectoryHistoryItem { Path = ActiveState.CurrentDirectory });
            StartWatching(ActiveState, _active);
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
        try { _ctrl.RefreshDirectory(state, visibleRows, PanelOptions); }
        catch { }
    }

    // ── auto-refresh ──────────────────────────────────────────────────────────

    private void StartWatching(FilePanelState state, PanelSide side)
    {
        if (_watcher == null || _locationService == null) return;

        var loc = _locationService.GetLocationInfo(state.CurrentDirectory);
        var opts = PanelOptions.AutoRefresh;
        var req = new PanelWatchRequest
        {
            PanelSide     = side,
            DirectoryPath = state.CurrentDirectory,
            ObjectCount   = state.Items.Count,
            IsNetworkDrive = loc.IsNetworkDrive,
            Options        = opts,
        };
        var refreshState = _watcher.StartWatching(req);
        state.AutoRefreshState = refreshState;
    }

    private readonly Queue<FileSystemPanelChanged> _pendingRefreshEvents = new();

    private void OnFileSystemChanged(object? sender, FileSystemPanelChanged e)
    {
        lock (_pendingRefreshEvents)
            _pendingRefreshEvents.Enqueue(e);

        // Wake the input loop
        var old = Interlocked.Exchange(ref _refreshCts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    private void ResetRefreshCts()
    {
        // Already replaced in OnFileSystemChanged; nothing else needed here.
    }

    private void ProcessPendingRefreshes()
    {
        while (true)
        {
            FileSystemPanelChanged? evt;
            lock (_pendingRefreshEvents)
            {
                evt = _pendingRefreshEvents.Count > 0 ? _pendingRefreshEvents.Dequeue() : null;
            }
            if (evt is null) break;

            var state = evt.PanelSide == PanelSide.Left ? _left : _right;
            if (!string.Equals(state.CurrentDirectory, evt.DirectoryPath,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            int vr = VisibleRows(evt.PanelSide);
            if (Directory.Exists(state.CurrentDirectory))
                SafeRefresh(state, vr);
        }
    }

    // ── alias to avoid namespace conflict with CSharpFar.Console ─────────────
    private static class SysConsole
    {
        public static int  WindowTop    { get => global::System.Console.WindowTop;    }
        public static bool CursorVisible { set => global::System.Console.CursorVisible = value; }
        public static void ResetColor() => global::System.Console.ResetColor();
        public static void SetCursorPosition(int x, int y) =>
            global::System.Console.SetCursorPosition(x, y);
    }
}
