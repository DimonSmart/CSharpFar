using System.Reflection;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationCommandRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationCommandRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarCommandRegistry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CreateDefault_ContainsHandlersForEveryFunctionKeyAndMenuCommand()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var commandIds = registry.CommandIds.ToHashSet(StringComparer.Ordinal);

        foreach (string commandId in ConstStringValues(typeof(FunctionKeyCommandIds)))
            Assert.Contains(commandId, commandIds);

        foreach (string commandId in ConstStringValues(typeof(MenuCommandIds)))
            Assert.Contains(commandId, commandIds);

        Assert.Contains(ApplicationCommandIds.OpenCurrentItem, commandIds);
        Assert.Contains(DirectoryShortcutCommandIds.Navigate, commandIds);
    }

    [Fact]
    public void CreateDefault_ContainsTerminalDiagnosticsCommand()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();

        Assert.Contains(MenuCommandIds.DiagnosticsPrintTerminalInfo, registry.CommandIds);
    }

    [Theory]
    [InlineData(MenuCommandIds.PanelSetViewMode)]
    [InlineData(MenuCommandIds.PanelSetSortMode)]
    [InlineData(MenuCommandIds.PanelToggleReverseSort)]
    [InlineData(MenuCommandIds.PanelRefresh)]
    public void Execute_MenuCommandWithMissingArgs_ReturnsFailedMenuResult(string commandId)
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var context = CreateContext();

        var result = registry.Execute(commandId, context).ToMenuCommandResult();

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsFailedMenuResult()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var context = CreateContext();

        var result = registry.Execute("missing.command", context).ToMenuCommandResult();

        Assert.False(result.Success);
        Assert.Contains("Unsupported", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PanelScopedCommandUsesCommittedSideInsteadOfLiveActiveSide()
    {
        var services = CreateServices(seedPanelFiles: true);
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        PrepareDistinctPanelSortState(left, right);
        SortMode originalRightSortMode = right.SortMode;
        services.CommandContext.ActiveSide = PanelSide.Left;
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left,
            10,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right));

        services.CommandContext.ActiveSide = PanelSide.Right;
        var result = services.CommandRegistry.Execute(
            FunctionKeyCommandIds.SortByName, services.CommandContext, invocation);

        Assert.True(result.ShouldRender);
        Assert.Equal(SortMode.Name, left.SortMode);
        Assert.Equal(originalRightSortMode, right.SortMode);
        Assert.Equal(["..", "a.txt", "z.txt"], PanelItemNames(left));
        Assert.Equal(["..", "right-z.bin", "right-a.txt"], PanelItemNames(right));
        Assert.Equal(PanelSide.Right, services.CommandContext.ActiveSide);
    }

    [Fact]
    public void ResolvePanelTarget_PreservesBothCommittedPanelSnapshots()
    {
        var services = CreateServices();
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        left.CurrentLocation = new PanelLocation(new PanelSourceId("left-provider"), @"/left");
        right.CurrentLocation = new PanelLocation(new PanelSourceId("right-provider"), @"/right");
        var active = new ApplicationPanelKeyboardFrame(left.CurrentLocation, false, null, null, null);
        var passive = new ApplicationPanelKeyboardFrame(right.CurrentLocation, false, null, null, null);
        var invocation = new ApplicationPanelCommandInvocation(PanelSide.Left, 4, active, passive);
        services.CommandContext.ActiveSide = PanelSide.Right;

        var target = services.CommandContext.ResolvePanelTarget(invocation);

        Assert.Same(left, target.State);
        Assert.Same(right, target.PassiveState);
        Assert.Same(active, target.ActiveCommitted);
        Assert.Same(passive, target.PassiveCommitted);
        Assert.Equal(PanelSide.Left, target.Side);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_StaleCommittedCurrentItemIsNotConsumedByDeleteCommand(bool samePathDifferentProvider)
    {
        var operations = new RecordingFileOperationService();
        var services = CreateServices(fileOperations: operations);
        services.CommandContext.Settings.Ui.ConfirmDelete = false;
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        AddItem(left, "committed.txt");
        left.CursorIndex = left.Items.Count - 1;
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left,
            10,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right));
        int committedIndex = left.CursorIndex;
        FilePanelItem committedItem = left.Items[committedIndex];
        left.Items[committedIndex] = new FilePanelItem
        {
            Name = "new.txt",
            FullPath = samePathDifferentProvider
                ? committedItem.FullPath
                : Path.Combine(_tempDir, "new.txt"),
            SourceId = samePathDifferentProvider
                ? new PanelSourceId("changed-provider")
                : PanelSourceId.Local,
            IsDirectory = false,
        };

        var result = services.CommandRegistry.Execute(FunctionKeyCommandIds.Delete, services.CommandContext, invocation);

        Assert.Empty(operations.Requests);
        Assert.True(result.ShouldRender);
    }

    [Fact]
    public void EveryPanelScopedFunctionKeyCommandExistsInDefaultRegistry()
    {
        var services = CreateServices(seedPanelFiles: true);
        string[] panelScoped =
        [
            FunctionKeyCommandIds.UserMenu, FunctionKeyCommandIds.View,
            FunctionKeyCommandIds.Edit, FunctionKeyCommandIds.OpenCreateFile,
            FunctionKeyCommandIds.Copy, FunctionKeyCommandIds.RenameOrMove,
            FunctionKeyCommandIds.Rename, FunctionKeyCommandIds.CreateFolder,
            FunctionKeyCommandIds.Delete, FunctionKeyCommandIds.TopMenu,
            FunctionKeyCommandIds.Search, FunctionKeyCommandIds.FileHistory,
            FunctionKeyCommandIds.DirectoryHistory, FunctionKeyCommandIds.SortByName,
            FunctionKeyCommandIds.SortByExtension, FunctionKeyCommandIds.SortByLastWriteTime,
            FunctionKeyCommandIds.SortBySize, FunctionKeyCommandIds.Attributes,
        ];
        Assert.All(panelScoped, commandId =>
        {
            Assert.True(services.CommandRegistry.TryGetCommand(commandId, out _), commandId);
        });
    }

    [Fact]
    public void EditorContextFactory_UsesCommittedPassiveItemOnly()
    {
        AssertEditorContextFactoryDoesNotUseLivePassiveItem(samePathDifferentProvider: false);
    }

    [Fact]
    public void EditorContextFactory_RejectsPassiveProviderMismatchWithSamePath()
    {
        AssertEditorContextFactoryDoesNotUseLivePassiveItem(samePathDifferentProvider: true);
    }

    private void AssertEditorContextFactoryDoesNotUseLivePassiveItem(bool samePathDifferentProvider)
    {
        var services = CreateServices();
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        AddItem(left, "active.txt");
        AddItem(right, "committed.txt");
        left.CursorIndex = left.Items.Count - 1;
        right.CursorIndex = right.Items.Count - 1;
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left,
            10,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right));
        FilePanelItem committedPassive = right.Items[right.CursorIndex];
        right.Items[right.CursorIndex] = new FilePanelItem
        {
            Name = "live.txt",
            FullPath = samePathDifferentProvider
                ? committedPassive.FullPath
                : Path.Combine(_tempDir, "live.txt"),
            SourceId = samePathDifferentProvider
                ? new PanelSourceId("changed-provider")
                : PanelSourceId.Local,
            IsDirectory = false,
        };
        var target = services.CommandContext.ResolvePanelTarget(invocation);

        var context = PanelCommandEditorContextFactory.Create(services.CommandContext, target);

        Assert.Null(context.PassivePanelItemName);
        Assert.Null(context.PassivePanelItemPath);
    }

    [Fact]
    public void UserMenuOperands_CurrentLocationsUnchanged_CreatesOperands()
    {
        var services = CreateServices();
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        AddItem(left, "committed.txt");
        AddItem(right, "passive.txt");
        left.CurrentLocation = PanelLocation.Local(_tempDir);
        right.CurrentLocation = PanelLocation.Local(_tempDir);
        left.CursorIndex = 1;
        right.CursorIndex = 1;
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left, 10,
            new ApplicationPanelKeyboardFrame(left.CurrentLocation, false, 1, left.Items[1].Location, left.Items[1].Name),
            new ApplicationPanelKeyboardFrame(right.CurrentLocation, false, 1, right.Items[1].Location, right.Items[1].Name));
        var target = services.CommandContext.ResolvePanelTarget(invocation);

        bool created = PanelCommandUserMenuOperands.TryCreate(target, services.CommandContext, out var operands);
        string expanded = operands.Expand("{current}|{panelDir}|{otherPanelDir}");

        Assert.True(created);
        Assert.Equal($"{Path.Combine(_tempDir, "committed.txt")}|{_tempDir}|{_tempDir}", expanded);
    }

    [Theory]
    [InlineData(CommittedLocationMutation.ActivePath)]
    [InlineData(CommittedLocationMutation.PassivePath)]
    [InlineData(CommittedLocationMutation.ActiveProvider)]
    [InlineData(CommittedLocationMutation.PassiveProvider)]
    public void UserMenuOperands_LocationChanged_DoesNotCreateOperands(CommittedLocationMutation mutation)
    {
        var services = CreateServices();
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        AddItem(left, "committed.txt");
        AddItem(right, "passive.txt");
        left.CursorIndex = left.Items.Count - 1;
        right.CursorIndex = right.Items.Count - 1;
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left,
            10,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right));
        MutateCommittedLocation(left, right, mutation);
        var target = services.CommandContext.ResolvePanelTarget(invocation);

        bool created = PanelCommandUserMenuOperands.TryCreate(target, services.CommandContext, out _);

        Assert.False(created);
    }

    [Fact]
    public void UserMenuOperands_StaleCurrentItemIsNotReplacedWithLiveItem()
    {
        var services = CreateServices();
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        AddItem(left, "committed.txt");
        left.CursorIndex = left.Items.Count - 1;
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left,
            10,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right));
        left.Items[left.CursorIndex] = new FilePanelItem
        {
            Name = "live.txt",
            FullPath = Path.Combine(_tempDir, "live.txt"),
            IsDirectory = false,
        };
        var target = services.CommandContext.ResolvePanelTarget(invocation);

        bool created = PanelCommandUserMenuOperands.TryCreate(target, services.CommandContext, out var operands);
        string expanded = operands.Expand("{current}");

        Assert.True(created);
        Assert.Equal(string.Empty, expanded);
    }

    [Fact]
    public void WorkspaceKeyboard_FunctionKeyExecutesRegistryCommandForCommittedPanel()
    {
        var services = CreateServices(seedPanelFiles: true);
        WireRegistry(services);
        PrepareDistinctPanelSortState(services.CommandContext.LeftPanel, services.CommandContext.RightPanel);
        var frame = Frame(PanelSide.Left, FunctionKeyCommandIds.SortByName, ConsoleKey.F3,
            services.CommandContext.LeftPanel, services.CommandContext.RightPanel);
        services.CommandContext.ActiveSide = PanelSide.Right;

        var request = services.ApplicationInputDispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, true)),
            frame, ApplicationTargetIds.WorkspaceKeyboard, UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(SortMode.Name, services.CommandContext.LeftPanel.SortMode);
        Assert.Equal(SortMode.Size, services.CommandContext.RightPanel.SortMode);
        Assert.Equal(PanelSide.Right, services.CommandContext.ActiveSide);
    }

    [Fact]
    public void FunctionKeyBarClickExecutesRegistryCommandForCommittedPanel()
    {
        var services = CreateServices(seedPanelFiles: true);
        WireRegistry(services);
        PrepareDistinctPanelSortState(services.CommandContext.LeftPanel, services.CommandContext.RightPanel);
        var frame = Frame(PanelSide.Left, FunctionKeyCommandIds.SortByName, ConsoleKey.F3,
            services.CommandContext.LeftPanel, services.CommandContext.RightPanel);
        services.CommandContext.ActiveSide = PanelSide.Right;

        var request = services.ApplicationInputDispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(2, 24, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame, ApplicationTargetIds.FunctionKeyBar, UiInputRouteKind.HitTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(SortMode.Name, services.CommandContext.LeftPanel.SortMode);
        Assert.Equal(SortMode.Size, services.CommandContext.RightPanel.SortMode);
        Assert.Equal(PanelSide.Right, services.CommandContext.ActiveSide);
    }

    [Theory]
    [InlineData(FunctionKeyCommandIds.Copy, CommittedLocationMutation.PassivePath)]
    [InlineData(FunctionKeyCommandIds.Copy, CommittedLocationMutation.ActivePath)]
    [InlineData(FunctionKeyCommandIds.Copy, CommittedLocationMutation.PassiveProvider)]
    [InlineData(FunctionKeyCommandIds.Copy, CommittedLocationMutation.ActiveProvider)]
    [InlineData(FunctionKeyCommandIds.RenameOrMove, CommittedLocationMutation.PassivePath)]
    [InlineData(FunctionKeyCommandIds.RenameOrMove, CommittedLocationMutation.ActivePath)]
    [InlineData(FunctionKeyCommandIds.RenameOrMove, CommittedLocationMutation.PassiveProvider)]
    [InlineData(FunctionKeyCommandIds.RenameOrMove, CommittedLocationMutation.ActiveProvider)]
    public void FileOperation_CommittedLocationChanged_DoesNotOpenDialogOrExecute(
        string commandId,
        CommittedLocationMutation mutation)
    {
        var operations = new RecordingFileOperationService();
        var services = CreateServices(fileOperations: operations);
        var left = services.CommandContext.LeftPanel;
        var right = services.CommandContext.RightPanel;
        string sourcePath = Path.Combine(_tempDir, "source.txt");
        left.SelectedPaths.Add(sourcePath);
        var invocation = new ApplicationPanelCommandInvocation(
            PanelSide.Left, 10,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right));
        MutateCommittedLocation(left, right, mutation);

        var result = services.CommandRegistry.Execute(commandId, services.CommandContext, invocation);

        Assert.True(result.ShouldRender);
        Assert.Empty(operations.Requests);
        Assert.Contains(sourcePath, left.SelectedPaths);
    }

    private ApplicationCommandContext CreateContext()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);

        return services.CommandContext;
    }

    private ApplicationServices CreateServices(
        bool seedPanelFiles = false,
        IFileOperationService? fileOperations = null)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        if (seedPanelFiles)
        {
            fs.AddDirectory(_tempDir,
                new FilePanelItem { Name = "z.txt", FullPath = Path.Combine(_tempDir, "z.txt"), IsDirectory = false },
                new FilePanelItem { Name = "a.txt", FullPath = Path.Combine(_tempDir, "a.txt"), IsDirectory = false });
        }
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()), fs, new NoOpShellService(),
            fileOperations ?? new NoOpFileOperationService(), new InMemoryHistoryStore(), settings);
        services.Callbacks.ClosePanelQuickSearchForState = _ => { };
        return services;
    }

    private static void WireRegistry(ApplicationServices services)
    {
        bool Execute(string commandId, object? args) =>
            services.CommandRegistry.Execute(commandId, services.CommandContext, args).ShouldRender;
        services.KeyboardInputContext.ExecuteRegisteredCommand = Execute;
        services.KeyboardInputContext.SetFunctionKeyLayer = _ => false;
        services.MouseInputContext.ExecuteRegisteredCommand = Execute;
        services.Callbacks.ClosePanelQuickSearchForState = _ => { };
    }

    private static void AddItem(FilePanelState state, string name) => state.Items.Add(new FilePanelItem
    {
        Name = name,
        FullPath = Path.Combine(state.SourcePath, name),
        IsDirectory = false,
    });

    private static void PrepareDistinctPanelSortState(FilePanelState left, FilePanelState right)
    {
        left.SortMode = SortMode.Extension;
        right.SortMode = SortMode.Size;
        ReplacePanelItems(left,
            new FilePanelItem { Name = "z.txt", FullPath = Path.Combine(left.SourcePath, "z.txt"), IsDirectory = false, Size = 1 },
            new FilePanelItem { Name = "a.txt", FullPath = Path.Combine(left.SourcePath, "a.txt"), IsDirectory = false, Size = 2 });
        ReplacePanelItems(right,
            new FilePanelItem { Name = "right-z.bin", FullPath = Path.Combine(right.SourcePath, "right-z.bin"), IsDirectory = false, Size = 1 },
            new FilePanelItem { Name = "right-a.txt", FullPath = Path.Combine(right.SourcePath, "right-a.txt"), IsDirectory = false, Size = 2 });
    }

    private static void ReplacePanelItems(FilePanelState state, params FilePanelItem[] items)
    {
        FilePanelItem? parent = state.Items.FirstOrDefault(static item => item.IsParentDirectory);
        state.Items.Clear();
        if (parent is not null)
            state.Items.Add(parent);
        state.Items.AddRange(items);
    }

    private static string[] PanelItemNames(FilePanelState state) =>
        [.. state.Items.Select(static item => item.Name)];

    private static void MutateCommittedLocation(
        FilePanelState left,
        FilePanelState right,
        CommittedLocationMutation mutation)
    {
        switch (mutation)
        {
            case CommittedLocationMutation.ActivePath:
                left.CurrentLocation = PanelLocation.Local(Path.Combine(left.SourcePath, "changed-active"));
                break;
            case CommittedLocationMutation.PassivePath:
                right.CurrentLocation = PanelLocation.Local(Path.Combine(right.SourcePath, "changed-passive"));
                break;
            case CommittedLocationMutation.ActiveProvider:
                left.CurrentLocation = new PanelLocation(new PanelSourceId("changed-provider"), left.SourcePath);
                break;
            case CommittedLocationMutation.PassiveProvider:
                right.CurrentLocation = new PanelLocation(new PanelSourceId("changed-provider"), right.SourcePath);
                break;
        }
    }

    private sealed class RecordingFileOperationService : IFileOperationService
    {
        public bool SupportsRecycleBin => true;
        public List<FileOperationRequest> Requests { get; } = [];

        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
        }
    }

    private static ApplicationUiFrame Frame(
        PanelSide side,
        string commandId,
        ConsoleKey key,
        FilePanelState left,
        FilePanelState right) => new(
        new ConsoleViewport(0, 0, 120, 25), ApplicationWorkspaceMode.Panels,
        new ApplicationKeyboardFrame(side, false, false,
            ApplicationPanelKeyboardSnapshot.Capture(left),
            ApplicationPanelKeyboardSnapshot.Capture(right)),
        new ApplicationCommandLineFrame(new Rect(0, 24, 120, 1), 0, 0, 0, null),
        new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 60, 23), 10, [], null, null),
        new ApplicationPanelFrame(PanelSide.Right, new Rect(60, 0, 60, 23), 10, [], null, null),
        new ApplicationFunctionKeyBarFrame([new ApplicationFunctionKeyHit(
            new Rect(0, 24, 10, 1), commandId,
            FunctionKeyLayer.Control, key)]), null);

    private static IEnumerable<string> ConstStringValues(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    public enum CommittedLocationMutation
    {
        ActivePath,
        PassivePath,
        ActiveProvider,
        PassiveProvider,
    }
}
