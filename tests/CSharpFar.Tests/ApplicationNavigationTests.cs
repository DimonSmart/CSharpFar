using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ApplicationNavigationTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationNavigationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarNavigationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Run_EnterOnParentDirectory_PositionsCursorOnChildDirectory()
    {
        string childName = "Sub25";
        string childPath = Path.Combine(_tempDir, childName);
        Directory.CreateDirectory(childPath);

        var fs = new FakeFileSystemService();
        fs.AddDirectory(childPath, new FilePanelItem
        {
            Name = "..",
            FullPath = _tempDir,
            IsDirectory = true,
            IsParentDirectory = true,
        });

        var parentItems = Enumerable.Range(0, 25)
            .Select(i => new FilePanelItem
            {
                Name = $"Dir{i:D2}",
                FullPath = Path.Combine(_tempDir, $"Dir{i:D2}"),
                IsDirectory = true,
            })
            .Append(new FilePanelItem
            {
                Name = childName,
                FullPath = childPath,
                IsDirectory = true,
            })
            .ToArray();
        fs.AddDirectory(_tempDir, parentItems);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, childPath);
        app.Run();

        var left = GetLeftPanel(app);
        Assert.Equal(_tempDir, left.CurrentDirectory);
        Assert.Equal(childName, left.Items[left.CursorIndex].Name);
        Assert.True(left.ScrollOffset <= left.CursorIndex);
        Assert.True(left.CursorIndex < left.ScrollOffset + 6);
    }

    [Fact]
    public void Run_LeftRightInHiddenPanelMode_EditCommandLine()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 160, height: 10);
        driver.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('b', ConsoleKey.B, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u000f', ConsoleKey.O, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();

        var commandLine = GetCommandLine(app);
        Assert.Equal("abXc", commandLine.Text);
        Assert.Equal(3, commandLine.CursorPosition);
        Assert.Equal(8, driver.CursorY);
    }

    [Fact]
    public void Run_CtrlOAfterViewportOriginChange_DoesNotRestoreStaleUnderlay()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.WriteAt(0, 0, "OLD-SHELL".AsSpan());
        driver.BeforeReadInput = d =>
        {
            d.SetViewportOrigin(0, 1);
            d.BeforeReadInput = afterCtrlO =>
            {
                Assert.DoesNotContain("OLD-SHELL", afterCtrlO.GetRow(0));
            };
        };
        driver.EnqueueKey(new ConsoleKeyInfo('\u000f', ConsoleKey.O, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_HiddenPanelsOriginOnlyViewportChange_DoesNotRepaint()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = afterScroll =>
                {
                    Assert.Equal(4, afterScroll.GetViewport().Top);
                    Assert.Equal(0, afterScroll.WriteAtCallCount);
                    Assert.Equal(0, afterScroll.ClearRegionCallCount);
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_HiddenPanelsSizeChange_RedrawsCommandLine()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.ClearRecordedOperations();
                afterHide.SetSize(50, 9);
                afterHide.BeforeReadInput = afterResize =>
                {
                    Assert.True(afterResize.WriteAtCallCount > 0);
                    Assert.Equal(7, afterResize.CursorY);
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void Run_HiddenPanelsInputAfterScroll_ReturnsViewportToBottom()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        int bottomTop = driver.GetViewport().Top;
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.X, keyChar: 'x'));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.WriteAt(0, 0, "BOTTOM-OUTPUT".AsSpan());
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = beforeInput =>
                {
                    Assert.Equal(4, beforeInput.GetViewport().Top);
                    beforeInput.BeforeReadInput = afterInput =>
                    {
                        Assert.Equal(bottomTop, afterInput.GetViewport().Top);
                        Assert.StartsWith("BOTTOM-OUTPUT", afterInput.GetRow(0));
                    };
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();

        Assert.Equal("x", GetCommandLine(app).Text);
    }

    [Fact]
    public void Run_CtrlOFromScrolledHiddenPanels_ReturnsViewportToBottomAndRendersPanels()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 40, height: 8);
        driver.SetBufferHeight(20);
        driver.TryScrollViewportToBottom();
        int bottomTop = driver.GetViewport().Top;
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.BeforeReadInput = d =>
        {
            d.BeforeReadInput = afterHide =>
            {
                afterHide.SetViewportOrigin(0, 4);
                afterHide.BeforeReadInput = beforeShow =>
                {
                    Assert.Equal(4, beforeShow.GetViewport().Top);
                    beforeShow.ClearRecordedOperations();
                    beforeShow.BeforeReadInput = afterShow =>
                    {
                        Assert.Equal(bottomTop, afterShow.GetViewport().Top);
                        Assert.True(afterShow.WriteAtCallCount > 0);
                    };
                };
            };
        };

        var app = CreateApp(fs, driver, _tempDir);
        app.Run();
    }

    [Fact]
    public void HandleKey_CtrlF1_HidesLeftPanelOnly()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));

        AssertRightPanelOnly(driver);
    }

    [Fact]
    public void HandleKey_CtrlF2_HidesRightPanelOnly()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.F2, control: true));

        AssertLeftPanelOnly(driver);
    }

    [Fact]
    public void HandleKey_CtrlF1ThenCtrlF2_HidesBothPanels()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));
        HandleKeyAndRender(app, Key(ConsoleKey.F2, control: true));

        AssertNoPanels(driver);
    }

    [Fact]
    public void HandleKey_CtrlOThenCtrlF1_ShowsOnlyLeftPanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));

        AssertLeftPanelOnly(driver);
    }

    [Fact]
    public void HandleKey_CtrlOThenCtrlF2_ShowsOnlyRightPanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        HandleKeyAndRender(app, Key(ConsoleKey.F2, control: true));

        AssertRightPanelOnly(driver);
    }

    [Fact]
    public void HandleKey_HidingActivePanel_ActivatesVisiblePanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));

        Assert.Equal(PanelSide.Right, GetActiveSide(app));
    }

    [Fact]
    public void HandleKey_Tab_DoesNotActivateHiddenPanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));
        HandleKeyAndRender(app, Key(ConsoleKey.Tab));

        Assert.Equal(PanelSide.Right, GetActiveSide(app));
    }

    [Fact]
    public void HandleMouse_LeftClickRightPanel_ActivatesRightPanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir,
            new FilePanelItem { Name = "first.txt", FullPath = Path.Combine(_tempDir, "first.txt"), IsDirectory = false },
            new FilePanelItem { Name = "second.txt", FullPath = Path.Combine(_tempDir, "second.txt"), IsDirectory = false });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);
        Render(app);

        Assert.True(HandleMouse(app, LeftMouse(41, 2, MouseEventKind.Down)));

        Assert.Equal(PanelSide.Right, GetActiveSide(app));
    }

    [Fact]
    public void HandleMouse_LeftClickRightPanelLeftBorder_SelectsRightPanelItem()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir,
            new FilePanelItem { Name = "first.txt", FullPath = Path.Combine(_tempDir, "first.txt"), IsDirectory = false },
            new FilePanelItem { Name = "second.txt", FullPath = Path.Combine(_tempDir, "second.txt"), IsDirectory = false });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);
        GetRightPanel(app).CursorIndex = 0;
        Render(app);

        Assert.True(HandleMouse(app, LeftMouse(40, 2, MouseEventKind.Down)));

        Assert.Equal(PanelSide.Right, GetActiveSide(app));
        Assert.Equal(1, GetRightPanel(app).CursorIndex);
    }

    [Fact]
    public void HandleMouse_LeftClickVisibleRightPanel_WhenLeftHidden_ActivatesRightPanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir,
            new FilePanelItem { Name = "first.txt", FullPath = Path.Combine(_tempDir, "first.txt"), IsDirectory = false });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);
        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));

        Assert.True(HandleMouse(app, LeftMouse(41, 2, MouseEventKind.Down)));

        Assert.Equal(PanelSide.Right, GetActiveSide(app));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    public void Run_NarrowConsole_DoesNotThrow(int width, int height)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width, height);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(fs, driver, _tempDir);

        app.Run();
    }

    private Application CreateApp(FakeFileSystemService fs, FakeConsoleDriver driver, string startDirectory)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = startDirectory;
        settings.Panels.RightStartDirectory = startDirectory;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey consoleKey,
        char keyChar = '\0',
        bool control = false,
        bool shift = false) =>
        new(keyChar, consoleKey, shift, alt: false, control);

    private static FilePanelState GetLeftPanel(Application app)
    {
        var field = typeof(Application).GetField("_left", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._left field not found.");
        return (FilePanelState)field.GetValue(app)!;
    }

    private static FilePanelState GetRightPanel(Application app)
    {
        var field = typeof(Application).GetField("_right", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._right field not found.");
        return (FilePanelState)field.GetValue(app)!;
    }

    private static void HandleKeyAndRender(Application app, ConsoleKeyInfo key)
    {
        var method = typeof(Application).GetMethod(
            "HandleKey",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.HandleKey method not found.");

        bool shouldRender = (bool)method.Invoke(app, [key])!;
        if (shouldRender)
            Render(app);
    }

    private static bool HandleMouse(Application app, MouseConsoleInputEvent mouse)
    {
        var method = typeof(Application).GetMethod(
            "HandleMouse",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.HandleMouse method not found.");

        return (bool)method.Invoke(app, [mouse])!;
    }

    private static void Render(Application app)
    {
        var method = typeof(Application).GetMethod(
            "Render",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application.Render method not found.");

        method.Invoke(app, []);
    }

    private static PanelSide GetActiveSide(Application app)
    {
        var field = typeof(Application).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._active field not found.");
        return (PanelSide)field.GetValue(app)!;
    }

    private static void AssertLeftPanelOnly(FakeConsoleDriver driver)
    {
        Assert.Equal('╔', driver.GetCell(0, 0).Character);
        Assert.Equal('╗', driver.GetCell(39, 0).Character);
        Assert.Equal('╢', driver.GetCell(39, 6).Character);
        Assert.Equal(' ', driver.GetCell(40, 0).Character);
    }

    private static void AssertRightPanelOnly(FakeConsoleDriver driver)
    {
        Assert.Equal(' ', driver.GetCell(0, 0).Character);
        Assert.Equal(' ', driver.GetCell(39, 0).Character);
        Assert.Equal('╔', driver.GetCell(40, 0).Character);
        Assert.Equal('╟', driver.GetCell(40, 6).Character);
    }

    private static void AssertNoPanels(FakeConsoleDriver driver)
    {
        Assert.Equal(' ', driver.GetCell(0, 0).Character);
        Assert.Equal(' ', driver.GetCell(39, 0).Character);
        Assert.Equal(' ', driver.GetCell(40, 0).Character);
    }

    private static MouseConsoleInputEvent LeftMouse(int x, int y, MouseEventKind kind) =>
        new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);

    private static CommandLineState GetCommandLine(Application app)
    {
        var field = typeof(Application).GetField("_cmdLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._cmdLine field not found.");
        return (CommandLineState)field.GetValue(app)!;
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) { }
    }

    private sealed class NoOpFileOperationService : IFileOperationService
    {
        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
    }
}
