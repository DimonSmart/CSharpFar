using CSharpFar.App.Dialogs;
using CSharpFar.App.Commands;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.HitTesting;
using CSharpFar.App.Menu;
using CSharpFar.App.Plugins;
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
using CSharpFar.FileSystem;
using CSharpFar.Plugin.Abstractions;
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
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly PluginManager _pluginManager;
    private readonly IHistoryStore _history;
    private readonly AppSettingsAlias _settings;
    private readonly UserMenuStore _userMenu;
    private readonly Action? _saveSettings;
    private readonly IVolumeService? _volumeService;

    private readonly FilePanelState _left;
    private readonly FilePanelState _right;
    private readonly CommandLineState _cmdLine = new();
    private readonly List<string> _commandCompletionMatches = [];
    private PanelQuickSearchState? _panelQuickSearch;

    private PanelSide     _active        = PanelSide.Left;
    private bool          _running       = true;
    private HiddenPanels  _hiddenPanels;
    private bool          _commandCompletionVisible;
    private bool          _commandCompletionTemporarilyHidden;
    private int           _commandCompletionSelectedIndex;
    private int           _commandCompletionFirstVisibleIndex;
    private ScrollBarDragState? _commandCompletionScrollbarDrag;
    private int?          _commandHistoryNavigationIndex;
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
    private readonly ApplicationCommandRegistry _commandRegistry;
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

    [Flags]
    private enum HiddenPanels
    {
        None = 0,
        Left = 1,
        Right = 2,
        Both = Left | Right,
    }

    private enum CommandHistoryNavigationStart
    {
        Oldest,
        Newest,
    }

    private enum PanelQuickSearchKeyResult
    {
        NotHandled,
        Handled,
        CloseAndContinue,
    }

    private sealed class PanelQuickSearchState
    {
        public PanelQuickSearchState(PanelSide panelSide, char firstCharacter)
        {
            PanelSide = panelSide;
            SearchText = NormalizeQuickSearchCharacter(firstCharacter).ToString();
        }

        public PanelSide PanelSide { get; }

        public string SearchText { get; private set; }

        public void Append(char ch) =>
            SearchText += NormalizeQuickSearchCharacter(ch);

        public bool RemoveLastCharacter()
        {
            if (SearchText.Length == 0)
                return false;

            SearchText = SearchText[..^1];
            return SearchText.Length > 0;
        }
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
        ISearchService?              searchService     = null,
        FilePanelSourceRegistry?     sourceRegistry    = null,
        ICredentialStore?            credentialStore   = null,
        IReadOnlyList<ICSharpFarPlugin>? plugins = null,
        string?                      configDirectory   = null)
    {
        _screen       = screen;
        _fs           = fs;
        var sortSvc   = new PanelSortService();
        _sourceRegistry = sourceRegistry ?? new FilePanelSourceRegistry([new LocalFilePanelSource(fs)]);
        var viewBuilder = new PanelViewBuilder(
            fs,
            sortSvc,
            volumeInfoService,
            mountPoints: mountPointService,
            sources: _sourceRegistry);
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
        _palette          = PaletteRegistry.Resolve(_settings.Ui.Palette);
        _pluginManager    = new PluginManager(
            plugins ?? [],
            new PluginStartupInfo
            {
                Ui = new PluginUiServices
                {
                    Screen = _screen,
                    Palette = () => _palette,
                },
                Settings = new PluginSettingsService(
                    configDirectory ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "CSharpFar")),
                Credentials = credentialStore,
                Panels = new ApplicationPluginPanelHost(this),
            });
        _commandRegistry = ApplicationCommandRegistry.CreateDefault();
        _commandContext   = new ApplicationCommandContext(this);
        _functionKeyBindings = _functionKeyBindingProvider.GetBindings();

        if (_watcher != null)
            _watcher.Changed += OnFileSystemChanged;
        _dirSizeCalc.Completed += OnDirSizeCalculated;
        _dirSizeCalc.Progress  += OnDirSizeProgress;
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

    internal IReadOnlyList<PluginMenuProjection> PluginDiskMenuItems =>
        _pluginManager.DiskMenuItems;

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
        set => SetActiveSide(value);
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
                    if (_running && HasVisiblePanels)
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
                    if (HasVisiblePanels)
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
        if (HasHiddenPanels)
            RestoreUnderlayForHiddenScreen();

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
        var leftBounds  = new Rect(0,     0, leftW,  panelH);
        var rightBounds = new Rect(leftW, 0, rightW, panelH);
        _leftBounds  = leftBounds;
        _rightBounds = rightBounds;

        if (_quickView)
        {
            if (_active == PanelSide.Left)
            {
                var item = _ctrl.CurrentItem(_left);
                if (IsPanelVisible(PanelSide.Left))
                    panelRenderer.Render(leftBounds, _left, true, _leftViewMode);
                if (IsPanelVisible(PanelSide.Right))
                {
                    new QuickViewRenderer(_screen, _palette).Render(
                        rightBounds,
                        item,
                        item is { IsDirectory: true } ? _quickViewDirState : null);
                }
            }
            else
            {
                var item = _ctrl.CurrentItem(_right);
                if (IsPanelVisible(PanelSide.Left))
                {
                    new QuickViewRenderer(_screen, _palette).Render(
                        leftBounds,
                        item,
                        item is { IsDirectory: true } ? _quickViewDirState : null);
                }
                if (IsPanelVisible(PanelSide.Right))
                    panelRenderer.Render(rightBounds, _right, true, _rightViewMode);
            }
        }
        else
        {
            if (IsPanelVisible(PanelSide.Left))
                panelRenderer.Render(leftBounds, _left, _active == PanelSide.Left, _leftViewMode);
            if (IsPanelVisible(PanelSide.Right))
                panelRenderer.Render(rightBounds, _right, _active == PanelSide.Right, _rightViewMode);
        }

        if (IsPanelVisible(PanelSide.Right))
            RenderClock(size);

        var cmdRenderer = new CommandLineRenderer(_screen, _palette);
        cmdRenderer.Render(panelH, size.Width, ActiveState.CurrentDirectory, _cmdLine);
        RenderCommandCompletion(size, panelH);

        RenderFunctionKeyBar(size);

        RenderMenuOverlay(size);

        if (_menuState.OpenState == MenuOpenState.Closed)
        {
            if (_panelQuickSearch is not null)
            {
                if (!RenderPanelQuickSearch())
                    _screen.SetCursorVisible(false);
            }
            else
            {
                PositionCommandCursor(cmdRenderer, size, panelH);
            }
        }
        else
            _screen.SetCursorVisible(false);
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

    private bool RenderPanelQuickSearch()
    {
        if (_panelQuickSearch is not { } quickSearch ||
            !IsPanelVisible(quickSearch.PanelSide))
        {
            return false;
        }

        var bounds = quickSearch.PanelSide == PanelSide.Left ? _leftBounds : _rightBounds;
        return new PanelQuickSearchRenderer(_screen, _palette)
            .Render(bounds, quickSearch.SearchText);
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
        if (HasVisiblePanels || viewportChange != ConsoleViewportChange.OriginOnly)
            return false;

        _lastRenderViewport = _screen.GetViewport();
        return true;
    }

    private bool ScrollHiddenViewportToBottomForInput()
    {
        if (HasVisiblePanels)
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

    private static bool IsPlainControlEnter(ConsoleKeyInfo key) =>
        HasOnlyControlModifier(key) && key.Key == ConsoleKey.Enter;

    private static bool IsPlainControlOpenBracket(ConsoleKeyInfo key) =>
        IsPlainControlBracket(key, ConsoleKey.Oem4, '[', '\u001b');

    private static bool IsPlainControlCloseBracket(ConsoleKeyInfo key) =>
        IsPlainControlBracket(key, ConsoleKey.Oem6, ']', '\u001d');

    private static bool IsPlainControlBracket(
        ConsoleKeyInfo key,
        ConsoleKey consoleKey,
        char printableChar,
        char controlChar)
    {
        if (!HasOnlyControlModifier(key))
            return false;

        return key.Key == consoleKey ||
               key.KeyChar == printableChar ||
               (key.Key != ConsoleKey.Escape && key.KeyChar == controlChar);
    }

    private static bool HasOnlyControlModifier(ConsoleKeyInfo key)
    {
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasAlt     = (key.Modifiers & ConsoleModifiers.Alt)     != 0;
        bool hasShift   = (key.Modifiers & ConsoleModifiers.Shift)   != 0;

        return hasControl && !hasAlt && !hasShift;
    }

    private static string QuoteCommandLineInsertion(string text) =>
        text.Contains(' ') ? $"\"{text}\"" : text;

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
            PluginMenuItems = _pluginManager.PluginMenuItems,
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

    // ── panel visibility ──────────────────────────────────────────────────────

    private bool HasHiddenPanels => _hiddenPanels != HiddenPanels.None;

    private bool HasVisiblePanels => _hiddenPanels != HiddenPanels.Both;

    private bool IsPanelVisible(PanelSide side) =>
        (_hiddenPanels & HiddenPanelFlag(side)) == 0;

    private static HiddenPanels HiddenPanelFlag(PanelSide side) =>
        side == PanelSide.Left ? HiddenPanels.Left : HiddenPanels.Right;

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private void SetActiveSide(PanelSide side)
    {
        if (_active == side)
            return;

        ClosePanelQuickSearch();
        _active = side;
    }

    private void EnsureActivePanelVisible()
    {
        if (IsPanelVisible(_active))
            return;

        var otherSide = OtherPanelSide(_active);
        if (IsPanelVisible(otherSide))
            SetActiveSide(otherSide);
    }

    private void ClosePanelQuickSearch() =>
        _panelQuickSearch = null;

    private void ClosePanelQuickSearchForPanel(PanelSide side)
    {
        if (_panelQuickSearch?.PanelSide == side)
            ClosePanelQuickSearch();
    }

    private void ClosePanelQuickSearchForState(FilePanelState state) =>
        ClosePanelQuickSearchForPanel(PanelSideForState(state));

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
        ClosePanelQuickSearch();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        _panelScrollbarDrag = null;

        if (_hiddenPanels == HiddenPanels.Both)
        {
            _hiddenPanels = HiddenPanels.None;
            _screen.TryScrollViewportToBottom();
            _lastRenderViewport = _screen.GetViewport();
            return true;
        }

        _hiddenPanels = HiddenPanels.Both;
        _screen.SetCursorVisible(true);
        RestoreUnderlayForHiddenScreen();
        RenderCommandLineOnlyUntilStable();
        return false;
    }

    internal bool TogglePanelVisibility(PanelSide side)
    {
        ClosePanelQuickSearch();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        _panelScrollbarDrag = null;

        var flag = HiddenPanelFlag(side);
        bool wasHidden = (_hiddenPanels & flag) != 0;

        if (wasHidden)
        {
            _hiddenPanels &= ~flag;
            _screen.TryScrollViewportToBottom();
            _lastRenderViewport = _screen.GetViewport();
        }
        else
        {
            _hiddenPanels |= flag;
        }

        EnsureActivePanelVisible();

        if (_hiddenPanels == HiddenPanels.Both)
        {
            _screen.SetCursorVisible(true);
            RestoreUnderlayForHiddenScreen();
            RenderCommandLineOnlyUntilStable();
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
        if (_panelQuickSearch is not null)
            ClosePanelQuickSearch();

        if (!HasVisiblePanels)
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
        bool inLeft  = IsPanelVisible(PanelSide.Left)  && _leftBounds.Contains(evt.X,  evt.Y);
        bool inRight = IsPanelVisible(PanelSide.Right) && _rightBounds.Contains(evt.X, evt.Y);
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

        if (evt.Button == MouseButton.Left &&
            (evt.Kind == MouseEventKind.Down || evt.Kind == MouseEventKind.Click) &&
            PanelErrorRenderer.HitTestRetry(evt.X, evt.Y, bounds, state, mode, PanelOptions))
        {
            SetActiveSide(side);
            SafeRefresh(state, visRows);
            _lastLeftPanelItemClick = null;
            return true;
        }

        // Mouse wheel: scroll the panel under cursor
        if (evt.Kind == MouseEventKind.Wheel)
        {
            SetActiveSide(side);
            int delta = evt.Button == MouseButton.WheelUp ? -3 : 3;
            _ctrl.ScrollView(state, delta, visRows);
            return true;
        }

        // Right click: activate panel, move cursor, optionally toggle selection
        if (evt.Button == MouseButton.Right && evt.Kind == MouseEventKind.Down)
        {
            _lastLeftPanelItemClick = null;
            SetActiveSide(side);
            int? itemIdx = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
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
            SetActiveSide(side);
            int? itemIdx = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
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
            SetActiveSide(side);
            int? itemIdx = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
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

    private int? HitTestPanelItemForMouse(
        MouseConsoleInputEvent evt,
        PanelSide side,
        Rect bounds,
        FilePanelState state,
        PanelViewMode mode)
    {
        int x = evt.X;

        // Before panels had separate frames, the first usable right-panel column was
        // where the new right-panel left border is now. Keep mouse targeting tolerant.
        if (side == PanelSide.Right && x == bounds.X)
            x++;

        return PanelHitTester.HitTestItem(x, evt.Y, bounds, state, mode, PanelOptions);
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
            if (!HasVisiblePanels)
            {
                _menuController.Close();
                return true;
            }

            return _menuController.HandleKey(key, BuildMenuDefinition(), _active);
        }

        var quickSearchResult = HandlePanelQuickSearchKey(key);
        if (quickSearchResult == PanelQuickSearchKeyResult.Handled)
            return true;

        // Ctrl+O: toggle panels — check before printable-char routing
        if (IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return TogglePanels();

        if (TryHandleFarCommandLineShortcut(key))
            return true;

        if (!HasVisiblePanels)
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
            SelectAllCommandLineTextOrPanelItems();
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

        // Ctrl+* - invert selection
        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        if (isControlShortcut)
        {
            switch (key.Key)
            {
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

        if (TryStartPanelQuickSearch(key))
            return true;

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
                var otherSide = OtherPanelSide(_active);
                if (IsPanelVisible(otherSide))
                    SetActiveSide(otherSide);
                else
                    EnsureActivePanelVisible();
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

    private PanelQuickSearchKeyResult HandlePanelQuickSearchKey(ConsoleKeyInfo key)
    {
        if (_panelQuickSearch is not { } quickSearch)
            return PanelQuickSearchKeyResult.NotHandled;

        if (!HasVisiblePanels ||
            !IsPanelVisible(quickSearch.PanelSide) ||
            quickSearch.PanelSide != _active)
        {
            ClosePanelQuickSearch();
            return PanelQuickSearchKeyResult.NotHandled;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            ClosePanelQuickSearch();
            return PanelQuickSearchKeyResult.Handled;
        }

        if (key.Key == ConsoleKey.Backspace &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
        {
            if (quickSearch.RemoveLastCharacter())
                MovePanelQuickSearchCursor();
            else
                ClosePanelQuickSearch();

            return PanelQuickSearchKeyResult.Handled;
        }

        if (TryGetPanelQuickSearchAppendCharacter(key, out char ch))
        {
            quickSearch.Append(ch);
            MovePanelQuickSearchCursor();
            return PanelQuickSearchKeyResult.Handled;
        }

        ClosePanelQuickSearch();
        return PanelQuickSearchKeyResult.CloseAndContinue;
    }

    private bool TryStartPanelQuickSearch(ConsoleKeyInfo key)
    {
        if (!TryGetPanelQuickSearchActivationCharacter(key, out char ch))
            return false;

        _panelQuickSearch = new PanelQuickSearchState(_active, ch);
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();
        MovePanelQuickSearchCursor();
        return true;
    }

    private bool TryGetPanelQuickSearchActivationCharacter(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if (!HasVisiblePanels ||
            !IsPanelVisible(_active) ||
            (key.Modifiers & ConsoleModifiers.Alt) == 0 ||
            (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            return false;
        }

        if (key.Key is ConsoleKey.D1 or ConsoleKey.NumPad1 or ConsoleKey.D2 or ConsoleKey.NumPad2 ||
            key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F24)
        {
            return false;
        }

        return TryGetQuickSearchCharacterFromKeyInfo(key, out ch) ||
               TryGetAltLetterQuickSearchCharacter(key, out ch);
    }

    private static bool TryGetPanelQuickSearchAppendCharacter(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
            return false;

        return TryGetQuickSearchCharacterFromKeyInfo(key, out ch);
    }

    private static bool TryGetQuickSearchCharacterFromKeyInfo(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if (key.KeyChar < ' ' || !IsQuickSearchFilenameCharacter(key.KeyChar))
            return false;

        ch = NormalizeQuickSearchCharacter(key.KeyChar);
        return true;
    }

    private static bool TryGetAltLetterQuickSearchCharacter(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if (key.Key is < ConsoleKey.A or > ConsoleKey.Z)
            return false;

        ch = (char)('a' + (int)key.Key - (int)ConsoleKey.A);
        return true;
    }

    private static bool IsQuickSearchFilenameCharacter(char ch)
    {
        if (char.IsControl(ch))
            return false;

        return Array.IndexOf(Path.GetInvalidFileNameChars(), ch) < 0;
    }

    private static char NormalizeQuickSearchCharacter(char ch) =>
        char.ToLowerInvariant(ch);

    private void MovePanelQuickSearchCursor()
    {
        if (_panelQuickSearch is not { } quickSearch)
            return;

        var state = GetPanelState(quickSearch.PanelSide);
        if (PanelController.TryFindFirstQuickSearchMatch(
                state,
                quickSearch.SearchText,
                out int itemIndex))
        {
            _ctrl.SetCursorTo(state, itemIndex, VisibleRows(quickSearch.PanelSide));
        }
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

    private bool TryHandlePanelVisibilityFunctionKey(ConsoleKeyInfo key, out bool shouldRender)
    {
        shouldRender = false;

        if (!HasOnlyControlModifier(key) ||
            key.Key is not (ConsoleKey.F1 or ConsoleKey.F2))
        {
            return false;
        }

        return TryHandleFunctionKey(key, out shouldRender);
    }

    private bool CanExecuteFunctionKeyCommand(string commandId) =>
        _commandRegistry.CanExecute(commandId, _commandContext);

    private bool ExecuteRegisteredCommand(string commandId, object? args = null) =>
        _commandRegistry.Execute(commandId, _commandContext, args).ShouldRender;

    private bool TryHandleFarCommandLineShortcut(ConsoleKeyInfo key)
    {
        if (IsPlainControlKey(key, ConsoleKey.E, '\u0005'))
            return BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

        if (IsPlainControlKey(key, ConsoleKey.X, '\u0018'))
            return BrowseCommandHistory(+1, CommandHistoryNavigationStart.Newest);

        if (IsPlainControlKey(key, ConsoleKey.F, '\u0006'))
            return InsertCurrentItemFullPathIntoCommandLine();

        if (IsPlainControlEnter(key))
            return InsertCurrentItemNameIntoCommandLine();

        if (IsPlainControlOpenBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(_left);

        if (IsPlainControlCloseBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(_right);

        return false;
    }

    private bool InsertCurrentItemNameIntoCommandLine()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null)
            return true;

        InsertTextIntoCommandLine(item.Name);
        return true;
    }

    private bool InsertCurrentItemFullPathIntoCommandLine()
    {
        var item = _ctrl.CurrentItem(ActiveState);
        if (item is null)
            return true;

        InsertTextIntoCommandLine(item.FullPath);
        return true;
    }

    private bool InsertPanelCurrentDirectoryIntoCommandLine(FilePanelState state)
    {
        InsertTextIntoCommandLine(state.CurrentDirectory);
        return true;
    }

    private void InsertTextIntoCommandLine(string text)
    {
        _cmdLine.InsertText(QuoteCommandLineInsertion(text));

        if (HasVisiblePanels)
            OnVisibleCommandLineTextEdited();
        else
            ResetCommandHistoryNavigation();
    }

    private bool HandleHiddenCommandLineKey(ConsoleKeyInfo key)
    {
        if (TryHandlePanelVisibilityFunctionKey(key, out bool shouldRender))
            return shouldRender;

        if (IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            _cmdLine.SelectAll();
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                ResetCommandHistoryNavigation();
                _cmdLine.MoveCursor(-1);
                return true;

            case ConsoleKey.RightArrow:
                ResetCommandHistoryNavigation();
                _cmdLine.MoveCursor(+1);
                return true;

            case ConsoleKey.Home:
                ResetCommandHistoryNavigation();
                _cmdLine.MoveToStart();
                return true;

            case ConsoleKey.End:
                ResetCommandHistoryNavigation();
                _cmdLine.MoveToEnd();
                return true;

            case ConsoleKey.Delete:
                ResetCommandHistoryNavigation();
                _cmdLine.DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                ResetCommandHistoryNavigation();
                _cmdLine.DeleteBack();
                return true;

            case ConsoleKey.Escape:
                ResetCommandHistoryNavigation();
                _cmdLine.Clear();
                return true;

            case ConsoleKey.Enter:
                ResetCommandHistoryNavigation();
                if (_cmdLine.HasText)
                    ExecuteCommand(_cmdLine.Text);
                return true;

            case ConsoleKey.UpArrow:
                return BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

            case ConsoleKey.DownArrow:
                return BrowseCommandHistory(+1, CommandHistoryNavigationStart.Oldest);

            case ConsoleKey.F10:
                _running = false;
                return false;
        }

        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            ResetCommandHistoryNavigation();
            _cmdLine.Insert(key.KeyChar);
            return true;
        }

        return false;
    }

    private void SelectAllCommandLineTextOrPanelItems()
    {
        if (_cmdLine.HasText)
            _cmdLine.SelectAll();
        else
            _ctrl.ToggleSelectAll(ActiveState, PanelOptions);
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

        SetActiveSide(drag.Side);
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

        SetActiveSide(side);
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
        ResetCommandHistoryNavigation();
        _commandCompletionTemporarilyHidden = false;
        RefreshCommandCompletion();
    }

    private void RefreshCommandCompletion()
    {
        _commandCompletionMatches.Clear();
        _commandCompletionSelectedIndex = 0;
        _commandCompletionFirstVisibleIndex = 0;
        _commandCompletionScrollbarDrag = null;

        if (!HasVisiblePanels ||
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
        ResetCommandHistoryNavigation();
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

    private bool BrowseCommandHistory(int direction, CommandHistoryNavigationStart start)
    {
        var history = _history.GetCommandHistory();
        if (history.Count == 0)
            return true;

        if (_commandHistoryNavigationIndex is null)
        {
            _commandHistoryNavigationIndex = start == CommandHistoryNavigationStart.Newest
                ? history.Count - 1
                : 0;
        }
        else
        {
            _commandHistoryNavigationIndex = Math.Clamp(
                _commandHistoryNavigationIndex.Value + direction,
                0,
                history.Count - 1);
        }

        _cmdLine.SetText(history[_commandHistoryNavigationIndex.Value].Command);
        HideCommandCompletion(temporarily: false);
        return true;
    }

    internal void ResetCommandHistoryNavigation()
    {
        _commandHistoryNavigationIndex = null;
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
        ClosePanelQuickSearch();
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
        ClosePanelQuickSearchForState(state);
        state.CurrentLocation = PanelLocation.SearchResult(request.RootPath);
        state.Items.Clear();
        state.Items.AddRange(results.Select(ToFilePanelItem));
        state.SelectedPaths.Clear();
        state.SelectedLocations.Clear();
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
        ClosePanelQuickSearchForPanel(side);
        var rootPath = state.SearchRequest!.RootPath;
        state.SearchRequest = null;
        state.SearchWasCancelled = false;
        state.ShowCurrentItemFullPath = false;
        state.DisplayTitle = null;
        if (_ctrl.TryLoadDirectory(state, rootPath, PanelOptions))
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
        ClosePanelQuickSearchForPanel(side);
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
            if (_ctrl.TryLoadDirectory(state, directoryPath, PanelOptions))
            {
                if (!isDirectory)
                    _ctrl.SetCursorByName(state, name, VisibleRows(side));

                _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
                StartWatching(state, side);
            }
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

    internal ApplicationCommandResult OpenPluginMenuItem(Guid pluginId, Guid itemId) =>
        HandlePluginOpenResult(
            _pluginManager.OpenFromPluginMenu(pluginId, itemId),
            _active);

    internal ApplicationCommandResult OpenPluginDiskMenuItem(Guid pluginId, Guid itemId, PanelSide panelSide)
    {
        var openFrom = panelSide == PanelSide.Left
            ? PluginOpenFrom.LeftDiskMenu
            : PluginOpenFrom.RightDiskMenu;
        return HandlePluginOpenResult(
            _pluginManager.OpenFromDiskMenu(pluginId, itemId, openFrom),
            panelSide);
    }

    internal void OpenPluginPanel(PanelSide panelSide, IPluginPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        _sourceRegistry.Add(panel);
        var panelInfo = panel.GetOpenPanelInfo();
        var state = GetPanelState(panelSide);
        _ctrl.TryLoadLocation(
            state,
            new PanelLocation(panel.SourceId, panelInfo.CurrentDirectory),
            PanelOptions);
        state.DisplayTitle = panelInfo.Title;
        state.ShowCurrentItemFullPath = true;
        QuickView = false;
        ActiveSide = panelSide;
    }

    private ApplicationCommandResult HandlePluginOpenResult(
        PluginOpenResult result,
        PanelSide defaultPanelSide)
    {
        switch (result.Kind)
        {
            case PluginOpenResultKind.OpenedPanel:
                OpenPluginPanel(defaultPanelSide, result.Panel!);
                return ApplicationCommandResult.Rendered();
            case PluginOpenResultKind.Failed:
                new MessageDialog(_screen, _palette).Show("Plugin", result.Message ?? "Plugin operation failed.");
                return ApplicationCommandResult.Rendered();
            default:
                return ApplicationCommandResult.Rendered();
        }
    }

    internal string CombinePanelPath(FilePanelState state, string name)
    {
        if (state.SourceId == PanelSourceId.Local)
            return Path.Combine(state.SourcePath, name);

        string directory = state.SourcePath.TrimEnd('/');
        return directory.Length == 0 || directory == "/"
            ? "/" + name
            : directory + "/" + name;
    }

    internal void ViewPanelFile(FilePanelState state, FilePanelItem item)
    {
        if (state.SourceId == PanelSourceId.Local)
        {
            _history.AddFile(new FileHistoryItem { Path = item.FullPath });
            new FileViewer(_screen, _palette).Show(item.FullPath);
            return;
        }

        var source = _sourceRegistry.GetSource(item.SourceId);
        string tempPath = Path.Combine(Path.GetTempPath(), "CSharpFar", Guid.NewGuid().ToString("N"), item.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        try
        {
            using (var input = source.OpenReadAsync(item.SourcePath).GetAwaiter().GetResult())
            using (var output = File.Create(tempPath))
            {
                input.CopyTo(output);
            }

            _history.AddFile(new FileHistoryItem { Path = $"{item.SourceId}:{item.SourcePath}" });
            new FileViewer(_screen, _palette).Show(tempPath);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(tempPath)!, recursive: true); }
            catch { }
        }
    }

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
        ClosePanelQuickSearch();
        _cmdLine.Clear();
        HideCommandCompletion(temporarily: false);
        ResetCommandHistoryNavigation();

        ExecuteInCurrentConsole(workDir, command, () => _shell.Execute(command, workDir));

        _history.AddCommand(new CommandHistoryItem
        {
            Command          = command,
            WorkingDirectory = workDir,
        });
    }

    internal void ExecuteInCurrentConsole(string workDir, string displayCommand, Action execute)
    {
        HiddenPanels hiddenPanelsAfterCommand = _hiddenPanels;

        ShowShellUnderlayForCommand();
        PrintExecutedCommandPrompt(workDir, displayCommand);

        try
        {
            execute();
        }
        finally
        {
            _screen.RestoreApplicationInputMode();
            MoveShellOutputAbovePromptArea();
            PrintInputPrompt(workDir);

            // Capture shell output NOW, before Render() paints panels over it.
            // This snapshot is what Ctrl+O will restore.
            CaptureUnderlay();

            RefreshPanels();
            _hiddenPanels = hiddenPanelsAfterCommand;
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

        ClosePanelQuickSearchForPanel(side);
        bool loaded = item.IsParentDirectory
            ? _ctrl.TryGoToParent(state, VisibleRows(side), PanelOptions)
            : _ctrl.TryLoadLocation(state, item.Location, PanelOptions);

        if (!loaded)
            return;

        if (state.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
        StartWatching(state, side);
    }

    private void OpenFileItem(FilePanelItem item)
    {
        if (!HasCapability(ActiveState, PanelProviderCapabilities.OpenRead))
            return;

        try
        {
            if (ActiveState.SourceId != PanelSourceId.Local)
            {
                ViewPanelFile(ActiveState, item);
                return;
            }

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
        if (!HasCapability(ActiveState, PanelProviderCapabilities.Enumerate))
            return;

        ClosePanelQuickSearchForPanel(_active);
        if (!_ctrl.TryGoToParent(ActiveState, VisibleRows(), PanelOptions))
            return;

        if (ActiveState.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = ActiveState.CurrentDirectory });
        StartWatching(ActiveState, _active);
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

        ClosePanelQuickSearchForState(state);
        if (state.SearchRequest is not null)
        {
            RefreshSearchResultsPanel(state, visibleRows);
            return;
        }

        _ctrl.TryRefreshDirectory(state, visibleRows, PanelOptions);
    }

    internal void SetPanelSortMode(FilePanelState state, SortMode mode, int visibleRows)
    {
        ClosePanelQuickSearchForState(state);
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
        ClosePanelQuickSearchForState(state);
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
            "The current panel source does not support this operation.");
    }

    // ── auto-refresh ──────────────────────────────────────────────────────────

    internal void StartWatching(FilePanelState state, PanelSide side)
    {
        if (_watcher == null || _locationService == null) return;
        if (state.SourceId != PanelSourceId.Local ||
            !HasCapability(state, PanelProviderCapabilities.Watch))
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
