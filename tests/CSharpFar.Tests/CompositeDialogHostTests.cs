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

    [Fact]
    public void Run_uses_actual_body_and_footer_heights_including_a_missing_footer()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var content = new TestContent();
        var form = new ScrollableFormDialog();
        form.SetRows(
            [FormControls.CheckBox("HEADER-1", true), FormControls.CheckBox("HEADER-2", true)],
            [FormControls.CheckBoxColumns([[FormControls.CheckBox("FOOTER-1", true), FormControls.CheckBox("FOOTER-2", true)]]),
             FormControls.Buttons(DialogButton.Cancel("Close", 'C'))]);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        Run(driver, form, content, status: () => "STATUS", semantic => semantic.Kind == CompositeDialogEventKind.Cancelled
            ? CompositeDialogOutcome<object?>.Complete(null)
            : CompositeDialogOutcome<object?>.ContinueNoChange);

        Assert.Equal(4, Assert.IsType<TestContentFrame>(content.Frames[0]).Bounds.Height);

        var bodyOnlyDriver = new FakeConsoleDriver(80, 25);
        var bodyOnlyContent = new TestContent();
        var bodyOnlyForm = new ScrollableFormDialog();
        bodyOnlyForm.SetRows([FormControls.CheckBox("HEADER-1", true), FormControls.CheckBox("HEADER-2", true)]);
        bodyOnlyDriver.EnqueueKey(Key(ConsoleKey.Escape));

        Run(bodyOnlyDriver, bodyOnlyForm, bodyOnlyContent, status: null, semantic => semantic.Kind == CompositeDialogEventKind.Cancelled
            ? CompositeDialogOutcome<object?>.Complete(null)
            : CompositeDialogOutcome<object?>.ContinueNoChange);

        Assert.Contains(bodyOnlyDriver.WriteRecords, write => write.Text.Contains("HEADER-1", StringComparison.Ordinal));
        Assert.Contains(bodyOnlyDriver.WriteRecords, write => write.Text.Contains("HEADER-2", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_traverses_header_content_and_enabled_footer_in_both_directions()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var content = new TestContent();
        var form = new ScrollableFormDialog();
        form.SetRows(
            [FormControls.CheckBox("HEADER", true)],
            [FormControls.CheckBox("DISABLED", true, enabled: false, disabledReason: "Unavailable"),
             FormControls.Buttons(DialogButton.Action("disabled", "Disabled", 'D') with { IsEnabled = false }, DialogButton.Action("enabled", "Enabled", 'E'))]);
        var commands = new List<string>();
        driver.BeforeReadInput = current =>
        {
            current.EnqueueKey(Key(ConsoleKey.Tab));
            current.EnqueueKey(Key(ConsoleKey.DownArrow));
            current.EnqueueKey(Key(ConsoleKey.Tab));
            current.EnqueueKey(Key(ConsoleKey.RightArrow));
            current.EnqueueKey(Key(ConsoleKey.Enter));
            current.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Tab, true, false, false));
            current.EnqueueKey(Key(ConsoleKey.DownArrow));
            current.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Tab, true, false, false));
            current.EnqueueKey(Key(ConsoleKey.Spacebar));
            current.EnqueueKey(Key(ConsoleKey.Escape));
        };

        Run(driver, form, content, status: null, semantic =>
        {
            if (semantic.Kind == CompositeDialogEventKind.Command && semantic.Command is { } command)
                commands.Add(command);
            return semantic.Kind == CompositeDialogEventKind.Cancelled
                ? CompositeDialogOutcome<object?>.Complete(null)
                : CompositeDialogOutcome<object?>.ContinueNoChange;
        });

        Assert.Equal(2, content.KeyboardRoutes);
        Assert.Equal(["enabled"], commands);
    }

    [Fact]
    public void Run_routes_mouse_to_enabled_footer_action_without_activating_disabled_action()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var form = new ScrollableFormDialog();
        form.SetRows([FormControls.CheckBox("HEADER", true)], [FormControls.Buttons(
            DialogButton.Action("disabled", "Disabled", 'D') with { IsEnabled = false },
            DialogButton.Action("enabled", "Enabled", 'E'))]);
        var commands = new List<string>();
        driver.BeforeReadInput = current =>
        {
            FakeConsoleDriver.WriteRecord buttonBar = current.WriteRecords.Last(write => write.Text.Contains("[ Disabled ] [ Enabled ]", StringComparison.Ordinal));
            int disabledX = buttonBar.X + buttonBar.Text.IndexOf("[ Disabled ]", StringComparison.Ordinal);
            int enabledX = buttonBar.X + buttonBar.Text.IndexOf("[ Enabled ]", StringComparison.Ordinal);
            current.EnqueueInput(new MouseConsoleInputEvent(disabledX, buttonBar.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            current.EnqueueInput(new MouseConsoleInputEvent(disabledX, buttonBar.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
            current.EnqueueInput(new MouseConsoleInputEvent(enabledX, buttonBar.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            current.EnqueueInput(new MouseConsoleInputEvent(enabledX, buttonBar.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
            current.EnqueueKey(Key(ConsoleKey.Escape));
        };

        Run(driver, form, new TestContent(), status: null, semantic =>
        {
            if (semantic.Kind == CompositeDialogEventKind.Command && semantic.Command is { } command)
                commands.Add(command);
            return semantic.Kind == CompositeDialogEventKind.Cancelled
                ? CompositeDialogOutcome<object?>.Complete(null)
                : CompositeDialogOutcome<object?>.ContinueNoChange;
        });

        Assert.Equal(["enabled"], commands);
    }

    [Fact]
    public void Run_omits_content_targets_at_zero_height_and_renders_once_after_one_semantic_change()
    {
        var driver = new FakeConsoleDriver(20, 8);
        var content = new TestContent();
        var form = new ScrollableFormDialog();
        form.SetRows(
            [FormControls.CheckBox("HEADER-1", true), FormControls.CheckBox("HEADER-2", true)],
            [FormControls.CheckBoxColumns([[FormControls.CheckBox("FOOTER-1", true), FormControls.CheckBox("FOOTER-2", true)]])]);
        driver.EnqueueKey(Key(ConsoleKey.Tab));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        Run(driver, form, content, status: null, semantic => semantic.Kind == CompositeDialogEventKind.Cancelled
            ? CompositeDialogOutcome<object?>.Complete(null)
            : semantic.Kind == CompositeDialogEventKind.ContentSelectionChanged
                ? CompositeDialogOutcome<object?>.ContinueChanged
                : CompositeDialogOutcome<object?>.ContinueNoChange,
            options: new CompositeDialogOptions("Composite", 20, 8));

        Assert.All(content.Frames, frame => Assert.Equal(0, frame.Bounds.Height));
        Assert.Empty(content.RoutedFrames);
        Assert.Equal(2, content.RenderCount);

        var renderDriver = new FakeConsoleDriver(80, 25);
        var renderContent = new TestContent();
        renderDriver.EnqueueKey(Key(ConsoleKey.Tab));
        renderDriver.EnqueueKey(Key(ConsoleKey.DownArrow));
        renderDriver.EnqueueKey(Key(ConsoleKey.Escape));
        Run(renderDriver, renderContent, status: null, semantic => semantic.Kind == CompositeDialogEventKind.Cancelled
            ? CompositeDialogOutcome<object?>.Complete(null)
            : semantic.Kind == CompositeDialogEventKind.ContentSelectionChanged
                ? CompositeDialogOutcome<object?>.ContinueChanged
                : CompositeDialogOutcome<object?>.ContinueNoChange);
        Assert.Equal(3, renderContent.RenderCount);
    }

    private static void Run(
        FakeConsoleDriver driver,
        ICompositeDialogContent content,
        Func<string?>? status,
        Func<CompositeDialogEvent, CompositeDialogOutcome<object?>> handle)
    {
        var form = new ScrollableFormDialog();
        form.SetRows([FormControls.CheckBox("HEADER", true)], [FormControls.Buttons([DialogButton.Action("footer", "Footer", 'F'), DialogButton.Cancel()])]);
        Run(driver, form, content, status, handle);
    }

    private static void Run(
        FakeConsoleDriver driver,
        ScrollableFormDialog form,
        ICompositeDialogContent content,
        Func<string?>? status,
        Func<CompositeDialogEvent, CompositeDialogOutcome<object?>> handle,
        CompositeDialogOptions? options = null)
    {
        UiTestHost host = UiTestHost.Create(driver);
        new CompositeDialogHost(host.ModalDialogs).Run(
            options ?? new CompositeDialogOptions("Composite", 50, 14), form, content, status,
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
        public int KeyboardRoutes { get; private set; }
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
            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.DownArrow }) { KeyboardRoutes++; return new(CompositeDialogContentEventKind.SelectionChanged, UiInputResult.HandledAndInvalidate, true); }
            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter }) return new(CompositeDialogContentEventKind.Confirmed, UiInputResult.HandledResult, true);
            return new(CompositeDialogContentEventKind.NotHandled, UiInputResult.NotHandled, true);
        }
        public void ApplyCommittedFrame(ICompositeDialogContentFrame frame) => LastFrame = frame;
        private static TestContentFrame Require(ICompositeDialogContentFrame frame) => Assert.IsType<TestContentFrame>(frame);
    }

    private sealed record TestContentFrame(Rect Bounds) : ICompositeDialogContentFrame;
}
