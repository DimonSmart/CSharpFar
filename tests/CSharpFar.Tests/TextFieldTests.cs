using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TextFieldTests
{
    [Fact]
    public void WithDefaults_AppliesScopedDefaultsAndAllowsPerFieldOverrides()
    {
        var provider = new CountingHistoryProvider();
        FormFieldFactory fields = new FormFieldFactory(provider).WithDefaults(
            new TextFieldDefaults(Width: 44, SubmitOnEnter: true));

        TextField inherited = fields.Text("inherited");
        TextField overridden = fields.Text("overridden", width: 12, submitOnEnter: false);
        TextField masked = fields.Text(
            "password",
            historyId: new TextHistoryId("TextFieldTests.ScopedPassword"),
            maskInput: true);

        Assert.Equal(44, inherited.Width);
        Assert.True(inherited.SubmitOnEnter);
        Assert.Equal(12, overridden.Width);
        Assert.False(overridden.SubmitOnEnter);
        Assert.Equal(44, masked.Width);
        Assert.Equal(0, provider.GetCallCount);
    }

    [Fact]
    public void LabeledRow_InheritsMaskedFieldConfigurationAndSharedInput()
    {
        var provider = new CountingHistoryProvider();
        TextField field = new FormFieldFactory(provider).Text(
            "password",
            "secret",
            new TextHistoryId("TextFieldTests.Password"),
            maskInput: true,
            width: 10,
            submitOnEnter: true);
        TextInputRow ordinary = field.AsRow();
        LabeledTextInputRow labeled = field.AsLabeledRow("Password:", labelWidth: 0);
        var driver = new FakeConsoleDriver(width: 20, height: 2);
        var screen = new ScreenRenderer(driver);

        UiTestRender.Render(screen, canvas =>
            labeled.Render(new FormRowRenderContext(canvas, new Rect(0, 0, 20, 1), focused: true)));

        Assert.Equal("password", labeled.Id);
        Assert.True(labeled.SubmitOnEnter);
        Assert.Equal(10, labeled.GetInputBounds(new Rect(0, 0, 20, 1)).Width);
        Assert.Same(field.Input, ordinary.Input);
        Assert.Same(field.Input, labeled.Input);
        Assert.Equal("secret", labeled.Buffer.Text);
        Assert.DoesNotContain("secret", driver.GetRow(0));
        Assert.Contains("******", driver.GetRow(0));
        Assert.Equal(0, provider.GetCallCount);
    }

    [Fact]
    public void FieldsWithSameHistoryId_ShareValuesButNotPopupState()
    {
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var id = new TextHistoryId("TextFieldTests.Shared");
        TextHistory persistent = provider.Get(id);
        for (int index = 0; index < 15; index++)
            persistent.Add($"item-{index:D2}");
        var factory = new FormFieldFactory(provider);
        TextField first = factory.Text("first", historyId: id);
        TextField second = factory.Text("second", historyId: id);
        SingleLineTextHistoryState firstPopup = Assert.IsType<SingleLineTextHistoryState>(first.Input.History);
        SingleLineTextHistoryState secondPopup = Assert.IsType<SingleLineTextHistoryState>(second.Input.History);

        Assert.Same(persistent, firstPopup.History);
        Assert.Same(persistent, secondPopup.History);
        Assert.True(firstPopup.OpenAll(availableContentRows: 4));
        Assert.True(firstPopup.MoveSelection(3, availableContentRows: 4));
        firstPopup.SetFirstVisibleIndex(4, availableContentRows: 4);

        Assert.True(firstPopup.IsDropdownOpen);
        Assert.NotEmpty(firstPopup.Matches);
        Assert.True(firstPopup.SelectedIndex > 0);
        Assert.True(firstPopup.FirstVisibleIndex > 0);
        Assert.False(secondPopup.IsDropdownOpen);
        Assert.Empty(secondPopup.Matches);
        Assert.Equal(0, secondPopup.SelectedIndex);
        Assert.Equal(0, secondPopup.FirstVisibleIndex);
    }

    [Fact]
    public void TextField_WithoutHistory_DoesNotQueryProviderOrRetainText()
    {
        var provider = new CountingHistoryProvider();
        var field = new FormFieldFactory(provider).Text("value", "  retained  ");

        field.AcceptHistory();

        Assert.Equal(0, provider.GetCallCount);
        Assert.Empty(provider.History.Items);
    }

    [Fact]
    public void TextField_Masked_DoesNotQueryProviderOrRetainText()
    {
        var provider = new CountingHistoryProvider();
        var field = new FormFieldFactory(provider).Text(
            "password",
            "secret",
            new TextHistoryId("TextFieldTests.Password"),
            maskInput: true);

        field.AcceptHistory();

        Assert.Equal(0, provider.GetCallCount);
        Assert.Empty(provider.History.Items);
    }

    [Fact]
    public void DisabledField_StateIsSharedByEveryRowProjectionAndPreventsInput()
    {
        TextField field = new FormFieldFactory(new CountingHistoryProvider()).Text("value", "retained");
        TextInputRow ordinary = field.AsRow();
        LabeledTextInputRow labeled = field.AsLabeledRow("Value:");
        field.Enabled = false;
        field.DisabledReason = "Unavailable";

        Assert.False(ordinary.IsEnabled);
        Assert.False(labeled.IsEnabled);
        Assert.False(ordinary.IsFocusable);
        Assert.Equal("Unavailable", ordinary.DisabledReason);
        Assert.Equal(FormInputResultKind.NotHandled,
            ordinary.HandleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), new FormRowInputContext(0, true)).Kind);
        Assert.Equal("retained", field.Text);

        field.Enabled = true;

        Assert.True(ordinary.IsEnabled);
        Assert.Equal(FormInputResultKind.ValueChanged,
            ordinary.HandleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), new FormRowInputContext(0, true)).Kind);
        Assert.Equal("retainedx", field.Text);
    }

    [Fact]
    public void TextField_AcceptHistory_RetainsTrimmedTextWithoutDuplicates()
    {
        var provider = new CountingHistoryProvider();
        var field = new FormFieldFactory(provider).Text(
            "value",
            "  retained  ",
            new TextHistoryId("TextFieldTests.Value"));

        field.AcceptHistory();
        field.AcceptHistory();

        Assert.Equal(1, provider.GetCallCount);
        Assert.Equal(["retained"], provider.History.Items);
    }

    private sealed class CountingHistoryProvider : ITextFieldHistoryProvider
    {
        private readonly SingleLineTextHistoryRegistry _registry =
            new(new InMemorySingleLineTextHistoryStore());

        public int GetCallCount { get; private set; }
        public TextHistory History => _registry.Get(new TextHistoryId("TextFieldTests.Stored"));

        public TextHistory Get(TextHistoryId id)
        {
            GetCallCount++;
            return History;
        }
    }
}
