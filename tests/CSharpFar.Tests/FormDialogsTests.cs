using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class FormDialogsTests
{
    [Fact]
    public void NaturalContentSize_MeasuresTitleRowsLabelsAndExplicitFieldWidthInTerminalCells()
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField field = fields.Text(new TextFieldOptions("界", Width: 26));
        var form = new ScrollableFormDialog(new FormLayoutOptions(LabelGap: 2));

        form.SetRows(
        [
            FormControls.Label("表題"),
            FormControls.Text("Very long label", field),
            FormControls.CompactChoice("Mode", ["A", "wide界"], static value => value, "A"),
        ],
        [FormControls.OkCancel()]);

        Assert.Equal(4, form.NaturalContentHeight);
        Assert.Equal(ConsoleTextMetrics.GetCellWidth("Very long label") + 2 + 26, form.NaturalContentWidth);
    }

    [Fact]
    public void FormDialogOptions_LeavePreferredSizeUnsetUnlessExplicitlyOverridden()
    {
        var natural = new FormDialogOptions("Natural");
        var explicitSize = new FormDialogOptions("Override", PreferredWidth: 70, PreferredHeight: 20, MinWidth: 32);

        Assert.Null(natural.PreferredWidth);
        Assert.Null(natural.PreferredHeight);
        Assert.Equal(70, explicitSize.PreferredWidth);
        Assert.Equal(20, explicitSize.PreferredHeight);
        Assert.Equal(32, explicitSize.MinWidth);
    }

    [Fact]
    public void Show_StandardSubmit_CommitsEveryCurrentHistoryField()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var fields = new FormFieldFactory(provider);
        TextField first = fields.Text(new TextFieldOptions("alpha", new TextHistoryId("first")));
        TextField second = fields.Text(new TextFieldOptions("beta", new TextHistoryId("second")));

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Submit", 30, 8),
            rows: () => [FormControls.Text(first), FormControls.Text(second)],
            footer: () => [FormControls.OkCancel()],
            submit: () => FormSubmit.Success<string?>(first.Text));

        Assert.Equal("alpha", result);
        Assert.Equal(["alpha"], provider.Get(new TextHistoryId("first")).Items);
        Assert.Equal(["beta"], provider.Get(new TextHistoryId("second")).Items);
    }

    [Fact]
    public void Show_StandardSubmit_AuxiliaryHandlerCanHandleF1()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F1));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        bool f1Handled = false;
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("f1");
        TextField value = new FormFieldFactory(provider).Text(new TextFieldOptions("value", historyId));

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Auxiliary", 30, 8),
            rows: () => [FormControls.Text(value)],
            submit: () => FormSubmit.Success<string?>("submitted"),
            auxiliary: formEvent =>
            {
                if (formEvent.Key != ConsoleKey.F1)
                    return false;

                f1Handled = true;
                return true;
            });

        Assert.True(f1Handled);
        Assert.Null(result);
        Assert.Empty(provider.Get(historyId).Items);
    }

    [Fact]
    public void Show_StandardSubmit_InvalidErrorFocusesTargetAndClearsAfterValueChange()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField value = fields.Text();
        int submits = 0;

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Validation", 30, 9),
            rows: () => [FormControls.Text(value)],
            footer: () => [FormControls.OkCancel()],
            submit: () => ++submits == 1
                ? FormSubmit.Invalid<string?>("Value is required", value)
                : FormSubmit.Success<string?>(value.Text));

        Assert.Equal("x", result);
        Assert.Equal(2, submits);
    }

    [Fact]
    public void Show_StandardSubmit_InvalidErrorWithoutFocusKeepsFormOpen()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        int submits = 0;

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Validation", 30, 9),
            rows: () => [FormControls.Label("Body")],
            submit: () =>
            {
                submits++;
                return FormSubmit.Invalid<string?>("Value is required");
            });

        Assert.Null(result);
        Assert.Equal(1, submits);
    }

    [Fact]
    public void Show_StandardSubmit_CancelDoesNotSubmitOrCommitHistory()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("cancel");
        TextField value = new FormFieldFactory(provider).Text(new TextFieldOptions("value", historyId));
        int submits = 0;

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Cancel", 30, 8),
            rows: () => [FormControls.Text(value)],
            submit: () =>
            {
                submits++;
                return FormSubmit.Success<string?>(value.Text);
            });

        Assert.Null(result);
        Assert.Equal(0, submits);
        Assert.Empty(provider.Get(historyId).Items);
    }

    [Fact]
    public void Show_StandardSubmit_DoesNotCommitDisabledHistoryField()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("disabled");
        TextField value = new FormFieldFactory(provider).Text(new TextFieldOptions("value", historyId));
        value.Enabled = false;

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Disabled", 30, 8),
            rows: () => [FormControls.Text(value)],
            submit: () => FormSubmit.Success<string?>("accepted"));

        Assert.Equal("accepted", result);
        Assert.Empty(provider.Get(historyId).Items);
    }

    [Fact]
    public void Show_StandardSubmit_TracksHistoryFieldsAfterDynamicRowRebuild()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("dynamic");
        TextField value = new FormFieldFactory(provider).Text(new TextFieldOptions("value", historyId));
        CheckBoxRow enabled = FormControls.CheckBox("Include value");

        string? result = new FormDialogs(ModalTestHost.Create(driver)).Show(
            new FormDialogOptions("Dynamic", 30, 8),
            rows: () => enabled.Value
                ? [enabled, FormControls.Text(value)]
                : [enabled],
            submit: () => FormSubmit.Success<string?>(value.Text));

        Assert.Equal("value", result);
        Assert.Equal(["value"], provider.Get(historyId).Items);
    }

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
    public void OkCancel_IsAnonymousAndEnterSubmitsItsDefaultOkAction()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        ButtonRow actions = FormControls.OkCancel();

        string result = dialogs.Show(
            new FormDialogOptions("Actions", 30, 8),
            rows: () => [FormControls.Label("Body")],
            footer: () => [actions],
            handle: formEvent => formEvent.IsSubmitted
                ? FormDialogOutcome<string>.Complete(formEvent.Command!)
                : FormDialogOutcome<string>.Continue());

        Assert.Null(actions.Id);
        Assert.Equal("ok", result);
    }

    [Fact]
    public void OkCancel_CancelActionReturnsCancelledEvent()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));

        bool cancelled = dialogs.Show(
            new FormDialogOptions("Actions", 30, 8),
            rows: () => [FormControls.Label("Body")],
            footer: () => [FormControls.OkCancel()],
            handle: formEvent => formEvent.IsCancelled
                ? FormDialogOutcome<bool>.Complete(true)
                : FormDialogOutcome<bool>.Continue());

        Assert.True(cancelled);
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
    public void Show_IdlessChoiceSupportsTypedFocusTarget()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        ChoiceFormRow<string> choice = FormControls.Choice("Choice:", ["one", "two"], static value => value, "one");

        _ = dialogs.Show(
            new FormDialogOptions("Focus", 30, 8),
            rows: () => [choice],
            handle: formEvent => formEvent.IsSubmitted
                ? FormDialogOutcome<object?>.ContinueWithFocus(choice)
                : formEvent.IsCancelled
                    ? FormDialogOutcome<object?>.Complete(null)
                    : FormDialogOutcome<object?>.Continue());

        Assert.Equal("two", choice.Value);
        Assert.Null(choice.Id);
    }

    [Fact]
    public void Show_IdlessTextFieldSupportsTypedFocusTarget()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        TextField value = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text();
        int submits = 0;

        string result = dialogs.Show(
            new FormDialogOptions("Validation", 30, 8),
            rows: () => [FormControls.Text(value)],
            handle: formEvent => formEvent.IsSubmitted
                ? ++submits == 1
                    ? FormDialogOutcome<string>.ContinueWithFocus(value)
                    : FormDialogOutcome<string>.Complete(value.Text)
                : FormDialogOutcome<string>.Continue());

        Assert.Equal("x", result);
        Assert.Null(value.Id);
    }

    [Fact]
    public void Show_IdlessTextFieldEventUsesTypedSource()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        TextField value = new FormFieldFactory(TextFieldHistoryTestProvider.Create()).Text();
        IFormFocusTarget? source = null;

        _ = dialogs.Show(
            new FormDialogOptions("Typed source", 30, 8),
            rows: () => [FormControls.Text(value)],
            handle: formEvent =>
            {
                if (formEvent.IsValueChanged)
                {
                    Assert.True(formEvent.IsValueChangedFrom(value));
                    source = formEvent.SourceTarget;
                }
                return formEvent.IsCancelled
                    ? FormDialogOutcome<object?>.Complete(null)
                    : FormDialogOutcome<object?>.Continue();
            });

        Assert.Same(value, source);
        Assert.Null(value.Id);
    }

    [Fact]
    public void Show_IdlessCheckboxEventUsesTypedSourceAfterDynamicRebuild()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        var dialogs = new FormDialogs(ModalTestHost.Create(driver));
        CheckBoxRow enabled = FormControls.CheckBox("Enabled", false);
        IFormFocusTarget? source = null;

        _ = dialogs.Show(
            new FormDialogOptions("Typed source", 30, 8),
            rows: () => [enabled, FormControls.Label(enabled.Value ? "On" : "Off")],
            handle: formEvent =>
            {
                if (formEvent.IsValueChanged)
                {
                    Assert.True(formEvent.IsValueChangedFrom(enabled));
                    source = formEvent.SourceTarget;
                }
                return formEvent.IsCancelled
                    ? FormDialogOutcome<object?>.Complete(null)
                    : FormDialogOutcome<object?>.Continue();
            });

        Assert.Same(enabled, source);
        Assert.Null(enabled.Id);
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
        string? sourceRowId = null;

        _ = dialogs.Show(
            new FormDialogOptions("Focus", 30, 8),
            rows: () => [FormControls.Text(value)],
            handle: formEvent =>
            {
                if (formEvent.IsValueChanged)
                {
                    focusedRowId = formEvent.FocusedRowId;
                    sourceRowId = formEvent.SourceRowId;
                }
                return formEvent.IsCancelled
                    ? FormDialogOutcome<object?>.Complete(null)
                    : FormDialogOutcome<object?>.Continue();
            });

        Assert.Equal("value", focusedRowId);
        Assert.Equal("value", sourceRowId);
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
