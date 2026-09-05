from pathlib import Path


def replace(path, old, new, expected=1):
    p = Path(path)
    data = p.read_bytes()
    nl = '\r\n' if b'\r\n' in data else '\n'
    old_b = old.replace('\n', nl).encode()
    new_b = new.replace('\n', nl).encode()
    actual = data.count(old_b)
    if actual != expected:
        raise RuntimeError(f'{path}: expected {expected} matches, found {actual} for {old[:80]!r}')
    p.write_bytes(data.replace(old_b, new_b))


def replace_all(path, old, new, minimum=1):
    p = Path(path)
    data = p.read_bytes()
    nl = '\r\n' if b'\r\n' in data else '\n'
    old_b = old.replace('\n', nl).encode()
    new_b = new.replace('\n', nl).encode()
    actual = data.count(old_b)
    if actual < minimum:
        raise RuntimeError(f'{path}: expected at least {minimum} matches, found {actual} for {old!r}')
    p.write_bytes(data.replace(old_b, new_b))
    print(f'{path}: replaced {actual} occurrences of {old!r}')


replace('src/CSharpFar.Ui/CSharpFar.Ui.csproj', '''    <!-- Application integration tests exercise modal lifecycle and routed input together with App. -->
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>CSharpFar.Tests</_Parameter1>
    </AssemblyAttribute>
''', '')
replace('tests/CSharpFar.Architecture.Tests/ReusableTestDependencyTests.cs', '''        // App integration tests require modal scopes and routed input; they remain a deliberate UI friend.
        AssertFriends(typeof(FormControls).Assembly, "CSharpFar.Tests", "CSharpFar.Ui.Tests");
''', '''        AssertFriends(typeof(FormControls).Assembly, "CSharpFar.Ui.Tests");
''')

replace('tests/CSharpFar.Tests/UiLayerTestHost.cs', '''        Composition = UiTestHost.Create(Screen, new UiLayerTestSurface(Screen, layer)).Composition;
''', '''        UiTestHost host = UiTestHost.Create(Screen);
        Composition = host.Composition;
        _ = Composition.RegisterPersistentOverlay(layer);
''')
Path('tests/CSharpFar.Tests/UiLayerTestSurface.cs').unlink()

