using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.HitTesting;
using CSharpFar.App.Menu;
using CSharpFar.App.Rendering;
using CSharpFar.App.Editor;
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
using CSharpFar.Shell;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App;

public sealed class Application
{
    private readonly ScreenRenderer _screen;
    private readonly IFileSystemService _fs;
    private readonly PanelController _ctrl;
    private readonly IShellService _shell;
    private readonly IFileLauncher _fileLauncher;
    private readonly IFileOperationService _fileOps;
    private readonly ISearchService _searchService;
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
    private readonly IReadOnlyList<FunctionKeyBinding> _functionKeyBindings;
    private PanelItemClick?                 _lastLeftPanelItemClick;
    private FunctionKeyLayer                _functionKeyLayer = FunctionKeyLayer.Plain;

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
        IVolumeMountPointService?    mountPointService = null,
        IFileLauncher?               fileLauncher      = null,
        ISearchService?              searchService     = null)
    {
        _screen       = screen;
        _fs           = fs;
        var sortSvc   = new PanelSortService();
        var viewBuilder = new PanelViewBuilder(fs, sortSvc, volumeInfoService, mountPoints: mountPointService);
        _ctrl         = new PanelController(viewBuilder);
        _shell        = shell;
        _fileLauncher = fileLauncher ?? new WindowsShellFileLauncher();
        _fileOps      = fileOps;
        _searchService = searchService ?? new CSharpFar.FileSystem.FileSystemSearchService();
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
        _functionKeyBindings = BuildFunctionKeyBindings();

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
                    case ModifierKeyConsoleInputEvent { Modifiers: var modifiers }:
                        shouldRender = SetFunctionKeyLayer(modifiers);
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

        RenderFunctionKeyBar(size);

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

    private void RenderFunctionKeyBar(ConsoleSize size)
    {
        var items = _functionKeyBindings
            .Where(binding => binding.Layer == _functionKeyLayer && binding.IsAvailable())
            .Select(binding => new FunctionKeyBarItem(binding.KeyNumber, binding.Label))
            .ToArray();

        new FunctionKeyBarRenderer(_screen, _palette).Render(size.Height - 1, size.Width, items);
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

    private bool SetFunctionKeyLayer(ConsoleModifiers modifiers)
    {
        var layer = FunctionKeyLayerResolver.ResolvePressedLayer(modifiers);
        if (_functionKeyLayer == layer)
            return false;

        _functionKeyLayer = layer;
        return true;
    }

    private void ResetFunctionKeyLayer() => _functionKeyLayer = FunctionKeyLayer.Plain;

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
            MenuBarNormalStyle = new CellStyle(_palette.MenuBarNormalFg, _palette.MenuBarNormalBg),
            MenuBarActiveStyle = new CellStyle(_palette.MenuBarActiveFg, _palette.MenuBarActiveBg),
            NormalStyle = new CellStyle(_palette.MenuNormalFg, _palette.MenuNormalBg),
            ActiveStyle = new CellStyle(_palette.MenuActiveFg, _palette.MenuActiveBg),
            HighlightStyle = new CellStyle(_palette.MenuHighlightFg, _palette.MenuHighlightBg),
            ActiveHighlightStyle = new CellStyle(_palette.MenuActiveHighlightFg, _palette.MenuActiveHighlightBg),
            DisabledStyle = new CellStyle(_palette.MenuDisabledFg, _palette.MenuDisabledBg),
            BorderStyle = new CellStyle(_palette.MenuBorderFg, _palette.MenuBorderBg),
            ShadowStyle = new CellStyle(_palette.MenuShadowFg, _palette.MenuShadowBg),
        };

    private IReadOnlyList<FunctionKeyBinding> BuildFunctionKeyBindings() =>
    [
        new(
            FunctionKeyCommandIds.Help,
            FunctionKeyLayer.Plain,
            ConsoleKey.F1,
            "Help",
            () => true,
            () =>
            {
                new HelpViewer(_screen, _palette).Show();
                return true;
            }),
        new(
            FunctionKeyCommandIds.UserMenu,
            FunctionKeyLayer.Plain,
            ConsoleKey.F2,
            "UserMn",
            () => true,
            () =>
            {
                HandleUserMenu();
                return true;
            }),
        new(
            FunctionKeyCommandIds.View,
            FunctionKeyLayer.Plain,
            ConsoleKey.F3,
            "View",
            CanViewCurrentFile,
            () =>
            {
                HandleViewFile();
                return true;
            }),
        new(
            FunctionKeyCommandIds.Edit,
            FunctionKeyLayer.Plain,
            ConsoleKey.F4,
            "Edit",
            CanEditCurrentFile,
            () =>
            {
                HandleEditFile();
                return true;
            },
            () =>
            {
                HandleEditFile();
                return true;
            }),
        new(
            FunctionKeyCommandIds.Copy,
            FunctionKeyLayer.Plain,
            ConsoleKey.F5,
            "Copy",
            CanCopy,
            () =>
            {
                HandleCopy();
                return true;
            },
            () =>
            {
                HandleCopy();
                return true;
            }),
        new(
            FunctionKeyCommandIds.RenameOrMove,
            FunctionKeyLayer.Plain,
            ConsoleKey.F6,
            "RenMov",
            CanMove,
            () =>
            {
                HandleMove();
                return true;
            },
            () =>
            {
                HandleMove();
                return true;
            }),
        new(
            FunctionKeyCommandIds.CreateFolder,
            FunctionKeyLayer.Plain,
            ConsoleKey.F7,
            "MkFold",
            () => HasCapability(ActiveState, PanelProviderCapabilities.CreateDirectory),
            () =>
            {
                HandleCreateFolder();
                return true;
            },
            () =>
            {
                HandleCreateFolder();
                return true;
            }),
        new(
            FunctionKeyCommandIds.Delete,
            FunctionKeyLayer.Plain,
            ConsoleKey.F8,
            "Delete",
            CanDelete,
            () =>
            {
                HandleDelete();
                return true;
            },
            () =>
            {
                HandleDelete();
                return true;
            }),
        new(
            FunctionKeyCommandIds.TopMenu,
            FunctionKeyLayer.Plain,
            ConsoleKey.F9,
            "ConfMn",
            () => true,
            OpenTopMenu),
        new(
            FunctionKeyCommandIds.Quit,
            FunctionKeyLayer.Plain,
            ConsoleKey.F10,
            "Quit",
            () => true,
            () =>
            {
                _running = false;
                return false;
            }),
        new(
            FunctionKeyCommandIds.LeftVolume,
            FunctionKeyLayer.Alt,
            ConsoleKey.F1,
            "Left",
            () => true,
            () =>
            {
                HandleDriveSelect(PanelSide.Left);
                ResetFunctionKeyLayer();
                return true;
            }),
        new(
            FunctionKeyCommandIds.RightVolume,
            FunctionKeyLayer.Alt,
            ConsoleKey.F2,
            "Right",
            () => true,
            () =>
            {
                HandleDriveSelect(PanelSide.Right);
                ResetFunctionKeyLayer();
                return true;
            }),
        new(
            FunctionKeyCommandIds.Search,
            FunctionKeyLayer.Alt,
            ConsoleKey.F7,
            "Search",
            () => true,
            () =>
            {
                HandleSearchFiles();
                ResetFunctionKeyLayer();
                return true;
            }),
        new(
            FunctionKeyCommandIds.CommandHistory,
            FunctionKeyLayer.Alt,
            ConsoleKey.F8,
            "History",
            () => true,
            () =>
            {
                HandleCommandHistory();
                ResetFunctionKeyLayer();
                return true;
            }),
        new(
            FunctionKeyCommandIds.FileHistory,
            FunctionKeyLayer.Alt,
            ConsoleKey.F11,
            "FHist",
            () => true,
            () =>
            {
                HandleFileHistory();
                ResetFunctionKeyLayer();
                return true;
            }),
        new(
            FunctionKeyCommandIds.DirectoryHistory,
            FunctionKeyLayer.Alt,
            ConsoleKey.F12,
            "DHist",
            () => true,
            () =>
            {
                HandleDirectoryHistory();
                ResetFunctionKeyLayer();
                return true;
            }),
        new(
            FunctionKeyCommandIds.SortByName,
            FunctionKeyLayer.Control,
            ConsoleKey.F3,
            "SortNm",
            CanSortActivePanel,
            () =>
            {
                SetPanelSortMode(ActiveState, SortMode.Name, VisibleRows());
                return true;
            }),
        new(
            FunctionKeyCommandIds.SortByExtension,
            FunctionKeyLayer.Control,
            ConsoleKey.F4,
            "SortExt",
            CanSortActivePanel,
            () =>
            {
                SetPanelSortMode(ActiveState, SortMode.Extension, VisibleRows());
                return true;
            }),
        new(
            FunctionKeyCommandIds.SortByLastWriteTime,
            FunctionKeyLayer.Control,
            ConsoleKey.F5,
            "SortTm",
            CanSortActivePanel,
            () =>
            {
                SetPanelSortMode(ActiveState, SortMode.LastWriteTime, VisibleRows());
                return true;
            }),
        new(
            FunctionKeyCommandIds.SortBySize,
            FunctionKeyLayer.Control,
            ConsoleKey.F6,
            "SortSz",
            CanSortActivePanel,
            () =>
            {
                SetPanelSortMode(ActiveState, SortMode.Size, VisibleRows());
                return true;
            }),
    ];

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
        if (!inLeft && !inRight)
        {
            ClearPanelItemClickOnMousePress(evt);
            return false;
        }

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
            _lastLeftPanelItemClick = null;
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

        if (evt.Button == MouseButton.Left && evt.Kind == MouseEventKind.DoubleClick)
        {
            _active = side;
            int? itemIdx = PanelHitTester.HitTestItem(evt.X, evt.Y, bounds, state, mode, PanelOptions);
            if (itemIdx.HasValue)
            {
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);

                var item = state.Items[itemIdx.Value];
                var currentClick = new PanelItemClick(side, itemIdx.Value, item.FullPath);
                if (_lastLeftPanelItemClick == currentClick)
                    OpenPanelItem(state, side, item);
            }

            _lastLeftPanelItemClick = null;
            return true;
        }

        // Left click: activate panel and move cursor
        if (evt.Button == MouseButton.Left &&
            (evt.Kind == MouseEventKind.Down || evt.Kind == MouseEventKind.Click))
        {
            _active = side;
            int? itemIdx = PanelHitTester.HitTestItem(evt.X, evt.Y, bounds, state, mode, PanelOptions);
            if (itemIdx.HasValue)
            {
                _ctrl.SetCursorTo(state, itemIdx.Value, visRows);
                var item = state.Items[itemIdx.Value];
                _lastLeftPanelItemClick = new PanelItemClick(side, itemIdx.Value, item.FullPath);
            }
            else
            {
                _lastLeftPanelItemClick = null;
            }
            return true;
        }

        return false;
    }

    private void ClearPanelItemClickOnMousePress(MouseConsoleInputEvent evt)
    {
        if (evt.Kind is MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick)
            _lastLeftPanelItemClick = null;
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

        // Ctrl+A — select all; Ctrl+* — invert selection
        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        if (isControlShortcut)
        {
            switch (key.Key)
            {
                case ConsoleKey.A:  _ctrl.ToggleSelectAll(ActiveState, PanelOptions);                          return true;
                case ConsoleKey.Multiply:
                    _ctrl.InvertSelection(ActiveState, PanelOptions);
                    return true;
                case ConsoleKey.D8 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                    _ctrl.InvertSelection(ActiveState, PanelOptions);
                    return true;
            }
        }

        if (TryHandleFunctionKey(key, out bool functionKeyShouldRender))
            return functionKeyShouldRender;

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
                else OpenCurrentItem();
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

    private bool TryHandleFunctionKey(ConsoleKeyInfo key, out bool shouldRender)
    {
        shouldRender = false;

        if (key.Key is < ConsoleKey.F1 or > ConsoleKey.F12)
            return false;

        if (!FunctionKeyLayerResolver.TryResolveChordLayer(key.Modifiers, out var layer))
            return false;

        var binding = _functionKeyBindings.FirstOrDefault(candidate =>
            candidate.Layer == layer &&
            candidate.Key == key.Key);

        if (binding is null)
            return false;

        shouldRender = binding.IsAvailable()
            ? binding.Execute()
            : binding.ExecuteUnavailable?.Invoke() ?? true;
        return true;
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

    private bool OpenTopMenu()
    {
        _menuController.HandleKey(
            new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false),
            BuildMenuDefinition(),
            _active);
        return true;
    }

    private bool CanViewCurrentFile() =>
        HasCapability(ActiveState, PanelProviderCapabilities.OpenRead);

    private bool CanEditCurrentFile() =>
        HasCapability(ActiveState, PanelProviderCapabilities.Edit);

    private bool CanCopy() =>
        HasCapability(ActiveState, PanelProviderCapabilities.CopyFrom) &&
        HasCapability(PassiveState, PanelProviderCapabilities.CopyTo);

    private bool CanMove() =>
        HasCapability(ActiveState, PanelProviderCapabilities.MoveFrom) &&
        HasCapability(PassiveState, PanelProviderCapabilities.MoveTo);

    private bool CanDelete() =>
        HasCapability(ActiveState, PanelProviderCapabilities.Delete);

    private bool CanSortActivePanel() =>
        HasCapability(ActiveState, PanelProviderCapabilities.Enumerate);

    private FilePanelState PassiveState => _active == PanelSide.Left ? _right : _left;

    // ── F4 — edit file ────────────────────────────────────────────────────────

    private void HandleEditFile()
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.Edit))
        {
            ShowReadOnlyPanelMessage("Edit");
            return;
        }

        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || item.IsParentDirectory || item.IsDirectory) return;
        _history.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileEditor(_screen, _palette).Show(item.FullPath);
        SafeRefresh(ActiveState, VisibleRows());
    }

    // ── F3 — view file ────────────────────────────────────────────────────────

    private void HandleViewFile()
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.OpenRead))
            return;

        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null || item.IsParentDirectory || item.IsDirectory) return;
        _history.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileViewer(_screen, _palette).Show(item.FullPath);
    }

    // ── Alt+F11 — file history ────────────────────────────────────────────────

    private void HandleFileHistory()
    {
        string? path = new FileHistoryDialog(_screen, _palette).Show(_history.GetFileHistory());
        if (path is null) return;

        if (!File.Exists(path))
        {
            new MessageDialog(_screen, _palette).Show("File History", $"File not found: {path}");
            return;
        }

        var choice = new OpenFileDialog(_screen, _palette).Show(Path.GetFileName(path));
        switch (choice)
        {
            case OpenFileChoice.View:
                _history.AddFile(new FileHistoryItem { Path = path });
                new FileViewer(_screen, _palette).Show(path);
                break;
            case OpenFileChoice.Edit:
                _history.AddFile(new FileHistoryItem { Path = path });
                new FileEditor(_screen, _palette).Show(path);
                SafeRefresh(ActiveState, VisibleRows());
                break;
        }
    }

    // ── F2 — user menu ────────────────────────────────────────────────────────

    private void HandleUserMenu()
    {
        if (_userMenu.Items.Count == 0)
        {
            new MessageDialog(_screen, _palette).Show(
                "User Menu", "User menu is empty.\nEdit user-menu.json to add commands.");
            return;
        }

        string? command = new UserMenuDialog(_screen, _palette).Show(_userMenu.Items);
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
        var request = new SearchDialog(_screen).Show(ActiveState.CurrentDirectory);
        if (request is null) return;

        SearchRunResult result;
        try
        {
            result = new SearchProgressDialog(_screen, _searchService, _palette).Show(request);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
        {
            new MessageDialog(_screen, _palette).Show("Search", ex.Message);
            return;
        }

        if (result.GoToResult is not null)
        {
            GoToSearchResult(ActiveState, _active, result.GoToResult);
            return;
        }

        if (result.DiscardResults)
            return;

        if (result.Results.Count == 0)
        {
            string message = result.Cancelled ? "Search cancelled. No files found." : "No files found.";
            new MessageDialog(_screen, _palette).Show("Search", message);
            return;
        }

        OpenSearchResultsPanel(ActiveState, request, result.Results, result.Cancelled);
    }

    private void OpenSearchResultsPanel(
        FilePanelState state,
        SearchRequest request,
        IReadOnlyList<SearchResultItem> results,
        bool cancelled)
    {
        state.CurrentDirectory = request.RootPath;
        state.Items.Clear();
        state.Items.AddRange(results.Select(ToFilePanelItem));
        state.SelectedPaths.Clear();
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
        state.ProviderCapabilities = PanelProviderCapabilities.SearchResults;
        state.DisplayTitle = BuildSearchResultsTitle(request, cancelled);
        state.ShowCurrentItemFullPath = true;
        state.SearchRequest = request;
        state.SearchWasCancelled = cancelled;
        state.AutoRefreshState = null;
        SortVirtualPanel(state, keepCursorPath: null);
        RefreshSearchResultsSummary(state);
    }

    private void GoToSearchResult(FilePanelState state, PanelSide side, SearchResultItem result)
    {
        GoToSearchResult(
            state,
            side,
            result.FullPath,
            result.Name,
            result.Kind == SearchResultItemKind.Directory);
    }

    private void GoToSearchResult(FilePanelState state, PanelSide side, FilePanelItem result)
    {
        GoToSearchResult(
            state,
            side,
            result.FullPath,
            result.Name,
            result.IsDirectory);
    }

    private void GoToSearchResult(
        FilePanelState state,
        PanelSide side,
        string fullPath,
        string name,
        bool isDirectory)
    {
        string? directoryPath = isDirectory
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            new MessageDialog(_screen, _palette).Show("Search", $"Cannot open search result: {fullPath}");
            return;
        }

        try
        {
            _ctrl.LoadDirectory(state, directoryPath, PanelOptions);
            if (!isDirectory)
                _ctrl.SetCursorByName(state, name, VisibleRows(side));

            _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
            StartWatching(state, side);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            new MessageDialog(_screen, _palette).Show("Search", ex.Message);
        }
    }

    private static FilePanelItem ToFilePanelItem(SearchResultItem item) =>
        new()
        {
            Name = item.Name,
            FullPath = item.FullPath,
            IsDirectory = item.Kind == SearchResultItemKind.Directory,
            Size = item.Size,
            LastWriteTime = item.LastWriteTime,
            Attributes = item.Attributes,
            IsParentDirectory = false,
        };

    private static string BuildSearchResultsTitle(SearchRequest request, bool cancelled)
    {
        string basis = !string.IsNullOrEmpty(request.ContainingText)
            ? request.ContainingText
            : request.FileMaskExpression;

        string title = $"Search results: {basis}";
        return cancelled ? $"{title}, cancelled" : title;
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

    private FileOperationOptions BuildFileOperationOptions() =>
        new()
        {
            DefaultConflictDecision = ParseEnum(
                _settings.FileOperations.ConflictDecision,
                ConflictDecisionMode.Ask),
            PreserveTimestamps = _settings.FileOperations.PreserveTimestamps,
            PreserveAttributes = _settings.FileOperations.PreserveAttributes,
            SecurityMode = ParseEnum(
                _settings.FileOperations.SecurityMode,
                FileSecurityMode.Inherit),
            SymlinkMode = ParseEnum(
                _settings.FileOperations.SymlinkMode,
                SymlinkCopyMode.CopyLink),
            UseRecycleBinForDelete = _settings.FileOperations.UseRecycleBinForDelete,
        };

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

    private FileOperationResult ExecuteFileOperation(FileOperationRequest request)
    {
        var progressDialog = new ProgressDialog(_screen, request.Destination ?? string.Empty);
        var conflictDialog = new ConflictDialog(_screen, _palette, request.Kind == FileOperationKind.Copy);
        var cancelDialog = new OperationCancelDialog(_screen);
        var resolver = new DialogConflictResolver(conflictDialog);
        var pauseController = new FileOperationPauseController();
        request = request with { PauseController = pauseController };
        using var cts = new CancellationTokenSource();

        FileOperationProgress? latestProgress = null;
        var progress = new Progress<FileOperationProgress>(p =>
        {
            latestProgress = p;
        });

        FileOperationResult? completedResult = null;
        Exception? completedException = null;
        Task task = Task.Run(async () =>
        {
            try
            {
                completedResult = await _fileOps.ExecuteAsync(request, progress, resolver, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                completedException = ex;
            }
        }, cts.Token);

        FileOperationProgress? renderedProgress = null;
        var lastRender = DateTime.MinValue;

        while (!task.IsCompleted)
        {
            if (resolver.ShowPendingConflict())
            {
                renderedProgress = null;
                lastRender = DateTime.MinValue;
                continue;
            }

            if (latestProgress is not null &&
                !ReferenceEquals(latestProgress, renderedProgress) &&
                DateTime.UtcNow - lastRender >= TimeSpan.FromMilliseconds(120))
            {
                progressDialog.Update(latestProgress, _settings.FileOperations.ShowTotalProgress);
                renderedProgress = latestProgress;
                lastRender = DateTime.UtcNow;
            }

            if (TryReadConsoleKey(out var key))
            {
                if (key.Key == ConsoleKey.Escape)
                {
                    if (latestProgress?.Phase == FileOperationPhase.Scanning)
                    {
                        cts.Cancel();
                    }
                    else
                    {
                        pauseController.Pause();
                        try
                        {
                            if (cancelDialog.Show())
                                cts.Cancel();
                        }
                        finally
                        {
                            pauseController.Resume();
                        }
                    }

                    renderedProgress = null;
                    lastRender = DateTime.MinValue;
                }
            }

            Thread.Sleep(30);
        }

        if (latestProgress is not null && !ReferenceEquals(latestProgress, renderedProgress))
            progressDialog.Update(latestProgress, _settings.FileOperations.ShowTotalProgress);

        if (task.IsCanceled)
            throw new OperationCanceledException();
        if (completedException is not null)
            throw completedException;

        FileOperationResult result = completedResult
            ?? throw new InvalidOperationException("File operation did not return a result.");
        if (result.Cancelled)
            throw new OperationCanceledException();
        if (result.Errors.Count > 0)
            new MessageDialog(_screen, _palette).Show(
                "File Operation",
                $"{result.FailedCount} item(s) failed. First: {result.Errors[0].Message}");

        return result;
    }

    private sealed class DialogConflictResolver : IFileOperationConflictResolver
    {
        private readonly ConflictDialog _dialog;
        private readonly object _gate = new();
        private PendingConflict? _pendingConflict;

        public DialogConflictResolver(ConflictDialog dialog)
        {
            _dialog = dialog;
        }

        public bool ShowPendingConflict()
        {
            PendingConflict? pendingConflict;
            lock (_gate)
            {
                pendingConflict = _pendingConflict;
            }

            if (pendingConflict is null)
                return false;

            var decision = _dialog.Show(pendingConflict.Conflict);

            lock (_gate)
            {
                if (ReferenceEquals(_pendingConflict, pendingConflict))
                    _pendingConflict = null;
                Monitor.PulseAll(_gate);
            }

            pendingConflict.SetDecision(decision);
            return true;
        }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            var pendingConflict = new PendingConflict(conflict);

            lock (_gate)
            {
                while (_pendingConflict is not null)
                    Monitor.Wait(_gate);

                _pendingConflict = pendingConflict;
                Monitor.PulseAll(_gate);
            }

            return pendingConflict.WaitForDecision();
        }

        private sealed class PendingConflict
        {
            private readonly ManualResetEventSlim _decisionReady = new();
            private FileOperationConflictDecision? _decision;

            public PendingConflict(FileOperationConflict conflict)
            {
                Conflict = conflict;
            }

            public FileOperationConflict Conflict { get; }

            public void SetDecision(FileOperationConflictDecision decision)
            {
                _decision = decision;
                _decisionReady.Set();
            }

            public FileOperationConflictDecision WaitForDecision()
            {
                _decisionReady.Wait();
                return _decision
                    ?? throw new InvalidOperationException("Conflict dialog closed without a decision.");
            }
        }
    }

    private sealed class FileOperationPauseController : IFileOperationPauseController
    {
        private readonly ManualResetEventSlim _canRun = new(initialState: true);

        public void Pause() => _canRun.Reset();

        public void Resume() => _canRun.Set();

        public void WaitIfPaused(CancellationToken cancellationToken) =>
            _canRun.Wait(cancellationToken);
    }

    private void HandleCopy()
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.CopyFrom))
        {
            ShowReadOnlyPanelMessage("Copy");
            return;
        }

        var targetState = _active == PanelSide.Left ? _right : _left;
        if (!HasCapability(targetState, PanelProviderCapabilities.CopyTo))
        {
            new MessageDialog(_screen, _palette).Show(
                "Copy",
                "Cannot copy to search results panel.\nSearch results are read-only.");
            return;
        }

        var sources = GetOperationSources();
        if (sources.Count == 0) return;

        var dialogResult = new FileOperationDialog(_screen).ShowCopy(
            sources,
            targetState.CurrentDirectory,
            BuildFileOperationOptions());
        if (dialogResult is null) return;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = sources,
                Destination = dialogResult.Destination,
                Options = dialogResult.Options,
            });

            ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _screen.Restore(saved);
            new MessageDialog(_screen, _palette).Show("Copy Error", ex.Message);
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
        if (!HasCapability(ActiveState, PanelProviderCapabilities.MoveFrom))
        {
            ShowReadOnlyPanelMessage("Move");
            return;
        }

        var targetState = _active == PanelSide.Left ? _right : _left;
        if (!HasCapability(targetState, PanelProviderCapabilities.MoveTo))
        {
            ShowReadOnlyPanelMessage("Move");
            return;
        }

        var sources = GetOperationSources();
        if (sources.Count == 0) return;

        // Single item: pre-fill with its name (user edits to rename or enters a path to move).
        // Multiple items: pre-fill with opposite panel dir (move destination).
        string preFill = sources.Count == 1
            ? Path.GetFileName(sources[0])
            : targetState.CurrentDirectory;

        var dialogResult = new FileOperationDialog(_screen).ShowMove(
            sources,
            preFill,
            BuildFileOperationOptions());
        if (dialogResult is null) return;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                Destination = dialogResult.Destination,
                Options = dialogResult.Options,
            });

            ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _screen.Restore(saved);
            new MessageDialog(_screen, _palette).Show("Move Error", ex.Message);
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
        if (!HasCapability(ActiveState, PanelProviderCapabilities.Delete))
        {
            ShowReadOnlyPanelMessage("Delete");
            return;
        }

        var sources = GetOperationSources();
        if (sources.Count == 0) return;

        string itemName = sources.Count == 1
            ? Path.GetFileName(sources[0])
            : $"{sources.Count} items";

        if (_settings.Ui.ConfirmDelete && !new ConfirmDialog(_screen).Show("Delete", "Do you wish to move to the Recycle Bin?", itemName))
            return;

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = sources,
                Options = BuildFileOperationOptions() with
                {
                    UseRecycleBinForDelete = _settings.FileOperations.UseRecycleBinForDelete,
                },
            });
            ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _screen.Restore(saved);
            new MessageDialog(_screen, _palette).Show("Delete Error", ex.Message);
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
        string? path = new DirectoryHistoryDialog(_screen, _palette).Show(_history.GetDirectoryHistory());
        if (path is null) return;

        if (!Directory.Exists(path))
        {
            new MessageDialog(_screen, _palette).Show("Directory History", $"Directory not found: {path}");
            return;
        }

        try
        {
            _ctrl.LoadDirectory(ActiveState, path, PanelOptions);
            StartWatching(ActiveState, _active);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen, _palette).Show("Directory History", ex.Message);
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

        var selected = new DriveDialog(_screen, _palette).Show(items, initialCursor);
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
            new MessageDialog(_screen, _palette).Show("Change drive", ex.Message);
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
        string? cmd = new HistoryDialog(_screen, _palette).Show(_history.GetCommandHistory());
        if (cmd is not null)
            _cmdLine.SetText(cmd);
    }

    // ── F7 — create folder ────────────────────────────────────────────────────

    private void HandleCreateFolder()
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.CreateDirectory))
        {
            ShowReadOnlyPanelMessage("Create folder");
            return;
        }

        var dialog = new CreateFolderDialog(_screen);
        string? name = dialog.Show(validate: attempt =>
        {
            if (attempt.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "Invalid characters in folder path.";

            string newPath = Path.Combine(ActiveState.CurrentDirectory, attempt);
            try
            {
                ExecuteFileOperation(new FileOperationRequest
                {
                    Kind = FileOperationKind.CreateDirectory,
                    Sources = [],
                    Destination = newPath,
                    Options = BuildFileOperationOptions(),
                });
                return null;
            }
            catch (IOException ex)              { return ex.Message; }
            catch (UnauthorizedAccessException) { return "Access denied."; }
            catch (ArgumentException ex)        { return ex.Message; }
        });

        if (name is null) return;

        int vr = VisibleRows();
        SafeRefresh(ActiveState, vr);
        _ctrl.SetCursorByName(ActiveState, FirstCreatedDirectoryName(name), vr);
    }

    private static string FirstCreatedDirectoryName(string path)
    {
        string trimmed = path.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int separator = trimmed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separator < 0 ? trimmed : trimmed[..separator];
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
                SetPanelSortMode(
                    GetPanelState(sortArgs.PanelSide),
                    sortArgs.SortMode,
                    VisibleRows(sortArgs.PanelSide));
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
        if (state.SearchRequest is null)
            SafeRefresh(state, VisibleRows(side));
        else
        {
            SortVirtualPanel(state, _ctrl.CurrentItem(state)?.FullPath);
            _ctrl.MoveCursor(state, 0, VisibleRows(side));
        }
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

    private void OpenCurrentItem()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null) return;

        OpenPanelItem(ActiveState, _active, item);
    }

    private void OpenPanelItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (state.SearchRequest is not null)
        {
            GoToSearchResult(state, side, item);
            return;
        }

        if (item.IsDirectory)
        {
            OpenDirectoryItem(state, side, item);
            return;
        }

        OpenFileItem(item);
    }

    private void OpenDirectoryItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Enumerate))
            return;

        try
        {
            if (item.IsParentDirectory)
                _ctrl.GoToParent(state, VisibleRows(side), PanelOptions);
            else
                _ctrl.LoadDirectory(state, item.FullPath, PanelOptions);

            _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
            StartWatching(state, side);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen, _palette).Show("Navigation", ex.Message);
        }
    }

    private void OpenFileItem(FilePanelItem item)
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.OpenRead))
            return;

        try
        {
            _fileLauncher.OpenFile(item.FullPath);
        }
        catch (Exception ex) when (
            ex is IOException or
                  UnauthorizedAccessException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception)
        {
            new MessageDialog(_screen, _palette).Show("Open file", ex.Message);
        }
    }

    private void TryGoUp()
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.Watch))
            return;

        try
        {
            _ctrl.GoToParent(ActiveState, VisibleRows(), PanelOptions);
            _history.AddDirectory(new DirectoryHistoryItem { Path = ActiveState.CurrentDirectory });
            StartWatching(ActiveState, _active);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(_screen, _palette).Show("Navigation", ex.Message);
        }
    }

    private void RefreshPanels()
    {
        SafeRefresh(_left,  VisibleRows(PanelSide.Left));
        SafeRefresh(_right, VisibleRows(PanelSide.Right));
    }

    private void RefreshSearchResultsPanel(FilePanelState state, int visibleRows)
    {
        if (state.SearchRequest is null)
            return;

        var previousItems = state.Items.ToList();
        var previousSelectedPaths = state.SelectedPaths.ToList();
        int previousCursor = state.CursorIndex;
        int previousScroll = state.ScrollOffset;
        string? cursorPath = _ctrl.CurrentItem(state)?.FullPath;

        SearchRunResult result;
        try
        {
            result = new SearchProgressDialog(_screen, _searchService, _palette).Show(state.SearchRequest);
        }
        catch
        {
            state.Items.Clear();
            state.Items.AddRange(previousItems);
            state.SelectedPaths.Clear();
            foreach (string selectedPath in previousSelectedPaths)
                state.SelectedPaths.Add(selectedPath);
            state.CursorIndex = previousCursor;
            state.ScrollOffset = previousScroll;
            RefreshSearchResultsSummary(state);
            return;
        }

        if (result.GoToResult is not null)
        {
            GoToSearchResult(state, PanelSideForState(state), result.GoToResult);
            return;
        }

        if (result.DiscardResults || result.Cancelled)
        {
            state.Items.Clear();
            state.Items.AddRange(previousItems);
            state.SelectedPaths.Clear();
            foreach (string selectedPath in previousSelectedPaths)
                state.SelectedPaths.Add(selectedPath);
            state.CursorIndex = previousCursor;
            state.ScrollOffset = previousScroll;
            RefreshSearchResultsSummary(state);
            return;
        }

        state.Items.Clear();
        state.Items.AddRange(result.Results.Select(ToFilePanelItem));
        state.SelectedPaths.Clear();
        state.SearchWasCancelled = false;
        state.DisplayTitle = BuildSearchResultsTitle(state.SearchRequest, cancelled: false);
        SortVirtualPanel(state, cursorPath);
        RefreshSearchResultsSummary(state);
        _ctrl.MoveCursor(state, 0, visibleRows);
    }

    private PanelSide PanelSideForState(FilePanelState state) =>
        ReferenceEquals(state, _left) ? PanelSide.Left : PanelSide.Right;

    private void SafeRefresh(FilePanelState state, int visibleRows)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Refresh))
            return;

        if (state.SearchRequest is not null)
        {
            RefreshSearchResultsPanel(state, visibleRows);
            return;
        }

        if (!Directory.Exists(state.CurrentDirectory)) return;
        try { _ctrl.RefreshDirectory(state, visibleRows, PanelOptions); }
        catch { }
    }

    private void SetPanelSortMode(FilePanelState state, SortMode mode, int visibleRows)
    {
        if (state.SearchRequest is null)
        {
            _ctrl.SetSortMode(state, mode, visibleRows, PanelOptions);
            return;
        }

        string? cursorPath = _ctrl.CurrentItem(state)?.FullPath;
        if (state.SortMode == mode)
            state.SortDescending = !state.SortDescending;
        else
        {
            state.SortMode = mode;
            state.SortDescending = false;
        }

        SortVirtualPanel(state, cursorPath);
        _ctrl.MoveCursor(state, 0, visibleRows);
    }

    private void SortVirtualPanel(FilePanelState state, string? keepCursorPath)
    {
        var sortOptions = new PanelSortOptions
        {
            SortFoldersByExtension = PanelOptions.SortFoldersByExtension,
            KeepParentDirectoryFirst = false,
            DirectoriesFirst = true,
        };
        var sorted = new PanelSortService().Sort(state.Items, state.SortMode, state.SortDescending, sortOptions);
        state.Items.Clear();
        state.Items.AddRange(sorted);

        if (keepCursorPath is null)
        {
            state.CursorIndex = 0;
            state.ScrollOffset = 0;
            return;
        }

        int index = state.Items.FindIndex(i => string.Equals(i.FullPath, keepCursorPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            state.CursorIndex = index;
    }

    private static void RefreshSearchResultsSummary(FilePanelState state)
    {
        int fileCount = 0;
        int directoryCount = 0;
        long totalFileSize = 0;
        int selectedCount = 0;
        long selectedFileSize = 0;

        foreach (var item in state.Items)
        {
            if (item.IsDirectory)
                directoryCount++;
            else
            {
                fileCount++;
                totalFileSize += item.Size ?? 0;
            }

            if (!state.SelectedPaths.Contains(item.FullPath))
                continue;

            selectedCount++;
            if (!item.IsDirectory)
                selectedFileSize += item.Size ?? 0;
        }

        state.Summary = new PanelSummary
        {
            VisibleItemCount = fileCount + directoryCount,
            FileCount = fileCount,
            DirectoryCount = directoryCount,
            TotalFileSize = totalFileSize,
            SelectedCount = selectedCount,
            SelectedFileSize = selectedFileSize,
        };
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;

    private static bool TryReadConsoleKey(out ConsoleKeyInfo key)
    {
        try
        {
            if (global::System.Console.KeyAvailable)
            {
                key = global::System.Console.ReadKey(intercept: true);
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        key = default;
        return false;
    }

    private void ShowReadOnlyPanelMessage(string action)
    {
        new MessageDialog(_screen, _palette).Show(
            action,
            "Search results are read-only.");
    }

    // ── auto-refresh ──────────────────────────────────────────────────────────

    private void StartWatching(FilePanelState state, PanelSide side)
    {
        if (_watcher == null || _locationService == null) return;
        if (!HasCapability(state, PanelProviderCapabilities.Watch))
        {
            state.AutoRefreshState = null;
            return;
        }

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

    private readonly record struct PanelItemClick(
        PanelSide PanelSide,
        int ItemIndex,
        string FullPath);
}
