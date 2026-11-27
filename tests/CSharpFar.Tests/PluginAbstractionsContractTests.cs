using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Tests;

public sealed class PluginAbstractionsContractTests
{
    [Fact]
    public void PluginUiService_NewDialogsHaveCompatibleDefaults()
    {
        IPluginUiService ui = new LegacyUi();

        Assert.False(ui.SupportsForms);
        Assert.Throws<NotSupportedException>(() => ui.ShowForm(new PluginForm("Form", [])));
        Assert.True(ui.Confirm("Confirm", "Continue?", "item"));
        Assert.Equal(0, ui.ShowChoice("Choice", "Choose", ["Yes", "No"]));
        ui.ShowHelp("Help", ["line 1", "line 2"]);
        Assert.Contains("line 1", ((LegacyUi)ui).LastMessage);
        Assert.Contains("line 2", ((LegacyUi)ui).LastMessage);
    }

    private sealed class LegacyUi : IPluginUiService
    {
        public string LastMessage { get; private set; } = "";
        public void ShowMessage(string title, string message) => LastMessage = message;
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => 0;
    }

    [Fact]
    public void PluginApplicationContext_ExposesRunMode()
    {
        var property = typeof(IPluginApplicationContext).GetProperty(nameof(IPluginApplicationContext.RunMode));

        Assert.NotNull(property);
        Assert.Equal(typeof(PluginRunMode), property.PropertyType);
    }
}