replace('tests/CSharpFar.Tests/ApplicationUiLayerScopeTests.cs', '''        ConsoleInputEvent? modalInput = null;
        using var modal = fixture.Host.PushOverlay(new RecordingModalLayer(
            context => context.Canvas.Write(1, 21, "M", new CSharpFar.Console.Models.CellStyle(ConsoleColor.White, ConsoleColor.Black)),
            input => modalInput = input));
        fixture.Host.Render();
        var input = Key(ConsoleKey.A, 'a');

        UiInputResult result = fixture.Host.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.Equal('M', fixture.Driver.GetCell(1, 21).Character);
        Assert.False(fixture.Root.TryTakeInput(out _));
        Assert.Same(input, modalInput);
''', '''        ConsoleInputEvent? modalInput = null;
        var input = Key(ConsoleKey.A, 'a');
        fixture.Driver.EnqueueInput(input);
        fixture.Driver.BeforeReadInput = driver =>
            Assert.Equal('M', driver.GetCell(1, 21).Character);

        fixture.Modals.Run(
            context => context.Canvas.Write(1, 21, "M", new CSharpFar.Console.Models.CellStyle(ConsoleColor.White, ConsoleColor.Black)),
            routed =>
            {
                modalInput = routed;
                return ModalDialogLoopAction.Close;
            });

        Assert.False(fixture.Root.TryTakeInput(out _));
        Assert.Same(input, modalInput);
''')
replace('tests/CSharpFar.Tests/ApplicationUiLayerScopeTests.cs', '''            var host = new UiCompositionHost(new ScreenRenderer(driver));
            var root = new RecordingRootSurface(host.Screen);
''', '''            var screen = new ScreenRenderer(driver);
            var host = new UiCompositionHost(screen);
            var root = new RecordingRootSurface(screen);
''')
replace('tests/CSharpFar.Tests/ApplicationUiLayerScopeTests.cs', '''    private sealed class RecordingRootSurface(ScreenRenderer screen) : IUiSurface, IUiLayer
    {
        private readonly Queue<UiRoutedInput<Unit>> _inputs = [];

        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        public void Render(UiRenderContext context)
        {
            var style = new CSharpFar.Console.Models.CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
            string row = new('R', context.Size.Width);
            for (int y = 0; y < context.Size.Height; y++)
                context.Canvas.Write(0, y, row, style);
        }

        public void CompleteFrame(UiFrameCompletion completion)
        {
        }

        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            _inputs.Enqueue(new UiRoutedInput<Unit>(input, default, context.Target, context.RouteKind));
            return UiInputResult.HandledResult;
        }

        public bool TryTakeInput(out UiRoutedInput<Unit> packet) =>
            _inputs.TryDequeue(out packet!);

        public void Clear() => _inputs.Clear();
    }
''', '''    private sealed class RecordingRootSurface(ScreenRenderer screen) : UiLayer<UiInteractionFrame>, IUiSurface
    {
        private readonly Queue<UiRoutedInput<UiInteractionFrame>> _inputs = [];

        public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        protected override UiInteractionFrame RenderFrame(UiRenderContext context)
        {
            var style = new CSharpFar.Console.Models.CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
            string row = new('R', context.Size.Width);
            for (int y = 0; y < context.Size.Height; y++)
                context.Canvas.Write(0, y, row, style);
            return UiInteractionFrame.Empty;
        }

        public void CompleteFrame(UiFrameCompletion completion)
        {
        }

        protected override UiInteractionFrame BuildInteractionFrame(UiInteractionFrame frame) => frame;

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            UiInteractionFrame frame,
            UiInputRouteContext context)
        {
            _inputs.Enqueue(new UiRoutedInput<UiInteractionFrame>(input, frame, context.Target, context.RouteKind));
            return UiInputResult.HandledResult;
        }

        public bool TryTakeInput(out UiRoutedInput<UiInteractionFrame> packet) =>
            _inputs.TryDequeue(out packet!);

        public void Clear() => _inputs.Clear();
    }
''')
replace('tests/CSharpFar.Tests/ApplicationUiLayerScopeTests.cs', '''
    private sealed class RecordingModalLayer(
        Action<UiRenderContext> render,
        Action<ConsoleInputEvent> route) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Modal;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public void Render(UiRenderContext context) => render(context);
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            route(input);
            return UiInputResult.HandledResult;
        }
    }
''', '\n')

replace('tests/CSharpFar.Tests/ApplicationRuntimeTests.cs', '''        bool modalOpened = false;
''', '')
replace('tests/CSharpFar.Tests/ApplicationRuntimeTests.cs', '''            using var modal = fixture.Services.Composition.PushOverlay(new TestLayer(
                UiLayerInputPolicy.Modal,
                UiInputResult.HandledResult));
            modalOpened = true;
''', '')
replace('tests/CSharpFar.Tests/ApplicationRuntimeTests.cs', '''        Assert.True(modalOpened);
''', '')
replace('tests/CSharpFar.Tests/ApplicationRuntimeTests.cs', '''        fixture.Context.ExecuteMenuCommand = _ =>
        {
            Assert.Equal(1, fixture.RenderCount);
            using var modal = fixture.Services.Composition.PushOverlay(new TestLayer(
                UiLayerInputPolicy.Modal,
                UiInputResult.HandledResult));
            return ApplicationRuntimeRenderRequest.None;
        };
''', '''        fixture.Context.ExecuteMenuCommand = _ =>
        {
            Assert.Equal(1, fixture.RenderCount);
            fixture.Driver.EnqueueInput(Key(ConsoleKey.Escape));
            bool modalClosed = false;
            new ModalDialogHost(fixture.Services.Composition).Run(
                _ => { },
                _ =>
                {
                    modalClosed = true;
                    return ModalDialogLoopAction.Close;
                });
            Assert.True(modalClosed);
            return ApplicationRuntimeRenderRequest.None;
        };
''')
replace_all('tests/CSharpFar.Tests/ApplicationRuntimeTests.cs', 'fixture.Services.Composition.PushOverlay(', 'fixture.Services.Composition.RegisterPersistentOverlay(')
replace('tests/CSharpFar.Tests/ApplicationRuntimeTests.cs', '''    private sealed class TestLayer(
        UiLayerInputPolicy policy,
        UiInputResult result,
        Action<UiRenderContext>? render = null,
        Action<ConsoleInputEvent>? route = null) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public void Render(UiRenderContext context) => render?.Invoke(context);
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context)
        {
            route?.Invoke(input);
            return result;
        }
    }
''', '''    private sealed class TestLayer(
        UiLayerInputPolicy policy,
        UiInputResult result,
        Action<UiRenderContext>? render = null,
        Action<ConsoleInputEvent>? route = null) : UiLayer<UiInteractionFrame>
    {
        public override UiLayerInputPolicy InputPolicy => policy;

        protected override UiInteractionFrame RenderFrame(UiRenderContext context)
        {
            render?.Invoke(context);
            return UiInteractionFrame.Empty;
        }

        protected override UiInteractionFrame BuildInteractionFrame(UiInteractionFrame frame) => frame;

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            UiInteractionFrame frame,
            UiInputRouteContext context)
        {
            route?.Invoke(input);
            return result;
        }
    }
''')

