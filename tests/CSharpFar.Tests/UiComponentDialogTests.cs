using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiComponentDialogTests
{
    [Fact]
    public void UiTheme_CurrentReturnsDefaultTheme()
    {
        Assert.Same(PaletteRegistry.Default, UiTheme.Current);
    }

    [Fact]
    public void SelectionListDialog_EnterReturnsSelectedItem()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<string>(["one", "two", "three"], static item => item, "Pick");

        var result = dialog.Show(CreateModalHost(driver));

        Assert.True(result.IsConfirmed);
        Assert.Equal("two", result.SelectedItem);
        Assert.Equal(1, result.SelectedIndex);
    }

    [Fact]
    public void SelectionListDialog_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialog = new SelectionListDialog<string>(["one"], static item => item, "Pick");

        var result = dialog.Show(CreateModalHost(driver));

        Assert.False(result.IsConfirmed);
        Assert.Null(result.SelectedItem);
        Assert.Equal(-1, result.SelectedIndex);
    }

    [Fact]
    public void SelectionListDialog_F10Cancels()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialog = new SelectionListDialog<string>(["one"], static item => item, "Pick");

        var result = dialog.Show(CreateModalHost(driver));

        Assert.False(result.IsConfirmed);
        Assert.Equal(-1, result.SelectedIndex);
    }

    [Fact]
    public void SelectionListDialog_PageAndHomeEndUpdateSelectionAndScroll()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.PageDown));
        driver.EnqueueKey(Key(ConsoleKey.End));
        driver.EnqueueKey(Key(ConsoleKey.PageUp));
        driver.EnqueueKey(Key(ConsoleKey.Home));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString(), "Pick")
        {
            MaxVisibleRows = 5,
        };

        var result = dialog.Show(CreateModalHost(driver));

        Assert.True(result.IsConfirmed);
        Assert.Equal(0, result.SelectedItem);
        Assert.Equal(0, result.SelectedIndex);
        Assert.Equal(0, dialog.ScrollTop);
    }

    [Fact]
    public void SelectionListDialog_MouseClickSelectsExpectedRow()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueInput(new MouseConsoleInputEvent(10, 5, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<string>(["one", "two", "three"], static item => item, "Pick");

        var result = dialog.Show(CreateModalHost(driver));

        Assert.True(result.IsConfirmed);
        Assert.Equal("two", result.SelectedItem);
    }

    [Fact]
    public void SelectionListDialog_DrawsScrollbarOnlyWhenNeeded()
    {
        var noScrollDriver = new FakeConsoleDriver(40, 12);
        noScrollDriver.EnqueueKey(Key(ConsoleKey.Escape));
        new SelectionListDialog<string>(["one", "two"], static item => item, "Pick").Show(CreateModalHost(noScrollDriver));

        var scrollDriver = new FakeConsoleDriver(40, 12);
        scrollDriver.EnqueueKey(Key(ConsoleKey.Escape));
        new SelectionListDialog<int>(Enumerable.Range(0, 20).ToArray(), static item => item.ToString(), "Pick")
        {
            MaxVisibleRows = 5,
        }.Show(CreateModalHost(scrollDriver));

        Assert.DoesNotContain(noScrollDriver.WriteRecords, write => write.Text.Contains('▲'));
        Assert.Contains(scrollDriver.WriteRecords, write => write.Text.Contains('▲'));
    }

    [Fact]
    public void SelectionListDialog_EmptyListShowsEmptyTextAndCancelsOnEnter()
    {
        var driver = new FakeConsoleDriver(40, 12);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialog = new SelectionListDialog<string>([], static item => item, "Pick")
        {
            EmptyText = "Nothing here",
        };

        var result = dialog.Show(CreateModalHost(driver));

        Assert.False(result.IsConfirmed);
        Assert.Equal(-1, result.SelectedIndex);
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("Nothing here", StringComparison.Ordinal));
    }

    [Fact]
    public void DropdownSelect_KeyboardSelectionAndCancelKeepsPreviousValue()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var dropdown = new DropdownSelect<string>(["utf-8", "utf-16", "ascii"], static item => item);
        var field = new Rect(5, 4, 12, 1);

        dropdown.Open();
        var frame = dropdown.CalculateFrame(driver.GetSize(), field);
        dropdown.ApplyCommittedFrame(frame);
        Assert.True(dropdown.IsOpen);
        Assert.True(dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), frame, out _, out _));
        Assert.Equal(1, dropdown.SelectedIndex);
        frame = dropdown.CalculateFrame(driver.GetSize(), field);
        dropdown.ApplyCommittedFrame(frame);
        Assert.True(dropdown.TryHandleKey(Key(ConsoleKey.Escape), frame, out _, out _));

        Assert.False(dropdown.IsOpen);
        Assert.Equal("utf-8", dropdown.SelectedItem);
    }

    [Fact]
    public void DropdownSelect_MouseSelectionAndScroll()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 10).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
        };
        var field = new Rect(5, 4, 12, 1);
        dropdown.Open();

        for (int i = 0; i < 4; i++)
        {
            var frame = dropdown.CalculateFrame(driver.GetSize(), field);
            dropdown.ApplyCommittedFrame(frame);
            dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), frame, out _, out _);
        }
        Assert.True(dropdown.ScrollTop > 0);

        var mouseFrame = dropdown.CalculateFrame(driver.GetSize(), field);
        dropdown.ApplyCommittedFrame(mouseFrame);
        Assert.True(dropdown.TryHandlePopupMouse(
            new MouseConsoleInputEvent(6, 7, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            mouseFrame,
            out bool selected,
            out _));
        Assert.True(selected);
        Assert.False(dropdown.IsOpen);
    }

    [Fact]
    public void DropdownSelect_ClickOnPopupBorder_DoesNotSelectItem()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var screen = new ScreenRenderer(driver);
        var dropdown = new DropdownSelect<string>(["one", "two", "three"], static item => item);
        var field = new Rect(5, 4, 12, 1);
        dropdown.Open();
        var frame = dropdown.CalculateFrame(driver.GetSize(), field);
        dropdown.ApplyCommittedFrame(frame);
        dropdown.RenderPopup(screen, frame);
        Rect popupBounds = frame.PopupBounds!.Value;

        bool handled = dropdown.TryHandlePopupMouse(
            new MouseConsoleInputEvent(popupBounds.X, popupBounds.Y + 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            out bool selected,
            out _);

        Assert.True(handled);
        Assert.False(selected);
        Assert.True(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
        Assert.Equal("one", dropdown.SelectedItem);
    }

    [Fact]
    public void DropdownSelect_ClickOnRenderedItem_SelectsItem()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var screen = new ScreenRenderer(driver);
        var dropdown = new DropdownSelect<string>(["one", "two", "three"], static item => item);
        var field = new Rect(5, 4, 12, 1);
        dropdown.Open();
        var frame = dropdown.CalculateFrame(driver.GetSize(), field);
        dropdown.ApplyCommittedFrame(frame);
        dropdown.RenderPopup(screen, frame);
        Rect popupBounds = frame.PopupBounds!.Value;

        bool handled = dropdown.TryHandlePopupMouse(
            new MouseConsoleInputEvent(popupBounds.X + 1, popupBounds.Y + 2, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            out bool selected,
            out _);

        Assert.True(handled);
        Assert.True(selected);
        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.Equal("two", dropdown.SelectedItem);
    }

    [Fact]
    public void DropdownSelect_ToggleCloseRestoresPreviousSelection()
    {
        var driver = new FakeConsoleDriver(40, 12);
        var dropdown = new DropdownSelect<string>(["utf-8", "utf-16", "ascii"], static item => item);
        var field = new Rect(5, 4, 12, 1);

        dropdown.Toggle();
        var frame = dropdown.CalculateFrame(driver.GetSize(), field);
        dropdown.ApplyCommittedFrame(frame);
        dropdown.TryHandleKey(Key(ConsoleKey.DownArrow), frame, out _, out _);
        dropdown.Toggle();

        Assert.False(dropdown.IsOpen);
        Assert.Equal(0, dropdown.SelectedIndex);
    }

    [Fact]
    public void DropdownSelect_SmallScreenCanHaveZeroContentRows()
    {
        var dropdown = new DropdownSelect<string>(["a", "b", "c"], static item => item);

        int rows = dropdown.ContentRows(new ConsoleSize(12, 3), new Rect(1, 1, 6, 1));

        Assert.Equal(0, rows);
    }

    [Fact]
    public void DropdownSelect_CalculateFrame_DoesNotMutateUntilApplied()
    {
        var dropdown = new DropdownSelect<int>(Enumerable.Range(0, 10).ToArray(), static item => item.ToString())
        {
            MaxVisibleRows = 3,
            SelectedIndex = 8,
            ScrollTop = 8,
        };
        var field = new Rect(1, 1, 8, 1);
        dropdown.Open();

        var rejectedFrame = dropdown.CalculateFrame(new ConsoleSize(12, 3), field);

        Assert.Equal(8, dropdown.SelectedIndex);
        Assert.Equal(8, dropdown.ScrollTop);

        var committedFrame = dropdown.CalculateFrame(new ConsoleSize(40, 12), field);
        dropdown.ApplyCommittedFrame(committedFrame);

        Assert.Equal(committedFrame.ListState.SelectedIndex, dropdown.SelectedIndex);
        Assert.Equal(committedFrame.ListState.ScrollTop, dropdown.ScrollTop);
        Assert.NotEqual(rejectedFrame.ContentRows, committedFrame.ContentRows);
    }

    [Fact]
    public void ListWithButtonsDialog_ReturnsListDefaultAndButtonActions()
    {
        var listDriver = new FakeConsoleDriver(80, 20);
        listDriver.EnqueueKey(Key(ConsoleKey.Enter));
        var listDialog = CreateListWithButtons(["alpha"]);

        var listResult = listDialog.Show(CreateModalHost(listDriver));

        Assert.NotNull(listResult);
        Assert.Equal("connect", listResult.ActionId);
        Assert.Equal("alpha", listResult.SelectedItem);

        var buttonDriver = new FakeConsoleDriver(80, 20);
        buttonDriver.EnqueueKey(Key(ConsoleKey.Tab));
        buttonDriver.EnqueueKey(Key(ConsoleKey.RightArrow));
        buttonDriver.EnqueueKey(Key(ConsoleKey.Enter));
        var buttonDialog = CreateListWithButtons(["alpha"]);

        var buttonResult = buttonDialog.Show(CreateModalHost(buttonDriver));

        Assert.NotNull(buttonResult);
        Assert.Equal("delete", buttonResult.ActionId);
    }

    [Fact]
    public void ListWithButtonsDialog_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = CreateListWithButtons(["alpha"]).Show(CreateModalHost(driver));

        Assert.Null(result);
    }

    [Fact]
    public void ListWithButtonsDialog_MouseClickButtonReturnsButtonAction()
    {
        var driver = new FakeConsoleDriver(80, 20);
        for (int y = 10; y <= 15; y++)
            for (int x = 35; x <= 50; x++)
                driver.EnqueueInput(new MouseConsoleInputEvent(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        var result = CreateListWithButtons(["alpha"]).Show(CreateModalHost(driver));

        Assert.NotNull(result);
        Assert.Equal("delete", result.ActionId);
    }

    [Fact]
    public void DialogButtonBar_MouseOutsideExplicitLayoutDoesNotActivateButton()
    {
        var buttonBar = new DialogButtonBar([new DialogButton("ok", "OK", 'O')]);
        int focused = 0;
        var layout = buttonBar.CalculateLayout(10, 2, 20);

        bool handled = buttonBar.TryHandleInput(
            new MouseConsoleInputEvent(0, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            layout,
            ref focused,
            out string? buttonId);

        Assert.False(handled);
        Assert.Null(buttonId);
    }

    [Fact]
    public void DialogButtonBar_DisabledButtonDoesNotActivate()
    {
        var driver = new FakeConsoleDriver(40, 8);
        var screen = new ScreenRenderer(driver);
        var buttonBar = new DialogButtonBar([new DialogButton("delete", "Delete", 'D', IsEnabled: false)]);
        int focused = 0;
        var layout = buttonBar.Render(screen, 0, 0, 20, focused, new CellStyle(ConsoleColor.White, ConsoleColor.Black), new CellStyle(ConsoleColor.Black, ConsoleColor.White));

        bool handled = buttonBar.TryHandleInput(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)), layout, ref focused, out string? buttonId);

        Assert.True(handled);
        Assert.Null(buttonId);
    }

    [Fact]
    public void DialogButtonBar_ClipsMouseTargetsToVisibleArea()
    {
        var buttonBar = new DialogButtonBar([new DialogButton("ok", "VeryLongButton", 'V')]);
        var layout = buttonBar.CalculateLayout(2, 1, 6);
        int focused = 0;

        Assert.Equal(new Rect(2, 1, 6, 1), layout.ButtonBounds[0]);

        bool handled = buttonBar.TryHandleInput(
            new MouseConsoleInputEvent(9, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            layout,
            ref focused,
            out string? buttonId);

        Assert.False(handled);
        Assert.Null(buttonId);
    }

    [Fact]
    public void ChoiceDialog_EnterActivatesDefaultButton()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new ChoiceDialog(CreateModalHost(driver)).Show(CreateChoiceOptions());

        Assert.Equal(0, result.ButtonIndex);
        Assert.Equal("yes", result.ButtonId);
    }

    [Fact]
    public void ChoiceDialog_EscapeReturnsCancelButton()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new ChoiceDialog(CreateModalHost(driver)).Show(CreateChoiceOptions());

        Assert.Equal(1, result.ButtonIndex);
        Assert.Equal("no", result.ButtonId);
    }

    [Fact]
    public void ChoiceDialog_MouseClickActivatesButton()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueInput(new MouseConsoleInputEvent(42, 10, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new ChoiceDialog(CreateModalHost(driver)).Show(CreateChoiceOptions());

        Assert.Equal("no", result.ButtonId);
    }

    [Fact]
    public void ChoiceDialog_NormalizesDefaultAndCancelButtonIndexes()
    {
        var enterDriver = new FakeConsoleDriver(80, 20);
        enterDriver.EnqueueKey(Key(ConsoleKey.Enter));
        var escapeDriver = new FakeConsoleDriver(80, 20);
        escapeDriver.EnqueueKey(Key(ConsoleKey.Escape));
        var options = new ChoiceDialogOptions
        {
            Title = "Question",
            Lines = ["Continue?"],
            Buttons =
            [
                new DialogButton("yes", "Yes", 'Y', IsDefault: true),
                new DialogButton("no", "No", 'N'),
            ],
            DefaultButtonIndex = 10,
            CancelButtonIndex = -1,
        };

        ChoiceDialogResult entered = new ChoiceDialog(CreateModalHost(enterDriver)).Show(options);
        ChoiceDialogResult cancelled = new ChoiceDialog(CreateModalHost(escapeDriver)).Show(options);

        Assert.Equal("no", entered.ButtonId);
        Assert.Equal("yes", cancelled.ButtonId);
    }

    [Fact]
    public void ConfirmDialog_EnterConfirms()
    {
        var enterDriver = new FakeConsoleDriver(80, 20);
        enterDriver.EnqueueKey(Key(ConsoleKey.Enter));

        bool confirmed = new ConfirmDialog(CreateModalHost(enterDriver)).Show("Confirm", "Delete?", "file.txt");

        Assert.True(confirmed);
    }

    [Fact]
    public void ConfirmDialog_EscapeCancels()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        bool cancelled = new ConfirmDialog(CreateModalHost(driver)).Show("Confirm", "Delete?", "file.txt");

        Assert.False(cancelled);
    }

    [Fact]
    public void SingleLineInputDialog_EnterConfirmsInitialText()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SingleLineInputDialog(CreateModalHost(driver)).Show(new SingleLineInputDialogOptions
        {
            Title = "Input",
            Prompt = "Name",
            InitialText = "value",
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("value", result.Text);
    }

    [Fact]
    public void SingleLineInputDialog_RejectsEmptyUntilAllowedValueEntered()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SingleLineInputDialog(CreateModalHost(driver)).Show(new SingleLineInputDialogOptions
        {
            Title = "Input",
            Prompt = "Name",
            AllowEmpty = false,
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("a", result.Text);
    }

    [Fact]
    public void SingleLineInputDialog_ShowsValidationErrorAndReprompts()
    {
        var driver = new FakeConsoleDriver(80, 20);
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SingleLineInputDialog(CreateModalHost(driver)).Show(new SingleLineInputDialogOptions
        {
            Title = "Input",
            Prompt = "Name",
            InitialText = "bad",
            Validate = text => text == "bad" ? "Invalid" : null,
        });

        Assert.True(result.IsConfirmed);
        Assert.Equal("badx", result.Text);
        Assert.Contains(driver.WriteRecords, write => write.Text.Contains("Invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckBoxLine_MouseClickTogglesValue()
    {
        var driver = new FakeConsoleDriver(30, 5);
        var screen = new ScreenRenderer(driver);
        var checkBox = new CheckBoxLine("Option");
        checkBox.Render(screen, 2, 2, 20, focused: false);

        bool handled = checkBox.TryHandleMouse(
            new MouseConsoleInputEvent(3, 2, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            new Rect(2, 2, 20, 1));

        Assert.True(handled);
        Assert.True(checkBox.Value);
    }

    [Fact]
    public void ChoiceRow_KeyboardMovesPreviousAndNext()
    {
        var row = new ChoiceRow<string>(["one", "two", "three"], static value => value, selectedIndex: 1);

        Assert.True(row.TryHandleKey(Key(ConsoleKey.LeftArrow)));
        Assert.Equal("one", row.Value);
        Assert.True(row.TryHandleKey(Key(ConsoleKey.RightArrow)));
        Assert.Equal("two", row.Value);
    }

    [Fact]
    public void ChoiceRow_SegmentedMouseSelectsConcreteItem()
    {
        var driver = new FakeConsoleDriver(80, 5);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value);
        var layout = row.RenderSegmented(
            screen,
            x: 2,
            y: 2,
            width: 60,
            label: "Access rights:",
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        int inheritX = driver.GetRow(2).IndexOf("Inherit", StringComparison.Ordinal);
        bool handled = row.TryHandleMouse(
            new MouseConsoleInputEvent(inheritX, 2, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            layout);

        Assert.True(handled);
        Assert.Equal("Inherit", row.Value);
    }

    [Fact]
    public void ChoiceRow_SegmentedMouseOutsideOptionDoesNotChangeValue()
    {
        var driver = new FakeConsoleDriver(80, 5);
        var screen = new ScreenRenderer(driver);
        var row = new ChoiceRow<string>(["Default", "Copy", "Inherit"], static value => value);
        var layout = row.RenderSegmented(
            screen,
            x: 2,
            y: 2,
            width: 60,
            label: "Access rights:",
            focused: true,
            fillStyle: new CellStyle(ConsoleColor.Gray, ConsoleColor.Black),
            focusedStyle: new CellStyle(ConsoleColor.Black, ConsoleColor.Gray));

        bool handled = row.TryHandleMouse(
            new MouseConsoleInputEvent(3, 2, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            layout);

        Assert.False(handled);
        Assert.Equal("Default", row.Value);
    }

    [Fact]
    public void ChoiceRow_SegmentedLayout_ClipsHitTargetsToVisibleArea()
    {
        var row = new ChoiceRow<string>(["VeryLongChoice"], static value => value);

        var layout = row.CalculateSegmentedLayout(2, 1, 8, string.Empty);

        Assert.Single(layout.Choices);
        Assert.Equal(new Rect(2, 1, 8, 1), layout.Choices[0].Bounds);
        Assert.False(row.TryHandleMouse(
            new MouseConsoleInputEvent(11, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            layout));
    }

    private static ListWithButtonsDialog<string> CreateListWithButtons(IReadOnlyList<string> items) =>
        new(
            items,
            static item => item,
            [
                new DialogButton("connect", "Connect", 'O', IsDefault: true),
                new DialogButton("delete", "Delete", 'D'),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            "Items")
        {
            EmptyText = "Empty",
            DefaultListActionId = "connect",
            DeleteActionId = "delete",
            CancelActionId = "cancel",
        };

    private static ChoiceDialogOptions CreateChoiceOptions() =>
        new()
        {
            Title = "Question",
            Lines = ["Continue?"],
            Buttons =
            [
                new DialogButton("yes", "Yes", 'Y', IsDefault: true),
                new DialogButton("no", "No", 'N'),
            ],
            DefaultButtonIndex = 0,
            CancelButtonIndex = 1,
        };

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static ModalDialogHost CreateModalHost(FakeConsoleDriver driver)
    {
        var screen = new ScreenRenderer(driver);
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        return new ModalDialogHost(composition);
    }
}
