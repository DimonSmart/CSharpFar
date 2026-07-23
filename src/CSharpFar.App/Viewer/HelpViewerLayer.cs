using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class HelpViewerLayer : InteractiveSurfaceLayer<HelpViewerFrame, HelpAction>
{
    internal static readonly UiTargetId Keyboard = new("help.keyboard");
    internal static readonly UiTargetId Content = new("help.content");
    internal static readonly UiTargetId Scrollbar = new("help.vertical-scrollbar");

    private static readonly FunctionKeyBarAction<HelpAction>[] FunctionKeyActions =
    [
        new(10, "Close", HelpAction.Close),
    ];

    private static readonly FunctionKeyBarController<HelpAction> FunctionKeysController =
        new(new UiTargetId("help.function-key-bar"));

    internal static UiTargetId FunctionKeys => FunctionKeysController.InteractionTarget;

    private const int KeyColumnWidth = 20;
    private readonly HelpLine[] _lines;
    private readonly ConsolePalette _palette;
    private readonly ScrollableViewport _verticalViewport = new();
    private int _scrollLeft;

    public HelpViewerLayer(HelpLine[] lines, ConsolePalette palette)
        : base(
            (_, _) => throw new InvalidOperationException("HelpViewerLayer uses overridden rendering."),
            _ => UiInteractionFrame.Empty,
            (_, _, _) => new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None))
    {
        _lines = lines;
        _palette = palette;
    }

    protected override HelpViewerFrame RenderFrameCore(UiRenderContext context)
    {
        HelpViewerFrame frame = CreateFrame(context);
        Draw(context.Canvas, _lines, frame, _palette);
        return frame;
    }

    protected override UiInteractionFrame BuildInteractionFrameCore(HelpViewerFrame frame) =>
        BuildInteraction(frame);

    protected override void OnFrameCommitted(HelpViewerFrame frame)
    {
        _verticalViewport.ApplyCommittedFrame(frame.VerticalViewport);
        _scrollLeft = frame.ScrollLeft;
    }

    private HelpViewerFrame CreateFrame(UiRenderContext context)
    {
        int width = context.Size.Width;
        int height = context.Size.Height;
        int visibleRows = Math.Max(0, height - 2);
        bool scrollbarVisible = visibleRows > 0 && _lines.Length > visibleRows && width > 1;
        Rect content = new(0, 1, Math.Max(0, width - (scrollbarVisible ? 1 : 0)), visibleRows);
        Rect? scrollbar = scrollbarVisible ? new Rect(width - 1, 1, 1, visibleRows) : null;
        ScrollableViewportFrameState verticalViewport = _verticalViewport.CalculateFrameState(
            _lines.Length,
            visibleRows,
            content,
            scrollbar);
        int maxLeft = Math.Max(0, MaximumDisplayWidth(_lines) - content.Width);
        int left = Math.Clamp(_scrollLeft, 0, maxLeft);
        Rect functionKeyBarBounds = height > 0
            ? new Rect(0, height - 1, width, 1)
            : new Rect(0, 0, 0, 0);
        IReadOnlyList<FunctionKeyBarActionHit<HelpAction>> footerActionHits =
            FunctionKeysController.BuildActionHits(
                functionKeyBarBounds.Y,
                functionKeyBarBounds.Width,
                FunctionKeyActions);

        return new HelpViewerFrame(
            context.Viewport,
            new Rect(0, 0, width, Math.Min(1, height)),
            verticalViewport,
            footerActionHits,
            functionKeyBarBounds,
            left,
            Math.Max(0, _lines.Length - visibleRows),
            maxLeft,
            visibleRows);
    }

    private static UiInteractionFrame BuildInteraction(HelpViewerFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFocusEntry(Keyboard, 0, cursor: new UiCursorPlacement(0, 0, false))
            .SetDefaultFocusTarget(Keyboard)
            .SetKeyboardTarget(Keyboard);
        if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
            builder.AddHitRegion(Content, frame.ContentBounds);
        if (frame.ScrollBarBounds is { } bar)
            builder.AddHitRegion(Scrollbar, bar);

        builder.AddFragment(FunctionKeysController.BuildInteractionFragment(frame.FooterActionHits));
        return builder.Build();
    }
}