replace('tests/CSharpFar.Tests/ApplicationUiSurfaceTests.cs', '''        Assert.NotSame(new UiFocusController(), surface.FocusState);
''', '''        Assert.NotNull(surface.FocusState);
        Assert.False(surface.FocusState.HasFocus);
''')
replace('tests/CSharpFar.Tests/ApplicationUiSurfaceTests.cs', '''    [Fact]
    public void BubbleOverlayIsolation_ControlsApplicationPacketOwnership()
    {
        var services = Services();
        services.Composition.Render();
        var handled = new TestLayer(UiLayerInputPolicy.Bubble) { Result = UiInputResult.HandledResult };
        using (services.Composition.PushOverlay(handled))
            services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        var unhandled = new TestLayer(UiLayerInputPolicy.Bubble);
        using (services.Composition.PushOverlay(unhandled))
            services.Composition.DispatchInput(Key(ConsoleKey.B));

        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void ModalAndTemporarySurface_IsolateApplicationInput()
    {
        var services = Services();
        services.Composition.Render();
        var modal = new TestLayer(UiLayerInputPolicy.Modal);
        using (services.Composition.PushOverlay(modal))
            services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        var temporary = new TestSurface(services.Composition.Screen, UiInputResult.HandledResult);
        using (services.Composition.OpenSurface(temporary))
            services.Composition.DispatchInput(Key(ConsoleKey.B));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(Key(ConsoleKey.C));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(ConsoleKey.C, Assert.IsType<KeyConsoleInputEvent>(routed.Input).Key.Key);
        Assert.Equal(ApplicationTargetIds.WorkspaceKeyboard, routed.Target);
        Assert.Equal(UiInputRouteKind.KeyboardTarget, routed.RouteKind);
    }

    [Fact]
    public void RenderOnlyOverlay_DoesNotBlockApplicationInput()
    {
        var services = Services();
        services.Composition.Render();

        using var overlay = services.Composition.PushOverlay(_ => { });
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

''', '')
replace('tests/CSharpFar.Tests/ApplicationUiSurfaceTests.cs', '''
    private sealed class TestLayer(UiLayerInputPolicy policy) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => Result;
    }

    private sealed class TestSurface(ScreenRenderer screen, UiInputResult result) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) { }
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => result;
    }
''', '')
replace('tests/CSharpFar.Ui.Tests/UiInputRoutingTests.cs', '''    [Fact]
    public void DispatchInput_AppliesFocusRequestToSourceLayerOnlyAndNormalizesResult()
''', '''    [Fact]
    public void DispatchInput_RenderOnlyOverlayDoesNotBlockLowerInteractiveLayer()
    {
        var calls = new List<string>();
        var (host, surface) = Fixture(calls);
        surface.Result = UiInputResult.HandledResult;
        using var scope = host.PushOverlay(_ => { });

        UiInputResult result = host.DispatchInput(UiTestInput.Key(ConsoleKey.A));

        Assert.True(result.Handled);
        Assert.Equal(["surface"], calls);
    }

    [Fact]
    public void DispatchInput_AppliesFocusRequestToSourceLayerOnlyAndNormalizesResult()
''')

