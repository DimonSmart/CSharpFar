using CSharpFar.App.Dialogs;
using CSharpFar.App.Commands;
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
    private const int MaxCommandCompletionRows = CommandHistoryCompletionRenderer.MaxVisibleRows;

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
    private readonly List<string> _commandCompletionMatches = [];

    private PanelSide     _active        = PanelSide.Left;
    private bool          _running       = true;
    private bool          _panelsVisible = true;
    private bool          _commandCompletionVisible;
    private bool          _commandCompletionTemporarilyHidden;
    private int           _commandCompletionSelectedIndex;
    private int           _commandCompletionFirstVisibleIndex;
    private ScrollBarDragState? _commandCompletionScrollbarDrag;
    private int?          _hiddenCommandHistoryIndex;
    private bool          _quickView     = false;
    private ConsoleViewport? _lastRenderViewport;
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
    private readonly DefaultFunctionKeyBindingProvider _functionKeyBindingProvider = new();
    private readonly ApplicationCommandRegistry _commandRegistry = ApplicationCommandRegistry.CreateDefault();
    private readonly ApplicationCommandContext _commandContext;
    private readonly MenuLayoutService      _menuLayoutService = new();
    private readonly MenuBarRenderer        _menuBarRenderer = new();
    private readonly DropdownMenuRenderer   _dropdownMenuRenderer = new();
    private readonly TopMenuController      _menuController;
    private readonly IReadOnlyList<FunctionKeyBinding> _functionKeyBindings;
    private PanelItemClick?                 _lastLeftPanelItemClick;
    private FunctionKeyLayer                _functionKeyLayer = FunctionKeyLayer.Plain;
    private readonly DirectorySizeCalculator _dirSizeCalc = new();
    private DirectorySizeState?              _quickViewDirState;
    private string?                          _quickViewDirPath;
    private PanelScrollbarDrag?              _panelScrollbarDrag;

    private readonly record struct PanelScrollbarDrag(PanelSide Side, ScrollBarDragState DragState);

    private enum ConsoleViewportChange
    {
        None,
        OriginOnly,
        Size,
    }

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
        _commandContext   = new ApplicationCommandContext(this);
        _functionKeyBindings = _functionKeyBindingProvider.GetBindings();

        if (_watcher != null)
            _watcher.Changed += OnFileSystemChanged;
        _dirSizeCalc.Completed += OnDirSizeCalculated;
        _dirSizeCalc.Progress  += OnDirSizeProgress;
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

    internal ScreenRenderer CommandScreen => _screen;

    internal PanelController CommandPanelController => _ctrl;

    internal IFileLauncher CommandFileLauncher => _fileLauncher;

    internal IFileOperationService CommandFileOperations => _fileOps;

    internal ISearchService CommandSearchService => _searchService;

    internal IHistoryStore CommandHistory => _history;

    internal UserMenuStore CommandUserMenu => _userMenu;

    internal AppSettingsAlias CommandSettings => _settings;

    internal IVolumeService? CommandVolumeService => _volumeService;

    internal FilePanelState CommandLeftPanel => _left;

    internal FilePanelState CommandRightPanel => _right;

    internal CommandLineState CommandLine => _cmdLine;

    internal bool CanSaveSettings => _saveSettings is not null;

    internal ConsolePalette CommandPalette
    {
        get => _palette;
        set => _palette = value;
    }

    internal PanelSide ActiveSide
    {
        get => _active;
        set => _active = value;
    }

    internal bool Running
    {
        get => _running;
        set => _running = value;
    }

    internal bool QuickView
    {
        get => _quickView;
        set => _quickView = value;
    }

    internal PanelViewMode LeftViewMode
    {
        get => _leftViewMode;
        set => _leftViewMode = value;
    }

    internal PanelViewMode RightViewMode
    {
        get => _rightViewMode;
        set => _rightViewMode = value;
    }

    internal IFileHighlightService? HighlightService
    {
        get => _highlightService;
        set => _highlightService = value;
    }

    internal AppSettingsAlias.PanelOptionsSettings PanelOptions => _settings.Panels.Options;

    internal void SaveSettings() => _saveSettings?.Invoke();

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

            RenderUntilStable();

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
                        RenderUntilStable();
                    continue;
                }

                bool isResize     = false;
                bool shouldRender = false;

                switch (evt)
                {
                    case ConsoleResizeInputEvent:
                    {
                        var viewportChange = GetConsoleViewportChange();
                        if (!AcceptHiddenViewportScroll(viewportChange) &&
                            viewportChange != ConsoleViewportChange.None)
                        {
                            isResize = true;
                            shouldRender = true;
                        }
                        break;
                    }
                    case KeyConsoleInputEvent { Key: var key }:
                    {
                        bool scrolledHiddenViewport = ScrollHiddenViewportToBottomForInput();
                        bool functionKeyLayerChanged = SetFunctionKeyLayer(key.Modifiers);
                        shouldRender = HandleKey(key) || scrolledHiddenViewport || functionKeyLayerChanged;
                        break;
                    }
                    case ModifierKeyConsoleInputEvent { Modifiers: var modifiers }:
                        shouldRender = SetFunctionKeyLayer(modifiers);
                        break;
                    case MouseConsoleInputEvent mouseEvt:
                    {
                        bool scrolledHiddenViewport = ScrollHiddenViewportToBottomForInput();
                        shouldRender = HandleMouse(mouseEvt) || scrolledHiddenViewport;
                        break;
                    }
                }

                if (!shouldRender)
                {
                    var viewportChange = GetConsoleViewportChange();
                    if (!AcceptHiddenViewportScroll(viewportChange) &&
                        viewportChange != ConsoleViewportChange.None)
                    {
                        isResize = true;
                        shouldRender = true;
                    }
                }

                if (_running && shouldRender)
                {
                    if (_panelsVisible)
                        RenderUntilStable();
                    else
                    {
                        if (isResize)
                            RestoreUnderlayForHiddenScreen();
                        RenderCommandLineOnlyUntilStable();
                    }
                }
            }

            _screen.ClearScreen();
        }
        finally
        {
            _dirSizeCalc.Dispose();
            _screen.SetCursorVisible(true);
        }
    }

    // ── quick view dir size ───────────────────────────────────────────────────

    private void UpdateQuickViewDirSize()
    {
        if (!_quickView) { _dirSizeCalc.Cancel(); return; }

        var item = _active == PanelSide.Left ? _ctrl.CurrentItem(_left) : _ctrl.CurrentItem(_right);
        if (item is not { IsDirectory: true, IsParentDirectory: false })
        {
            _dirSizeCalc.Cancel();
            _quickViewDirState = null;
            _quickViewDirPath  = null;
            return;
        }

        if (_quickViewDirPath == item.FullPath) return;

        _quickViewDirPath  = item.FullPath;
        _quickViewDirState = null;
        _dirSizeCalc.Start(item.FullPath);
    }

    private void OnDirSizeProgress(string path, DirectorySizeState state)
        => OnDirSizeUpdate(path, state);

    private void OnDirSizeCalculated(string path, DirectorySizeState state)
        => OnDirSizeUpdate(path, state);

    private void OnDirSizeUpdate(string path, DirectorySizeState state)
    {
        if (_quickViewDirPath != path) return;
        _quickViewDirState = state;
        // Signal the input loop to wake up and repaint.
        // Do NOT replace the CTS here — just cancel the current one.
        // ResetRefreshCts() will create a fresh CTS before the next ReadInput.
        _refreshCts.Cancel();
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private void Render()
    {
        UpdateQuickViewDirSize();
        _screen.SetRenderingOutputMode(true);
        using var frame = _screen.BeginFrame();
        _screen.SetCursorVisible(false);

        var viewport = _screen.FrameViewport;
        var size   = viewport.Size;
        _lastRenderViewport = viewport;
        int panelH = PanelHeight(size);
        int leftW  = size.Width / 2;
        int rightW = size.Width - leftW;

        var panelRenderer = new PanelRenderer(_screen, _palette, _highlightService, PanelOptions);

        if (_quickView)
        {
            if (_active == PanelSide.Left)
            {
                var item = _ctrl.CurrentItem(_left);
                panelRenderer.Render(new Rect(0,     0, leftW,  panelH), _left, true, _leftViewMode);
                new QuickViewRenderer(_screen, _palette).Render(
                    new Rect(leftW, 0, rightW, panelH),
                    item,
                    item is { IsDirectory: true } ? _quickViewDirState : null);
            }
            else
            {
                var item = _ctrl.CurrentItem(_right);
                new QuickViewRenderer(_screen, _palette).Render(
                    new Rect(0,     0, leftW,  panelH),
                    item,
                    item is { IsDirectory: true } ? _quickViewDirState : null);
                panelRenderer.Render(new Rect(leftW, 0, rightW, panelH), _right, true, _rightViewMode);
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
        RenderCommandCompletion(size, panelH);

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

        var viewport = _screen.FrameViewport;
        var size = viewport.Size;
        _lastRenderViewport = viewport;

        int row = CommandLineRow(size);
        var cmdRenderer = new CommandLineRenderer(_screen, _palette);
        cmdRenderer.Render(row, size.Width, ActiveState.CurrentDirectory, _cmdLine);
        PositionCommandCursor(cmdRenderer, size, row);
    }

    private void RenderCommandLineOnlyUntilStable()
    {
        while (_running)
        {
            RenderCommandLineOnly();
            if (!_screen.FrameWasInterrupted)
            {
                _screen.DrainResizeEvents();
                break;
            }
        }
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
            .Where(binding =>
                binding.Layer == _functionKeyLayer &&
                CanExecuteFunctionKeyCommand(binding.CommandId))
            .Select(binding => new FunctionKeyBarItem(binding.KeyNumber, binding.Label))
            .ToArray();

        new FunctionKeyBarRenderer(_screen, _palette).Render(size.Height - 1, size.Width, items);
    }

    private void RenderCommandCompletion(ConsoleSize size, int commandLineRow)
    {
        if (!_commandCompletionVisible)
            return;

        new CommandHistoryCompletionRenderer(_screen, _palette).Render(
            commandLineRow,
            size.Width,
            _commandCompletionMatches,
            _commandCompletionSelectedIndex,
            _commandCompletionFirstVisibleIndex);
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

    private ConsoleViewportChange GetConsoleViewportChange()
    {
        if (!_lastRenderViewport.HasValue)
            return ConsoleViewportChange.None;

        var viewport = _screen.GetViewport();
        var last = _lastRenderViewport.Value;
        if (viewport == last)
            return ConsoleViewportChange.None;

        return viewport.Width == last.Width && viewport.Height == last.Height
            ? ConsoleViewportChange.OriginOnly
            : ConsoleViewportChange.Size;
    }

    private bool AcceptHiddenViewportScroll(ConsoleViewportChange viewportChange)
    {
        if (_panelsVisible || viewportChange != ConsoleViewportChange.OriginOnly)
            return false;

        _lastRenderViewport = _screen.GetViewport();
        return true;
    }

    private bool ScrollHiddenViewportToBottomForInput()
    {
        if (_panelsVisible)
            return false;

        bool scrolled = _screen.TryScrollViewportToBottom();
        if (!scrolled)
            return false;

        CaptureUnderlay();
        _lastRenderViewport = _underlay?.Viewport ?? _screen.GetViewport();
        return scrolled;
    }

    private static int PanelHeight(ConsoleSize size) => Math.Max(0, size.Height - 2);

    private static int CommandLineRow(ConsoleSize size) => Math.Max(0, size.Height - 2);

    /// <summary>
    /// Renders the screen, retrying if the console was resized mid-frame.
    /// Loops until a complete, uninterrupted frame is flushed.
    /// </summary>
    private void RenderUntilStable()
    {
        int attempt = 0;
        while (_running)
        {
            attempt++;
            Render();
            if (!_screen.FrameWasInterrupted)
            {
                _screen.DrainResizeEvents();
                break;
            }
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

    internal void ResetFunctionKeyLayer() => _functionKeyLayer = FunctionKeyLayer.Plain;

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
        var viewport = _screen.GetViewport();
        _underlay = _screen.Capture(new Rect(0, 0, viewport.Width, viewport.Height));
    }

    /// <summary>
    /// Toggles panel visibility.
    /// Hide: restores the last captured underlay so the user sees shell output.
    /// Show: Render() will be called by the main loop.
    /// </summary>
    private bool TogglePanels()
    {
        _panelsVisible = !_panelsVisible;
        HideCommandCompletion(temporarily: false);
        ResetHiddenCommandHistoryBrowsing();

        if (!_panelsVisible)
        {
            _screen.SetCursorVisible(true);
            RestoreUnderlayForHiddenScreen();
            RenderCommandLineOnlyUntilStable();
            return false;
        }

        _screen.TryScrollViewportToBottom();
        _lastRenderViewport = _screen.GetViewport();
        return true;
    }

    private void RestoreUnderlayForHiddenScreen()
    {
        _screen.SetRenderingOutputMode(false);
        RestoreOrClearUnderlay();
    }

    private void RestoreOrClearUnderlay()
    {
        if (_underlay is not null && UnderlayMatchesCurrentViewport())
            _screen.Restore(_underlay);
        else
            _screen.ClearScreen();
    }

    private bool UnderlayMatchesCurrentViewport()
    {
        if (_underlay is null)
            return false;

        var viewport = _screen.GetViewport();
        return _underlay.Region.X == 0 &&
               _underlay.Region.Y == 0 &&
               _underlay.Region.Width == viewport.Width &&
               _underlay.Region.Height == viewport.Height &&
               _underlay.Viewport == viewport;
    }

    // ── key handling ──────────────────────────────────────────────────────────

    internal FilePanelState ActiveState => _active == PanelSide.Left ? _left : _right;

    private PanelViewMode ActiveViewMode =>
        _active == PanelSide.Left ? _leftViewMode : _rightViewMode;

    internal int VisibleRows()
    {
        return VisibleRows(ActiveViewMode);
    }

    internal int VisibleRows(PanelSide side)
    {
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
        var mode = ActiveViewMode;
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

        if (TryHandleCommandCompletionScrollbarMouse(evt))
            return true;

        if (TryHandlePanelScrollbarDrag(evt))
            return true;

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

        if (TryHandlePanelScrollbarMouse(evt, side, state, mode, bounds, visRows))
            return true;

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
            return ExecuteRegisteredCommand(MenuCommandIds.SettingsOpenPanelSettings);

        // Alt+1 / Alt+2: view mode for active panel
        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            {
                return ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _active,
                        ViewMode = PanelViewMode.Full,
                    });
            }

            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            {
                return ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _active,
                        ViewMode = PanelViewMode.BriefTwoColumns,
                    });
            }
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
                OnVisibleCommandLineTextEdited();
                return true;

            case ConsoleKey.Backspace:
                bool hadCommandText = _cmdLine.HasText;
                if (hadCommandText)
                {
                    _cmdLine.DeleteBack();
                    OnVisibleCommandLineTextEdited();
                }
                else
                {
                    HideCommandCompletion(temporarily: false);
                    TryGoUp();
                }
                return true;

            case ConsoleKey.Escape:
                if (TryHideCommandCompletionTemporarily())
                    return true;

                if (ActiveState.SearchRequest is not null)
                {
                    CloseSearchResultsPanel(ActiveState, _active);
                    return true;
                }

                _cmdLine.Clear();
                HideCommandCompletion(temporarily: false);
                return true;

            // ── Execution ─────────────────────────────────────────────────────
            case ConsoleKey.Enter:
                if (TryAcceptCommandCompletion())
                    return true;

                if (_cmdLine.HasText) ExecuteCommand(_cmdLine.Text);
                else ExecuteRegisteredCommand(ApplicationCommandIds.OpenCurrentItem);
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
                if (TryMoveCommandCompletionSelection(-1))
                    return true;

                _ctrl.MoveCursor(ActiveState, -1, vr);
                return true;

            case ConsoleKey.DownArrow:
                if (TryMoveCommandCompletionSelection(+1))
                    return true;

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
            OnVisibleCommandLineTextEdited();
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

        if (!CanExecuteFunctionKeyCommand(binding.CommandId) && !binding.RunsWhenUnavailable)
        {
            shouldRender = true;
            return true;
        }

        shouldRender = ExecuteRegisteredCommand(binding.CommandId);
        return true;
    }

    private bool CanExecuteFunctionKeyCommand(string commandId) =>
        _commandRegistry.CanExecute(commandId, _commandContext);

    private bool ExecuteRegisteredCommand(string commandId, object? args = null) =>
        _commandRegistry.Execute(commandId, _commandContext, args).ShouldRender;

    private bool HandleHiddenCommandLineKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.MoveCursor(-1);
                return true;

            case ConsoleKey.RightArrow:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.MoveCursor(+1);
                return true;

            case ConsoleKey.Home:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.MoveToStart();
                return true;

            case ConsoleKey.End:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.MoveToEnd();
                return true;

            case ConsoleKey.Delete:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.DeleteBack();
                return true;

            case ConsoleKey.Escape:
                ResetHiddenCommandHistoryBrowsing();
                _cmdLine.Clear();
                return true;

            case ConsoleKey.Enter:
                ResetHiddenCommandHistoryBrowsing();
                if (_cmdLine.HasText)
                    ExecuteCommand(_cmdLine.Text);
                return true;

            case ConsoleKey.UpArrow:
                return BrowseHiddenCommandHistory(-1);

            case ConsoleKey.DownArrow:
                return BrowseHiddenCommandHistory(+1);

            case ConsoleKey.F10:
                _running = false;
                return false;
        }

        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            ResetHiddenCommandHistoryBrowsing();
            _cmdLine.Insert(key.KeyChar);
            return true;
        }

        return false;
    }

    private bool TryHandlePanelScrollbarDrag(MouseConsoleInputEvent evt)
    {
        if (_panelScrollbarDrag is not { } drag)
            return false;

        var state = GetPanelState(drag.Side);
        int firstVisibleIndex = state.ScrollOffset;
        ScrollBarDragState? dragState = drag.DragState;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                drag.DragState.Bounds,
                drag.DragState.TotalItems,
                drag.DragState.ViewportItems,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _panelScrollbarDrag = dragState.HasValue
            ? new PanelScrollbarDrag(drag.Side, dragState.Value)
            : null;

        _active = drag.Side;
        _ctrl.ScrollView(state, firstVisibleIndex - state.ScrollOffset, drag.DragState.ViewportItems);
        _lastLeftPanelItemClick = null;
        return true;
    }

    private bool TryHandlePanelScrollbarMouse(
        MouseConsoleInputEvent evt,
        PanelSide side,
        FilePanelState state,
        PanelViewMode mode,
        Rect bounds,
        int visibleRows)
    {
        if (!TryGetPanelScrollbarBounds(bounds, mode, out var scrollbarBounds))
            return false;

        int firstVisibleIndex = state.ScrollOffset;
        ScrollBarDragState? dragState = null;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                scrollbarBounds,
                state.Items.Count,
                visibleRows,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _panelScrollbarDrag = dragState.HasValue
            ? new PanelScrollbarDrag(side, dragState.Value)
            : null;

        _active = side;
        _ctrl.ScrollView(state, firstVisibleIndex - state.ScrollOffset, visibleRows);
        _lastLeftPanelItemClick = null;
        return true;
    }

    private bool TryGetPanelScrollbarBounds(Rect bounds, PanelViewMode mode, out Rect scrollbarBounds)
    {
        if (mode == PanelViewMode.BriefTwoColumns)
        {
            int rowsPerColumn = BriefTwoColumnsPanelRenderer.RowsPerColumn(bounds, PanelOptions);
            scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 2, 1, rowsPerColumn);
            return rowsPerColumn > 0;
        }

        int visibleRows = PanelRenderer.VisibleRows(bounds, PanelOptions);
        scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 1, 1, visibleRows);
        return visibleRows > 0;
    }

    private void OnVisibleCommandLineTextEdited()
    {
        ResetHiddenCommandHistoryBrowsing();
        _commandCompletionTemporarilyHidden = false;
        RefreshCommandCompletion();
    }

    private void RefreshCommandCompletion()
    {
        _commandCompletionMatches.Clear();
        _commandCompletionSelectedIndex = 0;
        _commandCompletionFirstVisibleIndex = 0;
        _commandCompletionScrollbarDrag = null;

        if (!_panelsVisible ||
            _commandCompletionTemporarilyHidden ||
            !HasCommandCompletionRows() ||
            string.IsNullOrWhiteSpace(_cmdLine.Text))
        {
            _commandCompletionVisible = false;
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var history = _history.GetCommandHistory();
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string command = history[i].Command;
            if (!command.StartsWith(_cmdLine.Text, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(command))
                _commandCompletionMatches.Add(command);
        }

        _commandCompletionVisible = _commandCompletionMatches.Count > 0;
        _commandCompletionFirstVisibleIndex = 0;
    }

    private bool HasCommandCompletionRows()
    {
        var size = LastRenderSizeOrCurrent();
        return CommandCompletionVisibleRows(size) > 0;
    }

    private ConsoleSize LastRenderSizeOrCurrent() =>
        _lastRenderViewport?.Size ?? _screen.GetSize();

    private static int CommandCompletionVisibleRows(ConsoleSize size)
    {
        int rowsAboveCommandLine = CommandLineRow(size) - 2;
        return Math.Max(0, Math.Min(MaxCommandCompletionRows, rowsAboveCommandLine));
    }

    private bool TryHandleCommandCompletionScrollbarMouse(MouseConsoleInputEvent evt)
    {
        if (!_commandCompletionVisible && !_commandCompletionScrollbarDrag.HasValue)
            return false;

        var size = LastRenderSizeOrCurrent();
        int visibleRows = CommandCompletionVisibleRows(size);
        if (visibleRows <= 0 || _commandCompletionMatches.Count <= visibleRows)
            return false;

        int height = visibleRows + 2;
        int commandLineRow = CommandLineRow(size);
        var scrollbarBounds = new Rect(size.Width - 1, commandLineRow - height + 1, 1, visibleRows);
        int firstVisibleIndex = _commandCompletionFirstVisibleIndex;
        var dragState = _commandCompletionScrollbarDrag;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                scrollbarBounds,
                _commandCompletionMatches.Count,
                visibleRows,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _commandCompletionScrollbarDrag = dragState;
        _commandCompletionFirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            _commandCompletionMatches.Count,
            visibleRows);
        ClampCommandCompletionSelectionToViewport(visibleRows);
        return true;
    }

    private void ClampCommandCompletionSelectionToViewport(int visibleRows)
    {
        if (_commandCompletionMatches.Count == 0)
        {
            _commandCompletionSelectedIndex = 0;
            _commandCompletionFirstVisibleIndex = 0;
            return;
        }

        int lastVisibleIndex = Math.Min(
            _commandCompletionMatches.Count - 1,
            _commandCompletionFirstVisibleIndex + visibleRows - 1);
        _commandCompletionSelectedIndex = Math.Clamp(
            _commandCompletionSelectedIndex,
            _commandCompletionFirstVisibleIndex,
            lastVisibleIndex);
    }

    private bool TryMoveCommandCompletionSelection(int delta)
    {
        if (!_commandCompletionVisible || _commandCompletionMatches.Count == 0 || !HasCommandCompletionRows())
            return false;

        _commandCompletionSelectedIndex = Math.Clamp(
            _commandCompletionSelectedIndex + delta,
            0,
            _commandCompletionMatches.Count - 1);
        int visibleRows = CommandCompletionVisibleRows(LastRenderSizeOrCurrent());
        _commandCompletionFirstVisibleIndex = ScrollStateCalculator.EnsureIndexVisible(
            _commandCompletionSelectedIndex,
            _commandCompletionFirstVisibleIndex,
            visibleRows);
        _commandCompletionFirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            _commandCompletionFirstVisibleIndex,
            _commandCompletionMatches.Count,
            visibleRows);
        return true;
    }

    private bool TryAcceptCommandCompletion()
    {
        if (!_commandCompletionVisible || _commandCompletionMatches.Count == 0 || !HasCommandCompletionRows())
            return false;

        _cmdLine.SetText(_commandCompletionMatches[_commandCompletionSelectedIndex]);
        HideCommandCompletion(temporarily: false);
        ResetHiddenCommandHistoryBrowsing();
        return true;
    }

    private bool TryHideCommandCompletionTemporarily()
    {
        if (!_commandCompletionVisible)
            return false;

        HideCommandCompletion(temporarily: true);
        return true;
    }

    internal void HideCommandCompletion(bool temporarily)
    {
        _commandCompletionVisible = false;
        _commandCompletionTemporarilyHidden = temporarily;
        _commandCompletionMatches.Clear();
        _commandCompletionSelectedIndex = 0;
        _commandCompletionFirstVisibleIndex = 0;
        _commandCompletionScrollbarDrag = null;
    }

    private bool BrowseHiddenCommandHistory(int direction)
    {
        var history = _history.GetCommandHistory();
        if (history.Count == 0)
            return true;

        if (_hiddenCommandHistoryIndex is null)
        {
            _hiddenCommandHistoryIndex = direction < 0 ? history.Count - 1 : 0;
        }
        else
        {
            _hiddenCommandHistoryIndex = Math.Clamp(
                _hiddenCommandHistoryIndex.Value + direction,
                0,
                history.Count - 1);
        }

        _cmdLine.SetText(history[_hiddenCommandHistoryIndex.Value].Command);
        return true;
    }

    internal void ResetHiddenCommandHistoryBrowsing()
    {
        _hiddenCommandHistoryIndex = null;
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

    internal bool OpenTopMenu()
    {
        _menuController.HandleKey(
            new ConsoleKeyInfo('\0', ConsoleKey.F9, shift: false, alt: false, control: false),
            BuildMenuDefinition(),
            _active);
        return true;
    }

    internal FilePanelState PassiveState => _active == PanelSide.Left ? _right : _left;

    internal void OpenSearchResultsPanel(
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

    private void CloseSearchResultsPanel(FilePanelState state, PanelSide side)
    {
        var rootPath = state.SearchRequest!.RootPath;
        state.SearchRequest = null;
        state.SearchWasCancelled = false;
        state.ShowCurrentItemFullPath = false;
        state.DisplayTitle = null;
        _ctrl.LoadDirectory(state, rootPath, PanelOptions);
        StartWatching(state, side);
    }

    internal void GoToSearchResult(FilePanelState state, PanelSide side, SearchResultItem result)
    {
        GoToSearchResult(
            state,
            side,
            result.FullPath,
            result.Name,
            result.Kind == SearchResultItemKind.Directory);
    }

    internal void GoToSearchResult(FilePanelState state, PanelSide side, FilePanelItem result)
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

    internal FileOperationOptions BuildFileOperationOptions() =>
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

    private MenuCommandResult ExecuteMenuCommand(MenuCommandRequest request)
    {
        return _commandRegistry
            .Execute(request.CommandId, _commandContext, request.Args)
            .ToMenuCommandResult();
    }

    internal FilePanelState GetPanelState(PanelSide side) =>
        side == PanelSide.Left ? _left : _right;

    // ── file highlighting ─────────────────────────────────────────────────────

    internal static IFileHighlightService? CreateHighlightService(AppSettingsAlias settings)
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

    internal void ExecuteCommand(string command)
    {
        string workDir = ActiveState.CurrentDirectory;
        _cmdLine.Clear();
        HideCommandCompletion(temporarily: false);
        ResetHiddenCommandHistoryBrowsing();

        ExecuteInCurrentConsole(workDir, command, () => _shell.Execute(command, workDir));

        _history.AddCommand(new CommandHistoryItem
        {
            Command          = command,
            WorkingDirectory = workDir,
        });
    }

    internal void ExecuteInCurrentConsole(string workDir, string displayCommand, Action execute)
    {
        bool showPanelsAfterCommand = _panelsVisible;

        ShowShellUnderlayForCommand();
        PrintExecutedCommandPrompt(workDir, displayCommand);

        try
        {
            execute();
        }
        finally
        {
            MoveShellOutputAbovePromptArea();
            PrintInputPrompt(workDir);

            // Capture shell output NOW, before Render() paints panels over it.
            // This snapshot is what Ctrl+O will restore.
            CaptureUnderlay();

            RefreshPanels();
            _panelsVisible = showPanelsAfterCommand;
            // Stable rendering is called by the main loop.
        }
    }

    private void MoveShellOutputAbovePromptArea()
    {
        var size = _screen.GetSize();
        if (size.Width <= 0 || size.Height <= 0)
            return;

        int cursorRow = SysConsole.CursorTop - SysConsole.WindowTop;
        if (cursorRow < CommandLineRow(size))
            return;

        _screen.SetRenderingOutputMode(false);
        SysConsole.ResetColor();
        SysConsole.WriteLine();
        SysConsole.WriteLine();
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

    internal void OpenPanelItem(FilePanelState state, PanelSide side, FilePanelItem item)
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
            string workDir = ActiveState.CurrentDirectory;
            if (_fileLauncher.GetLaunchMode(item.FullPath) == FileLaunchMode.CurrentConsole)
            {
                ExecuteInCurrentConsole(
                    workDir,
                    item.FullPath,
                    () => _fileLauncher.OpenFile(item.FullPath, workDir));
                return;
            }

            _fileLauncher.OpenFile(item.FullPath, workDir);
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

    internal void RefreshPanels()
    {
        SafeRefresh(_left,  VisibleRows(PanelSide.Left));
        SafeRefresh(_right, VisibleRows(PanelSide.Right));
    }

    internal void RefreshPanelsAfterFileOperation()
    {
        RefreshPanelAfterFileOperation(_left, PanelSide.Left);
        RefreshPanelAfterFileOperation(_right, PanelSide.Right);
    }

    private void RefreshPanelAfterFileOperation(FilePanelState state, PanelSide side)
    {
        if (state.SearchRequest is not null)
        {
            RefreshSearchResultsSummary(state);
            return;
        }

        SafeRefresh(state, VisibleRows(side));
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

    internal void SafeRefresh(FilePanelState state, int visibleRows)
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

    internal void SetPanelSortMode(FilePanelState state, SortMode mode, int visibleRows)
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

    internal void SortVirtualPanel(FilePanelState state, string? keepCursorPath)
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

    internal static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
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

    internal ConsoleKeyInfo? TryReadConsoleKeyForCommand() =>
        TryReadConsoleKey(out var key) ? key : null;

    internal void ShowReadOnlyPanelMessage(string action)
    {
        new MessageDialog(_screen, _palette).Show(
            action,
            "Search results are read-only.");
    }

    // ── auto-refresh ──────────────────────────────────────────────────────────

    internal void StartWatching(FilePanelState state, PanelSide side)
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
        _refreshCts.Cancel();
    }

    private void ResetRefreshCts()
    {
        var old = Interlocked.Exchange(ref _refreshCts, new CancellationTokenSource());
        old.Dispose();
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

    private readonly record struct PanelItemClick(
        PanelSide PanelSide,
        int ItemIndex,
        string FullPath);
}
