from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one match in {path}, found {count}")
    file_path.write_text(text.replace(old, new), encoding="utf-8", newline="")


replace_once(
    "tests/CSharpFar.Tests/HelpViewerLayerTests.cs",
    '''    private sealed class CursorRootSurface(ScreenRenderer screen) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public IUiFocusState FocusState { get; } = new UiFocusController();
        public UiInteractionFrame CommittedInteractionFrame { get; private set; } = UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        public void Render(UiRenderContext context)
        {
            var focus = FocusFrame(
                [new UiFocusEntry(new UiTargetId("root.cursor"), 0, Cursor: new UiCursorPlacement(2, 2, true))],
                new UiTargetId("root.cursor"));
            CommittedInteractionFrame = new UiInteractionFrame([], focus, new UiTargetId("root.cursor"));
        }

        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => UiInputResult.NotHandled;
    }
''',
    '''    private sealed class CursorRootSurface(ScreenRenderer screen) : UiLayer<UiFocusFrame>, IUiSurface
    {
        private static readonly UiTargetId CursorTarget = new("root.cursor");

        public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

        public void CompleteFrame(UiFrameCompletion completion) { }

        protected override UiFocusFrame RenderFrame(UiRenderContext context) =>
            FocusFrame(
                [new UiFocusEntry(CursorTarget, 0, Cursor: new UiCursorPlacement(2, 2, true))],
                CursorTarget);

        protected override UiInteractionFrame BuildInteractionFrame(UiFocusFrame frame) =>
            new([], frame, CursorTarget);

        protected override UiInputResult RouteInput(
            ConsoleInputEvent input,
            UiFocusFrame frame,
            UiInputRouteContext context) => UiInputResult.NotHandled;
    }
''')

replace_once(
    "tests/CSharpFar.Tests/UiLayerTests.cs",
    '''        host.DispatchInput(Key(ConsoleKey.A));
        Assert.Equal(first, layer.LastRoute!.Target);
        host.DispatchInput(Key(ConsoleKey.B));

        Assert.Equal(second, layer.LastRoute!.Target);
''',
    '''        host.DispatchInput(Key(ConsoleKey.A));
        Assert.Equal(first, layer.LastRoute!.Target);
        Assert.Equal(first, layer.FocusState.FocusedTarget);

        host.Render();
        Assert.Equal(second, layer.FocusState.FocusedTarget);
        host.DispatchInput(Key(ConsoleKey.B));

        Assert.Equal(second, layer.LastRoute!.Target);
''')

replace_once(
    "tests/CSharpFar.Tests/UiLayerTests.cs",
    '''    private sealed class SurfaceLayer : IUiSurface, IUiLayer
''',
    '''    private sealed class SurfaceLayer : IUiSurface, IUiLayer, IUiFocusRuntime
''')

replace_once(
    "tests/CSharpFar.Tests/UiLayerTests.cs",
    '''        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            _layer.RouteInput(input, context);
    }
''',
    '''        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
            _layer.RouteInput(input, context);

        void IUiFocusRuntime.RequestFocusOnNextCommit(UiFocusRequest request) =>
            ((IUiFocusRuntime)_layer).RequestFocusOnNextCommit(request);
    }
''')

replace_once(
    "tests/CSharpFar.Tests/UiLayerTargetRoutingTests.cs",
    '''    public void FocusRequest_IsValidatedAndAffectsOnlyNextRouteAndSourceLayer()
    {
        var first = new UiTargetId("first");
        var second = new UiTargetId("second");
        var root = Layer(Focus(first, second));
        var overlay = Layer(Focus(first, second));
        overlay.Result = (_, context) => context.Target == first ? UiInputResult.RequestFocus(second) : UiInputResult.NotHandled;
        var host = Host(root);
        host.Render();
        using var scope = host.PushOverlay(overlay);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));
        AssertRoute(overlay, first, UiInputRouteKind.FocusedTarget);
        Assert.Equal(second, overlay.FocusState.FocusedTarget);
        Assert.Equal(first, root.FocusState.FocusedTarget);
        host.DispatchInput(Key(ConsoleKey.B));
        AssertRoute(overlay, second, UiInputRouteKind.FocusedTarget);

        overlay.Result = (_, _) => UiInputResult.RequestFocus(new UiTargetId("missing"));
        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(Key(ConsoleKey.C)));
    }
''',
    '''    public void FocusRequest_AppliesOnNextCommitAndOnlyToSourceLayer()
    {
        var first = new UiTargetId("first");
        var second = new UiTargetId("second");
        var root = Layer(Focus(first, second));
        var overlay = Layer(Focus(first, second));
        overlay.Result = (_, context) => context.Target == first ? UiInputResult.RequestFocus(second) : UiInputResult.NotHandled;
        var host = Host(root);
        host.Render();
        using var scope = host.PushOverlay(overlay);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));
        AssertRoute(overlay, first, UiInputRouteKind.FocusedTarget);
        Assert.Equal(first, overlay.FocusState.FocusedTarget);
        Assert.Equal(first, root.FocusState.FocusedTarget);

        host.Render();
        Assert.Equal(second, overlay.FocusState.FocusedTarget);
        Assert.Equal(first, root.FocusState.FocusedTarget);
        host.DispatchInput(Key(ConsoleKey.B));
        AssertRoute(overlay, second, UiInputRouteKind.FocusedTarget);

        overlay.Result = (_, _) => UiInputResult.RequestFocus(new UiTargetId("missing"));
        host.DispatchInput(Key(ConsoleKey.C));
        host.Render();
        Assert.Equal(second, overlay.FocusState.FocusedTarget);
    }
''')

replace_once(
    "tests/CSharpFar.Tests/UiLayerTargetRoutingTests.cs",
    '''    private sealed class Surface(ScreenRenderer screen, TestLayer layer) : IUiSurface, IUiLayer
''',
    '''    private sealed class Surface(ScreenRenderer screen, TestLayer layer) : IUiSurface, IUiLayer, IUiFocusRuntime
''')

replace_once(
    "tests/CSharpFar.Tests/UiLayerTargetRoutingTests.cs",
    '''        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);
    }
''',
    '''        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);

        void IUiFocusRuntime.RequestFocusOnNextCommit(UiFocusRequest request) =>
            ((IUiFocusRuntime)layer).RequestFocusOnNextCommit(request);
    }
''')