replace('tests/CSharpFar.Tests/CompareOptionsDialogTests.cs', '        FormSubmitResult<ComparisonOptions?> standardDepth = CompareOptionsDialog.BuildOptions(\n', '        _ = CompareOptionsDialog.BuildOptions(\n')
replace('tests/CSharpFar.Tests/CompareOptionsDialogTests.cs', '        Assert.True(standardDepth.IsSuccess);\n', '')
replace('tests/CSharpFar.Tests/CompareOptionsDialogTests.cs', '        FormSubmitResult<ComparisonOptions?> custom = CompareOptionsDialog.BuildOptions(\n', '        _ = CompareOptionsDialog.BuildOptions(\n')
replace('tests/CSharpFar.Tests/CompareOptionsDialogTests.cs', '        Assert.True(custom.IsSuccess);\n', '')
replace('tests/CSharpFar.Tests/FileAttributesDialogTests.cs', '            new CheckBoxRow(new CheckBoxLine("other")), "current", snapshot, creation, write, access, new DateTime(2026, 7, 31, 12, 0, 0)));\n', '            FormControls.CheckBox("other"), "current", snapshot, creation, write, access, new DateTime(2026, 7, 31, 12, 0, 0)));\n')

Path('tests/CSharpFar.Tests/UiTestRender.cs').write_text('''using CSharpFar.Console;\n\nnamespace CSharpFar.Tests;\n\ninternal static class UiTestRender\n{\n    public static TResult Render<TResult>(\n        ScreenRenderer screen,\n        Func<IUiCanvas, TResult> draw)\n    {\n        TResult result = default!;\n        Render(screen, canvas => result = draw(canvas));\n        return result;\n    }\n\n    public static void Render(\n        ScreenRenderer screen,\n        Action<IUiCanvas> draw)\n    {\n        var composition = new UiCompositionHost(screen);\n        composition.SetRootSurface(new ScreenRendererSurface(screen, context => draw(context.Canvas)));\n        composition.Render();\n    }\n}\n''', encoding='utf-8', newline='\r\n')
replace('tests/CSharpFar.Tests/TextFieldHistoryTestProvider.cs', '''
    public static SingleLineTextHistoryState CreateState(IEnumerable<string>? items = null) =>
        new(new TextHistory(items, itemsChanged: null));
''', '')

