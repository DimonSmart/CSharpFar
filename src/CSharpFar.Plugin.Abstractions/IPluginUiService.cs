namespace CSharpFar.Plugin.Abstractions;

public interface IPluginUiService
{
    void ShowMessage(string title, string message);

    string? Input(string title, string prompt, string? initialText = null);

    int? ShowMenu(string title, IReadOnlyList<string> items, int selected);
}
