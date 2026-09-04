using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Demo;

internal sealed record DemoFrame(Rect TableBounds, TableListFrame Table, int StatusY, int KeyBarY, int Width);

internal sealed class DemoApplication : UiLayer<DemoFrame>, IUiSurface
{
    private static readonly FunctionKeyBarItem[] KeyItems =
    [
        new(1, "Help"), new(2, "Create"), new(3, "Details"), new(4, "Branch"),
        new(5, "Copy"), new(6, "Dates"), new(7, "Unicode"), new(8, "Delete"), new(10, "Quit")
    ];

    private readonly ScreenRenderer _screen;
    private readonly DemoRepository _repository;
    private readonly FunctionKeyBar _keyBar = new();
    private readonly TableList<DemoCommit> _table;
    private readonly CommitWorkflows _commits;
    private readonly RepositoryWorkflows _workflows;
    private readonly ShowcaseWorkflows _showcase;
    private readonly DialogService _dialogs;
    private readonly Queue<string> _pendingCommands = new();
    private IDisposable? _themeScope;
    private string _status = "Ready — arrows/mouse select, wheel/PageUp/PageDown scroll, F9 opens menu";
    private CommitSearch _search = new("", "Any author", "All branches", false, true);

    public DemoApplication(ScreenRenderer screen, UiCompositionHost host, DemoRepository repository, ITextClipboard? clipboard = null)
    {
        _screen = screen;
        _repository = repository;
        var fields = new FormFieldFactory(new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore()));
        var dialogs = new DialogService(new ModalDialogHost(host), fields);
        _dialogs = dialogs;
        _commits = new CommitWorkflows(dialogs, fields, clipboard ?? TextCopyTextClipboard.Instance);
        _workflows = new RepositoryWorkflows(dialogs, fields, repository);
        _showcase = new ShowcaseWorkflows(dialogs);
        _table = new TableList<DemoCommit>(VisibleCommits(), new TableListDefinition<DemoCommit>
        {
            Columns =
            [
                TableColumn<DemoCommit>.Text("Hash", c => c.Hash, TableWidth.Fixed(8), emphasized: true),
                TableColumn<DemoCommit>.Text("Branch", c => c.Branch, TableWidth.Optional(24, 8, 1)),
                TableColumn<DemoCommit>.Text("Author", c => c.Author, TableWidth.Optional(18, 8, 0)),
                TableColumn<DemoCommit>.Text("Date", FormatDate, TableWidth.Optional(16, 10, 2)),
                TableColumn<DemoCommit>.Text("Subject", c => c.IsMerge ? "↪ " + c.Subject : c.Subject, TableWidth.Flexible(56, 4))
            ]
        }, appearance: ListAppearance.Menu);