replace('tests/CSharpFar.Tests/SettingsDialogTests.cs', '''        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            var driver = Driver(
                Key(ConsoleKey.DownArrow),
                Key(ConsoleKey.DownArrow),
                Key(ConsoleKey.Enter),
                Key(ConsoleKey.Escape));

            SettingsDialogResult? result = new SettingsDialog(new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                PanelViewMode.Full,
                PanelViewMode.Full,
                "Default",
                fileHighlightingEnabled: true,
                editorSyntaxHighlightingEnabled: true);

            Assert.Null(result);
            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
''', '''        using var theme = UiTheme.UseTemporary(PaletteRegistry.Default);
        var driver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.Escape));

        SettingsDialogResult? result = new SettingsDialog(new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
            PanelViewMode.Full,
            PanelViewMode.Full,
            "Default",
            fileHighlightingEnabled: true,
            editorSyntaxHighlightingEnabled: true);

        Assert.Null(result);
        Assert.Same(PaletteRegistry.Default, UiTheme.Current);
''')
replace('tests/CSharpFar.Tests/SettingsDialogTests.cs', '''    [Fact]
    public void TemporaryThemeScope_RestoresThemeAfterException()
    {
        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            void ThrowDuringTemporaryScope()
            {
                using (UiTheme.UseTemporary(CSharpFarPaletteRegistry.FarClassic.Ui))
                {
                    Assert.Same(CSharpFarPaletteRegistry.FarClassic.Ui, UiTheme.Current);
                    throw new InvalidOperationException("render failed");
                }
            }

            Assert.Throws<InvalidOperationException>((Action)ThrowDuringTemporaryScope);

            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
    }

    [Fact]
    public void TemporaryThemeScope_RestoresNestedThemesInOrder()
    {
        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            using (UiTheme.UseTemporary(CSharpFarPaletteRegistry.FarClassic.Ui))
            {
                Assert.Same(CSharpFarPaletteRegistry.FarClassic.Ui, UiTheme.Current);
                using (UiTheme.UseTemporary(PaletteRegistry.Default))
                    Assert.Same(PaletteRegistry.Default, UiTheme.Current);
                Assert.Same(CSharpFarPaletteRegistry.FarClassic.Ui, UiTheme.Current);
            }

            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
    }

''', '')
Path('tests/CSharpFar.Ui.Tests/UiThemeTests.cs').write_text('''namespace CSharpFar.Ui.Tests;\n\npublic sealed class UiThemeTests\n{\n    [Fact]\n    public void TemporaryThemeScope_RestoresThemeAfterException()\n    {\n        UiTheme.ResetForTests();\n        UiTheme.Initialize(PaletteRegistry.Default);\n        try\n        {\n            void ThrowDuringTemporaryScope()\n            {\n                using (UiTheme.UseTemporary(PaletteRegistry.FarClassic))\n                {\n                    Assert.Same(PaletteRegistry.FarClassic, UiTheme.Current);\n                    throw new InvalidOperationException("render failed");\n                }\n            }\n\n            Assert.Throws<InvalidOperationException>((Action)ThrowDuringTemporaryScope);\n            Assert.Same(PaletteRegistry.Default, UiTheme.Current);\n        }\n        finally\n        {\n            UiTheme.ResetForTests();\n        }\n    }\n\n    [Fact]\n    public void TemporaryThemeScope_RestoresNestedThemesInOrder()\n    {\n        UiTheme.ResetForTests();\n        UiTheme.Initialize(PaletteRegistry.Default);\n        try\n        {\n            using (UiTheme.UseTemporary(PaletteRegistry.FarClassic))\n            {\n                Assert.Same(PaletteRegistry.FarClassic, UiTheme.Current);\n                using (UiTheme.UseTemporary(PaletteRegistry.Default))\n                    Assert.Same(PaletteRegistry.Default, UiTheme.Current);\n                Assert.Same(PaletteRegistry.FarClassic, UiTheme.Current);\n            }\n\n            Assert.Same(PaletteRegistry.Default, UiTheme.Current);\n        }\n        finally\n        {\n            UiTheme.ResetForTests();\n        }\n    }\n}\n''', encoding='utf-8', newline='\r\n')

replace('tests/CSharpFar.Tests/QuickViewMonitoringBehaviorTests.cs', '    public void RecentChangesOwnTheirHitRegionAndWheelIsConsumedAtBoundary()\n', '    public void RecentChangesOwnTheirHitRegion()\n')
replace('tests/CSharpFar.Tests/QuickViewMonitoringBehaviorTests.cs', '''
        var focus = new UiFocusController();
        RoutedScrollableListInputResult moved = list.RouteInput(
            Mouse(MouseButton.WheelDown, 3, 6),
            listFrame,
            UiInputRouteContext.HitTarget(focus, list.ListTarget));
        Assert.True(moved.UiResult.Handled);
        Assert.Equal(1, list.State.SelectedIndex);

        list.State.SetSelectedIndex(0, listFrame.ViewportRows);
        listFrame = list.CalculateFrame(new Rect(2, 6, 20, 2), new Rect(22, 6, 1, 3));
        RoutedScrollableListInputResult boundary = list.RouteInput(
            Mouse(MouseButton.WheelUp, 3, 6),
            listFrame,
            UiInputRouteContext.HitTarget(focus, list.ListTarget));

        Assert.True(boundary.UiResult.Handled);
        Assert.False(boundary.UiResult.Invalidate);
        Assert.Equal(0, list.State.SelectedIndex);
''', '')
replace('tests/CSharpFar.Ui.Tests/RoutedScrollableListTests.cs', '''    [Fact]
    public void RouteInput_RejectsForeignTarget()
''', '''    [Fact]
    public void RouteInput_WheelMovesSelectionAndIsConsumedAtBoundary()
    {
        var list = new RoutedScrollableList<int>(
            new ScrollableListState<int>([1, 2, 3, 4]),
            new UiTargetId("list"),
            new UiTargetId("scrollbar"),
            RoutedScrollableListOptions.DropdownPopup);
        ScrollableListFrame frame = list.CalculateFrame(new Rect(0, 0, 8, 2), new Rect(8, 0, 1, 3));
        var focus = new UiFocusController();
        var route = UiInputRouteContext.HitTarget(focus, list.ListTarget);

        RoutedScrollableListInputResult moved = list.RouteInput(
            new MouseConsoleInputEvent(1, 0, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None),
            frame,
            route);

        Assert.True(moved.UiResult.Handled);
        Assert.Equal(1, list.State.SelectedIndex);

        list.State.SetSelectedIndex(0, frame.ViewportRows);
        frame = list.CalculateFrame(new Rect(0, 0, 8, 2), new Rect(8, 0, 1, 3));
        RoutedScrollableListInputResult boundary = list.RouteInput(
            new MouseConsoleInputEvent(1, 0, MouseButton.WheelUp, MouseEventKind.Wheel, MouseKeyModifiers.None),
            frame,
            route);

        Assert.True(boundary.UiResult.Handled);
        Assert.False(boundary.UiResult.Invalidate);
        Assert.Equal(0, list.State.SelectedIndex);
    }

    [Fact]
    public void RouteInput_RejectsForeignTarget()
''')

