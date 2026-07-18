using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationOverlayLayerTests
{
    [Fact]
    public void CommandCompletion_InputPolicyAndInteractionMetadataFollowVisibility()
    {
        var services = Services();

        services.Composition.Render();
        Assert.Equal(UiLayerInputPolicy.None, services.Inner.CommandCompletionLayer.InputPolicy);
        Assert.Empty(services.Inner.CommandCompletionLayer.CommittedInteractionFrame.Focus.Entries);
        Assert.Empty(services.Inner.CommandCompletionLayer.CommittedInteractionFrame.HitRegions);

        services.Session.CommandLine.Completion.Visible = true;
        services.Session.CommandLine.Completion.Matches.AddRange(["", "alpha"]);
        services.Composition.Render();

        Assert.Equal(UiLayerInputPolicy.Bubble, services.Inner.CommandCompletionLayer.InputPolicy);
        Assert.Empty(services.Inner.CommandCompletionLayer.CommittedInteractionFrame.Focus.Entries);
    }

    [Fact]
    public void CommandCompletion_TinyViewportUsesCommittedInvisibleFrameAndBubblesInput()
    {
        var services = Services(new FakeConsoleDriver(80, 3));
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "alpha"]);

        services.Composition.Render();

        Assert.False(services.Inner.CommandCompletionLayer.CommittedFrame.Visible);
        Assert.Equal(UiLayerInputPolicy.None, services.Inner.CommandCompletionLayer.InputPolicy);
        Assert.Same(UiInteractionFrame.Empty, services.Inner.CommandCompletionLayer.CommittedInteractionFrame);
        Assert.True(completion.Visible);
        Assert.Equal(["", "alpha"], completion.Matches);

        var input = Key(ConsoleKey.A, keyChar: 'a');
        Assert.True(services.Composition.DispatchInput(input).Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
    }

    [Fact]
    public void CommandCompletion_HiddenCommandLine_DoesNotRenderOrPublishInput()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "git status"]);
        services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;

        services.Composition.Render();

        var layer = services.Inner.CommandCompletionLayer;
        Assert.False(layer.CommittedFrame.Visible);
        Assert.Equal(UiLayerInputPolicy.None, layer.InputPolicy);
        Assert.Same(UiInteractionFrame.Empty, layer.CommittedInteractionFrame);
        Assert.Empty(layer.CommittedFrame.Items);
        Assert.Null(layer.CommittedFrame.ScrollbarBounds);
        Assert.True(completion.Visible);
        Assert.Equal(["", "git status"], completion.Matches);

        var input = Key(ConsoleKey.DownArrow);
        Assert.True(services.Composition.DispatchInput(input).Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
    }

    [Fact]
    public void CommandCompletion_ResizeRestoresCommittedVisibilityAndInputPolicy()
    {
        var services = Services(new FakeConsoleDriver(80, 3));
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "alpha"]);
        services.Composition.Render();

        services.Driver.SetSize(80, 25);
        services.Composition.Render();

        Assert.True(services.Inner.CommandCompletionLayer.CommittedFrame.Visible);
        Assert.Equal(UiLayerInputPolicy.Bubble, services.Inner.CommandCompletionLayer.InputPolicy);
        Assert.NotEmpty(services.Inner.CommandCompletionLayer.CommittedInteractionFrame.HitRegions);
    }

    [Fact]
    public void CommandCompletion_EmptyMatchesHasNoCommittedInputPolicy()
    {
        var services = Services();
        services.Session.CommandLine.Completion.Visible = true;

        services.Composition.Render();

        Assert.False(services.Inner.CommandCompletionLayer.CommittedFrame.Visible);
        Assert.Equal(UiLayerInputPolicy.None, services.Inner.CommandCompletionLayer.InputPolicy);
    }

    [Fact]
    public void CommandCompletion_RejectedRenderPreservesCommittedInputPolicyUntilCommit()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "alpha"]);
        services.Composition.Render();
        var interaction = services.Inner.CommandCompletionLayer.CommittedInteractionFrame;
        bool observedRejectedAttempt = false;
        completion.SelectedIndex = 1;

        services.Driver.ResizeAfterWriteCount = services.Driver.WriteAtCallCount + 1;
        services.Driver.ResizeAfterWrite = driver => driver.SetSize(80, 3);
        services.Driver.BeforeViewportWrite = _ =>
        {
            observedRejectedAttempt = true;
            Assert.True(services.Inner.CommandCompletionLayer.CommittedFrame.Visible);
            Assert.Equal(UiLayerInputPolicy.Bubble, services.Inner.CommandCompletionLayer.InputPolicy);
            Assert.Same(interaction, services.Inner.CommandCompletionLayer.CommittedInteractionFrame);
            services.Driver.BeforeViewportWrite = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
        Assert.False(services.Inner.CommandCompletionLayer.CommittedFrame.Visible);
        Assert.Equal(UiLayerInputPolicy.None, services.Inner.CommandCompletionLayer.InputPolicy);
    }

    [Fact]
    public void CommandCompletion_NeutralEnterClosesAndContinuesTheSameInput()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "alpha"]);
        services.Composition.Render();
        var input = Key(ConsoleKey.Enter);

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.False(completion.Visible);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
    }

    [Fact]
    public void CommandCompletion_RejectedRenderAttemptDoesNotMutateSelectionBeforeStableCommit()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(["", "alpha", "beta", "gamma"]);
        completion.SelectedIndex = 99;
        completion.FirstVisibleIndex = 99;

        services.Driver.ResizeAfterWriteCount = 1;
        services.Driver.ResizeAfterWrite = driver => driver.SetSize(100, 35);
        services.Driver.BeforeViewportWrite = _ =>
        {
            Assert.Equal(99, completion.SelectedIndex);
            Assert.Equal(99, completion.FirstVisibleIndex);
        };

        services.Composition.Render();

        Assert.Equal(3, completion.SelectedIndex);
        Assert.Equal(0, completion.FirstVisibleIndex);
    }

    [Fact]
    public void CommandCompletion_CtrlEnterBubblesToApplicationCommandLineShortcut()
    {
        var services = Services();
        var item = Item("current.txt");
        services.Session.Panels.Left.Items.Add(item);
        services.Session.Panels.Left.CursorIndex = services.Session.Panels.Left.Items.IndexOf(item);
        services.Session.CommandLine.Completion.Visible = true;
        services.Session.CommandLine.Completion.Matches.AddRange(["", "history"]);
        services.Session.CommandLine.Completion.SelectedIndex = 1;

        services.Composition.Render();
        var input = Key(ConsoleKey.Enter, control: true);
        UiInputResult routed = services.Composition.DispatchInput(input);

        Assert.True(routed.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);

        ApplicationRuntimeRenderRequest request = services.Inner.ApplicationInputDispatcher.Handle(packet);

        Assert.True(request.ShouldRender);
        Assert.Equal("current.txt", services.Session.CommandLine.State.Text);
    }

    [Fact]
    public void CommandCompletion_CommittedFrameWithoutScrollbarCancelsScrollbarDrag()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(Enumerable.Range(0, 12).Select(i => $"item-{i}"));

        services.Composition.Render();
        UiInputResult down = services.Composition.DispatchInput(Mouse(79, 15));
        Assert.True(down.Handled);
        Assert.NotNull(completion.ScrollbarDrag);

        completion.Matches.RemoveRange(1, completion.Matches.Count - 1);
        services.Composition.Render();

        Assert.Null(completion.ScrollbarDrag);
    }

    [Fact]
    public void CommandCompletion_NonInteractiveScrollbarClearsDragAndCapture()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(Enumerable.Range(0, 12).Select(i => $"item-{i}"));

        services.Composition.Render();
        Assert.True(services.Composition.DispatchInput(Mouse(79, 15)).Handled);
        Assert.NotNull(completion.ScrollbarDrag);

        services.Driver.SetSize(80, 6);
        services.Composition.Render();

        Assert.Null(completion.ScrollbarDrag);
        UiInputResult move = services.Composition.DispatchInput(Mouse(0, 0, MouseEventKind.Move));
        Assert.True(move.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.IsType<MouseConsoleInputEvent>(packet.Input);

        services.Composition.DispatchInput(Mouse(79, 1));
        Assert.Null(completion.ScrollbarDrag);
    }

    [Fact]
    public void CommandCompletion_RejectedRenderAttemptPreservesScrollbarDragUntilCommit()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(Enumerable.Range(0, 12).Select(i => $"item-{i}"));
        services.Composition.Render();
        services.Composition.DispatchInput(Mouse(79, 15));
        ScrollBarDragState dragBeforeRetry = Assert.IsType<ScrollBarDragState>(completion.ScrollbarDrag);
        bool observedRejectedAttempt = false;

        completion.SelectedIndex = 1;
        services.Driver.ResizeAfterWriteCount = services.Driver.WriteAtCallCount + 1;
        services.Driver.ResizeAfterWrite = driver => driver.SetSize(100, 35);
        services.Driver.BeforeViewportWrite = _ =>
        {
            observedRejectedAttempt = true;
            Assert.Equal(dragBeforeRetry, completion.ScrollbarDrag);
            services.Driver.BeforeViewportWrite = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
    }

    [Fact]
    public void CommandCompletion_CommittedResizeRebasesScrollbarDrag()
    {
        var services = Services();
        var completion = services.Session.CommandLine.Completion;
        completion.Visible = true;
        completion.Matches.AddRange(Enumerable.Range(0, 12).Select(i => $"item-{i}"));
        services.Composition.Render();
        services.Composition.DispatchInput(Mouse(79, 15));

        completion.SelectedIndex = 1;
        services.Driver.ResizeAfterWriteCount = services.Driver.WriteAtCallCount + 1;
        services.Driver.ResizeAfterWrite = driver => driver.SetSize(100, 35);
        services.Composition.Render();

        ScrollBarDragState drag = Assert.IsType<ScrollBarDragState>(completion.ScrollbarDrag);
        Assert.Equal(new Rect(99, 24, 1, 8), drag.Bounds);
        Assert.Equal(12, drag.TotalItems);
        Assert.Equal(8, drag.ViewportItems);
        int thumbHeight = ScrollBarInteraction.CalculateThumb(
            drag.Bounds,
            new ScrollState
            {
                TotalItems = drag.TotalItems,
                ViewportItems = drag.ViewportItems,
                FirstVisibleIndex = 0,
            }).ThumbHeight;
        Assert.InRange(drag.PointerOffsetInThumb, 0, thumbHeight - 1);

        UiInputResult move = services.Composition.DispatchInput(Mouse(0, drag.Bounds.Bottom - 2, MouseEventKind.Move));

        Assert.True(move.Handled);
        Assert.Equal(4, completion.FirstVisibleIndex);
    }

    [Fact]
    public void PanelQuickSearch_ActiveTinyViewportKeepsHandlingKeyboardWithoutPopup()
    {
        var services = Services(new FakeConsoleDriver(80, 3));
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Session.Panels.Left.Items.Add(Item("alpha.txt"));
        services.Composition.Render();

        UiInputResult activate = services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        Assert.True(activate.Handled);
        services.Composition.Render();

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
        UiInputResult refine = services.Composition.DispatchInput(Key(ConsoleKey.E, keyChar: 'e'));
        Assert.True(refine.Handled);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
        Assert.Equal("ge", services.PanelQuickSearch.State?.SearchText);
    }

    [Fact]
    public void PanelQuickSearch_TinyViewportPublishesCursorlessFocusAndHidesCursor()
    {
        var services = Services(new FakeConsoleDriver(80, 3));
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        services.Composition.Render();

        var layer = services.Inner.PanelQuickSearchLayer;
        UiFocusEntry entry = Assert.Single(layer.CommittedInteractionFrame.Focus.Entries);
        Assert.True(layer.CommittedFrame.Active);
        Assert.False(layer.CommittedFrame.PopupVisible);
        Assert.Empty(layer.CommittedInteractionFrame.HitRegions);
        Assert.Equal(new UiTargetId("application.panel-quick-search.input"), entry.Target);
        Assert.True(entry.IsEnabled);
        Assert.Null(entry.Cursor);
        Assert.Equal(entry.Target, layer.FocusScope.FocusedTarget);
        Assert.False(services.Driver.CursorVisible);
    }

    [Fact]
    public void PanelQuickSearch_ResizeRestoresAndRemovesPopupCursorWithoutLosingFocus()
    {
        var services = Services(new FakeConsoleDriver(80, 3));
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        services.Composition.Render();
        var layer = services.Inner.PanelQuickSearchLayer;
        UiTargetId target = Assert.Single(layer.CommittedInteractionFrame.Focus.Entries).Target;

        services.Driver.SetSize(80, 25);
        services.Composition.Render();

        UiFocusEntry visible = Assert.Single(layer.CommittedInteractionFrame.Focus.Entries);
        Assert.True(layer.CommittedFrame.Active);
        Assert.True(layer.CommittedFrame.PopupVisible);
        Assert.Equal(target, visible.Target);
        Assert.NotNull(visible.Cursor);
        Assert.Single(layer.CommittedInteractionFrame.HitRegions);
        Assert.True(services.Driver.CursorVisible);
        Assert.True(layer.CommittedFrame.InputBounds.Contains(services.Driver.CursorX, services.Driver.CursorY));

        services.Driver.SetSize(80, 3);
        services.Composition.Render();

        Assert.True(layer.CommittedFrame.Active);
        Assert.False(layer.CommittedFrame.PopupVisible);
        Assert.Equal(target, Assert.Single(layer.CommittedInteractionFrame.Focus.Entries).Target);
        Assert.Empty(layer.CommittedInteractionFrame.HitRegions);
        Assert.False(services.Driver.CursorVisible);
    }

    [Fact]
    public void PanelQuickSearch_RejectedRenderPreservesCommittedFocusAndCursor()
    {
        var services = Services();
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        services.Composition.Render();
        var layer = services.Inner.PanelQuickSearchLayer;
        var interaction = layer.CommittedInteractionFrame;
        var cursor = (services.Driver.CursorVisible, services.Driver.CursorX, services.Driver.CursorY);
        bool observedRejectedAttempt = false;
        services.Composition.DispatchInput(Key(ConsoleKey.E, keyChar: 'e'));

        services.Driver.ResizeAfterWriteCount = services.Driver.WriteAtCallCount + 1;
        services.Driver.ResizeAfterWrite = driver => driver.SetSize(80, 3);
        services.Driver.BeforeViewportWrite = _ =>
        {
            observedRejectedAttempt = true;
            Assert.True(layer.CommittedFrame.Active);
            Assert.True(layer.CommittedFrame.PopupVisible);
            Assert.Same(interaction, layer.CommittedInteractionFrame);
            Assert.Equal(cursor, (services.Driver.CursorVisible, services.Driver.CursorX, services.Driver.CursorY));
            services.Driver.BeforeViewportWrite = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
        Assert.True(layer.CommittedFrame.Active);
        Assert.False(layer.CommittedFrame.PopupVisible);
        Assert.False(services.Driver.CursorVisible);
    }

    [Fact]
    public void PanelQuickSearch_CloseAndContinueKeyboard_ForwardsSameInputToApplicationSurface()
    {
        var services = Services();
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        services.Composition.Render();
        var input = Key(ConsoleKey.Enter);

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Null(services.PanelQuickSearch.State);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
    }

    [Fact]
    public void PanelQuickSearch_MouseContinuation_ForwardsSameInputOnce()
    {
        var services = Services();
        services.Session.Panels.Left.Items.Add(Item("gemini.md"));
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.G, alt: true));
        services.Composition.Render();
        var input = Mouse(20, 4);

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Null(services.PanelQuickSearch.State);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Same(input, packet.Input);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void TopMenu_HiddenCommandLineDoesNotInterceptF9OrTopRowMouse()
    {
        var services = Services();
        services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;
        services.Composition.Render();

        services.Composition.DispatchInput(Key(ConsoleKey.F9));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var keyPacket));
        Assert.Equal(ConsoleKey.F9, Assert.IsType<KeyConsoleInputEvent>(keyPacket.Input).Key.Key);

        services.Composition.DispatchInput(Mouse(0, 0));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var mousePacket));
        Assert.IsType<MouseConsoleInputEvent>(mousePacket.Input);
    }

    private static TestServices Services(FakeConsoleDriver? driver = null)
    {
        driver ??= new FakeConsoleDriver(80, 25);
        var fs = new FakeFileSystemService();
        const string root = @"C:\Root";
        fs.AddDirectory(root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = root;
        settings.Panels.RightStartDirectory = root;
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            enableBuiltInNetworkModules: false);
        _ = new Application(services);
        return new TestServices(driver, services);
    }

    private static KeyConsoleInputEvent Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool alt = false,
        bool control = false) =>
        new(new ConsoleKeyInfo(keyChar, key, shift: false, alt, control));

    private static MouseConsoleInputEvent Mouse(int x, int y, MouseEventKind kind = MouseEventKind.Down) =>
        new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);

    private static FilePanelItem Item(string name) => new()
    {
        Name = name,
        FullPath = Path.Combine(@"C:\Root", name),
        IsDirectory = false,
        Size = 1,
        LastWriteTime = new DateTime(2026, 1, 1),
        Attributes = FileAttributes.Archive,
    };

    private sealed record TestServices(FakeConsoleDriver Driver, ApplicationServices Inner)
    {
        public ApplicationSession Session => Inner.Session;
        public UiCompositionHost Composition => Inner.Composition;
        public ApplicationUiSurface ApplicationSurface => Inner.ApplicationSurface;
        public CSharpFar.App.Input.KeyboardInputRouter KeyboardInputRouter => Inner.KeyboardInputRouter;
        public CSharpFar.App.Panels.PanelQuickSearchController PanelQuickSearch => Inner.PanelQuickSearch;
    }
}