        Menu = new TopMenu(
            () => true, BuildMenu, BuildMenuOptions, () => "repository", _ => "repository",
            () => { }, request => { QueueCommand(request.CommandId); return new MenuCommandResult { Success = true }; }, new MenuLayoutService());
    }

    public TopMenu Menu { get; }
    public bool ShouldExit { get; private set; }
    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

    public IDisposable BeginFrame(UiRenderRequest request) => _screen.BeginFrame();
    public bool TryAcceptViewportChange(ConsoleViewport viewport, ConsoleViewportChange change) => false;
    public void CompleteFrame(UiFrameCompletion completion) { }

    protected override DemoFrame RenderFrame(UiRenderContext context)
    {
        var canvas = context.Canvas;
        int width = context.Size.Width;
        int height = context.Size.Height;
        var palette = UiTheme.Current;
        canvas.FillRegion(new Rect(0, 0, width, height), new CellStyle(palette.NormalFileFg, palette.PanelBackground));

        string menuText = " Repository   Commit   Branch   View   Demo   Help ";
        canvas.FillRegion(new Rect(0, 0, width, Math.Min(1, height)), new CellStyle(palette.MenuBarNormalFg, palette.MenuBarNormalBg));
        if (height > 0) canvas.Write(0, 0, ConsoleTextMetrics.FitToCells(menuText, width), new CellStyle(palette.MenuBarNormalFg, palette.MenuBarNormalBg));

        int statusY = Math.Max(1, height - 2);
        int keyBarY = Math.Max(1, height - 1);
        var tableBounds = new Rect(0, Math.Min(1, height), width, Math.Max(0, statusY - 1));
        TableListFrame tableFrame = _table.CalculateFrame(tableBounds);
        _table.Render(canvas, tableFrame);
        context.PublishOnStable(() => _table.ApplyCommittedFrame(tableFrame));

        if (height > 1)
        {
            string selection = _table.TryGetSelectedItem(out var selected)
                ? $"{selected.Hash}  {selected.ChangedFiles.Count} file(s)  " : string.Empty;
            selection += $"[{_repository.CurrentBranch.Name}] ";
            if (_search.IsActive) selection += "[FILTER ACTIVE] ";
            canvas.FillRegion(new Rect(0, statusY, width, 1), PaletteStyles.CommandLine(palette));
            canvas.Write(0, statusY, ConsoleTextMetrics.FitToCells(selection + _status, width), PaletteStyles.CommandLine(palette));
        }
        if (height > 0) _keyBar.Render(canvas, keyBarY, width, KeyItems);
        return new DemoFrame(tableBounds, tableFrame, statusY, keyBarY, width);
    }

    protected override UiInteractionFrame BuildInteractionFrame(DemoFrame frame) => _table.BuildInteractionFrame(frame.Table);

    protected override UiInputResult RouteInput(ConsoleInputEvent input, DemoFrame frame, UiInputRouteContext context)
    {
        if (input is KeyConsoleInputEvent { Key: var key } && key.Modifiers == 0 && IsActionKey(key.Key))
        {
            QueueCommand(key.Key.ToString().ToLowerInvariant());
            return UiInputResult.HandledAndInvalidate;
        }
        if (input is MouseConsoleInputEvent mouse && _keyBar.TryHitTest(mouse, frame.KeyBarY, frame.Width, out var hit))
        {
            QueueCommand(hit.Key.ToString().ToLowerInvariant());
            return UiInputResult.HandledAndInvalidate;
        }

        int before = _table.SelectedIndex;
        var routed = _table.RouteInput(input, frame.Table, context);
        if (routed.Semantic.IsHandled)
        {
            if (routed.Semantic.Kind == ScrollableListInputResultKind.Confirmed)
            {
                QueueCommand("details");
                return UiInputResult.HandledAndInvalidate;
            }
            if (_table.SelectedIndex != before) _status = "Selection changed";
            return routed.UiResult with { Invalidate = true };
        }
        return UiInputResult.NotHandled;
    }

    private static bool IsActionKey(ConsoleKey key) => key is ConsoleKey.F1 or ConsoleKey.F2 or ConsoleKey.F3 or ConsoleKey.F4
        or ConsoleKey.F5 or ConsoleKey.F6 or ConsoleKey.F7 or ConsoleKey.F8 or ConsoleKey.F10;

    public bool ExecutePendingCommand()
    {
        if (!_pendingCommands.TryDequeue(out string? command)) return false;
        Execute(command);
        return true;
    }

    private void QueueCommand(string command) => _pendingCommands.Enqueue(command);

    private void Execute(string command)
    {
        switch (command)
        {
            case "f1": case "help": ShowKeyboardHelp(); break;
            case "f2": case "create": CreateCommit(); break;
            case "f3": case "details": case "files": ShowSelectedDetails(); break;
            case "f4": case "branch": SwitchBranch(); break;
            case "f5": case "copy": CopySelectedHash(); break;
            case "f6": case "dates": _repository.ToggleDates(); _status = _repository.Settings.CompactDates ? "Compact dates" : "Full timestamps"; break;
            case "f7":
            case "unicode":
                var visible = VisibleCommits(); int unicode = visible.ToList().FindIndex(c => c.Author.Any(ch => ch > 127) || c.Subject.Any(ch => ch > 127));
                if (unicode >= 0) _table.SetSelectedIndex(unicode); _status = unicode >= 0 ? "Selected next Unicode example" : "No Unicode example in this filter"; break;
            case "f8": case "delete": DeleteSelected(); break;
            case "merges": _repository.ToggleMerges(); RefreshItems(); _status = _repository.Settings.ShowMerges ? "Merge commits shown" : "Merge commits hidden"; break;
            case "search": SearchCommits(); break;
            case "clear-search": _search = new("", "Any author", "All branches", false, true); RefreshItems(); _status = "Commit search cleared"; break;
            case "manage-branches":
                _workflows.ManageBranches();
                if (_search.Branch != "All branches" && !_repository.Branches.Any(branch => branch.Name == _search.Branch))
                    _search = _search with { Branch = "All branches" };
                RefreshItems(); _status = $"Current branch: {_repository.CurrentBranch.Name}"; break;
            case "settings": _status = _workflows.EditSettings() ? "Repository settings saved" : "Repository settings unchanged"; break;
            case "pull": RunPull(false); break;
            case "pull-fail": RunPull(true); break;
            case "push": _dialogs.Message("Push", "Fake push completed locally. No remote or network was contacted."); _status = "Fake push completed"; break;
            case "conflict": ResolveConflict(); break;
            case "recent": OpenRecent(); break;
            case "theme": SelectTheme(); break;
            case "about": _dialogs.Message("About", "CSharpFar.Ui Demo\nA deterministic, in-memory Git client showcase.\nKeyboard and mouse are both supported."); break;
            case "reset": ResetDemoData(); break;
            case "f10": case "quit": ShouldExit = true; break;
        }
        Menu.Close();
    }

    private void RunPull(bool simulateFailure)
    {
        PullOutcome outcome = _showcase.Pull(simulateFailure);
        _status = outcome switch
        {
            PullOutcome.Completed => "Pull completed — commit list refreshed",
            PullOutcome.Cancelled => "Pull cancelled — temporary state cleaned up",
            _ => "Pull failed — repository unchanged",
        };
        RefreshItems();
    }

    private void ResolveConflict()
    {
        string choice = _showcase.MergeConflict();
        _status = choice switch
        {
            "local" => "Conflict resolved: kept local version",
            "remote" => "Conflict resolved: kept remote version",
            _ => "Conflict resolution cancelled",
        };
    }

    private void OpenRecent()
    {
        string[] repositories = ["CSharpFar (current)", "Unicode Widgets", "Terminal Experiments"];
        SelectionListDialogResult<string> result = _dialogs.Select(repositories, value => value, "Open Recent");
        if (!result.IsConfirmed || result.SelectedItem is null) { _status = "Open Recent cancelled"; return; }
        _dialogs.Message("Open Recent", $"Opened the in-memory showcase: {result.SelectedItem}");
        _status = $"Recent repository: {result.SelectedItem}";
    }

    private void SelectTheme()
    {
        int selected = PaletteRegistry.All.ToList().FindIndex(p => p.Name == UiTheme.Current.Name);
        SelectionListDialogResult<ConsolePalette> result = _dialogs.Select(
            PaletteRegistry.All, palette => palette.Name, "Select Theme", Math.Max(0, selected));
        if (!result.IsConfirmed || result.SelectedItem is null) { _status = "Theme unchanged"; return; }
        _themeScope?.Dispose();
        _themeScope = ReferenceEquals(result.SelectedItem, PaletteRegistry.Default)
            ? null
            : UiTheme.UseTemporary(result.SelectedItem);
        _status = $"Theme: {result.SelectedItem.Name}";
    }

    private void ResetDemoData()
    {
        if (!_dialogs.Confirm("Reset Demo Data", "Restore all deterministic commits, branches, and settings?", "All in-memory changes will be discarded."))
        {
            _status = "Reset cancelled";
            return;
        }
        _repository.Reset();
        _search = new("", "Any author", "All branches", false, true);
        RefreshItems();
        _status = "Demo data reset";
    }

    private void ShowKeyboardHelp()
    {
        _dialogs.Message("Keyboard Shortcuts",
            "F1 Help   F2 Create   F3 Details   F4 Branch   F5 Copy hash\n" +
            "F6 Compact dates   F7 Unicode   F8 Delete   F9 Menu   F10 Quit\n\n" +
            "Arrows, PageUp/PageDown, Home/End and mouse wheel navigate.\nEnter or double-click opens details; Alt+letter activates menu mnemonics.");
        _status = "Keyboard shortcuts viewed";
    }

    private void CreateCommit()
    {
        string author = _table.SelectedItem?.Author ?? "Ada Lovelace";
        NewCommitInput? input = _commits.CreateCommit(author);
        if (input is null) { _status = "Create commit cancelled"; return; }
        DemoCommit created = _repository.CreateCommit(input.Author, input.Subject, input.Type, input.Amend, input.SignOff);
        RefreshItems();
        int createdIndex = VisibleCommits().ToList().FindIndex(commit => commit.Hash == created.Hash);
        if (createdIndex >= 0) _table.SetSelectedIndex(createdIndex);
        _status = $"Created {created.Hash}";
    }

    private void ShowSelectedDetails()
    {
        if (!_table.TryGetSelectedItem(out DemoCommit? commit)) { _status = "No commit selected"; return; }
        _commits.ShowDetails(commit);
        _status = $"Viewed {commit.Hash}";
    }

    private void CopySelectedHash()
    {
        if (!_table.TryGetSelectedItem(out DemoCommit? commit)) { _status = "No commit selected"; return; }
        _status = _commits.CopyHash(commit) ? $"Copied {commit.Hash}" : "Clipboard unavailable";
    }

    private void DeleteSelected()
    {
        if (!_table.TryGetSelectedItem(out DemoCommit? commit)) { _status = "No commit selected"; return; }
        int formerIndex = _table.SelectedIndex;
        if (!_commits.ConfirmDelete(commit)) { _status = "Delete cancelled"; return; }
        _repository.DeleteCommit(commit.Hash);
        RefreshItems();
        if (_table.Count > 0) _table.SetSelectedIndex(Math.Min(formerIndex, _table.Count - 1));
        _status = $"Deleted {commit.Hash}";
    }

    private void RefreshItems() => _table.ReplaceItems(VisibleCommits(), c => c.Hash);
    private IReadOnlyList<DemoCommit> VisibleCommits() => DemoCommitQuery.Apply(
        _repository.Commits,
        new DemoCommitFilter(_search.Text, _search.Author, _search.Branch, _search.CaseSensitive, _search.IncludeMerges),
        _repository.Settings.ShowMerges);

    private void SwitchBranch()
    {
        DemoBranch? branch = _workflows.SwitchBranch();
        if (branch is null) { _status = "Branch switch cancelled"; return; }
        _repository.SwitchBranch(branch.Name);
        _status = $"Switched to {branch.Name}";
    }

    private void SearchCommits()
    {
        CommitSearch? search = _workflows.Search(_search);
        if (search is null) { _status = "Search unchanged"; return; }
        _search = search;
        RefreshItems();
        _status = _search.IsActive ? $"Search applied — {_table.Count} match(es)" : "Search cleared";
    }

    private string FormatDate(DemoCommit commit) => _repository.Settings.CompactDates
        ? commit.Timestamp.ToString("yyyy-MM-dd") : commit.Timestamp.ToString("yyyy-MM-dd HH:mm");

    private static MenuBarDefinition BuildMenu() => new()
    {
        Items =
        [
            Top("repository", "Repository", Cmd("recent", "Open Recent", 'O'), Sep("repository"), Cmd("pull", "Pull (fake)", 'P'), Cmd("push", "Push (fake)", 'U'), Disabled("clone", "Clone… (unavailable)"), Sep("repository-2"), Cmd("settings", "Repository settings", 'S'), Cmd("reset", "Reset demo data", 'R'), Cmd("quit", "Quit", 'Q')),
            Top("commit", "Commit", Cmd("create", "Create commit", 'C'), Cmd("details", "Commit details", 'D'), Cmd("copy", "Copy hash", 'H'), Cmd("delete", "Delete commit", 'L'), Sep(), Cmd("search", "Search commits", 'S'), Cmd("clear-search", "Clear search", 'E')),
            Top("branch", "Branch", Cmd("branch", "Switch branch", 'S'), Cmd("manage-branches", "Manage branches", 'M')),
            Top("view", "View", Cmd("theme", "Select theme", 'T'), Cmd("dates", "Toggle compact dates", 'D'), Cmd("merges", "Toggle merges", 'M')),
            Top("demo", "Demo", Cmd("unicode", "Find Unicode example", 'U'), Cmd("conflict", "Merge conflict warning", 'M'), Cmd("pull-fail", "Pull failure", 'F'), Cmd("reset", "Reset data", 'R')),
            Top("help", "Help", Cmd("help", "Keyboard Shortcuts", 'K'), Cmd("about", "About", 'A'), Disabled("online-help", "Online Help (unavailable)"))
        ]
    };

    private static TopMenuItemDefinition Top(string id, string text, params MenuItemDefinition[] children) =>
        new() { Id = id, Text = text, HotChar = text[0], Children = children };
    private static MenuItemDefinition Cmd(string id, string text, char hot) =>
        new() { Id = id, Text = text, HotChar = hot, CommandId = id };
    private static MenuItemDefinition Sep(string id = "commit-separator") => new() { Id = id, Text = string.Empty, Kind = MenuItemKind.Separator };
    private static MenuItemDefinition Disabled(string id, string text) => new() { Id = id, Text = text, IsEnabled = false };

    private static MenuRenderOptions BuildMenuOptions()
    {
        var p = UiTheme.Current;
        return new MenuRenderOptions
        {
            MenuBarNormalStyle = new(p.MenuBarNormalFg, p.MenuBarNormalBg),
            MenuBarActiveStyle = new(p.MenuBarActiveFg, p.MenuBarActiveBg),
            NormalStyle = new(p.MenuNormalFg, p.MenuNormalBg),
            ActiveStyle = new(p.MenuActiveFg, p.MenuActiveBg),
            HighlightStyle = new(p.MenuHighlightFg, p.MenuHighlightBg),
            ActiveHighlightStyle = new(p.MenuActiveHighlightFg, p.MenuActiveHighlightBg),
            DisabledStyle = new(p.MenuDisabledFg, p.MenuDisabledBg),
            BorderStyle = new(p.MenuBorderFg, p.MenuBorderBg),
            ShadowStyle = new(p.MenuShadowFg, p.MenuShadowBg)
        };
    }
}
