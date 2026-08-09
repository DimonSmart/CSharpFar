using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class FormDialogsTests
{
    [Fact]
    public void Show_BodyOnlyForm_CancelsWithHandlerResult()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));

        string? result = dialogs.Show(
            new FormDialogOptions("Body", 30, 8),
            rows: () => [new LabelRow("Body only")],
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<string?>.Complete(null)
                : FormDialogOutcome<string?>.Continue());

        Assert.Null(result);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Body only", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_FooterForm_SubmitsHandlerResult()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        ButtonRow actions = FormControls.Buttons(DialogButton.Default("ok", "OK", 'O'), DialogButton.Cancel());

        int result = dialogs.Show(
            new FormDialogOptions("Footer", 30, 8),
            rows: () => [new LabelRow("Body")],
            footer: () => FormFooter.ErrorAndButtons(() => "Error", actions),
            handle: formEvent => formEvent.IsSubmitted
                ? FormDialogOutcome<int>.Complete(42)
                : FormDialogOutcome<int>.Continue());

        Assert.Equal(42, result);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Error", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("OK", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_ValidationError_KeepsFormOpenAndFocusesRequestedControl()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        TextField value = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text("value");
        var firstControl = new CheckBoxRow(new CheckBoxLine("Other")) { Id = "other" };
        string? error = null;
        int submissions = 0;

        string? result = dialogs.Show(
            new FormDialogOptions("Validation", 30, 9),
            rows: () => [firstControl, FormControls.Text(value)],
            footer: () => FormFooter.ErrorAndButtons(() => error, FormControls.Buttons(DialogButton.Default("ok", "OK", 'O'))),
            handle: formEvent =>
            {
                if (!formEvent.IsSubmitted)
                    return FormDialogOutcome<string?>.Continue();

                submissions++;
                if (value.Text.Length == 0)
                {
                    error = "Value is required";
                    return FormDialogOutcome<string?>.ContinueWithFocus(value);
                }

                return FormDialogOutcome<string?>.Complete(value.Text);
            });

        Assert.Equal("x", result);
        Assert.Equal(2, submissions);
    }

    [Fact]
    public void Show_ValidationError_StringFocusTargetRemainsSupported()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        TextField value = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text("value");

        string result = dialogs.Show(
            new FormDialogOptions("Validation", 30, 8),
            rows: () => [FormControls.Text(value)],
            handle: formEvent => formEvent.IsSubmitted
                ? value.Text.Length == 0
                    ? FormDialogOutcome<string>.ContinueWithFocus("value")
                    : FormDialogOutcome<string>.Complete(value.Text)
                : FormDialogOutcome<string>.Continue());

        Assert.Equal("x", result);
    }

    [Fact]
    public void Show_TypedFocusTargetFocusesChoiceControl()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        ChoiceFormRow<string> choice = FormControls.Choice("choice", "Choice:", ["one", "two"], static value => value, "one");

        _ = dialogs.Show(
            new FormDialogOptions("Focus", 30, 8),
            rows: () => [choice],
            handle: formEvent => formEvent.IsSubmitted
                ? FormDialogOutcome<object?>.ContinueWithFocus(choice)
                : formEvent.IsCancelled
                    ? FormDialogOutcome<object?>.Complete(null)
                    : FormDialogOutcome<object?>.Continue());

        Assert.Equal("two", choice.Value);
    }

    [Fact]
    public void FormFocusTarget_ExcludesNonFocusableRows()
    {
        Assert.IsNotAssignableFrom<IFormFocusTarget>(FormControls.Label("Label"));
        Assert.IsNotAssignableFrom<IFormFocusTarget>(FormControls.Separator());
        Assert.IsNotAssignableFrom<IFormFocusTarget>(FormControls.Spacer());
    }

    [Fact]
    public void Show_RebuildsBodyRowsAfterValueChanges()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        var enabled = new CheckBoxRow(new CheckBoxLine("Enabled")) { Id = "enabled" };
        int builds = 0;

        _ = dialogs.Show(
            new FormDialogOptions("Dynamic", 30, 8),
            rows: () =>
            {
                builds++;
                return [enabled, new LabelRow(enabled.Value ? "Enabled now" : "Disabled now")];
            },
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<object?>.Complete(null)
                : FormDialogOutcome<object?>.Continue());

        Assert.True(builds >= 2);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Enabled now", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_EventExposesFocusedRowIdentity()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        TextField value = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text("value");
        string? focusedRowId = null;

        _ = dialogs.Show(
            new FormDialogOptions("Focus", 30, 8),
            rows: () => [FormControls.Text(value)],
            handle: formEvent =>
            {
                if (formEvent.IsValueChanged)
                    focusedRowId = formEvent.FocusedRowId;
                return formEvent.IsCancelled
                    ? FormDialogOutcome<object?>.Complete(null)
                    : FormDialogOutcome<object?>.Continue();
            });

        Assert.Equal("value", focusedRowId);
    }

    [Fact]
    public void Show_AppliesLayoutAndDynamicThemeThroughSemanticOptions()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        bool themeRequested = false;

        _ = dialogs.Show(
            new FormDialogOptions("Themed", 30, 8)
            {
                Layout = new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden),
                Theme = () =>
                {
                    themeRequested = true;
                    return PaletteRegistry.Default;
                },
            },
            rows: () => [FormControls.Label("Body")],
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<object?>.Complete(null)
                : FormDialogOutcome<object?>.Continue());

        Assert.True(themeRequested);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