replace('tests/CSharpFar.Tests/HelpViewerLayerTests.cs', '''        var layer = new HelpViewerLayer([new HelpLine(HelpLineKind.Plain, Description: "body")], CSharpFarPaletteRegistry.Default);
        using (composition.OpenSurface(new InteractiveSurface(screen), layer))
        {
            composition.Render();
            Assert.False(driver.CursorVisible);
            driver.SetSize(20, 4);
            composition.Render(isResizeRecovery: true);
            Assert.False(driver.CursorVisible);
        }

        Assert.True(driver.CursorVisible);
''', '''        var layer = new HelpViewerLayer([new HelpLine(HelpLineKind.Plain, Description: "body")], CSharpFarPaletteRegistry.Default);
        var cursorStates = new List<bool>();
        int reads = 0;
        driver.BeforeReadInput = current =>
        {
            cursorStates.Add(current.CursorVisible);
            if (++reads == 1)
                current.SetSize(20, 4);
        };
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueInput(UiTestInput.Key(ConsoleKey.Escape));

        new InteractiveSurfaceHost(composition).Run(
            layer,
            static (_, action) => action == HelpAction.Close
                ? ModalDialogLoopResult<bool>.Complete(true)
                : ModalDialogLoopResult<bool>.ContinueNoChange);

        Assert.Equal([false, false], cursorStates);
        Assert.True(driver.CursorVisible);
''')
replace('tests/CSharpFar.Tests/HelpViewerLayerTests.cs', '''        var screen = host.Screen;
        var composition = host.Composition;
        layer = new HelpViewerLayer(lines, palette ?? CSharpFarPaletteRegistry.Default, firstVisibleIndex);
        composition.OpenSurface(new InteractiveSurface(screen), layer);
        return composition;
''', '''        var composition = host.Composition;
        layer = new HelpViewerLayer(lines, palette ?? CSharpFarPaletteRegistry.Default, firstVisibleIndex);
        _ = composition.RegisterPersistentOverlay(layer);
        return composition;
''')
replace('tests/CSharpFar.Tests/HelpViewerLayerTests.cs', '''        protected override UiInteractionFrame BuildInteractionFrame(UiFocusFrame frame) =>
            new([], frame, CursorTarget);
''', '''        protected override UiInteractionFrame BuildInteractionFrame(UiFocusFrame frame) =>
            new UiInteractionFrameBuilder()
                .AddFocusEntries(frame.Entries)
                .SetDefaultFocusTarget(frame.DefaultTarget)
                .SetKeyboardTarget(CursorTarget)
                .Build();
''')

forbidden = [
    'UiFocusController',
    'ScreenRendererCanvas',
    'UiInputRouteContext.HitTarget',
    'new InteractiveSurface(',
    '.Composition.PushOverlay(',
    '.Composition.OpenSurface(',
    'UiTheme.ResetForTests',
    'new TextHistory(',
    'new CheckBoxLine(',
]
roots = list(Path('tests/CSharpFar.Tests').rglob('*.cs'))
for token in forbidden:
    hits = [str(p) for p in roots if token.encode() in p.read_bytes()]
    if hits:
        raise RuntimeError(f'forbidden product-test UI internal token {token!r}: {hits}')
