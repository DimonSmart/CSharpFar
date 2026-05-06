namespace CSharpFar.App.UserMenu;

/// <summary>
/// Expands placeholders in a user-menu command string.
///
/// Supported placeholders:
///   {current}       — full path of the item under the cursor (empty if none / parent)
///   {selected}      — space-separated quoted selected paths;
///                     if nothing is selected, falls back to {current}
///   {panelDir}      — current directory of the active panel
///   {otherPanelDir} — current directory of the inactive panel
/// </summary>
public static class PlaceholderExpander
{
    public static string Expand(
        string              command,
        string              currentFile,
        IReadOnlyList<string> selectedPaths,
        string              panelDir,
        string              otherPanelDir)
    {
        string selected = selectedPaths.Count > 0
            ? string.Join(" ", selectedPaths.Select(p => $"\"{p}\""))
            : string.IsNullOrEmpty(currentFile) ? string.Empty : $"\"{currentFile}\"";

        return command
            .Replace("{current}",       currentFile)
            .Replace("{selected}",      selected)
            .Replace("{panelDir}",      panelDir)
            .Replace("{otherPanelDir}", otherPanelDir);
    }
}
