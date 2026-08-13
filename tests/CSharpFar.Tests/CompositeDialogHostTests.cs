using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CompositeDialogHostTests
{
    [Fact]
    public void Run_composes_sections_and_routes_keyboard_semantics_through_non_table_content()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Tab, true, false, false));
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var content = new TestContent();
        List<CompositeDialogEventKind> events = [];

        Run(driver, content, status: () => "STATUS", semantic =>
        {
            events.Add(semantic.Kind);
            return semantic.Kind == CompositeDialogEventKind.Cancelled
                ? CompositeDialogOutcome<object?>.Complete(null)
                : semantic.Kind == CompositeDialogEventKind.ContentSelectionChanged
                    ? CompositeDialogOutcome<object?>.ContinueChanged
                    : CompositeDialogOutcome<object?>.ContinueNoChange;
        });

        Assert.True(content.RenderCount >= 2);
        Assert.Contains(CompositeDialogEventKind.ContentSelectionChanged, events);
        Assert.Contains(CompositeDialogEventKind.ContentConfirmed, events);
        Assert.Contains(CompositeDialogEventKind.Command, events);
        Assert.Contains(CompositeDialogEventKind.Cancelled, events);
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("HEADER", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("CONTENT", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("STATUS", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("Footer", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_routes_mouse_to_content_and_keeps_optional_status_absent()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var content = new TestContent();
        driver.BeforeReadInput = d =>
        {
            TestContentFrame frame = Assert.IsType<TestContentFrame>(content.LastFrame);
            d.EnqueueInput(new MouseConsoleInputEvent(frame.Bounds.X, frame.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            d.EnqueueKey(Key(ConsoleKey.Escape));
        };

        Run(driver, content, status: null, semantic => semantic.Kind == CompositeDialogEventKind.Cancelled
            ? CompositeDialogOutcome<object?>.Complete(null)
            : CompositeDialogOutcome<object?>.ContinueNoChange);

        Assert.Equal(1, content.MouseRoutes);
        Assert.DoesNotContain(driver.WriteRecords, write => write.Text.Contains("STATUS", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_republishes_content_frame_after_resize_without_stale_target()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var content = new TestContent();
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.BeforeReadInput = d => d.SetSize(30, 8);

        Run(driver, content, status: () => "STATUS", semantic => semantic.Kind == CompositeDialogEventKind.Cancelled
            ? CompositeDialogOutcome<object?>.Complete(null)
            : semantic.Kind == CompositeDialogEventKind.ContentSelectionChanged
                ? CompositeDialogOutcome<object?>.ContinueChanged
                : CompositeDialogOutcome<object?>.ContinueNoChange);

        Assert.True(content.Frames.Count >= 2);
        Assert.NotEqual(content.Frames[0].Bounds, content.Frames[^1].Bounds);
        Assert.All(content.RoutedFrames, frame => Assert.Contains(frame, content.Frames));
    }

    [Fact]
    public void Run_supports_table_content_and_preserves_selection_when_items_are_replaced()
    {
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.F5));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var table = new TableList<string>(["one", "two"], new TableListDefinition<string>
        {
            Columns = [TableColumn<string>.Text("Item", value => value, TableWidth.Flexible(20, 1))],
        });

        Run(driver, table, status: null, semantic =>
        {
            if (semantic.Kind == CompositeDialogEventKind.Command)
            {
                table.ReplaceItems(["zero", "two", "three"], value => value);
                return CompositeDialogOutcome<object?>.ContinueChanged;
            }
            return semantic.Kind == CompositeDialogEventKind.Cancelled
                ? CompositeDialogOutcome<object?>.Complete(null)
                : CompositeDialogOutcome<object?>.ContinueNoChange;
        });

        Assert.Equal("two", table.SelectedItem);
    }

    private static void Run(
        FakeConsoleDriver driver,
        ICompositeDialogContent content,
        Func<string?>? status,
        Func<CompositeDialogEvent, CompositeDialogOutcome<object?>> handle)
    {
        var form = new ScrollableFormDialog();
        form.SetRows([FormControls.CheckBox("HEADER", true)], [FormControls.Buttons([DialogButton.Action("footer", "Footer", 'F'), DialogButton.Cancel()])]);
        UiTestHost host = UiTestHost.Create(driver);
        new CompositeDialogHost(host.ModalDialogs).Run(
            new CompositeDialogOptions("Composite", 50, 14), form, content, status,
            new Dictionary<ConsoleKey, string> { [ConsoleKey.F5] = "refresh" }, handle);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private sealed class TestContent : ICompositeDialogContent
    {
        private readonly UiTargetId _target = new("test-content");
        public List<TestContentFrame> Frames { get; } = [];
        public List<TestContentFrame> RoutedFrames { get; } = [];
        public ICompositeDialogContentFrame? LastFrame { get; private set; }
        public int RenderCount { get; private set; }
        public int MouseRoutes { get; private set; }
        public ICompositeDialogContentFrame CalculateFrame(Rect bounds)
        {
            var frame = new TestContentFrame(bounds);
            Frames.Add(frame);
            return frame;
        }
        public void Render(IUiCanvas canvas, ICompositeDialogContentFrame frame)
        {
            RenderCount++;
            LastFrame = frame;
            TestContentFrame value = Require(frame);
            if (value.Bounds.Width > 0 && value.Bounds.Height > 0)
                canvas.Write(value.Bounds.X, value.Bounds.Y, ConsoleTextMetrics.FitToCells("CONTENT", value.Bounds.Width), new CellStyle(ConsoleColor.Gray, ConsoleColor.Black));
        }
        public UiInteractionFragment BuildInteractionFragment(ICompositeDialogContentFrame frame, int focusOrder)
        {
            TestContentFrame value = Require(frame);
            return value.Bounds.Width > 0 && value.Bounds.Height > 0
                ? new UiInteractionFrameBuilder().AddHitRegion(_target, value.Bounds).AddFocusEntry(_target, focusOrder).BuildFragment()
                : UiInteractionFragment.Empty;
        }
        public CompositeDialogContentInputResult RouteInput(ConsoleInputEvent input, ICompositeDialogContentFrame frame, UiInputRouteContext route)
        {
            TestContentFrame value = Require(frame);
            if (route.Target != _target) return CompositeDialogContentInputResult.NotHandled;
            RoutedFrames.Add(value);
            if (input is MouseConsoleInputEvent) { MouseRoutes++; return new(CompositeDialogContentEventKind.SelectionChanged, UiInputResult.HandledAndInvalidate, true); }
            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.DownArrow }) return new(CompositeDialogContentEventKind.SelectionChanged, UiInputResult.HandledAndInvalidate, true);
            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter }) return new(CompositeDialogContentEventKind.Confirmed, UiInputResult.HandledResult, true);
            return new(CompositeDialogContentEventKind.NotHandled, UiInputResult.NotHandled, true);
        }
        public void ApplyCommittedFrame(ICompositeDialogContentFrame frame) => LastFrame = frame;
        private static TestContentFrame Require(ICompositeDialogContentFrame frame) => Assert.IsType<TestContentFrame>(frame);
    }

    private sealed record TestContentFrame(Rect Bounds) : ICompositeDialogContentFrame;
}
