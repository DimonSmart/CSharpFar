namespace CSharpFar.Plugin.Abstractions;

public interface IPluginUiService
{
    /// <summary>Whether this host implements semantic modal forms (SDK 2.1).</summary>
    bool SupportsForms => false;

    /// <summary>Returns the accepted submit button ID, or null on user cancellation.</summary>
    string? ShowForm(PluginForm form, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This host does not support plugin forms.");

    void ShowMessage(string title, string message);

    string? Input(string title, string prompt, string? initialText = null);

    int? ShowMenu(string title, IReadOnlyList<string> items, int selected);

    // Default implementations let older plugin hosts and test doubles continue
    // working without adding new interface members immediately.
    bool Confirm(string title, string question, string itemName) =>
        ShowChoice(title, question + (string.IsNullOrEmpty(itemName) ? "" : "\n" + itemName),
            ["Yes", "No"]) == 0;

    /// <summary>Returns the selected button index or null on Escape.</summary>
    int? ShowChoice(string title, string message, IReadOnlyList<string> buttons) =>
        ShowMenu(title + ": " + message, buttons, selected: 0);

    void ShowHelp(string title, IReadOnlyList<string> lines) =>
        ShowMessage(title, string.Join(Environment.NewLine, lines));
}
